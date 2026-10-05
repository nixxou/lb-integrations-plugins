// The options window, opened from the right-click entry on one game or a selection.
//
// TWO TABS (Mehdi, 04/10 - the rest was too much to keep up with): "Session" (where the console lives - RAM disk, disk,
// VHDX - and the sizes that go with it) and "Updates & DLC" (Vita3kOptionsForm.Extras.cs: which of them the game is
// launched with). Under the title, where the game stands in Vita3K's compatibility list. A game's own settings are
// Vita3K's own Custom Config: "Game settings in Vita3K..." opens Vita3K on it. The System, Graphics, Compatibility and
// Advanced tabs are gone, and what they saved for a game is no longer laid over its custom config at launch.
//
// DRESSED AS THE NIXX WINDOW (Mehdi, 05/10, NixxShell): the two tabs are pages chosen at the left, their title above them;
// each page reads top-down as cards, a grey line one short sentence with what it said before on hover (LbipHint).
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
using LbIntegrations.Lbip;
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
            public Vita3kLayout Layout;          // the game's Vita3K, for the System tab
            public bool LineIsOurs;              // the game's OWN emulator is this Vita3K: its command line is ours to change
            public string EmulatorTitle;         // the game's own emulator, to say whose line it is otherwise
            public string TitleId;               // null when the archive could not be read
            public VitaSystemSettings System;    // the game's own here, null for none
            public VitaSystemSettings Base;      // what it runs on without: its custom config, else config.yml
            public Dictionary<string, string> Graphics;       // the game's own, null for none
            public Dictionary<string, string> GraphicsBase;   // what it runs on without them
            public Dictionary<string, string> Compat;         // the Compatibility tab's, section/attribute, null for none
            public Dictionary<string, string> CompatBase;     // what it runs on without them
            public HashSet<string> GraphicsFromGame, CompatFromGame;   // what the game's own custom config sets
            public bool SystemFromGame;                        // and whether it sets the system settings
            public VitaCompat CompatState;                     // where it stands in Vita3K's list, null when not in it
            public string Advanced;                            // the text set by hand, null for none
            public bool AdvancedOn;                            // and whether it is the one in use
            public string Key => Options.Key;
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
        private Panel _compatState;


        /// <summary>The games that were changed, once OK has run.</summary>
        public int Changed { get; private set; }

        public Vita3kOptionsForm(List<Entry> games)
        {
            _games = games;
            _groups = games.GroupBy(g => g.Key).Select(g => g.ToList()).ToList();


            Text = "Nixx-Vita3K - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            // Taller by the combo box a selection that does not agree adds above the pages: the Session page fits unscrolled.
            ClientSize = new Size(680, 634 + (_groups.Count > 1 ? 28 : 0));

            // ── the top: which games, and where to start from
            var top = new Panel { Dock = DockStyle.Top, Height = (_groups.Count > 1 ? 86 : 58) + 34, Padding = new Padding(12, 10, 12, 0) };
            // Under its state: the whole list downloaded again (Mehdi, 05/10), the date of the last one. Docked first, so lowest.
            var layoutShown = games.Select(g => g.Layout).FirstOrDefault(l => l != null);
            var refresh = new Panel { Dock = DockStyle.Top, Height = 34 };
            refresh.Controls.Add(LbIntegrations.Lbip.LbipListRefresh.Row("Vita3K's compatibility list",
                () => layoutShown == null ? "not known: this game's Vita3K is not found" : Vita3kCompat.Downloaded(layoutShown) is DateTime d ? "downloaded " + d.ToString("g") + " (Vita3K/compatibility)" : "never downloaded yet",
                job => Vita3kCompat.DownloadWhole(layoutShown, job),
                () =>
                {
                    foreach (var g in _games) if (g.TitleId != null && g.Layout != null) g.CompatState = Vita3kCompat.Lookup(g.Layout, g.TitleId);
                    ShowCompatState(_source != null && _source.SelectedIndex >= 0 ? _groups[_source.SelectedIndex][0] : _groups[0][0]);
                }, 640));
            top.Controls.Add(refresh);
            // Under the title: where the game stands in Vita3K's compatibility list (Mehdi, 04/10).
            _compatState = new Panel { Dock = DockStyle.Top, Height = 28 };
            top.Controls.Add(_compatState);
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
                _source.SelectedIndexChanged += (_, _) => LoadFrom(_groups[_source.SelectedIndex][0]);
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
            // Vita3K's OWN per-game settings - its Custom Config - edited in Vita3K itself, for the selection.
            var own = new Button { Text = (games.Count == 1 ? "Game settings" : "Games' settings") + " in Vita3K...", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            LbipHint.Attach(own, "Opens Vita3K on " + (games.Count == 1 ? "this game" : "these games") + ", not started, to edit "
                                          + (games.Count == 1 ? "its" : "each one's") + " own Custom Config in Vita3K.\n"
                                          + "What this window sets is not written there: it is laid over it while the game runs.");
            own.Click += (_, _) =>
            {
                var layout = games.Select(g => g.Layout).FirstOrDefault(l => l != null);
                var same = games.Where(g => g.Layout != null && string.Equals(g.Layout.Executable, layout?.Executable, StringComparison.OrdinalIgnoreCase)).ToList();
                if (same.Count < games.Count)
                    Log.Info("options: " + (games.Count - same.Count) + " game(s) of another Vita3K left out of its settings window");
                Vita3kGameMenu.OpenSettings(this, layout, same.Select(g => (g.Title, g.RomFull)).ToList());
            };
            bottom.Controls.Add(_notice);
            bottom.Controls.Add(own);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            bottom.Layout += (_, _) =>
            {
                int y = bottom.ClientSize.Height - bottom.Padding.Bottom - ok.Height;
                cancel.Location = new Point(bottom.ClientSize.Width - bottom.Padding.Right - cancel.Width, y);
                ok.Location = new Point(cancel.Left - 8 - ok.Width, y);
                own.Location = new Point(bottom.Padding.Left, y);
            };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_source != null) _source.SelectedIndex = 0;
            else LoadFrom(_groups[0][0]);
            // Dressed as the Nixx window (Mehdi, 05/10): LiteBox's look, its tabs a page bar at the left.
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private void LoadFrom(Entry e)
        {
            LoadOptions(e.Options);
            ShowCompatState(e);
        }

        /// <summary>Where the game shown stands in Vita3K's own list - see Vita3kCompat.</summary>
        private void ShowCompatState(Entry e)
        {
            _compatState.Controls.Clear();
            var row = Vita3kCompatRow.Build(e.CompatState, _compatState.Width);
            _compatState.Controls.Add(row ?? new Label
            {
                AutoSize = true, ForeColor = SystemColors.GrayText, Location = new Point(0, 4),
                Text = e.TitleId == null ? "Vita3K compatibility: the game's title id could not be read."
                     : "Vita3K compatibility: " + e.TitleId + " is not in Vita3K's list (or Vita3K has not downloaded it yet).",
            });
        }

        // ── the Session tab ──────────────────────────────────────────────────

        // THREE CARDS, read top-down (Mehdi, 05/10: "je veux uniformiser l'ensemble"): where the console lives, its sizes, and
        // what a launch is given. Each grey line is one short sentence; what it said before is on hover, word for word.
        private TabPage SessionTab()
        {
            var page = new TabPage("Session") { Padding = new Padding(12), UseVisualStyleBackColor = true };
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            page.Controls.Add(stack);
            GroupBox card = null;
            int y = 0;
            GroupBox Card(string title)
            {
                card = new GroupBox { Text = title, Width = 610, Margin = new Padding(0, 0, 0, 8) };
                stack.Controls.Add(card);
                y = 22;
                return card;
            }
            void End() => card.Height = y + 10;
            Control Add(Control c, int x, int width = 0, int height = 0)
            {
                c.Location = new Point(x, y);
                if (width > 0) c.Width = width;
                if (height > 0) c.Height = height;
                card.Controls.Add(c);
                return c;
            }
            Label Hint(string text, string full, int x, int width)
            {
                var label = (Label)Add(new Label { Text = text, AutoSize = false, Height = 18, ForeColor = SystemColors.GrayText, UseMnemonic = false }, x, width);
                LbipHint.Attach(label, full);
                return label;
            }

            Card("Where the console lives during a game");
            _ramDisk = (RadioButton)Add(new RadioButton { Text = "RAM disk - on the disk instead when memory is short (default)", AutoSize = true }, 12);
            y += 24;
            _diskOnly = (RadioButton)Add(new RadioButton { Text = "Disk only, never a RAM disk  (" + Vita3kPlugin.NoRamDiskFlag + ")", AutoSize = true }, 12);
            y += 24;
            _useVhdx = (RadioButton)Add(new RadioButton { Text = "VHDX files - the game installed once, kept on the disk  (" + Vita3kPlugin.UseVhdxFlag + ")", AutoSize = true }, 12);
            y += 27;
            Add(new Label { Text = "Folder:", AutoSize = true }, 32).Top += 3;
            _vhdxDir = (TextBox)Add(new TextBox(), 86, 420);
            _browse = (Button)Add(new Button { Text = "Browse...", Width = 80, Height = 25 }, 514);
            y += 29;
            _vhdxHint = Hint("Empty: the vhdx folder beside Vita3K.",
                             "Empty: the vhdx folder beside Vita3K. When the VHDX cannot be used, the session takes the RAM disk.", 86, 500);
            LbipHint.Attach(_vhdxDir, "Empty: the vhdx folder beside Vita3K. When the VHDX cannot be used, the session takes the RAM disk.");
            y += 18;
            End();

            Card("Sizes");
            Add(new Label { Text = "RAM disk margin (MB):", AutoSize = true }, 12).Top += 3;
            _margin = (TextBox)Add(new TextBox(), 184, 80);
            var marginFull = "empty: " + Vita3kWorkspace.MarginMb + " MB, for saves, caches and logs  (" + Vita3kPlugin.RamDiskMarginFlag + "=)";
            Hint("Empty: " + Vita3kWorkspace.MarginMb + " MB, for saves, caches and logs.", marginFull, 274, 320).Top += 3;
            LbipHint.Attach(_margin, marginFull);
            y += 28;
            Add(new Label { Text = "RAM kept for Vita3K (MB):", AutoSize = true }, 12).Top += 3;
            _vitaRam = (TextBox)Add(new TextBox(), 184, 80);
            var ramFull = "empty: this game's last peak + 15%, at least " + Vita3kWorkspace.MinReserveMb + " MB (2048 before any measure)\n\n"
                + "Free RAM the RAM disk must leave for Vita3K itself (" + Vita3kPlugin.Vita3kRamFlag + "=).\n"
                + "Empty: the peak Vita3K reached with this game last time, plus 15% - the largest peak of any\n"
                + "game for one never played, 2048 MB before anything was measured - never less than " + Vita3kWorkspace.MinReserveMb + " MB.\n"
                + "Short of it, other programs' idle memory is freed first, then the session runs on the disk.";
            Hint("Empty: this game's last peak + 15%.", ramFull, 274, 320).Top += 3;
            LbipHint.Attach(_vitaRam, ramFull);
            y += 24;
            End();

            Card("Applied at launch");
            _preview = (TextBox)Add(new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical }, 12, 584, 52);
            y += 58;
            // KEPT BY THIS PLUGIN, NOT IN THE GAME'S COMMAND LINE (Mehdi, 29/09): that line is the game's default
            // emulator's - kept here, the choice holds whichever emulator of ours runs it.
            Hint("Kept by this plugin; the game's command line stays as it is.",
                 "Kept by this plugin for the game and applied at launch - whichever emulator of ours runs it; the game's "
                 + "command line is not changed (flags of ours left on it are moved here).", 12, 584);
            y += 18;
            End();

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
            RefreshMarks();
            // The hint stays enabled: greyed it would be drawn engraved, and say nothing on hover.
            _vhdxDir.Enabled = _browse.Enabled = _useVhdx.Checked;
            // The sizes are the RAM disk's: nothing reads them on the disk, a VHDX reads them if it falls back.
            _margin.Enabled = _vitaRam.Enabled = !_diskOnly.Checked;

            var o = Read(out var problem);
            var shown = SourceGame();
            if (o == null) { _preview.Text = problem; _preview.ForeColor = Color.Firebrick; return; }
            _preview.ForeColor = SystemColors.WindowText;
            _preview.Text = o.None ? "(none - the defaults)" : o.Key;

            // WHAT OK WILL DO, said before it is done.
            var notes = new List<string>();
            int moved = _games.Count(g => g.LineIsOurs && !string.IsNullOrWhiteSpace(g.Own) && Vita3kCommandLines.Strip(g.Own, g.Rom) != g.Own);
            if (moved > 0)
                notes.Add((_games.Count == 1 ? "This game's" : moved + " game(s)'") + " command line carries options of ours: they move out of it, kept here.");
            int both = _games.Count(g => g.Options.UseVhdx && g.Options.NoRamDisk);
            if (both > 0 && o.UseVhdx)
                notes.Add((_games.Count == 1 ? "Its" : both + " game(s) have a") + " " + Vita3kPlugin.NoRamDiskFlag + " beside "
                          + Vita3kPlugin.UseVhdxFlag + ": it goes - a VHDX session falls back to the RAM disk.");
            _notice.Text = string.Join("  ", notes);
        }

        private Entry SourceGame() => _source != null ? _groups[_source.SelectedIndex][0] : _games[0];

        // No colour bars any more (Mehdi, 04/10): the legend that explained them went with the settings tabs.
        private void RefreshMarks() { }

        // ── OK ───────────────────────────────────────────────────────────────

        private void Apply()
        {
            var o = Read(out var problem);
            if (o == null) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ApplyExtras()) return;   // the Updates & DLC tab: backed out of its warning, nothing applied

            if (_games.Count > 1
                && MessageBox.Show(this, "Your changes will be applied to all " + _games.Count + " selected games; what each one has of its own and you did not change stays as it is.\n\n"
                                         + "Each game keeps the rest of its own command line; only this plugin's options change.",
                                   Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            int changed = 0;
            foreach (var g in _games)
            {
                // Kept for the game when it differs from what it runs with now - or when its line still carries them.
                var ownLine = !string.IsNullOrWhiteSpace(g.Own) ? Vita3kCommandLines.Strip(g.Own, g.Rom) : g.Own;
                bool carries = g.LineIsOurs && !string.IsNullOrWhiteSpace(g.Own) && ownLine != g.Own;
                if (g.Options.Key != o.Key || carries || Vita3kSessionStore.Load(g.InstallDir, g.GameId) == null && !o.None)
                {
                    Vita3kSessionStore.Save(g.InstallDir, g.GameId, o.Key);
                    changed++;
                }
                // Options of ours still on the game's OWN line move here: taken off the line.
                if (!carries) continue;
                try
                {
                    g.Game.CommandLine = ownLine;
                    Log.Info("options of " + g.Title + ": moved off the line - \"" + g.Own + "\" -> \"" + ownLine + "\"");
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
