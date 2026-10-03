// The options window of the melonDS plugin, opened from the right-click entry on one DS game or a
// selection (MelonDsGameMenu).
//
// ONE TAB PER GROUP OF OPTIONS (Mehdi, 29/09):
//   "Session" - where a DSiWare title's working NAND lives: a RAM disk (the default, when one can be
//               had) or the disk (--no-ramdisk). Only when the selection holds DSiWare: a cartridge has
//               no working NAND. With no RAM disk available the RAM choice is greyed, and says why.
//   "Video"   - the game's OWN video settings, the ones of melonDS's Video settings window, for every DS
//               game - cartridge or DSiWare - written for its session and taken back after
//               (MelonDsGameSettings). EACH SETTING ON ITS OWN (Mehdi, 29/09 - as Vita3K's Graphics): a
//               list starts with "<Default : melonDS's value>", a box has three states (the filled one:
//               default), the VSync interval an "Override default" beside it - and only what is not on
//               default is written: the fewer keys written, the less a newer melonDS can trip on them.
//               Showing the window changes no game.
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
using LbIntegrations.Lbip;
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
            public bool LineIsOurs;          // the game's OWN emulator is this melonDS: its command line is ours to change
            public string EmulatorTitle;     // the game's own emulator, to say whose line it is otherwise
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
        private CheckBox _threaded, _useGl, _vsync, _better, _hires, _intervalOverride;
        private ComboBox _renderer;
        private CheckBox _fwOverwrite;
        private MelonDsFirmwareFields _firmware;
        private CheckBox _handOn;
        private TextBox _handText;
        private Label _handStatus, _videoHandNote, _fwHandNote;
        private string _keptHand;
        private bool _handLoading;
        private ComboBox _scale;
        private NumericUpDown _interval;
        private bool _loading;
        private readonly OptionMarks _marks = new OptionMarks();

        public MelonDsOptionsForm(List<Entry> games)
        {
            _games = games;
            _ware = games.Where(g => g.IsDSiWare).ToList();
            _groups = games.GroupBy(g => g.Key).Select(g => g.ToList()).ToList();
            var exe = games.Select(g => g.Exe).FirstOrDefault(e => !string.IsNullOrEmpty(e));
            // As melonDS has them - a value out of our lists shown as it is, not as our default (only shown, never written).
            _global = MelonDsGameSettings.Current(exe == null ? null : MelonDsPaths.Resolve(exe)?.ConfigFile, asWritten: true);

            Text = "Nixx-melonDS - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(560, 588);

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

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            // Where each value comes from, said by colour - see OptionMarks.
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("melonDS", gameConfig: false, hereLoses: false),
                                            OptionMarks.ResetButton(ResetDefaults), ok, cancel);
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
            _disk = new RadioButton { AutoSize = true, Text = "Disk - survives a crash of the machine mid-game" };
            Add(_disk, 18); y += 34;
            _disk.CheckedChanged += (_, _) => { if (!_loading) RefreshMarks(); };
            // KEPT BY THIS PLUGIN, NOT IN THE GAME'S COMMAND LINE (Mehdi, 29/09): that line is the game's
            // default emulator's - kept here, the choice holds whichever emulator of ours runs it.
            Add(new Label
            {
                AutoSize = false, Size = new Size(510, 34), ForeColor = SystemColors.GrayText,
                Text = "Kept by this plugin for the game and applied at launch - whichever emulator of ours runs it; "
                       + "the game's command line is not changed (a " + MelonDsCommandLine.NoRamDiskFlag + " left on it is moved here).",
            }, 12);
            y += 40;
            if (_games.Count > _ware.Count)
                Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText,
                                Text = (_games.Count - _ware.Count) + " cartridge game(s) of the selection have no session option and keep theirs." }, 12);
            return page;
        }

        // ── Video ────────────────────────────────────────────────────────────

        private static readonly string[] Renderers = { "Software", "OpenGL (Classic)", "OpenGL (Compute shader)" };

        private TabPage VideoTab()
        {
            var page = new TabPage("Video") { UseVisualStyleBackColor = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 8), Size = new Size(520, 52), ForeColor = SystemColors.GrayText,
                Text = "A <Default> entry, a filled box or an untouched \"Override default\" leaves the setting as melonDS has it; only "
                     + "what is set here is written, for the game's session only - melonDS's own come back when it quits.",
            });

            var display = new GroupBox { Text = "Display settings", Location = new Point(12, 64), Size = new Size(250, 232) };
            display.Controls.Add(new Label { Text = "3D renderer:", AutoSize = true, Location = new Point(12, 24) });
            _renderer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 44), Width = 226 };
            _useGl = Box("OpenGL display", new Point(12, 80));
            _vsync = Box("VSync", new Point(12, 106));
            display.Controls.Add(new Label { Text = "VSync interval:", AutoSize = true, Location = new Point(12, 140) });
            _intervalOverride = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(120, 138) };
            _interval = new NumericUpDown { Minimum = 1, Maximum = 20, Width = 50, Location = new Point(14, 164) };
            display.Controls.AddRange(new Control[] { _renderer, _useGl, _vsync, _intervalOverride, _interval });

            var softBox = new GroupBox { Text = "Software renderer", Location = new Point(276, 64), Size = new Size(250, 56) };
            _threaded = Box("Use separate thread", new Point(12, 24));
            softBox.Controls.Add(_threaded);

            var glBox = new GroupBox { Text = "OpenGL renderer", Location = new Point(276, 128), Size = new Size(250, 168) };
            glBox.Controls.Add(new Label { Text = "Internal resolution:", AutoSize = true, Location = new Point(12, 24) });
            _scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 46), Width = 224 };
            // Two lines each: "(default: ...)" would not fit beside the name.
            _better = Box("Improved polygon splitting", new Point(12, 80));
            _better.AutoSize = false; _better.Size = new Size(230, 36);
            _hires = Box("Use high resolution coordinates", new Point(12, 118));
            _hires.AutoSize = false; _hires.Size = new Size(230, 36);
            glBox.Controls.AddRange(new Control[] { _scale, _better, _hires });

            page.Controls.AddRange(new Control[] { display, softBox, glBox });
            _videoHandNote = new Label { AutoSize = false, Location = new Point(12, 304), Size = new Size(520, 34), ForeColor = Color.Firebrick, Visible = false,
                                         Text = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again." };
            page.Controls.Add(_videoHandNote);

            EventHandler refresh = (_, _) => { if (!_loading) RefreshEnabled(); };
            foreach (var cb in new[] { _useGl, _vsync, _threaded, _better, _hires }) cb.CheckStateChanged += refresh;
            _renderer.SelectedIndexChanged += refresh;
            _scale.SelectedIndexChanged += refresh;
            _intervalOverride.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                if (!_intervalOverride.Checked) _interval.Value = Interval(Default("VSyncInterval"));
                RefreshEnabled();
            };
            return page;
        }

        private static CheckBox Box(string text, Point at) => new CheckBox { Text = text, Tag = text, AutoSize = true, Location = at, ThreeState = true };

        private string Default(string id) => _global.TryGetValue(id, out var v) ? v : null;

        private static decimal Interval(string v)
            => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(1, Math.Min(20, n)) : 1;

        /// <summary>A list's entry: the value written, the words shown; a null value is the default.</summary>
        private sealed class Item
        {
            public string Value, Text;
            public override string ToString() => Text;
        }

        private static string RendererName(string v)
            => int.TryParse(v, out var n) && n >= 0 && n < Renderers.Length ? Renderers[n] : v + " (not in this list)";

        private static string ScaleName(string v)
            => int.TryParse(v, out var n) && n >= 1 && n <= 16 ? n + "x" + (n == 1 ? " native" : "") + " (" + (256 * n) + "x" + (192 * n) + ")" : v;

        private static void FillList(ComboBox box, IEnumerable<(string Value, string Text)> choices, string defaultText, string own)
        {
            box.Items.Clear();
            box.Items.Add(new Item { Value = null, Text = string.IsNullOrEmpty(defaultText) ? "<Default>" : "<Default : " + defaultText + ">" });
            foreach (var c in choices) box.Items.Add(new Item { Value = c.Value, Text = c.Text });
            if (own == null) { box.SelectedIndex = 0; return; }
            var at = box.Items.Cast<Item>().ToList().FindIndex(i => i.Value == own);
            if (at < 0) { box.Items.Add(new Item { Value = own, Text = own }); at = box.Items.Count - 1; }
            box.SelectedIndex = at;
        }

        private static string Chosen(ComboBox box) => (box.SelectedItem as Item)?.Value;

        private static string OfBox(CheckBox box) => box.CheckState == CheckState.Indeterminate ? null : box.Checked ? "true" : "false";

        private static void SetBox(CheckBox box, string own)
            => box.CheckState = own == null ? CheckState.Indeterminate : own == "true" ? CheckState.Checked : CheckState.Unchecked;

        /// <summary>A setting's value for the game: its own, else melonDS's.</summary>
        private string Effective(string id, string own) => own ?? Default(id);

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
                RefreshMarks();
            };
            return page;
        }

        /// <summary>What melonDS's own window does, by the values the game will RUN on (its own, else
        /// melonDS's): the GL options only for a GL renderer, the thread only for the software one, a GL
        /// renderer always on a GL display, VSync only on a GL display - and nothing at all while the
        /// Advanced tab's text is in use. A box on default says which: "VSync   (default: off)".</summary>
        private void RefreshEnabled()
        {
            bool on = !(_handOn?.Checked ?? false);
            bool gl = Effective("Renderer", Chosen(_renderer)) is string r && r != "0";
            // melonDS runs a GL renderer on a GL display, whatever the box says: set so, it is said so.
            if (on && gl && Chosen(_renderer) != null && Effective("UseGL", OfBox(_useGl)) != "true")
            { _loading = true; _useGl.CheckState = CheckState.Checked; _loading = false; }
            bool glDisplay = gl || Effective("UseGL", OfBox(_useGl)) == "true";
            _renderer.Enabled = on;
            _useGl.Enabled = on && !gl;
            _vsync.Enabled = on && glDisplay;
            _intervalOverride.Enabled = on && glDisplay && Effective("VSync", OfBox(_vsync)) == "true";
            _interval.Enabled = _intervalOverride.Enabled && _intervalOverride.Checked;
            _threaded.Enabled = on && !gl;
            _scale.Enabled = _better.Enabled = _hires.Enabled = on && gl;

            foreach (var (cb, id) in new[] { (_useGl, "UseGL"), (_vsync, "VSync"), (_threaded, "Threaded"), (_better, "BetterPolygons"), (_hires, "HiresCoordinates") })
            {
                var d = Default(id);
                cb.Text = cb.CheckState != CheckState.Indeterminate ? (string)cb.Tag
                        : (string)cb.Tag + "   (default: " + (d == "true" ? "on" : d == "false" ? "off" : "not set") + ")";
            }
            RefreshMarks();
        }

        /// <summary>Each setting's bar: set here, else melonDS's - it has no per-game config of its own. A text set
        /// by hand in use: Video and Firmware are not used, so not marked. See OptionMarks.</summary>
        private void RefreshMarks()
        {
            if (_fwOverwrite == null) return;
            bool on = !(_handOn?.Checked ?? false);
            OptionLevel Of(bool here) => !on ? OptionLevel.Unused : here ? OptionLevel.Here : OptionLevel.Emulator;
            _marks.Set(_renderer, Of(Chosen(_renderer) != null));
            _marks.Set(_scale, Of(Chosen(_scale) != null));
            foreach (var cb in new[] { _useGl, _vsync, _threaded, _better, _hires }) _marks.Set(cb, Of(OfBox(cb) != null));
            _marks.Set(_interval, Of(_intervalOverride.Checked));
            _marks.Set(_fwOverwrite, Of(_fwOverwrite.Checked));
            _marks.Set(_firmware, Of(_fwOverwrite.Checked));
            // The session: the disk when the RAM disk could be had - the default is the RAM disk.
            if (_disk != null)
            {
                _marks.Set(_ram, _disk.Checked ? OptionLevel.Unused : OptionLevel.Emulator);
                _marks.Set(_disk, !_disk.Checked ? OptionLevel.Unused : _ram.Enabled ? OptionLevel.Here : OptionLevel.Emulator);
            }
        }

        /// <summary>"Reset to defaults": every setting on default - the RAM disk, melonDS's video and firmware -
        /// and the text set by hand off and forgotten; nothing saved before OK.</summary>
        private void ResetDefaults()
        {
            _loading = true;
            if (_ram != null && _ram.Enabled) _ram.Checked = true;
            _fwOverwrite.Checked = false;
            _loading = false;
            ShowVideo(null);
            _firmware.ShowValues(_global);
            _firmware.SetEditable(false);
            _handLoading = true;
            _handOn.Checked = false;
            _keptHand = null;
            _handLoading = false;
            ShowGenerated();
        }

        /// <summary>melonDS's values as the defaults, and the game's own over them.</summary>
        private void ShowVideo(Dictionary<string, string> own)
        {
            bool was = _loading;
            _loading = true;
            string Own(string id) => own != null && own.TryGetValue(id, out var x) ? x : null;
            FillList(_renderer, Renderers.Select((n, i) => (i.ToString(CultureInfo.InvariantCulture), n)), RendererName(Default("Renderer")), Own("Renderer"));
            FillList(_scale, Enumerable.Range(1, 16).Select(i => (i.ToString(CultureInfo.InvariantCulture), ScaleName(i.ToString(CultureInfo.InvariantCulture)))),
                     ScaleName(Default("ScaleFactor")), Own("ScaleFactor"));
            SetBox(_threaded, Own("Threaded"));
            SetBox(_better, Own("BetterPolygons"));
            SetBox(_hires, Own("HiresCoordinates"));
            SetBox(_useGl, Own("UseGL"));
            SetBox(_vsync, Own("VSync"));
            _intervalOverride.Checked = Own("VSyncInterval") != null;
            _interval.Value = Interval(Own("VSyncInterval") ?? Default("VSyncInterval"));
            _loading = was;
            RefreshEnabled();
        }

        /// <summary>Only what is not on default.</summary>
        private Dictionary<string, string> ReadVideo()
        {
            var v = new Dictionary<string, string>(StringComparer.Ordinal);
            void Put(string id, string value) { if (value != null) v[id] = value; }
            Put("Renderer", Chosen(_renderer));
            Put("ScaleFactor", Chosen(_scale));
            Put("Threaded", OfBox(_threaded));
            Put("BetterPolygons", OfBox(_better));
            Put("HiresCoordinates", OfBox(_hires));
            Put("UseGL", OfBox(_useGl));
            Put("VSync", OfBox(_vsync));
            if (_intervalOverride.Checked) v["VSyncInterval"] = ((int)_interval.Value).ToString(CultureInfo.InvariantCulture);
            return v;
        }

        // ── the source ───────────────────────────────────────────────────────

        private void LoadFrom(Entry e)
        {
            _loading = true;
            if (_ram != null)
            {
                bool wantDisk = e.IsDSiWare ? e.Options.NoRamDisk : _ware[0].Options.NoRamDisk;
                if (!_ram.Enabled || wantDisk) _disk.Checked = true; else _ram.Checked = true;
            }
            bool firmware = MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Firmware);
            _fwOverwrite.Checked = firmware;
            _loading = false;
            ShowVideo(e.Settings);
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
            RefreshEnabled();
            _fwOverwrite.Enabled = !on;
            _firmware.SetEditable(!on && _fwOverwrite.Checked);
            _videoHandNote.Visible = _fwHandNote.Visible = on;
            RefreshMarks();

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
            foreach (var kv in ReadVideo()) own[kv.Key] = kv.Value;
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
                var flags = string.Join(" ", options.Words());
                foreach (var g in _ware)
                {
                    // Kept for the game when it differs from what it runs with now.
                    if (MelonDsSessionStore.Load(g.InstallDir, g.GameId) != flags && g.Options.NoRamDisk != options.NoRamDisk
                        || MelonDsSessionStore.Load(g.InstallDir, g.GameId) == null && MelonDsCommandLine.Carries(g.Own, MelonDsCommandLine.NoRamDiskFlag))
                        MelonDsSessionStore.Save(g.InstallDir, g.GameId, flags);
                    // A flag of ours still on the game's OWN line moves here: taken off the line.
                    if (!g.LineIsOurs || string.IsNullOrWhiteSpace(g.Own) || !MelonDsCommandLine.Carries(g.Own, MelonDsCommandLine.NoRamDiskFlag)) continue;
                    var line = MelonDsCommandLine.Strip(g.Own);
                    try { g.Game.CommandLine = line; lines++; Log.Info("options of " + g.Title + ": " + MelonDsCommandLine.NoRamDiskFlag + " moved off the line - \"" + g.Own + "\" -> \"" + line + "\""); }
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
