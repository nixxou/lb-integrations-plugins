// The options window, opened from the right-click entry on one game or a selection.
//
// TABS FROM THE START: "Session" (where the console lives - RAM disk, disk, VHDX - and the sizes that
// go with it), "Updates & DLC" (Vita3kOptionsForm.Extras.cs: which of them the game is launched with),
// "System" (the language, date, time and enter button the game is told - its own, kept by this plugin
// and put into Vita3K's per-game settings file for the length of a session, Vita3kGameConfig; NOTHING IS
// OVERWRITTEN unless its box is ticked), "Graphics" (Vita3K's Renderer and Image Quality groups and its FPS
// Hack, each on "default" unless set - Vita3kGraphicsFields; kept and applied the same way), "Advanced" (what
// the two tabs above override, as the partial xml our fork reads - and, "Edit by hand" ticked, the user's
// own text instead, any section and attribute; the System and Graphics tabs are then greyed). Each tab is built by
// its own method and reads and writes its own part of the options.
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
            public Vita3kLayout Layout;          // the game's Vita3K, for the System tab
            public string TitleId;               // null when the archive could not be read
            public VitaSystemSettings System;    // the game's own here, null for none
            public VitaSystemSettings Base;      // what it runs on without: its custom config, else config.yml
            public Dictionary<string, string> Graphics;       // the game's own, null for none
            public Dictionary<string, string> GraphicsBase;   // what it runs on without them
            public Dictionary<string, string> Compat;         // the Compatibility tab's, section/attribute, null for none
            public Dictionary<string, string> CompatBase;     // what it runs on without them
            public VitaCompat CompatState;                     // where it stands in Vita3K's list, null when not in it
            public string Advanced;                            // the text set by hand, null for none
            public bool AdvancedOn;                            // and whether it is the one in use
            public string Key => Options.Key + "|" + Vita3kSystemFields.Key(System) + "|" + Vita3kGraphicsFields.Key(Graphics)
                                 + "|" + Vita3kCompatFields.Key(Compat) + "|" + (AdvancedOn ? "hand:" : "") + (Advanced ?? "");
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
        private CheckBox _sysOverwrite;
        private Vita3kSystemFields _system;
        private Vita3kGraphicsFields _graphics;
        private Vita3kCompatFields _compat;
        private Panel _compatState;
        private Label _compatHandNote;
        private CheckBox _handOn;
        private TextBox _handText;
        private Label _handStatus, _sysHandNote, _gfxHandNote, _gfxIntro;
        private string _keptHand;
        private bool _handLoading;


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
            ClientSize = new Size(680, 642);

            // ── the top: which games, and where to start from
            var top = new Panel { Dock = DockStyle.Top, Height = _groups.Count > 1 ? 86 : 56, Padding = new Padding(12, 10, 12, 0) };
            // WHAT THIS WINDOW IS (Mehdi, 29/09): a layer of this plugin's own, over the game's Custom Config.
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText,
                Text = "These are this plugin's settings: laid over the game's own Custom Config while it runs - that file itself is never changed here.",
            });
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
                                      + "   -   " + (g[0].Options.None ? "defaults" : g[0].Options.Key)
                                      + (g[0].System != null ? ", its own system settings" : ""));
                _source.SelectedIndexChanged += (_, _) => LoadFrom(_groups[_source.SelectedIndex][0]);
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            // ── the tabs
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(SessionTab());
            tabs.TabPages.Add(ExtrasTab(tabs));
            tabs.TabPages.Add(SystemTab());
            tabs.TabPages.Add(GraphicsTab());
            tabs.TabPages.Add(CompatibilityTab());
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
            // What the two tabs above override, shown as it is when the tab is looked at.
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == advanced && !_handOn.Checked) ShowGenerated(); };

            // ── the bottom: what OK will do, and the buttons
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(12, 6, 12, 8) };
            _notice = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 34, ForeColor = SystemColors.GrayText };
            var ok = new Button { Text = "OK", Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            ok.Click += (_, _) => Apply();
            // Vita3K's OWN per-game settings - its Custom Config - edited in Vita3K itself, for the selection.
            var own = new Button { Text = (games.Count == 1 ? "Game settings" : "Games' settings") + " in Vita3K...", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            new ToolTip().SetToolTip(own, "Opens Vita3K on " + (games.Count == 1 ? "this game" : "these games") + ", not started, to edit "
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
                cancel.Location = new Point(bottom.ClientSize.Width - bottom.Padding.Right - cancel.Width, bottom.ClientSize.Height - bottom.Padding.Bottom - cancel.Height);
                ok.Location = new Point(cancel.Left - 8 - ok.Width, cancel.Top);
                own.Location = new Point(bottom.Padding.Left, cancel.Top + (cancel.Height - own.Height) / 2);
            };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_source != null) _source.SelectedIndex = 0;
            else LoadFrom(_groups[0][0]);
        }

        private void LoadFrom(Entry e)
        {
            LoadOptions(e.Options);
            _sysOverwrite.Checked = e.System != null;
            _system.ShowValues(e.System ?? e.Base);
            _system.SetEditable(e.System != null);
            _graphics.ShowValues(e.GraphicsBase, e.Graphics);
            _compat.ShowValues(e.CompatBase, e.Compat);
            ShowCompatState(e);
            _handLoading = true;
            _keptHand = e.Advanced;
            _handOn.Checked = e.AdvancedOn && e.Advanced != null;
            _handText.Text = Lines(_handOn.Checked ? e.Advanced : Generated());
            _handLoading = false;
            HandChanged();
        }

        // ── the Advanced tab ─────────────────────────────────────────────────

        private TabPage AdvancedTab()
        {
            var page = new TabPage("Advanced") { Padding = new Padding(12), UseVisualStyleBackColor = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 6), Size = new Size(636, 112), ForeColor = SystemColors.GrayText,
                Text = "Write ONLY what you want to change, inside <config>: <section attribute=\"value\" />, e.g. <gpu fps-hack=\"true\" />. "
                     + "Any section Vita3K reads per game.\n"
                     + "- For this game's sessions only: its custom config is set aside as it starts and comes back when Vita3K quits.\n"
                     + "- A section you name keeps what the game's custom config has in it; what you write goes over. The rest of it is "
                     + "filled in for you (the official Vita3K reads a section only whole): from Vita3K's settings for system, gpu, cpu, audio and emulator.\n"
                     + "- A list you write (<ime-langs>, <lle-modules>) replaces the game's. Sections you do not name stay as they are.",
            });
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 122), Text = "Edit by hand (System, Graphics and Compatibility are then not used)" };
            page.Controls.Add(_handOn);
            var preview = new Button { Text = "Preview result...", AutoSize = true, Location = new Point(530, 118) };
            preview.Click += (_, _) => PreviewResult();
            page.Controls.Add(preview);
            _handText = new TextBox
            {
                Location = new Point(12, 148), Size = new Size(636, 214), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 9f), ReadOnly = true,
            };
            page.Controls.Add(_handText);
            _handStatus = new Label { AutoSize = false, Location = new Point(12, 364), Size = new Size(636, 80) };
            page.Controls.Add(_handStatus);

            _handOn.CheckedChanged += (_, _) =>
            {
                if (_handLoading) return;
                _handLoading = true;
                if (_handOn.Checked) _handText.Text = Lines(_keptHand ?? Generated());
                else { _keptHand = _handText.Text; _handText.Text = Lines(Generated()); }
                _handLoading = false;
                HandChanged();
            };
            _handText.TextChanged += (_, _) => { if (!_handLoading) HandChanged(); };
            return page;
        }

        /// <summary>The game's custom config as its next session will have it - what is shown, set by hand
        /// or not - in a window; nothing is written.</summary>
        private void PreviewResult()
        {
            var g = SourceGame();
            if (g.Layout == null || g.TitleId == null)
            { MessageBox.Show(this, "This game's title id or its Vita3K could not be found: there is nothing to preview.", Text); return; }
            var result = Vita3kGameConfig.Preview(g.Layout, g.TitleId, _handOn.Checked ? _handText.Text : Generated(), out var before, out var error);
            if (result == null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            ConfigPreviewWindow.Show(this, "config_" + g.TitleId + ".xml - for a session of " + g.Title,
                "What Vita3K will read for this game's session" + (before.Length == 0 ? " (it has no custom config of its own)." : ", over its own custom config."),
                before, result);
        }

        /// <summary>The partial xml of what the System and Graphics tabs set now.</summary>
        private string Generated()
        {
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            if (_sysOverwrite.Checked)
            {
                var s = _system.Read();
                sections[Vita3kGameConfig.SystemSection] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Vita3kConfig.EnterKey] = s.EnterButton.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [Vita3kConfig.LanguageKey] = s.Language.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [Vita3kConfig.DateKey] = s.DateFormat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [Vita3kConfig.TimeKey] = s.TimeFormat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [Vita3kConfig.PstvKey] = s.Pstv == true ? "true" : "false",
                };
            }
            sections[Vita3kGameConfig.GpuSection] = _graphics.Read();
            foreach (var g in _compat.Read().GroupBy(kv => kv.Key.Split('/')[0]))
                sections[g.Key] = g.ToDictionary(kv => kv.Key.Substring(g.Key.Length + 1), kv => kv.Value, StringComparer.Ordinal);
            return Vita3kGameConfig.Partial(sections);
        }

        private void ShowGenerated()
        {
            _handLoading = true;
            _handText.Text = Lines(Generated());
            _handLoading = false;
            HandChanged();
        }

        /// <summary>A TextBox shows a line break only as CR LF.</summary>
        private static string Lines(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        /// <summary>The text's state said under it, and the two tabs greyed while it is the one in use.</summary>
        private void HandChanged()
        {
            bool on = _handOn.Checked;
            _handText.ReadOnly = !on;
            _handText.BackColor = on ? SystemColors.Window : SystemColors.Control;
            _sysOverwrite.Enabled = !on;
            _system.SetEditable(!on && _sysOverwrite.Checked);
            _sysHandNote.Visible = on;
            _graphics.SetEditable(!on);
            _gfxHandNote.Visible = on;
            _compat.SetEditable(!on);
            _compatHandNote.Visible = on;
            _gfxIntro.Visible = !on;

            if (!on) { _handStatus.ForeColor = SystemColors.GrayText; _handStatus.Text = "Generated from the System, Graphics and Compatibility tabs."; return; }
            var g = SourceGame();
            var warnings = Vita3kGameConfig.CheckHand(g.Layout, g.TitleId, _handText.Text, out var error);
            _handStatus.ForeColor = error != null ? Color.Firebrick : warnings.Count > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            _handStatus.Text = error != null ? "Not valid: " + error
                             : warnings.Count > 0 ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? "\n(and " + (warnings.Count - 3) + " more)" : "")
                             : "Valid.";
        }

        // ── the Compatibility tab ────────────────────────────────────────────

        private TabPage CompatibilityTab()
        {
            var page = new TabPage("Compatibility") { Padding = new Padding(12), UseVisualStyleBackColor = true };
            _compatState = new Panel { Location = new Point(12, 8), Size = new Size(636, 26) };
            page.Controls.Add(_compatState);
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 38), Size = new Size(636, 34), ForeColor = SystemColors.GrayText,
                Text = "What Vita3K never sets for a game by itself, and some games need. A filled box or an untouched \"Override default\" "
                     + "leaves it as the game has it without this plugin: its custom config, else Vita3K's settings.",
            });
            _compat = new Vita3kCompatFields { Location = new Point(12, 76) };
            page.Controls.Add(_compat);
            _compatHandNote = new Label { AutoSize = false, Location = new Point(12, 332), Size = new Size(636, 34), ForeColor = Color.Firebrick, Visible = false,
                                          Text = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again." };
            page.Controls.Add(_compatHandNote);
            return page;
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

        // ── the Graphics tab ─────────────────────────────────────────────────

        private TabPage GraphicsTab()
        {
            var page = new TabPage("Graphics") { Padding = new Padding(12), UseVisualStyleBackColor = true, AutoScroll = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 8), Size = new Size(620, 34), ForeColor = SystemColors.GrayText,
                Text = "A filled box, a <Default> entry or an untouched \"Override default\" leaves the setting as the game has it "
                     + "without this plugin: its custom config, else Vita3K's settings. What is set here is used for its sessions only.",
            });
            _gfxIntro = (Label)page.Controls[page.Controls.Count - 1];
            _gfxHandNote = new Label { AutoSize = false, Location = new Point(12, 8), Size = new Size(620, 34), ForeColor = Color.Firebrick, Visible = false,
                                       Text = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again." };
            page.Controls.Add(_gfxHandNote);
            _graphics = new Vita3kGraphicsFields(perGame: true) { Location = new Point(12, 46) };
            page.Controls.Add(_graphics);
            return page;
        }

        // ── the System tab ───────────────────────────────────────────────────

        private TabPage SystemTab()
        {
            var page = new TabPage("System") { Padding = new Padding(12), UseVisualStyleBackColor = true };
            _sysOverwrite = new CheckBox
            {
                AutoSize = true, Location = new Point(12, 12),
                Text = "Overwrite Vita3K's system settings for " + (_games.Count == 1 ? "this game" : "these games"),
            };
            page.Controls.Add(_sysOverwrite);
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(30, 34), Size = new Size(610, 46), ForeColor = SystemColors.GrayText,
                Text = "What Vita3K tells the game, for its session only: put into its custom config (portable\\config\\config_<TITLE_ID>.xml) "
                     + "as it starts, and the custom config as it was comes back when Vita3K quits. Unticked, the game runs on "
                     + "its custom config, else Vita3K's settings - shown below.",
            });
            _system = new Vita3kSystemFields { Location = new Point(30, 90) };
            page.Controls.Add(_system);
            _sysHandNote = new Label { AutoSize = false, Location = new Point(12, 262), Size = new Size(620, 34), ForeColor = Color.Firebrick, Visible = false,
                                       Text = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again." };
            page.Controls.Add(_sysHandNote);
            _sysOverwrite.CheckedChanged += (_, _) =>
            {
                if (!_sysOverwrite.Checked) _system.ShowValues(SourceGame().Base);
                _system.SetEditable(_sysOverwrite.Checked);
            };
            return page;
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

            // The System tab: each game's own settings file, only where the choice differs from what it has.
            // The Advanced tab: a text in use must be usable; what it may not do is said, and asked.
            if (_handOn.Checked)
            {
                var g0 = SourceGame();
                var warnings = Vita3kGameConfig.CheckHand(g0.Layout, g0.TitleId, _handText.Text, out var handError);
                if (handError != null) { MessageBox.Show(this, "The settings set by hand are not valid xml: " + handError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (warnings.Count > 0
                    && MessageBox.Show(this, "The settings set by hand:\n\n- " + string.Join("\n- ", warnings) + "\n\nUse them anyway?",
                                       Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            var handText = _handOn.Checked ? _handText.Text.Trim() : _keptHand;
            bool handOn = _handOn.Checked;

            var system = _sysOverwrite.Checked ? _system.Read() : null;
            int systems = 0;
            foreach (var g in _games)
            {
                if (g.Layout == null || Vita3kSystemFields.Key(g.System) == Vita3kSystemFields.Key(system)) continue;
                Vita3kGameConfig.Save(g.Layout, g.GameId, system);
                systems++;
            }

            // The Compatibility tab: what is not on default, by section.
            var compat = _compat.Read();
            if (compat.Count == 0) compat = null;
            foreach (var g in _games)
            {
                if (g.Layout == null || Vita3kCompatFields.Key(g.Compat) == Vita3kCompatFields.Key(compat)) continue;
                foreach (var section in new[] { Vita3kGameConfig.CpuSection, Vita3kGameConfig.AudioSection, Vita3kGameConfig.EmulatorSection })
                {
                    var mine = compat?.Where(kv => kv.Key.StartsWith(section + "/", StringComparison.Ordinal))
                                      .ToDictionary(kv => kv.Key.Substring(section.Length + 1), kv => kv.Value, StringComparer.Ordinal);
                    Vita3kGameConfig.SaveSection(g.Layout, g.GameId, section, mine != null && mine.Count > 0 ? mine : null);
                }
                systems++;
            }

            // The Graphics tab: what is not on default.
            var graphics = _graphics.Read();
            if (graphics.Count == 0) graphics = null;
            foreach (var g in _games)
            {
                if (g.Layout == null || Vita3kGraphicsFields.Key(g.Graphics) == Vita3kGraphicsFields.Key(graphics)) continue;
                Vita3kGameConfig.SaveSection(g.Layout, g.GameId, Vita3kGameConfig.GpuSection, graphics);
                systems++;
            }

            foreach (var g in _games)
            {
                if (g.Layout == null) continue;
                bool same = g.AdvancedOn == (handOn && handText != null) && string.Equals((g.Advanced ?? "").Trim(), (handText ?? "").Trim(), StringComparison.Ordinal);
                if (same) continue;
                Vita3kGameConfig.SaveAdvanced(g.Layout, g.GameId, handText, handOn);
                systems++;
            }

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
            Log.Info("options window: " + changed + " of " + _games.Count + " game(s) changed, " + systems + " system setting(s) written");
            Changed = changed + systems;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
