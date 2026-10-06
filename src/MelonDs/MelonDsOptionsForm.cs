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
// SEVERAL GAMES - the pack's rule (Mehdi, 06/10; LbipSelection, first in the ares window): at the top,
// "All selected games (N)", the default, then the games grouped by identical options ("Mario Kart DS <and 3
// others> - RAM disk, its own video, melonDS's firmware") to start from that group's. "All selected games"
// shows a setting they agree on as it is, one they do not as "Various (n of N)" with the purple bar - a first
// item of a list; a box filled, its name saying so; an empty text box whose cue says so; for the session, no
// choice ticked and a line under the title. Left so, each game keeps its own; changed, it goes to all. OK asks
// first, then each game gets only what the user changed (LbipGameEdit) - the session choice the DSiWare ones
// only, each keeping the rest of its own command line. Nothing in this window is about one game only but its
// title, which goes for several.

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
            internal Dictionary<string, string> Stored;    // the settings as the store has them (LbipGameEdit), read by the window
        }

        internal static string SettingsKey(Dictionary<string, string> settings) => MelonDsGameSettings.Format(settings);

        // The window's keys besides the settings' ids (MelonDsGameSettings.Settings): the session, the text set by hand.
        private const string SessionKey = "\u0001session", HandOnKey = "\u0001handOn", HandTextKey = "\u0001handText";
        private const string WareMark = "\u0001dsiware";   // groups a DSiWare title apart from a cartridge, as before - never shown
        private const string Various = LbipSelection.Various;

        private readonly List<Entry> _games;
        private readonly List<Entry> _ware;
        private readonly LbipSelection _sel;
        private readonly List<string> _keys;
        private readonly Dictionary<string, string> _global;

        private RadioButton _ram, _disk;
        private Label _sessionVariousNote;
        private bool _sessionVarious;
        private CheckBox _threaded, _useGl, _vsync, _better, _hires, _intervalOverride;
        private ComboBox _renderer;
        private CheckBox _fwOverwrite;
        private MelonDsFirmwareFields _firmware;
        private CheckBox _handOn;
        private TextBox _handText;
        private Label _handStatus, _videoHandNote, _fwHandNote;
        private string _keptHand;            // the text set by hand while not shown - Various when the games' differ
        private bool _handTextVarious;       // the box shows the games' different texts: empty, its cue says so
        private bool _handLoading;
        private ComboBox _scale;
        private NumericUpDown _interval;
        private bool _loading;
        private readonly OptionMarks _marks = new OptionMarks();
        private readonly HashSet<Control> _various = new HashSet<Control>();   // a box shown Various

        public MelonDsOptionsForm(List<Entry> games)
        {
            _games = games;
            _ware = games.Where(g => g.IsDSiWare).ToList();
            foreach (var g in games) g.Stored = MelonDsGameSettings.Load(g.InstallDir, g.GameId, asStored: true);
            _keys = MelonDsGameSettings.Settings.Select(s => s.Id).Concat(new[] { SessionKey, HandOnKey, HandTextKey }).ToList();
            _sel = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = g.Title, Stored = ValuesOf(g, mark: true), Tag = g }), StringComparer.Ordinal);
            var exe = games.Select(g => g.Exe).FirstOrDefault(e => !string.IsNullOrEmpty(e));
            // As melonDS has them - a value out of our lists shown as it is, not as our default (only shown, never written).
            _global = MelonDsGameSettings.Current(exe == null ? null : MelonDsPaths.Resolve(exe)?.ConfigFile, asWritten: true);

            Text = "Nixx-melonDS - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(580, 588);

            // ── the top: the game - or, several, where to start from (LbipSelection)
            var top = _sel.Top(_keys, m => Describe((Entry)m.Tag), Picked);
            if (top == null)
            {
                top = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 10, 12, 0) };
                top.Controls.Add(new Label { AutoSize = false, Dock = DockStyle.Top, Height = 20, UseMnemonic = false, Text = games[0].Title });
            }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            if (_ware.Count > 0) tabs.TabPages.Add(SessionTab());
            tabs.TabPages.Add(VideoTab());
            tabs.TabPages.Add(FirmwareTab());
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
            // What the two tabs above override, shown as it is when the tab is looked at.
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == advanced && !HandOn) ShowGenerated(); };

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            // Where each value comes from, said by colour - see OptionMarks; purple, several games that do not agree.
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("melonDS", gameConfig: false, hereLoses: false, various: _sel.Several),
                                            OptionMarks.ResetButton(ResetDefaults), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_sel.Source != null) _sel.Source.SelectedIndex = 0;
            else Fill(_sel.From(_sel.Members[0], _keys));
            // Dressed as the Nixx window (Mehdi, 05/10): LiteBox's look, the tabs a page bar at the left, each page's title
            // above it - and each long explanation one short sentence, the whole of it on hover (LbipHint).
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        /// <summary>A game's values as the window shows them: its settings, its session (DSiWare), its text set by hand.
        /// <paramref name="mark"/>: with the DSiWare mark, for grouping - else the settings as STORED, for a save to keep
        /// what the window does not show (LbipGameEdit).</summary>
        private static Dictionary<string, string> ValuesOf(Entry e, bool mark)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in (mark ? e.Settings : e.Stored) ?? new Dictionary<string, string>()) d[kv.Key] = kv.Value;
            // A game's own firmware always runs with the override on (MelonDsGameSettings.Save): said so, the box shows it.
            if (MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Firmware)) d[MelonDsGameSettings.OverrideId] = "true";
            if (e.IsDSiWare && e.Options.NoRamDisk) d[SessionKey] = "disk";
            var hand = Norm(e.Advanced);
            if (hand != null) d[HandTextKey] = hand;
            if (hand != null && e.AdvancedOn) d[HandOnKey] = "true";
            if (mark && e.IsDSiWare) d[WareMark] = "1";
            return d;
        }

        /// <summary>A text set by hand as compared and kept: CR LF, trimmed, null when empty.</summary>
        private static string Norm(string text) => string.IsNullOrWhiteSpace(text) ? null : Lines(text).Trim();

        private static string Describe(Entry e)
            => (e.IsDSiWare ? (e.Options.NoRamDisk ? "disk" : "RAM disk") + ", " : "")
               + (MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Video) ? "its own video" : "melonDS's video")
               + (MelonDsGameSettings.Has(e.Settings, MelonDsGameSettings.Firmware) ? ", its own firmware" : ", melonDS's firmware");

        /// <summary>The top's choice: All selected games, or a group. The session is the DSiWare titles' alone: All - or a
        /// group of cartridges - shows it as they have it, the cartridges left out.</summary>
        private void Picked(Dictionary<string, string> shown)
        {
            int at = _sel.Source?.SelectedIndex ?? 0;
            var from = at <= 0 ? null : (Entry)_sel.Groups[at - 1][0].Tag;
            if (_ware.Count > 0 && (from == null || !from.IsDSiWare))
            {
                var values = _ware.Select(w => w.Options.NoRamDisk).Distinct().ToList();
                shown[SessionKey] = values.Count > 1 ? Various : values[0] ? "disk" : null;
            }
            Fill(shown);
        }

        /// <summary>"Various (n of N)": n the games with a value of their own - for the session, of the DSiWare titles.</summary>
        private string VariousText(string key)
            => key == SessionKey ? "Various (" + _ware.Count(w => w.Options.NoRamDisk) + " of " + _ware.Count + ")" : _sel.VariousText(key);

        // ── Session ──────────────────────────────────────────────────────────

        private TabPage SessionTab()
        {
            var page = new TabPage("Session") { UseVisualStyleBackColor = true };
            // One card, as the Nixx window's pages (Mehdi, 05/10): the choice, and a line under each part of it.
            var card = new GroupBox { Text = "Working NAND", Location = new Point(12, 6), Size = new Size(532, 40) };
            page.Controls.Add(card);
            int y = 24;
            // Placed, then the next one under it: a note that wraps (a long reason) pushes the rest down.
            void Add(Control c, int x, int gap = 6)
            {
                c.Location = new Point(x, y);
                card.Controls.Add(c);
                y += Math.Max(c.PreferredSize.Height, 16) + gap;
            }

            var whereFull = "Where the working NAND of " + (_ware.Count == 1 ? "this DSiWare title" : "the " + _ware.Count + " DSiWare titles of the selection")
                          + " lives while it plays. Its save is kept either way.";
            Add(LbipHint.Note("Where the DSiWare NAND lives while it plays; its save is kept either way.", whereFull, 500, Padding.Empty), 12, 10);
            // The titles do not agree: neither choice ticked, and this line - only when they can disagree.
            if (_ware.Count > 1)
            {
                _sessionVariousNote = new Label { AutoSize = true, ForeColor = OptionMarks.VariousColor, Visible = false,
                                                  Text = VariousText(SessionKey) + ": each title keeps its own." };
                Add(_sessionVariousNote, 18, 6);
            }

            string why = null;
            try
            {
                var exe = _ware.Select(g => g.Exe).FirstOrDefault(e => !string.IsNullOrEmpty(e));
                var layout = exe == null ? null : MelonDsPaths.Resolve(exe);
                if (layout == null || !MelonDsRamDisk.WillTry(layout, noRamDisk: false)) why = MelonDsRamDisk.NotReadyReason();
            }
            catch (Exception ex) { why = ex.Message; }

            _ram = new RadioButton { AutoSize = true, Text = "RAM disk - faster, nothing written to the disk while playing (the default)", Enabled = why == null };
            Add(_ram, 18, 2);
            Add(why != null
                ? new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.Firebrick, Text = "Not available: " + why + ". These titles play on the disk." }
                : new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Text = "Needs about 300 MB of free memory; without it, the disk is used." }, 36, 10);
            _disk = new RadioButton { AutoSize = true, Text = "Disk - survives a crash of the machine mid-game" };
            Add(_disk, 18, 12);
            LbipHint.Attach(_ram, whereFull, _disk);
            EventHandler picked = (_, _) =>
            {
                if (_loading) return;
                if (_sessionVarious) { _sessionVarious = false; if (_sessionVariousNote != null) _sessionVariousNote.Visible = false; }
                RefreshMarks();
            };
            _ram.CheckedChanged += picked;
            _disk.CheckedChanged += picked;
            // KEPT BY THIS PLUGIN, NOT IN THE GAME'S COMMAND LINE (Mehdi, 29/09): that line is the game's
            // default emulator's - kept here, the choice holds whichever emulator of ours runs it.
            Add(LbipHint.Note("Kept by this plugin for the game; its command line is not changed.",
                "Kept by this plugin for the game and applied at launch - whichever emulator of ours runs it; "
                + "the game's command line is not changed (a " + MelonDsCommandLine.NoRamDiskFlag + " left on it is moved here).", 500, Padding.Empty), 12, 8);
            card.Height = y;
            if (_games.Count > _ware.Count)
                page.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText, Location = new Point(14, card.Bottom + 8),
                                              Text = (_games.Count - _ware.Count) + " cartridge game(s) of the selection have no session option and keep theirs." });
            return page;
        }

        private void ShowSession(string value)
        {
            if (_ram == null) return;
            _sessionVarious = value == Various && _ware.Count > 1;
            if (_sessionVariousNote != null) _sessionVariousNote.Visible = _sessionVarious;
            if (_sessionVarious) { _ram.Checked = false; _disk.Checked = false; }
            else if (!_ram.Enabled || value == "disk") _disk.Checked = true;
            else _ram.Checked = true;
        }

        private string ShownSession() => _ram == null ? null : _sessionVarious ? Various : _disk.Checked ? "disk" : null;

        // ── Video ────────────────────────────────────────────────────────────

        private static readonly string[] Renderers = { "Software", "OpenGL (Classic)", "OpenGL (Compute shader)" };

        private TabPage VideoTab()
        {
            var page = new TabPage("Video") { UseVisualStyleBackColor = true };
            var intro = LbipHint.Note((_games.Count == 1 ? "For this game only" : "For these games only") + "; left on default, melonDS's own setting applies.",
                "A <Default> entry, a filled box or an untouched \"Override default\" leaves the setting as melonDS has it; only "
                + "what is set here is written, for the game's session only - melonDS's own come back when it quits.", 520);
            intro.Location = new Point(12, 8);
            page.Controls.Add(intro);

            var display = new GroupBox { Text = "Display settings", Location = new Point(12, 34), Size = new Size(250, 232) };
            display.Controls.Add(new Label { Text = "3D renderer:", AutoSize = true, Location = new Point(12, 24) });
            _renderer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 44), Width = 226 };
            _useGl = Box("OpenGL display", new Point(12, 80));
            _vsync = Box("VSync", new Point(12, 106));
            display.Controls.Add(new Label { Text = "VSync interval:", AutoSize = true, Location = new Point(12, 140) });
            _intervalOverride = new CheckBox { Text = "Override default", Tag = "Override default", AutoSize = true, Location = new Point(120, 138) };
            _interval = new NumericUpDown { Minimum = 1, Maximum = 20, Width = 50, Location = new Point(14, 164) };
            display.Controls.AddRange(new Control[] { _renderer, _useGl, _vsync, _intervalOverride, _interval });

            var softBox = new GroupBox { Text = "Software renderer", Location = new Point(276, 34), Size = new Size(268, 56) };
            _threaded = Box("Use separate thread", new Point(12, 24));
            softBox.Controls.Add(_threaded);

            var glBox = new GroupBox { Text = "OpenGL renderer", Location = new Point(276, 98), Size = new Size(268, 168) };
            glBox.Controls.Add(new Label { Text = "Internal resolution:", AutoSize = true, Location = new Point(12, 24) });
            _scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 46), Width = 224 };
            // Two lines each: "(default: ...)" would not fit beside the name.
            _better = Box("Improved polygon splitting", new Point(12, 80));
            _better.AutoSize = false; _better.Size = new Size(230, 36);
            _hires = Box("Use high resolution coordinates", new Point(12, 118));
            _hires.AutoSize = false; _hires.Size = new Size(230, 36);
            glBox.Controls.AddRange(new Control[] { _scale, _better, _hires });

            page.Controls.AddRange(new Control[] { display, softBox, glBox });
            _videoHandNote = new Label { AutoSize = false, Location = new Point(12, 274), Size = new Size(532, 34), ForeColor = Color.Firebrick, Visible = false,
                                         Text = HandNoteText };
            page.Controls.Add(_videoHandNote);

            foreach (var cb in new[] { _useGl, _vsync, _threaded, _better, _hires })
                cb.CheckStateChanged += (_, _) => { if (_loading) return; _various.Remove(cb); RefreshEnabled(); };
            EventHandler refresh = (_, _) => { if (!_loading) RefreshEnabled(); };
            _renderer.SelectedIndexChanged += refresh;
            _scale.SelectedIndexChanged += refresh;
            _intervalOverride.Click += LeaveVarious;
            _intervalOverride.CheckStateChanged += (_, _) =>
            {
                if (_loading) return;
                if (_intervalOverride.CheckState != CheckState.Checked) _interval.Value = Interval(Default("VSyncInterval"));
                RefreshEnabled();
            };
            return page;
        }

        // Under Video and Firmware while the Advanced page's text is the one in use.
        private const string HandNoteText = "Set by hand on the Advanced page - untick \"Edit by hand\" there to use this page again.";

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

        /// <summary>A list: "Various (n of N)" first when the games do not agree, then the default, then the choices.</summary>
        private void FillList(ComboBox box, string id, IEnumerable<(string Value, string Text)> choices, string defaultText, string own)
        {
            box.Items.Clear();
            if (own == Various) box.Items.Add(new Item { Value = Various, Text = VariousText(id) });
            box.Items.Add(new Item { Value = null, Text = string.IsNullOrEmpty(defaultText) ? "<Default>" : "<Default : " + defaultText + ">" });
            foreach (var c in choices) box.Items.Add(new Item { Value = c.Value, Text = c.Text });
            if (own == null || own == Various) { box.SelectedIndex = 0; return; }
            var at = box.Items.Cast<Item>().ToList().FindIndex(i => i.Value == own);
            if (at < 0) { box.Items.Add(new Item { Value = own, Text = own }); at = box.Items.Count - 1; }
            box.SelectedIndex = at;
        }

        private static string Chosen(ComboBox box) => (box.SelectedItem as Item)?.Value;

        private string OfBox(CheckBox box)
            => _various.Contains(box) ? Various : box.CheckState == CheckState.Indeterminate ? null : box.Checked ? "true" : "false";

        /// <summary>A three-state box: filled for the default - and for Various, its name saying so.</summary>
        private void SetBox(CheckBox box, string own)
        {
            if (own == Various) _various.Add(box); else _various.Remove(box);
            box.CheckState = own == null || own == Various ? CheckState.Indeterminate : own == "true" ? CheckState.Checked : CheckState.Unchecked;
        }

        /// <summary>A two-state box: null for Various - filled, and not ticked by a click but by <see cref="LeaveVarious"/>.</summary>
        private void SetTwoState(CheckBox box, bool? on)
        {
            if (on == null) _various.Add(box); else _various.Remove(box);
            box.AutoCheck = on != null;
            box.CheckState = on == null ? CheckState.Indeterminate : on.Value ? CheckState.Checked : CheckState.Unchecked;
        }

        /// <summary>A two-state box shown Various, clicked: ticked (WinForms would untick it - Indeterminate counts as checked).</summary>
        private void LeaveVarious(object sender, EventArgs e)
        {
            var box = (CheckBox)sender;
            if (!_various.Remove(box)) return;
            box.AutoCheck = true;
            box.CheckState = CheckState.Checked;
        }

        /// <summary>A setting's value for the game: its own, else melonDS's; null for Various.</summary>
        private string Effective(string id, string own) => own == Various ? null : own ?? Default(id);

        private bool HandOn => _handOn != null && _handOn.CheckState == CheckState.Checked;
        private bool FwOn => _fwOverwrite.CheckState == CheckState.Checked && !_various.Contains(_fwOverwrite);
        private bool FwVarious => _various.Contains(_fwOverwrite);

        /// <summary>A box's name, and " - Various (n of N)" while it is shown so.</summary>
        private void SayName(CheckBox box, string key, string text = null)
            => box.Text = (text ?? (string)box.Tag) + (_various.Contains(box) ? " - " + VariousText(key) : "");

        // ── Firmware ─────────────────────────────────────────────────────────

        private TabPage FirmwareTab()
        {
            var page = new TabPage("Firmware") { UseVisualStyleBackColor = true };
            var fwText = "Overwrite melonDS's firmware settings for " + (_games.Count == 1 ? "this game" : "these games");
            _fwOverwrite = new CheckBox { AutoSize = true, Location = new Point(12, 8), Text = fwText, Tag = fwText };
            page.Controls.Add(_fwOverwrite);
            var fwFull = "melonDS then overrides the console's own settings with these, for the game's session only. "
                       + (_ware.Count > 0 ? "A DSiWare title's save keeps the console's own: the game sees these only while this is ticked. " : "")
                       + "Unticked, the game runs on melonDS's settings, shown below.";
            var fwNote = LbipHint.Note("For the game's session only; unticked, melonDS's settings below apply.", fwFull, 510);
            fwNote.Location = new Point(30, 32);
            page.Controls.Add(fwNote);
            LbipHint.Attach(_fwOverwrite, fwFull);
            _firmware = new MelonDsFirmwareFields { Location = new Point(12, 58) };
            _firmware.Changed += (_, _) => { if (!_loading) RefreshMarks(); };
            page.Controls.Add(_firmware);
            _fwHandNote = new Label { AutoSize = false, Location = new Point(12, 328), Size = new Size(532, 34), ForeColor = Color.Firebrick, Visible = false,
                                      Text = HandNoteText };
            page.Controls.Add(_fwHandNote);
            _fwOverwrite.Click += LeaveVarious;
            _fwOverwrite.CheckStateChanged += (_, _) =>
            {
                if (_loading) return;
                if (_fwOverwrite.CheckState != CheckState.Checked) _firmware.ShowValues(_global);
                _firmware.SetEditable(FwOn);
                RefreshEnabled();
            };
            return page;
        }

        /// <summary>What melonDS's own window does, by the values the game will RUN on (its own, else
        /// melonDS's): the GL options only for a GL renderer, the thread only for the software one, a GL
        /// renderer always on a GL display, VSync only on a GL display - and nothing at all while the
        /// Advanced tab's text is in use. A box on default says which: "VSync   (default: off)". A setting
        /// the games do not agree on leaves what depends on it open.</summary>
        private void RefreshEnabled()
        {
            bool on = !HandOn;
            bool rendererVarious = Chosen(_renderer) == Various;
            bool gl = !rendererVarious && Effective("Renderer", Chosen(_renderer)) is string r && r != "0";
            // melonDS runs a GL renderer on a GL display, whatever the box says: set so, it is said so.
            if (on && gl && Chosen(_renderer) != null && Effective("UseGL", OfBox(_useGl)) != "true")
            { _loading = true; _various.Remove(_useGl); _useGl.CheckState = CheckState.Checked; _loading = false; }
            bool glDisplay = gl || rendererVarious || OfBox(_useGl) == Various || Effective("UseGL", OfBox(_useGl)) == "true";
            _renderer.Enabled = on;
            _useGl.Enabled = on && !gl;
            _vsync.Enabled = on && glDisplay;
            _intervalOverride.Enabled = on && glDisplay && (OfBox(_vsync) == Various || Effective("VSync", OfBox(_vsync)) == "true");
            _interval.Enabled = _intervalOverride.Enabled && _intervalOverride.CheckState == CheckState.Checked && !_various.Contains(_intervalOverride);
            _threaded.Enabled = on && !gl;
            _scale.Enabled = _better.Enabled = _hires.Enabled = on && (gl || rendererVarious);

            foreach (var (cb, id) in new[] { (_useGl, "UseGL"), (_vsync, "VSync"), (_threaded, "Threaded"), (_better, "BetterPolygons"), (_hires, "HiresCoordinates") })
            {
                var d = Default(id);
                if (_various.Contains(cb)) SayName(cb, id);
                else cb.Text = cb.CheckState != CheckState.Indeterminate ? (string)cb.Tag
                             : (string)cb.Tag + "   (default: " + (d == "true" ? "on" : d == "false" ? "off" : "not set") + ")";
            }
            // Short where room is short: the interval's box sits beside its label, "Edit by hand" beside the Preview button.
            _intervalOverride.Text = _various.Contains(_intervalOverride) ? VariousText("VSyncInterval") : (string)_intervalOverride.Tag;
            SayName(_fwOverwrite, MelonDsGameSettings.OverrideId);
            if (_handOn != null) SayName(_handOn, HandOnKey, _various.Contains(_handOn) ? "Edit by hand" : null);
            RefreshMarks();
        }

        /// <summary>Each setting's bar: set here, else melonDS's - it has no per-game config of its own - and purple for
        /// a setting the selected games do not agree on. A text set by hand in use: Video and Firmware are not used,
        /// so not marked. See OptionMarks.</summary>
        private void RefreshMarks()
        {
            if (_fwOverwrite == null) return;
            bool on = !HandOn;
            OptionLevel Of(bool here) => !on ? OptionLevel.Unused : here ? OptionLevel.Here : OptionLevel.Emulator;
            OptionLevel Value(string v) => !on ? OptionLevel.Unused : v == Various ? OptionLevel.Various : Of(v != null);
            _marks.Set(_renderer, Value(Chosen(_renderer)));
            _marks.Set(_scale, Value(Chosen(_scale)));
            foreach (var cb in new[] { _useGl, _vsync, _threaded, _better, _hires }) _marks.Set(cb, Value(OfBox(cb)));
            _marks.Set(_interval, !on ? OptionLevel.Unused : _various.Contains(_intervalOverride) ? OptionLevel.Various : Of(_intervalOverride.CheckState == CheckState.Checked));
            _marks.Set(_fwOverwrite, !on ? OptionLevel.Unused : FwVarious ? OptionLevel.Various : Of(FwOn));
            _marks.Set(_firmware, !on ? OptionLevel.Unused : FwVarious || FwOn && _firmware.AnyVarious ? OptionLevel.Various : Of(FwOn));
            _marks.Set(_handOn, _various.Contains(_handOn) ? OptionLevel.Various : OptionLevel.Unused);
            // The session: the disk when the RAM disk could be had - the default is the RAM disk.
            if (_disk != null)
            {
                _marks.Set(_ram, _sessionVarious ? OptionLevel.Various : _disk.Checked ? OptionLevel.Unused : OptionLevel.Emulator);
                _marks.Set(_disk, _sessionVarious || !_disk.Checked ? OptionLevel.Unused : _ram.Enabled ? OptionLevel.Here : OptionLevel.Emulator);
            }
        }

        /// <summary>"Reset to defaults": every setting on default - the RAM disk, melonDS's video and firmware -
        /// and the text set by hand off and forgotten, for every selected game; nothing saved before OK.</summary>
        private void ResetDefaults()
        {
            _loading = true;
            if (_ram != null && _ram.Enabled) ShowSession(null);
            SetTwoState(_fwOverwrite, false);
            _loading = false;
            ShowVideo(null);
            _firmware.ShowValues(_global);
            _firmware.SetEditable(false);
            _handLoading = true;
            SetTwoState(_handOn, false);
            _keptHand = null;
            HandTextVarious(false);
            _handLoading = false;
            ShowGenerated();
        }

        /// <summary>melonDS's values as the defaults, and the game's own over them - Various where the games differ.</summary>
        private void ShowVideo(Dictionary<string, string> own)
        {
            bool was = _loading;
            _loading = true;
            string Own(string id) => own != null && own.TryGetValue(id, out var x) ? x : null;
            FillList(_renderer, "Renderer", Renderers.Select((n, i) => (i.ToString(CultureInfo.InvariantCulture), n)), RendererName(Default("Renderer")), Own("Renderer"));
            FillList(_scale, "ScaleFactor", Enumerable.Range(1, 16).Select(i => (i.ToString(CultureInfo.InvariantCulture), ScaleName(i.ToString(CultureInfo.InvariantCulture)))),
                     ScaleName(Default("ScaleFactor")), Own("ScaleFactor"));
            SetBox(_threaded, Own("Threaded"));
            SetBox(_better, Own("BetterPolygons"));
            SetBox(_hires, Own("HiresCoordinates"));
            SetBox(_useGl, Own("UseGL"));
            SetBox(_vsync, Own("VSync"));
            var interval = Own("VSyncInterval");
            SetTwoState(_intervalOverride, interval == Various ? (bool?)null : interval != null);
            _interval.Value = Interval(interval == Various ? Default("VSyncInterval") : interval ?? Default("VSyncInterval"));
            _loading = was;
            RefreshEnabled();
        }

        /// <summary>Only what is not on default - Various for what the games do not agree on and the user left so.</summary>
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
            if (_various.Contains(_intervalOverride)) v["VSyncInterval"] = Various;
            else if (_intervalOverride.CheckState == CheckState.Checked) v["VSyncInterval"] = ((int)_interval.Value).ToString(CultureInfo.InvariantCulture);
            return v;
        }

        // ── the source ───────────────────────────────────────────────────────

        /// <summary>Fill the window from a set of values (LbipSelection: All selected games, a group, the one game).</summary>
        private void Fill(Dictionary<string, string> shown)
        {
            string Get(string key) => shown.TryGetValue(key, out var v) ? v : null;
            _loading = true;
            ShowSession(Get(SessionKey));
            var fw = Get(MelonDsGameSettings.OverrideId);
            SetTwoState(_fwOverwrite, fw == Various ? (bool?)null : fw == "true");
            _loading = false;
            ShowVideo(shown);
            _firmware.ShowValues(fw == null ? _global : shown, VariousText);
            _firmware.SetEditable(fw == "true");
            _handLoading = true;
            var handOn = Get(HandOnKey);
            _keptHand = Get(HandTextKey);
            SetTwoState(_handOn, handOn == Various ? (bool?)null : handOn == "true" && _keptHand != null);
            HandTextVarious(false);
            if (HandOn) ShowHand();
            else _handText.Text = Lines(Generated());
            _handLoading = false;
            HandChanged();
            _atOpen = ShownValues();
        }

        // What the window showed once filled from the source - see LbipGameEdit.
        private Dictionary<string, string> _atOpen;

        /// <summary>What the window shows, every key of ours present - null for one not set, Various for one the games
        /// do not agree on and the user left so - for LbipSelection.MergeFor.</summary>
        private Dictionary<string, string> ShownValues()
        {
            var shown = TabValues(various: true);
            foreach (var s in MelonDsGameSettings.Settings) if (!shown.ContainsKey(s.Id)) shown[s.Id] = null;
            if (_ram != null) shown[SessionKey] = ShownSession();
            shown[HandOnKey] = _various.Contains(_handOn) ? Various : HandOn ? "true" : null;
            shown[HandTextKey] = HandOn ? (_handTextVarious ? Various : Norm(_handText.Text)) : _keptHand == Various ? Various : Norm(_keptHand);
            return shown;
        }

        // ── Advanced ─────────────────────────────────────────────────────────

        private TabPage AdvancedTab()
        {
            var page = new TabPage("Advanced") { UseVisualStyleBackColor = true };
            var full = "Write ONLY the keys you want to change: [table], then key = value - true/false, a number, 'text' or [a, b]. "
                     + "Any key of melonDS.toml, in any table.\n"
                     + "- Only these keys are written into melonDS.toml, for this game's sessions; the rest of the file is not touched.\n"
                     + "- melonDS's own values come back when it quits, and a key that was not there is taken out again.\n"
                     + "- Left out: the keys this plugin sets for every launch (console, boot, NAND, BIOS, save folders).";
            var intro = LbipHint.Note("Only the keys to change, as melonDS.toml has them: [table], then key = value.", full, 532);
            intro.Location = new Point(12, 6);
            page.Controls.Add(intro);
            var handText = "Edit by hand (Video and Firmware are then not used)";
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 32), Text = handText, Tag = handText };
            page.Controls.Add(_handOn);
            var preview = new Button { Text = "Preview result...", AutoSize = true };
            preview.Location = new Point(544 - preview.PreferredSize.Width, 28);
            preview.Click += (_, _) => PreviewResult();
            page.Controls.Add(preview);
            _handText = new TextBox
            {
                Location = new Point(12, 60), Size = new Size(532, 270), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 9f), ReadOnly = true,
            };
            LbipHint.Attach(_handText, full, _handOn);
            page.Controls.Add(_handText);
            _handStatus = new Label { AutoSize = false, Location = new Point(12, 336), Size = new Size(532, 66) };
            page.Controls.Add(_handStatus);

            _handOn.Click += LeaveVarious;
            _handOn.CheckStateChanged += (_, _) =>
            {
                if (_handLoading) return;
                _handLoading = true;
                if (HandOn) ShowHand();
                else
                {
                    _keptHand = _handTextVarious ? Various : _handText.Text;
                    HandTextVarious(false);
                    _handText.Text = Lines(Generated());
                }
                _handLoading = false;
                HandChanged();
            };
            _handText.TextChanged += (_, _) =>
            {
                if (_handLoading) return;
                HandTextVarious(false);
                HandChanged();
            };
            return page;
        }

        /// <summary>The text set by hand, "Edit by hand" ticked: the one kept - empty, its cue saying so, when the games'
        /// texts differ - else what the tabs set.</summary>
        private void ShowHand()
        {
            HandTextVarious(_keptHand == Various);
            _handText.Text = _keptHand == Various ? "" : Lines(_keptHand ?? Generated());
        }

        private void HandTextVarious(bool on)
        {
            _handTextVarious = on;
            _handText.PlaceholderText = on ? VariousText(HandTextKey) + " - each keeps its own until you type here" : "";
        }

        /// <summary>melonDS.toml as this game's next session will have it - what is shown, set by hand or
        /// not - in a window; nothing is written.</summary>
        private void PreviewResult()
        {
            List<MelonDsGameSettings.Raw> keys;
            if (HandOn)
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

        /// <summary>The fragment of what the Video and Firmware tabs set now - what varies left out.</summary>
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
            bool on = HandOn;
            _handText.ReadOnly = !on;
            // Read-only said by its text, not its background: the dark theme maps a text colour each time it is set, a background
            // only once (NixxTheme) - a background set here later would bring Windows' white back.
            _handText.ForeColor = on ? SystemColors.WindowText : SystemColors.GrayText;
            RefreshEnabled();
            _fwOverwrite.Enabled = !on;
            _firmware.SetEditable(!on && FwOn);
            _videoHandNote.Visible = _fwHandNote.Visible = on;
            RefreshMarks();

            if (!on)
            {
                _handStatus.ForeColor = SystemColors.GrayText;
                _handStatus.Text = _various.Contains(_handOn) ? "Each game keeps its own." : "Generated from the Video and Firmware pages.";
                return;
            }
            if (_handTextVarious) { _handStatus.ForeColor = SystemColors.GrayText; _handStatus.Text = "Each game keeps its own text."; return; }
            var warnings = MelonDsGameSettings.CheckHand(Layout0(), _handText.Text, out var error);
            _handStatus.ForeColor = error != null ? Color.Firebrick : warnings.Count > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            _handStatus.Text = error != null ? "Not valid: " + error
                             : warnings.Count > 0 ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? "\n(and " + (warnings.Count - 3) + " more)" : "")
                             : "Valid.";
        }

        /// <summary>The game's own set from the tabs: the families whose box is ticked, nothing of the others.
        /// <paramref name="various"/>: what the games do not agree on as Various (for OK) - else left out (a fragment).</summary>
        private Dictionary<string, string> TabValues(bool various = false)
        {
            var own = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in ReadVideo()) if (various || kv.Value != Various) own[kv.Key] = kv.Value;
            if (FwOn)
            {
                foreach (var kv in _firmware.Read())
                {
                    if (kv.Value == Various) { if (various) own[kv.Key] = Various; continue; }
                    var s = MelonDsGameSettings.Settings.First(x => x.Id == kv.Key);
                    own[kv.Key] = MelonDsGameSettings.Normal(s, kv.Value) ?? s.Default;
                }
                own[MelonDsGameSettings.OverrideId] = "true";
            }
            else if (FwVarious && various)
                foreach (var s in MelonDsGameSettings.Of(MelonDsGameSettings.Firmware)) own[s.Id] = Various;
            return own;
        }

        // ── OK ───────────────────────────────────────────────────────────────

        private void Apply()
        {
            var problem = FwOn && !HandOn ? _firmware.Problem() : null;
            if (problem != null) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            // The Advanced tab: a text in use must be usable; what it will not do is said, and asked.
            if (HandOn && !_handTextVarious)
            {
                var warnings = MelonDsGameSettings.CheckHand(Layout0(), _handText.Text, out var handError);
                if (handError != null) { MessageBox.Show(this, "The settings set by hand are not valid: " + handError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (warnings.Count > 0
                    && MessageBox.Show(this, "The settings set by hand:\n\n- " + string.Join("\n- ", warnings) + "\n\nUse them anyway?",
                                       Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            var now = ShownValues();
            if (LbipSelection.Changed(_atOpen, now) && !_sel.Confirm(this, Text)) return;

            int lines = 0, settings = 0;
            if (_ram != null)
            {
                // The session goes to every DSiWare title when changed - a Various left as it is, each keeps its own.
                var picked = now[SessionKey];
                _atOpen.TryGetValue(SessionKey, out var before);
                bool changed = picked != Various && !string.Equals(picked, before, StringComparison.Ordinal);
                foreach (var g in _ware)
                {
                    var options = new MelonDsOptions { NoRamDisk = changed ? picked == "disk" : g.Options.NoRamDisk };
                    var flags = string.Join(" ", options.Words());
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

            // OK changes only what was changed (LbipGameEdit, Mehdi 04/10): a value kept for the game that this window does
            // not show as it is - a key this plugin no longer has, a number out of its range - stays in its store; a Various
            // left as it is, each game keeps its own (LbipSelection.MergeFor - given the settings AS STORED, not as shown).
            foreach (var g in _games)
            {
                var stored = ValuesOf(g, mark: false);
                var merged = _sel.MergeFor(new LbipSelection.Member { Title = g.Title, Stored = stored, Tag = g }, _atOpen, now);

                var chosen = merged.Where(kv => kv.Key != SessionKey && kv.Key != HandOnKey && kv.Key != HandTextKey)
                                   .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                // ValuesOf said the override of a game's own firmware: it is Save's to put, and only with the firmware.
                if (!MelonDsGameSettings.Of(MelonDsGameSettings.Firmware).Any(s => s.Id != MelonDsGameSettings.OverrideId && chosen.ContainsKey(s.Id))
                    && !(g.Stored?.ContainsKey(MelonDsGameSettings.OverrideId) ?? false))
                    chosen.Remove(MelonDsGameSettings.OverrideId);
                var result = chosen.Count > 0 ? chosen : null;
                if (!LbipGameEdit.Same(g.Stored, result))
                {
                    MelonDsGameSettings.Save(g.InstallDir, g.GameId, result);
                    settings++;
                    Log.Info("options of " + g.Title + ": " + (result == null ? "melonDS's settings" : "its own settings - " + SettingsKey(result)));
                }

                // The text set by hand: kept even when not in use, so ticking the box again finds it.
                merged.TryGetValue(HandTextKey, out var hand);
                merged.TryGetValue(HandOnKey, out var handOn);
                bool on = handOn == "true" && hand != null;
                if (string.Equals(hand, Norm(g.Advanced), StringComparison.Ordinal) && on == (g.AdvancedOn && Norm(g.Advanced) != null)) continue;
                MelonDsGameSettings.SaveAdvanced(g.InstallDir, g.GameId, hand, on);
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
