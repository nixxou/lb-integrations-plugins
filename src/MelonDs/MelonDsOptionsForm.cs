// The options window of the melonDS plugin, opened from the right-click entry on one DS game or a
// selection (MelonDsGameMenu).
//
// ONE TAB PER GROUP OF OPTIONS (Mehdi, 29/09):
//   "Session" - where a DSiWare title's working NAND lives: a RAM disk (the default, when one can be
//               had) or the disk (--no-ramdisk). Only when the selection holds DSiWare: a cartridge has
//               no working NAND. With no RAM disk available the RAM choice is greyed, and says why.
//   "Video"   - the game's OWN video settings, the ones of melonDS's Video settings window, for every DS
//               game - cartridge or DSiWare - written for its session and taken back after
//               (MelonDsGameSettings). NOTHING IS OVERWRITTEN unless its box is ticked: showing the
//               window changes no game.
//   "Firmware" - the same for the ones of melonDS's Firmware settings window: name, language,
//               birthday, colour, message, MAC. A DSiWare title's save keeps the console's own
//               settings through it - see DsiWorkspace.MarkForced.
//   "Advanced" - what the two tabs above override, as the TOML fragment written into melonDS.toml for the
//               session - and, "Edit by hand" ticked, the user's own text instead: any table, any key
//               (MelonDsGameSettings, "set by hand"). Video and Firmware are then greyed.
//
// A SELECTION THAT DOES NOT AGREE (Mehdi's rule, as in the Vita3K window): the games are grouped by
// identical options, a combo box names each group ("Mario Kart DS <and 3 others>") and the one chosen is
// what the window starts from. OK then applies what is shown to EVERY selected game, after asking - the
// session choice to the DSiWare ones only, each keeping the rest of its own command line.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.MelonDs
{
    internal sealed class MelonDsOptionsForm : Form
    {
        internal sealed class Entry
        {
            public IGame Game;
            public string Title, Own, Inherited, Exe, InstallDir, GameId;
            public bool IsDSiWare;
            public MelonDsOptions Options;
            public Dictionary<string, string> Settings;   // the game's own settings, null for melonDS's
            public string Advanced;                        // the text set by hand, null for none
            public bool AdvancedOn;                        // and whether it is the one in use

            public string Key => (IsDSiWare ? (Options.NoRamDisk ? "disk" : "ram") : "-") + "|" + SettingsKey(Settings)
                                 + "|" + (AdvancedOn ? "hand:" : "") + (Advanced ?? "");
        }

        internal static string SettingsKey(Dictionary<string, string> settings) => MelonDsGameSettings.Format(settings);

        private readonly List<Entry> _games;
        private readonly List<Entry> _ware;
        private readonly List<List<Entry>> _groups;
        private readonly Dictionary<string, string> _global;
        private ComboBox _source;

        private RadioButton _ram, _disk;
        private CheckBox _overwrite, _threaded, _useGl, _vsync, _better, _hires;
        private CheckBox _fwOverwrite;
        private MelonDsFirmwareFields _firmware;
        private CheckBox _handOn;
        private TextBox _handText;
        private Label _handStatus, _videoHandNote, _fwHandNote;
        private string _keptHand;
        private bool _handLoading;
        private RadioButton _soft, _glClassic, _glCompute;
        private ComboBox _scale;
        private NumericUpDown _interval;
        private bool _loading;

        public MelonDsOptionsForm(List<Entry> games)
        {
            _games = games;
            _ware = games.Where(g => g.IsDSiWare).ToList();
            _groups = games.GroupBy(g => g.Key).Select(g => g.ToList()).ToList();
            var exe = games.Select(g => g.Exe).FirstOrDefault(e => !string.IsNullOrEmpty(e));
            _global = MelonDsGameSettings.Current(exe == null ? null : MelonDsPaths.Resolve(exe)?.ConfigFile);

            Text = "Nixx-melonDS - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(560, 560);

            // ── the top: which games, and where to start from
            var top = new Panel { Dock = DockStyle.Top, Height = _groups.Count > 1 ? 58 : 32, Padding = new Padding(12, 10, 12, 0) };
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
                                      + "   -   " + Describe(g[0]));
                _source.SelectedIndexChanged += (_, _) => LoadFrom(_groups[_source.SelectedIndex][0]);
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            if (_ware.Count > 0) tabs.TabPages.Add(SessionTab());
            tabs.TabPages.Add(VideoTab());
            tabs.TabPages.Add(FirmwareTab());
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
            // What the two tabs above override, shown as it is when the tab is looked at.
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == advanced && !_handOn.Checked) ShowGenerated(); };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            bottom.Layout += (_, _) =>
            {
                cancel.Location = new Point(bottom.ClientSize.Width - 12 - cancel.Width, 10);
                ok.Location = new Point(cancel.Left - 8 - ok.Width, 10);
            };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_source != null) _source.SelectedIndex = 0;
            else LoadFrom(_groups[0][0]);
        }

        private static string Describe(Entry e)
            => (e.IsDSiWare ? (e.Options.NoRamDisk ? "disk" : "RAM disk") + ", " : "")
               + (MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Video) ? "its own video" : "melonDS's video")
               + (MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Firmware) ? ", its own firmware" : ", melonDS's firmware");

        // ── Session ──────────────────────────────────────────────────────────

        private TabPage SessionTab()
        {
            var page = new TabPage("Session") { UseVisualStyleBackColor = true };
            int y = 14;
            void Add(Control c, int x) { c.Location = new Point(x, y); page.Controls.Add(c); }

            Add(new Label
            {
                Text = "Where the working NAND of " + (_ware.Count == 1 ? "this DSiWare title" : "the " + _ware.Count + " DSiWare titles of the selection")
                       + " lives while it plays. Its save is kept either way.",
                AutoSize = false, Size = new Size(510, 34),
            }, 12);
            y += 40;

            string why = null;
            try
            {
                var exe = _ware.Select(g => g.Exe).FirstOrDefault(e => !string.IsNullOrEmpty(e));
                var layout = exe == null ? null : MelonDsPaths.Resolve(exe);
                if (layout == null || !MelonDsRamDisk.WillTry(layout, noRamDisk: false)) why = MelonDsRamDisk.NotReadyReason();
            }
            catch (Exception ex) { why = ex.Message; }

            _ram = new RadioButton { AutoSize = true, Text = "RAM disk - faster, nothing written to the disk while playing (the default)", Enabled = why == null };
            Add(_ram, 18); y += 22;
            Add(why != null
                ? new Label { AutoSize = true, ForeColor = Color.Firebrick, Text = "Not available: " + why + ". These titles play on the disk." }
                : new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "Needs about 300 MB of free memory; without it, the disk is used." }, 36);
            y += 28;
            _disk = new RadioButton { AutoSize = true, Text = "Disk (" + MelonDsCommandLine.NoRamDiskFlag + ") - survives a crash of the machine mid-game" };
            Add(_disk, 18); y += 34;
            if (_games.Count > _ware.Count)
                Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText,
                                Text = (_games.Count - _ware.Count) + " cartridge game(s) of the selection have no session option and keep theirs." }, 12);
            return page;
        }

        // ── Video ────────────────────────────────────────────────────────────

        private TabPage VideoTab()
        {
            var page = new TabPage("Video") { UseVisualStyleBackColor = true };
            _overwrite = new CheckBox
            {
                AutoSize = true, Location = new Point(12, 12),
                Text = "Overwrite melonDS's video settings for " + (_games.Count == 1 ? "this game" : "these games"),
            };
            page.Controls.Add(_overwrite);
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(30, 34), Size = new Size(500, 32), ForeColor = SystemColors.GrayText,
                Text = "Written into melonDS for the game's session only; melonDS's own settings come back when it quits. "
                       + "Unticked, the game runs on melonDS's settings, shown below.",
            });

            var display = new GroupBox { Text = "Display settings", Location = new Point(12, 72), Size = new Size(250, 220) };
            display.Controls.Add(new Label { Text = "3D renderer:", AutoSize = true, Location = new Point(12, 24) });
            _soft = new RadioButton { Text = "Software", AutoSize = true, Location = new Point(18, 46) };
            _glClassic = new RadioButton { Text = "OpenGL (Classic)", AutoSize = true, Location = new Point(18, 70) };
            _glCompute = new RadioButton { Text = "OpenGL (Compute shader)", AutoSize = true, Location = new Point(18, 94) };
            _useGl = new CheckBox { Text = "OpenGL display", AutoSize = true, Location = new Point(12, 128) };
            _vsync = new CheckBox { Text = "VSync", AutoSize = true, Location = new Point(12, 152) };
            display.Controls.Add(new Label { Text = "VSync interval:", AutoSize = true, Location = new Point(12, 184) });
            _interval = new NumericUpDown { Minimum = 1, Maximum = 20, Width = 50, Location = new Point(120, 182) };
            display.Controls.AddRange(new Control[] { _soft, _glClassic, _glCompute, _useGl, _vsync, _interval });

            var softBox = new GroupBox { Text = "Software renderer", Location = new Point(276, 72), Size = new Size(250, 56) };
            _threaded = new CheckBox { Text = "Use separate thread", AutoSize = true, Location = new Point(12, 24) };
            softBox.Controls.Add(_threaded);

            var glBox = new GroupBox { Text = "OpenGL renderer", Location = new Point(276, 136), Size = new Size(250, 156) };
            glBox.Controls.Add(new Label { Text = "Internal resolution:", AutoSize = true, Location = new Point(12, 24) });
            _scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 46), Width = 224 };
            for (int i = 1; i <= 16; i++) _scale.Items.Add(i + "x" + (i == 1 ? " native" : "") + " (" + (256 * i) + "x" + (192 * i) + ")");
            _better = new CheckBox { Text = "Improved polygon splitting", AutoSize = true, Location = new Point(12, 82) };
            _hires = new CheckBox { Text = "Use high resolution coordinates", AutoSize = true, Location = new Point(12, 108) };
            glBox.Controls.AddRange(new Control[] { _scale, _better, _hires });

            page.Controls.AddRange(new Control[] { display, softBox, glBox });
            _videoHandNote = new Label { AutoSize = false, Location = new Point(12, 300), Size = new Size(520, 34), ForeColor = Color.Firebrick, Visible = false,
                                         Text = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again." };
            page.Controls.Add(_videoHandNote);

            EventHandler refresh = (_, _) => { if (!_loading) RefreshEnabled(); };
            foreach (var cb in new[] { _overwrite, _useGl, _vsync }) cb.CheckedChanged += refresh;
            foreach (var rb in new[] { _soft, _glClassic, _glCompute }) rb.CheckedChanged += refresh;
            // Unticked, the values shown go back to melonDS's own: what the game will run on.
            _overwrite.CheckedChanged += (_, _) => { if (!_loading && !_overwrite.Checked) ShowVideo(_global); };
            return page;
        }

        // ── Firmware ─────────────────────────────────────────────────────────

        private TabPage FirmwareTab()
        {
            var page = new TabPage("Firmware") { UseVisualStyleBackColor = true };
            _fwOverwrite = new CheckBox
            {
                AutoSize = true, Location = new Point(12, 12),
                Text = "Overwrite melonDS's firmware settings for " + (_games.Count == 1 ? "this game" : "these games"),
            };
            page.Controls.Add(_fwOverwrite);
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(30, 34), Size = new Size(500, 46), ForeColor = SystemColors.GrayText,
                Text = "melonDS then overrides the console's own settings with these, for the game's session only. "
                       + (_ware.Count > 0 ? "A DSiWare title's save keeps the console's own: the game sees these only while this is ticked. " : "")
                       + "Unticked, the game runs on melonDS's settings, shown below.",
            });
            _firmware = new MelonDsFirmwareFields { Location = new Point(12, 84) };
            page.Controls.Add(_firmware);
            _fwHandNote = new Label { AutoSize = false, Location = new Point(12, 354), Size = new Size(520, 34), ForeColor = Color.Firebrick, Visible = false,
                                      Text = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again." };
            page.Controls.Add(_fwHandNote);
            _fwOverwrite.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                if (!_fwOverwrite.Checked) _firmware.ShowValues(_global);
                _firmware.SetEditable(_fwOverwrite.Checked);
            };
            return page;
        }

        /// <summary>What melonDS's own window does: the GL options only for a GL renderer, the thread only
        /// for the software one, a GL renderer always on a GL display, VSync only on a GL display - and
        /// nothing at all while the box is unticked.</summary>
        private void RefreshEnabled()
        {
            bool on = _overwrite.Checked && !(_handOn?.Checked ?? false);
            bool gl = !_soft.Checked;
            foreach (var c in new Control[] { _soft, _glClassic, _glCompute }) c.Enabled = on;
            if (gl && !_useGl.Checked) { _loading = true; _useGl.Checked = true; _loading = false; }
            _useGl.Enabled = on && !gl;
            bool glDisplay = gl || _useGl.Checked;
            _vsync.Enabled = on && glDisplay;
            _interval.Enabled = on && glDisplay && _vsync.Checked;
            _threaded.Enabled = on && !gl;
            _scale.Enabled = _better.Enabled = _hires.Enabled = on && gl;
        }

        private void ShowVideo(Dictionary<string, string> v)
        {
            bool was = _loading;
            _loading = true;
            string Get(string id) => v != null && v.TryGetValue(id, out var x) ? x : (_global.TryGetValue(id, out var g) ? g : null);
            bool B(string id) => Get(id) == "true";
            int I(string id, int d) => int.TryParse(Get(id), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : d;
            int r = I("Renderer", 0);
            _soft.Checked = r == 0; _glClassic.Checked = r == 1; _glCompute.Checked = r == 2;
            _threaded.Checked = B("Threaded");
            _scale.SelectedIndex = Math.Max(0, Math.Min(15, I("ScaleFactor", 1) - 1));
            _better.Checked = B("BetterPolygons");
            _hires.Checked = B("HiresCoordinates");
            _useGl.Checked = B("UseGL");
            _vsync.Checked = B("VSync");
            _interval.Value = Math.Max(1, Math.Min(20, I("VSyncInterval", 1)));
            _loading = was;
            RefreshEnabled();
        }

        private Dictionary<string, string> ReadVideo() => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Renderer"] = _soft.Checked ? "0" : _glClassic.Checked ? "1" : "2",
            ["Threaded"] = _threaded.Checked ? "true" : "false",
            ["ScaleFactor"] = (_scale.SelectedIndex + 1).ToString(CultureInfo.InvariantCulture),
            ["BetterPolygons"] = _better.Checked ? "true" : "false",
            ["HiresCoordinates"] = _hires.Checked ? "true" : "false",
            ["UseGL"] = _useGl.Checked ? "true" : "false",
            ["VSync"] = _vsync.Checked ? "true" : "false",
            ["VSyncInterval"] = ((int)_interval.Value).ToString(CultureInfo.InvariantCulture),
        };

        // ── the source ───────────────────────────────────────────────────────

        private void LoadFrom(Entry e)
        {
            _loading = true;
            if (_ram != null)
            {
                bool wantDisk = e.IsDSiWare ? e.Options.NoRamDisk : _ware[0].Options.NoRamDisk;
                if (!_ram.Enabled || wantDisk) _disk.Checked = true; else _ram.Checked = true;
            }
            bool video = MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Video);
            bool firmware = MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Firmware);
            _overwrite.Checked = video;
            _fwOverwrite.Checked = firmware;
            _loading = false;
            ShowVideo(video ? e.Settings : _global);
            _firmware.ShowValues(firmware ? e.Settings : _global);
            _firmware.SetEditable(firmware);
            _handLoading = true;
            _keptHand = e.Advanced;
            _handOn.Checked = e.AdvancedOn && e.Advanced != null;
            _handText.Text = Lines(_handOn.Checked ? e.Advanced : Generated());
            _handLoading = false;
            HandChanged();
        }

        // ── Advanced ─────────────────────────────────────────────────────────

        private TabPage AdvancedTab()
        {
            var page = new TabPage("Advanced") { UseVisualStyleBackColor = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 6), Size = new Size(524, 116), ForeColor = SystemColors.GrayText,
                Text = "Write ONLY the keys you want to change: [table], then key = value - true/false, a number, 'text' or [a, b]. "
                     + "Any key of melonDS.toml, in any table.\n"
                     + "- Only these keys are written into melonDS.toml, for this game's sessions; the rest of the file is not touched.\n"
                     + "- melonDS's own values come back when it quits, and a key that was not there is taken out again.\n"
                     + "- Left out: the keys this plugin sets for every launch (console, boot, NAND, BIOS, save folders).",
            });
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 126), Text = "Edit by hand (Video and Firmware are then not used)" };
            page.Controls.Add(_handOn);
            var preview = new Button { Text = "Preview result...", AutoSize = true, Location = new Point(418, 122) };
            preview.Click += (_, _) => PreviewResult();
            page.Controls.Add(preview);
            _handText = new TextBox
            {
                Location = new Point(12, 152), Size = new Size(524, 196), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 9f), ReadOnly = true,
            };
            page.Controls.Add(_handText);
            _handStatus = new Label { AutoSize = false, Location = new Point(12, 352), Size = new Size(524, 70) };
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

        /// <summary>melonDS.toml as this game's next session will have it - what is shown, set by hand or
        /// not - in a window; nothing is written.</summary>
        private void PreviewResult()
        {
            List<MelonDsGameSettings.Raw> keys;
            if (_handOn.Checked)
            {
                keys = MelonDsGameSettings.ParseHand(_handText.Text, out var error);
                if (keys == null) { MessageBox.Show(this, "Not valid: " + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            else keys = MelonDsGameSettings.ToRaw(TabValues());
            var result = MelonDsGameSettings.Preview(Layout0(), keys, out var before, out var why);
            if (result == null) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var left = keys.Where(k => MelonDsGameSettings.IsManaged(k.Table, k.Key)).Select(k => "[" + k.Table + "] " + k.Key).ToList();
            ConfigPreviewWindow.Show(this, "melonDS.toml - for a session of " + (_games.Count == 1 ? _games[0].Title : _games.Count + " games"),
                "melonDS.toml as melonDS will read it for the session; it comes back as it is now when melonDS quits."
                + (left.Count > 0 ? " Left out: " + string.Join(", ", left) + "." : ""),
                before, result);
        }

        /// <summary>A TextBox shows a line break only as CR LF.</summary>
        private static string Lines(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        /// <summary>The fragment of what the Video and Firmware tabs set now.</summary>
        private string Generated() => MelonDsGameSettings.Fragment(MelonDsGameSettings.ToRaw(TabValues()));

        private void ShowGenerated()
        {
            _handLoading = true;
            _handText.Text = Lines(Generated());
            _handLoading = false;
            HandChanged();
        }

        private MelonDsLayout Layout0()
        {
            var exe = _games.Select(g => g.Exe).FirstOrDefault(x => !string.IsNullOrEmpty(x));
            return exe == null ? null : MelonDsPaths.Resolve(exe);
        }

        /// <summary>The text's state said under it, and the two tabs greyed while it is the one in use.</summary>
        private void HandChanged()
        {
            bool on = _handOn.Checked;
            _handText.ReadOnly = !on;
            _handText.BackColor = on ? SystemColors.Window : SystemColors.Control;
            _overwrite.Enabled = !on;
            RefreshEnabled();
            _fwOverwrite.Enabled = !on;
            _firmware.SetEditable(!on && _fwOverwrite.Checked);
            _videoHandNote.Visible = _fwHandNote.Visible = on;

            if (!on) { _handStatus.ForeColor = SystemColors.GrayText; _handStatus.Text = "Generated from the Video and Firmware tabs."; return; }
            var warnings = MelonDsGameSettings.CheckHand(Layout0(), _handText.Text, out var error);
            _handStatus.ForeColor = error != null ? Color.Firebrick : warnings.Count > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            _handStatus.Text = error != null ? "Not valid: " + error
                             : warnings.Count > 0 ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? "\n(and " + (warnings.Count - 3) + " more)" : "")
                             : "Valid.";
        }

        /// <summary>The game's own set from the tabs: the families whose box is ticked, nothing of the others.</summary>
        private Dictionary<string, string> TabValues()
        {
            var own = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_overwrite.Checked) foreach (var kv in ReadVideo()) own[kv.Key] = kv.Value;
            if (_fwOverwrite.Checked)
            {
                foreach (var kv in _firmware.Read())
                {
                    var s = MelonDsGameSettings.Settings.First(x => x.Id == kv.Key);
                    own[kv.Key] = MelonDsGameSettings.Normal(s, kv.Value) ?? s.Default;
                }
                own[MelonDsGameSettings.OverrideId] = "true";
            }
            return own;
        }

        // ── OK ───────────────────────────────────────────────────────────────

        private void Apply()
        {
            var problem = _fwOverwrite.Checked && !_handOn.Checked ? _firmware.Problem() : null;
            if (problem != null) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            // The Advanced tab: a text in use must be usable; what it will not do is said, and asked.
            if (_handOn.Checked)
            {
                var warnings = MelonDsGameSettings.CheckHand(Layout0(), _handText.Text, out var handError);
                if (handError != null) { MessageBox.Show(this, "The settings set by hand are not valid: " + handError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (warnings.Count > 0
                    && MessageBox.Show(this, "The settings set by hand:\n\n- " + string.Join("\n- ", warnings) + "\n\nUse them anyway?",
                                       Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            if (_games.Count > 1
                && MessageBox.Show(this, "These options will be applied to all " + _games.Count + " selected games"
                                         + (_ware.Count > 0 && _ware.Count < _games.Count ? " (the session option to the " + _ware.Count + " DSiWare ones)" : "") + ".",
                                   Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            int lines = 0, settings = 0;
            if (_ram != null)
            {
                var options = new MelonDsOptions { NoRamDisk = _disk.Checked };
                foreach (var g in _ware)
                {
                    var line = MelonDsCommandLine.NewOwnLine(g.Own, g.Inherited, options);
                    if (string.Equals(line ?? "", g.Own ?? "", StringComparison.Ordinal)) continue;
                    try { g.Game.CommandLine = line; lines++; Log.Info("options of " + g.Title + ": \"" + (g.Own ?? "") + "\" -> \"" + line + "\""); }
                    catch (Exception ex) { Log.Warn("options: could not write the line of " + g.Title, ex); }
                }
            }

            var own = TabValues();
            var chosen = own.Count > 0 ? own : null;
            foreach (var g in _games)
            {
                if (SettingsKey(g.Settings) == SettingsKey(chosen)) continue;
                MelonDsGameSettings.Save(g.InstallDir, g.GameId, chosen);
                settings++;
                Log.Info("options of " + g.Title + ": " + (chosen == null ? "melonDS's settings" : "its own settings - " + SettingsKey(chosen)));
            }

            // The text set by hand: kept even when not in use, so ticking the box again finds it.
            var handText = _handOn.Checked ? _handText.Text.Trim() : _keptHand?.Trim();
            foreach (var g in _games)
            {
                if (g.AdvancedOn == (_handOn.Checked && !string.IsNullOrEmpty(handText))
                    && string.Equals((g.Advanced ?? "").Trim(), handText ?? "", StringComparison.Ordinal)) continue;
                MelonDsGameSettings.SaveAdvanced(g.InstallDir, g.GameId, handText, _handOn.Checked);
                settings++;
            }

            if (lines > 0)
            {
                try { PluginHelper.DataManager?.Save(true); } catch (Exception ex) { Log.Warn("options: could not save the games", ex); }
            }
            Log.Info("options window: " + lines + " command line(s), " + settings + " game setting(s) changed, of " + _games.Count + " game(s)");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
