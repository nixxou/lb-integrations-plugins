// The options window, opened from the right-click entry on one game or a selection.
//
// TABS FROM THE START: "Session" (where the console lives - RAM disk, disk, VHDX - and the sizes that
// go with it), "Updates & DLC" (Vita3kOptionsForm.Extras.cs: which of them the game is launched with),
// Vita3K's own settings later (resolution and the like). Each tab is built by its own method and reads
// and writes its own part of the options.
//
// A SELECTION THAT DOES NOT AGREE: the games are grouped by identical options; when there is more than
// one group, a combo box names each ("KILLALLZOMBIES <and 3 others>") and the one chosen is what the
// window starts from. OK then applies what is shown to EVERY selected game, after asking - each game
// keeping the rest of its own line.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Vita3k
{
    internal sealed partial class Vita3kOptionsForm : Form
    {
        /// <summary>One selected game: its lines, and what they give.</summary>
        internal sealed class Entry
        {
            public IGame Game;
            public string Title, Rom, Own, Inherited;
            public string RomFull, GameId, InstallDir;   // for the Updates & DLC tab
            public Vita3kOptions Options;
            public bool Inherits => string.IsNullOrWhiteSpace(Own);
            public string Effective => Inherits ? Inherited : Own;
        }

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private ComboBox _source;
        // THREE EXCLUSIVE CHOICES (Mehdi): VHDX supplants the other two, so it is one of them rather than
        // a box on top. What that costs: "VHDX, and disk only if it cannot be used" is no longer said -
        // a VHDX session that falls back takes the default path (RAM disk, then disk). A line carrying
        // both flags shows as VHDX, and the window says --no-ramdisk goes on OK.
        private RadioButton _ramDisk, _diskOnly, _useVhdx;
        private TextBox _vhdxDir, _margin, _vitaRam, _preview;
        private Button _browse;
        private Label _vhdxHint, _notice;
        private bool _loading;

        /// <summary>The games that were changed, once OK has run.</summary>
        public int Changed { get; private set; }

        public Vita3kOptionsForm(List<Entry> games)
        {
            _games = games;
            _groups = games.GroupBy(g => g.Options.Key).Select(g => g.ToList()).ToList();

            Text = "Nixx-Vita3K - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(680, 560);

            // ── the top: which games, and where to start from
            var top = new Panel { Dock = DockStyle.Top, Height = _groups.Count > 1 ? 64 : 34, Padding = new Padding(12, 10, 12, 0) };
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20,
                Text = games.Count == 1 ? games[0].Title
                     : games.Count + " games" + (_groups.Count > 1 ? " - their options differ: start from" : ", all with the same options"),
            });
            if (_groups.Count > 1)
            {
                _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                foreach (var g in _groups)
                    _source.Items.Add(g[0].Title + (g.Count > 1 ? " <and " + (g.Count - 1) + " other" + (g.Count > 2 ? "s" : "") + ">" : "")
                                      + "   -   " + (g[0].Options.None ? "defaults" : g[0].Options.Key));
                _source.SelectedIndexChanged += (_, _) => LoadOptions(_groups[_source.SelectedIndex][0].Options);
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            // ── the tabs
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(SessionTab());
            tabs.TabPages.Add(ExtrasTab(tabs));

            // ── the bottom: what OK will do, and the buttons
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(12, 6, 12, 8) };
            _notice = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 34, ForeColor = SystemColors.GrayText };
            var ok = new Button { Text = "OK", Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            ok.Click += (_, _) => Apply();
            bottom.Controls.Add(_notice);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            bottom.Layout += (_, _) =>
            {
                cancel.Location = new Point(bottom.ClientSize.Width - bottom.Padding.Right - cancel.Width, bottom.ClientSize.Height - bottom.Padding.Bottom - cancel.Height);
                ok.Location = new Point(cancel.Left - 8 - ok.Width, cancel.Top);
            };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_source != null) _source.SelectedIndex = 0;
            else LoadOptions(_groups[0][0].Options);
        }

        // ── the Session tab ──────────────────────────────────────────────────

        private TabPage SessionTab()
        {
            var page = new TabPage("Session") { Padding = new Padding(12), UseVisualStyleBackColor = true };
            int y = 10;
            Control Add(Control c, int x, int width = 0, int height = 0)
            {
                c.Location = new Point(x, y);
                if (width > 0) c.Width = width;
                if (height > 0) c.Height = height;
                page.Controls.Add(c);
                return c;
            }
            Label Hint(string text, int x, int width = 520)
                => (Label)Add(new Label { Text = text, AutoSize = false, Height = 18, ForeColor = SystemColors.GrayText }, x, width);

            Add(new Label { Text = "Where the console lives during a game", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 8);
            y += 26;
            _ramDisk = (RadioButton)Add(new RadioButton { Text = "RAM disk - on the disk instead when memory is short (default)", AutoSize = true }, 18);
            y += 24;
            _diskOnly = (RadioButton)Add(new RadioButton { Text = "Disk only, never a RAM disk  (" + Vita3kPlugin.NoRamDiskFlag + ")", AutoSize = true }, 18);
            y += 24;
            _useVhdx = (RadioButton)Add(new RadioButton { Text = "VHDX files - the game installed once, kept on the disk  (" + Vita3kPlugin.UseVhdxFlag + ")", AutoSize = true }, 18);
            y += 24;
            Add(new Label { Text = "Folder:", AutoSize = true }, 38);
            _vhdxDir = (TextBox)Add(new TextBox(), 92, 450);
            _browse = (Button)Add(new Button { Text = "Browse...", Width = 80, Height = 25 }, 550);
            y += 28;
            _vhdxHint = Hint("Empty: the vhdx folder beside Vita3K. When the VHDX cannot be used, the session takes the RAM disk.", 38, 590);
            y += 34;

            Add(new Label { Text = "Sizes", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 8);
            y += 26;
            Add(new Label { Text = "RAM disk margin (MB):", AutoSize = true }, 18);
            _margin = (TextBox)Add(new TextBox(), 190, 80);
            Hint("empty: " + Vita3kWorkspace.MarginMb + " MB, for saves, caches and logs  (" + Vita3kPlugin.RamDiskMarginFlag + "=)", 280, 360);
            y += 28;
            Add(new Label { Text = "RAM kept for Vita3K (MB):", AutoSize = true }, 18);
            _vitaRam = (TextBox)Add(new TextBox(), 190, 80);
            new ToolTip().SetToolTip(_vitaRam, "Free RAM the RAM disk must leave for Vita3K itself (" + Vita3kPlugin.Vita3kRamFlag + "=).\n"
                + "Empty: the peak Vita3K reached with this game last time, plus 15% - the largest peak of any\n"
                + "game for one never played, 2048 MB before anything was measured - never less than " + Vita3kWorkspace.MinReserveMb + " MB.\n"
                + "Short of it, other programs' idle memory is freed first, then the session runs on the disk.");
            Hint("empty: this game's last peak + 15%, at least " + Vita3kWorkspace.MinReserveMb + " MB (2048 before any measure)", 280, 380);
            y += 38;

            Add(new Label { Text = "Command line", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 8);
            y += 24;
            _preview = (TextBox)Add(new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical }, 18, 610, 52);

            EventHandler changed = (_, _) => Refresh_();
            _ramDisk.CheckedChanged += changed;
            _diskOnly.CheckedChanged += changed;
            _useVhdx.CheckedChanged += changed;
            _vhdxDir.TextChanged += changed;
            _margin.TextChanged += changed;
            _vitaRam.TextChanged += changed;
            _browse.Click += (_, _) => Browse();
            return page;
        }

        private void Browse()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Where the VHDX files of the console are kept",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (Directory.Exists(_vhdxDir.Text.Trim())) dialog.SelectedPath = _vhdxDir.Text.Trim();
            if (dialog.ShowDialog(this) == DialogResult.OK) _vhdxDir.Text = dialog.SelectedPath;
        }

        // ── reading and writing the controls ─────────────────────────────────

        private void LoadOptions(Vita3kOptions o)
        {
            _loading = true;
            _useVhdx.Checked = o.UseVhdx;
            _diskOnly.Checked = !o.UseVhdx && o.NoRamDisk;
            _ramDisk.Checked = !o.UseVhdx && !o.NoRamDisk;
            _vhdxDir.Text = o.VhdxDir ?? "";
            _margin.Text = o.MarginMb?.ToString() ?? "";
            _vitaRam.Text = o.Vita3kRamMb?.ToString() ?? "";
            _loading = false;
            Refresh_();
        }

        /// <summary>The options as the controls say them - null, with <paramref name="problem"/>, when
        /// a field holds something a launch would refuse.</summary>
        private Vita3kOptions Read(out string problem)
        {
            problem = null;
            var o = new Vita3kOptions { NoRamDisk = _diskOnly.Checked, UseVhdx = _useVhdx.Checked };
            if (o.UseVhdx)
            {
                var dir = _vhdxDir.Text.Trim();
                if (dir.Length > 0)
                {
                    if (!Path.IsPathFullyQualified(dir) || dir.IndexOf('"') >= 0)
                    { problem = "The VHDX folder must be a full path (like E:\\VitaVhdx), or empty."; return null; }
                    o.VhdxDir = dir;
                }
            }
            if (!Mb(_margin.Text, "RAM disk margin", out o.MarginMb, out problem)) return null;
            if (!Mb(_vitaRam.Text, "RAM kept for Vita3K", out o.Vita3kRamMb, out problem)) return null;
            return o;
        }

        private static bool Mb(string text, string what, out int? mb, out string problem)
        {
            mb = null; problem = null;
            text = (text ?? "").Trim();
            if (text.Length == 0) return true;
            if (int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v) && v <= 65536)
            { mb = v; return true; }
            problem = what + ": a whole number of MB up to 65536, or empty for the default.";
            return false;
        }

        private void Refresh_()
        {
            if (_loading || _preview == null) return;
            _vhdxDir.Enabled = _browse.Enabled = _vhdxHint.Enabled = _useVhdx.Checked;
            // The sizes are the RAM disk's: nothing reads them on the disk, a VHDX reads them if it falls back.
            _margin.Enabled = _vitaRam.Enabled = !_diskOnly.Checked;

            var o = Read(out var problem);
            var shown = SourceGame();
            if (o == null) { _preview.Text = problem; _preview.ForeColor = Color.Firebrick; return; }
            _preview.ForeColor = SystemColors.WindowText;
            var own = Vita3kCommandLines.NewOwnLine(shown.Own, shown.Inherited, o, shown.Rom);
            _preview.Text = own.Length == 0
                ? "(the emulator's own line, inherited)   " + shown.Inherited
                : own;

            // WHAT OK WILL DO, said before it is done.
            int willOwn = _games.Count(g => g.Inherits && Vita3kCommandLines.NewOwnLine(g.Own, g.Inherited, o, g.Rom).Length > 0);
            int willInherit = _games.Count(g => !g.Inherits && Vita3kCommandLines.NewOwnLine(g.Own, g.Inherited, o, g.Rom).Length == 0);
            var notes = new List<string>();
            if (willOwn > 0)
                notes.Add((_games.Count == 1 ? "This game" : willOwn + " game(s)") + " will get a command line of its own, based on the emulator's:"
                          + " a later change to the emulator's line will no longer reach " + (willOwn == 1 ? "it." : "them."));
            if (willInherit > 0)
                notes.Add((_games.Count == 1 ? "This game" : willInherit + " game(s)") + " will go back to the emulator's line.");
            int both = _games.Count(g => g.Options.UseVhdx && g.Options.NoRamDisk);
            if (both > 0 && o.UseVhdx)
                notes.Add((_games.Count == 1 ? "Its" : both + " game(s) have a") + " " + Vita3kPlugin.NoRamDiskFlag + " beside "
                          + Vita3kPlugin.UseVhdxFlag + ": it goes - a VHDX session falls back to the RAM disk.");
            _notice.Text = string.Join("  ", notes);
        }

        private Entry SourceGame() => _source != null ? _groups[_source.SelectedIndex][0] : _games[0];

        // ── OK ───────────────────────────────────────────────────────────────

        private void Apply()
        {
            var o = Read(out var problem);
            if (o == null) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ApplyExtras()) return;   // the Updates & DLC tab: backed out of its warning, nothing applied

            if (_games.Count > 1
                && MessageBox.Show(this, "These options will be applied to all " + _games.Count + " selected games.\n\n"
                                         + "Each game keeps the rest of its own command line; only this plugin's options change.",
                                   Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            int changed = 0;
            foreach (var g in _games)
            {
                var line = Vita3kCommandLines.NewOwnLine(g.Own, g.Inherited, o, g.Rom);
                if (string.Equals(line, g.Own ?? "", StringComparison.Ordinal)) continue;
                try
                {
                    g.Game.CommandLine = line;
                    changed++;
                    Log.Info("options of " + g.Title + ": \"" + (g.Own ?? "") + "\" -> \"" + line + "\"" + (line.Length == 0 ? " (inherits again)" : ""));
                }
                catch (Exception ex) { Log.Warn("could not set the command line of " + g.Title, ex); }
            }
            if (changed > 0)
            {
                try { PluginHelper.DataManager?.Save(true); }
                catch (Exception ex) { Log.Warn("could not save the games", ex); }
            }
            Log.Info("options window: " + changed + " of " + _games.Count + " game(s) changed");
            Changed = changed;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
