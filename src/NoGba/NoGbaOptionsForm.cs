// The options window of the no$gba plugin, opened from the right-click entry on one game or a selection
// (NoGbaGameMenu): a game's OWN settings, laid over no$gba's for its session (NoGbaGameSettings).
//
// ONE TAB PER FAMILY, and in each only what applies to the games shown (Mehdi, 29/09): a GBA cartridge,
// a DS cartridge and a DSiWare title are not offered the same things - the Link tab is there only for GBA
// games, a GBA setting is marked so when the selection mixes kinds, and each game keeps only what applies
// to its own kind. EVERY SETTING IS ON "DEFAULT" UNTIL SET (Mehdi, 29/09 - as Vita3K's Graphics tab): a
// list starts with "<Default : no$gba's value>", and Game Screen Sizing - two boxes over one key, as no$gba
// shows it - has an "Override default" box beside it. Only what is not on default is kept for the game:
// showing the window changes no game. "Advanced" shows the lines the game changes, and takes the user's own
// instead, "Edit by hand".
//
// SEVERAL GAMES - the pack's rule (LbipSelection, Mehdi 06/10): at the top "All selected games", the default, then
// the groups of games with identical settings. In "All", a setting they do not agree on shows "Various (n of N)" with
// the purple bar - a list's first item; for Game Screen Sizing and "Edit by hand" their box half-ticked; for the text
// set by hand an empty box that says so. Left so, each game keeps its own; changed, it goes to all. OK asks first,
// then writes only what the user changed (LbipGameEdit) - and a setting of another kind than the game's is still not
// kept for it. The agreement is counted over the games a setting APPLIES to (n of N among them, not of the whole
// selection): a GBA setting all GBA games agree on is not "various" because DS games have none. What is about one game
// only - its kind in the title, the "Preview result" of its NO$GBA.INI - is not shown for several.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.NoGba
{
    internal sealed class NoGbaOptionsForm : Form
    {
        internal sealed class Entry
        {
            public IGame Game;
            public string Title, GameId;
            public NoGbaKind Kind;
            public NoGbaLayout Layout;
            public Dictionary<string, string> Own;     // the game's own, null for none
            public string Advanced;                    // the text set by hand, null for none
            public bool AdvancedOn;
        }

        private static string Fragment(Dictionary<string, string> v)
            => v == null ? "" : string.Join(";", v.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));

        private const string SizingKey = "Game Screen Sizing";
        // The text set by hand, kept with the settings for the selection's bookkeeping - never a NO$GBA.INI key.
        private const string HandOnKey = "\u0002hand-on", HandTextKey = "\u0002hand-text";
        private const string Various = LbipSelection.Various;
        private const string NoteText = "Set by hand on the Advanced page - untick \"Edit by hand\" there to use this page again.";
        private const string HandOnText = "Edit by hand (the other pages are then not used)";
        // What the window is, said once under the game's name before (Mehdi, 05/10): now on hover over each page's note.
        private const string LaidOver = "This plugin's settings, laid over NO$GBA.INI only while the game runs.";

        private readonly List<Entry> _games;
        private readonly NoGbaKind _kinds;
        private readonly Dictionary<string, string> _global;
        private readonly LbipSelection _sel;
        private readonly Dictionary<Entry, LbipSelection.Member> _member = new Dictionary<Entry, LbipSelection.Member>();
        private bool Several => _games.Count > 1;

        /// <summary>One family's tab: its box, and a control per setting.</summary>
        private sealed class TabState
        {
            public string Name;
            public Label HandNote;
            public CheckBox SizingOverride;           // Game Screen Sizing's "Override default" - half-ticked: various
            public readonly Dictionary<string, ComboBox> Combos = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
            public CheckBox Step50, Aspect;           // Game Screen Sizing, shown as no$gba shows it
        }

        private readonly List<TabState> _tabs = new List<TabState>();
        private readonly OptionMarks _marks = new OptionMarks();
        private CheckBox _handOn;                     // half-ticked: various
        private TextBox _handText;
        private Label _handStatus;
        private string _keptHand;
        private bool _handTextVarious;                // "All": the games' texts set by hand differ, and nothing typed yet
        private bool _loading;
        private bool _wasHand;                        // "Edit by hand" ticked before its last change

        private bool Hand => _handOn != null && _handOn.CheckState == CheckState.Checked;

        public NoGbaOptionsForm(List<Entry> games)
        {
            _games = games;
            _kinds = games.Aggregate(NoGbaKind.None, (k, g) => k | g.Kind);
            _global = NoGbaGameSettings.DefaultsOf(games.Select(g => g.Layout).FirstOrDefault(l => l != null));
            foreach (var g in games) _member[g] = new LbipSelection.Member { Title = g.Title, Stored = StoredOf(g), Tag = g };
            _sel = new LbipSelection(games.Select(g => _member[g]), StringComparer.OrdinalIgnoreCase);

            Text = "Nixx-no$gba - Options" + (Several ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(580, 548);

            var keys = NoGbaGameSettings.Offered.Where(s => (s.Kinds & _kinds) != 0).Select(s => s.Key).Concat(new[] { HandOnKey, HandTextKey }).ToList();
            var top = _sel.Top(keys, m => Describe((Entry)m.Tag),
                               shown => Show(_sel.Source == null || _sel.Source.SelectedIndex <= 0 ? AllShown(keys) : shown));
            if (top != null)
            {
                // The kinds of the selection, as the title said before: the "(GBA only)" marks of the pages read against it.
                var title = top.Controls.OfType<Label>().FirstOrDefault();
                if (title != null) title.Text += "  (" + string.Join(", ", new[] { NoGbaKind.Gba, NoGbaKind.Ds, NoGbaKind.DsiWare }.Where(k => (_kinds & k) != 0).Select(KindName)) + ")";
            }
            else
            {
                top = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 10, 12, 0) };
                top.Controls.Add(new Label
                {
                    AutoSize = false, Dock = DockStyle.Top, Height = 20, UseMnemonic = false,
                    Text = games[0].Title + "   (" + KindName(games[0].Kind) + ")",
                });
            }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var name in new[] { NoGbaGameSettings.EmulationTab, NoGbaGameSettings.CartridgeTab, NoGbaGameSettings.DisplayTab, NoGbaGameSettings.LinkTab })
            {
                var page = FamilyTab(name);
                if (page != null) tabs.TabPages.Add(page);
            }
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == advanced && !Hand) ShowGenerated(); };

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            // Where each value comes from, said by colour - see OptionMarks.
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("no$gba", gameConfig: false, hereLoses: false, various: Several),
                                            OptionMarks.ResetButton(ResetDefaults), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_sel.Source != null) _sel.Source.SelectedIndex = 0;
            else Show(_sel.From(_sel.Members[0], keys));
            // Dressed as the Nixx window (Mehdi, 05/10): LiteBox's look, the tabs a page bar at the left, each page's title
            // above it - and each long explanation one short sentence, the whole of it on hover (LbipHint).
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        internal static string KindName(NoGbaKind k) => k == NoGbaKind.Gba ? "GBA" : k == NoGbaKind.Ds ? "DS cartridge" : k == NoGbaKind.DsiWare ? "DSiWare" : "?";

        private static string KindsName(NoGbaKind kinds)
            => string.Join(" and ", new[] { NoGbaKind.Gba, NoGbaKind.Ds, NoGbaKind.DsiWare }.Where(k => (kinds & k) != 0).Select(KindName));

        // ── a game's stored values, and the selection's ─────────────────────

        /// <summary>A game's stored values as LbipSelection keeps them: its own settings, and its text set by hand.</summary>
        private static Dictionary<string, string> StoredOf(Entry e)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (e.Own != null) foreach (var kv in e.Own) if (kv.Value != null) d[kv.Key] = kv.Value;
            var text = string.IsNullOrWhiteSpace(e.Advanced) ? null : e.Advanced.Trim();
            if (text != null) d[HandTextKey] = text;
            if (e.AdvancedOn && text != null) d[HandOnKey] = "1";
            return d;
        }

        private static string Describe(Entry e) => e.AdvancedOn ? "set by hand" : e.Own == null || e.Own.Count == 0 ? "no$gba's settings" : "its own settings";

        private static NoGbaGameSettings.Setting SettingOf(string key)
            => NoGbaGameSettings.Offered.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        /// <summary>Does a key apply to a game of this kind: the text set by hand, and a setting of its kind.</summary>
        private static bool Applies(string key, NoGbaKind kind)
        {
            if (key == HandOnKey || key == HandTextKey) return true;
            var s = SettingOf(key);
            return s == null || (s.Kinds & kind) != 0;
        }

        private static string Get(IDictionary<string, string> d, string key) => d != null && d.TryGetValue(key, out var v) ? v : null;

        /// <summary>"All selected games": each key as the games it applies to all have it, else Various. LbipSelection.All
        /// counts every game of the selection - here a GBA setting is not various because the DS games have none.</summary>
        private Dictionary<string, string> AllShown(IEnumerable<string> keys)
        {
            var shown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var values = _games.Where(g => Applies(key, g.Kind)).Select(g => Get(_member[g].Stored, key)).Distinct().ToList();
                shown[key] = values.Count <= 1 ? values.FirstOrDefault() : Various;
            }
            return shown;
        }

        /// <summary>"Various (n of N)", over the games the key applies to - LbipSelection.VariousText's words.</summary>
        private string VariousText(string key)
        {
            var who = _games.Where(g => Applies(key, g.Kind)).ToList();
            return "Various (" + who.Count(g => Get(_member[g].Stored, key) != null) + " of " + who.Count + ")";
        }

        // ── a family's tab ───────────────────────────────────────────────────

        private TabPage FamilyTab(string name)
        {
            var settings = NoGbaGameSettings.Offered.Where(s => s.Tab == name && (s.Kinds & _kinds) != 0).ToList();
            if (settings.Count == 0) return null;
            var state = new TabState { Name = name };
            var page = new TabPage(name) { UseVisualStyleBackColor = true, AutoScroll = true };
            var full = LaidOver + "\n"
                     + "A <Default> entry or an untouched \"Override default\" leaves the setting as no$gba has it. What is set here is "
                     + "written for the game's session only; no$gba's own come back when it quits.";
            var intro = LbipHint.Note((Several ? "For these games only" : "For this game only") + "; left on <Default>, no$gba's own setting applies.", full, 520);
            intro.Location = new Point(12, 8);
            page.Controls.Add(intro);

            // A card per part of the page, as the Nixx window's pages (Mehdi, 05/10) - the settings in no$gba's order.
            int y = 34;
            GroupBox card = null;
            string cardName = null;
            int cy = 0;
            void EndCard() { if (card != null) { card.Height = cy + 4; y = card.Bottom + 10; } }
            foreach (var s in settings)
            {
                var wanted = CardOf(s);
                if (card == null || wanted != cardName)
                {
                    EndCard();
                    cardName = wanted;
                    card = new GroupBox { Text = wanted, Location = new Point(12, y), Size = new Size(520, 40) };
                    page.Controls.Add(card);
                    cy = 22;
                }
                bool partial = (s.Kinds & _kinds) != _kinds;   // the selection holds games it does not apply to
                var label = s.Key + (partial ? "   (" + KindsName(s.Kinds & _kinds) + " only)" : "");
                card.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(14, cy), UseMnemonic = false });
                if (s.Key == SizingKey)
                {
                    // no$gba's own two boxes over its four words: Free, Force 50% step, Force Aspect Ratio, Strict (both) -
                    // one key, so one "Override default" for the pair.
                    state.SizingOverride = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(196, cy - 2) };
                    card.Controls.Add(state.SizingOverride);
                    cy += 22;
                    state.Step50 = new CheckBox { Text = "Force 50% step", AutoSize = true, Location = new Point(20, cy) };
                    state.Aspect = new CheckBox { Text = "Force Aspect Ratio", AutoSize = true, Location = new Point(196, cy) };
                    card.Controls.Add(state.Step50);
                    card.Controls.Add(state.Aspect);
                    state.SizingOverride.CheckStateChanged += (_, _) =>
                    {
                        if (_loading) return;
                        // Clicked out of "various": a plain box again (a click from half-ticked unticks it, as Windows does).
                        if (state.SizingOverride.ThreeState && state.SizingOverride.CheckState != CheckState.Indeterminate)
                        {
                            state.SizingOverride.ThreeState = false;
                            state.SizingOverride.Text = "Override default";
                        }
                        if (state.SizingOverride.CheckState != CheckState.Checked) ShowSizing(state, null);
                        RefreshEnabled();
                        RefreshMarks();
                    };
                    cy += 30;
                    continue;
                }
                cy += 19;
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(20, cy), Width = 330 };
                combo.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshMarks(); };
                card.Controls.Add(combo);
                state.Combos[s.Key] = combo;
                cy += 32;
            }
            EndCard();
            state.HandNote = new Label { AutoSize = false, Location = new Point(12, y - 4), Size = new Size(520, 34), ForeColor = Color.Firebrick, Visible = false, Text = NoteText };
            page.Controls.Add(state.HandNote);
            _tabs.Add(state);
            return page;
        }

        /// <summary>The card a setting sits in on its page: Display &amp; sound split in two, the other pages one card each.</summary>
        private static string CardOf(NoGbaGameSettings.Setting s)
        {
            if (s.Tab == NoGbaGameSettings.DisplayTab) return s.Key.StartsWith("Sound", StringComparison.OrdinalIgnoreCase) || s.Key == "Volume Control" ? "Sound" : "Screen";
            if (s.Tab == NoGbaGameSettings.EmulationTab) return "Speed and video";
            if (s.Tab == NoGbaGameSettings.CartridgeTab) return "Cartridge hardware";
            if (s.Tab == NoGbaGameSettings.LinkTab) return "Multiplayer link";
            return s.Tab;
        }

        /// <summary>Fill a list: "Various (n of N)" first when the games do not agree, then "&lt;Default : no$gba's
        /// value&gt;" ("&lt;Default&gt;" when NO$GBA.INI does not hold the key), then no$gba's choices as its window
        /// shows them - and a value it does not list (a newer no$gba's) as one more, so it is shown and kept.</summary>
        private static void Fill(ComboBox box, string key, string defaultValue, string own, string variousText)
        {
            box.Items.Clear();
            if (own == Various) box.Items.Add(new Choice(Various, variousText));
            box.Items.Add(new Choice(null, string.IsNullOrEmpty(defaultValue) ? "<Default>" : "<Default : " + NoGbaGameSettings.Shown(defaultValue) + ">"));
            var choices = NoGbaGameSettings.Choices.TryGetValue(key, out var c) ? c : new string[0];
            foreach (var raw in choices) box.Items.Add(new Choice(raw));
            if (own == null || own == Various) { box.SelectedIndex = 0; return; }
            int at = Array.IndexOf(choices, own);
            if (at < 0) { box.Items.Add(new Choice(own)); box.SelectedIndex = box.Items.Count - 1; }
            else box.SelectedIndex = at + 1;
        }

        private sealed class Choice
        {
            public readonly string Raw;        // null: the default; Various: the games do not agree
            private readonly string _text;
            public Choice(string raw, string text = null) { Raw = raw; _text = text; }
            public override string ToString() => _text ?? NoGbaGameSettings.Shown(Raw);
        }

        /// <summary>The tab from no$gba's values (the defaults) and the values shown over them (null: default).</summary>
        private void ShowTab(TabState t, Func<string, string> shown)
        {
            string Default(string key) => _global.TryGetValue(key, out var g) ? g : null;
            foreach (var kv in t.Combos)
            {
                var v = shown(kv.Key);
                Fill(kv.Value, kv.Key, Default(kv.Key), v, v == Various ? VariousText(kv.Key) : null);
            }
            if (t.Step50 != null)
            {
                var v = shown(SizingKey);
                var box = t.SizingOverride;
                box.ThreeState = v == Various;
                box.Text = v == Various ? VariousText(SizingKey) : "Override default";
                box.CheckState = v == Various ? CheckState.Indeterminate : v != null ? CheckState.Checked : CheckState.Unchecked;
                ShowSizing(t, v == Various ? null : v);
            }
        }

        private void ShowSizing(TabState t, string own)
        {
            var sizing = own ?? (_global.TryGetValue(SizingKey, out var g) ? g : null) ?? "Strict";   // no$gba's own
            t.Step50.Checked = sizing == "Force 50% step" || sizing == "Strict";
            t.Aspect.Checked = sizing == "Force Aspect Ratio" || sizing == "Strict";
        }

        private static string SizingOf(TabState t)
            => t.Step50.Checked && t.Aspect.Checked ? "Strict" : t.Step50.Checked ? "Force 50% step" : t.Aspect.Checked ? "Force Aspect Ratio" : "Free";

        /// <summary>Only what is set for all - not on default, not various.</summary>
        private static Dictionary<string, string> ReadTab(TabState t)
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in t.Combos)
                if (kv.Value.SelectedItem is Choice c && c.Raw != null && c.Raw != Various) v[kv.Key] = c.Raw;
            if (t.Step50 != null && t.SizingOverride.CheckState == CheckState.Checked) v[SizingKey] = SizingOf(t);
            return v;
        }

        /// <summary>Each setting's bar: set here, else no$gba's - it has no per-game config of its own; purple when the
        /// games do not agree. A text set by hand in use: the tabs are not used, so not marked. See OptionMarks.</summary>
        private void RefreshMarks()
        {
            bool hand = Hand;
            foreach (var t in _tabs)
            {
                foreach (var kv in t.Combos)
                {
                    var raw = (kv.Value.SelectedItem as Choice)?.Raw;
                    _marks.Set(kv.Value, hand ? OptionLevel.Unused : raw == Various ? OptionLevel.Various : raw != null ? OptionLevel.Here : OptionLevel.Emulator);
                }
                if (t.Step50 != null)
                    _marks.Set(t.Step50, hand ? OptionLevel.Unused : t.SizingOverride.CheckState == CheckState.Indeterminate ? OptionLevel.Various
                                       : t.SizingOverride.Checked ? OptionLevel.Here : OptionLevel.Emulator);
            }
            if (_handOn != null)
            {
                _marks.Set(_handOn, _handOn.CheckState == CheckState.Indeterminate ? OptionLevel.Various : OptionLevel.Unused);
                _marks.Set(_handText, Hand && _handTextVarious ? OptionLevel.Various : OptionLevel.Unused);
            }
        }

        /// <summary>"Reset to defaults": every setting on default, the text set by hand off and forgotten -
        /// nothing saved before OK.</summary>
        private void ResetDefaults()
        {
            _loading = true;
            foreach (var t in _tabs) ShowTab(t, _ => null);
            ShowHandOn(null);
            _keptHand = null;
            _handTextVarious = false;
            _loading = false;
            ShowGenerated();
        }

        private void RefreshEnabled()
        {
            bool hand = Hand;
            foreach (var t in _tabs)
            {
                foreach (var c in t.Combos.Values) c.Enabled = !hand;
                if (t.Step50 != null)
                {
                    t.SizingOverride.Enabled = !hand;
                    t.Step50.Enabled = t.Aspect.Enabled = !hand && t.SizingOverride.CheckState == CheckState.Checked;
                }
                t.HandNote.Visible = hand;
            }
        }

        // ── Advanced ─────────────────────────────────────────────────────────

        private TabPage AdvancedTab()
        {
            var page = new TabPage("Advanced") { UseVisualStyleBackColor = true };
            var full = "Write ONLY the lines you want to change: Key == Value, as NO$GBA.INI writes them - any key.\n"
                     + "- A value must be one of no$gba's own, word for word (a leading \"-\" included): it ignores any other IN SILENCE. "
                     + "The check below says when a value is not one of them.\n"
                     + "- Only these lines are written, for this game's sessions; no$gba's own come back when it quits, and a line that was not there is taken out.\n"
                     + "- Left out: the lines this plugin sets for every launch (NDS Mode/Colors, Reset/Startup Entrypoint, SAV/SNA File Format).";
            var intro = LbipHint.Note("Only the lines to change, as NO$GBA.INI has them: Key == Value.", full, 532);
            intro.Location = new Point(12, 6);
            page.Controls.Add(intro);
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 32), Text = HandOnText };
            page.Controls.Add(_handOn);
            // The NO$GBA.INI of one game's session: not offered for several (the pack's rule, Mehdi 06/10).
            if (!Several)
            {
                var preview = new Button { Text = "Preview result...", AutoSize = true };
                preview.Location = new Point(544 - preview.PreferredSize.Width, 28);
                preview.Click += (_, _) => PreviewResult();
                page.Controls.Add(preview);
            }
            _handText = new TextBox
            {
                Location = new Point(12, 60), Size = new Size(532, 236), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 9f), ReadOnly = true,
            };
            LbipHint.Attach(_handText, full, _handOn);
            page.Controls.Add(_handText);
            _handStatus = new Label { AutoSize = false, Location = new Point(12, 302), Size = new Size(532, 60) };
            page.Controls.Add(_handStatus);

            _handOn.CheckStateChanged += (_, _) =>
            {
                if (_loading) return;
                _loading = true;
                // Clicked out of "various": a plain box again (a click from half-ticked unticks it, as Windows does).
                if (_handOn.ThreeState && _handOn.CheckState != CheckState.Indeterminate) { _handOn.ThreeState = false; _handOn.Text = HandOnText; }
                if (Hand != _wasHand)   // half-ticked to unticked: the text shown was already the generated one
                {
                    if (Hand) _handText.Text = _handTextVarious ? "" : Lines(_keptHand ?? Generated());
                    else
                    {
                        if (!_handTextVarious) _keptHand = _handText.Text;
                        _handText.Text = Lines(Generated());
                    }
                    _wasHand = Hand;
                }
                _loading = false;
                HandChanged();
            };
            _handText.TextChanged += (_, _) =>
            {
                if (_loading) return;
                _handTextVarious = false;   // typed: this text goes to all
                HandChanged();
            };
            return page;
        }

        private static string Lines(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        /// <summary>"Edit by hand" as shown: ticked, unticked, or half-ticked "Various (n of N)".</summary>
        private void ShowHandOn(string value)
        {
            _handOn.ThreeState = value == Various;
            _handOn.Text = value == Various ? "Edit by hand: " + VariousText(HandOnKey) : HandOnText;
            _handOn.CheckState = value == Various ? CheckState.Indeterminate : value != null ? CheckState.Checked : CheckState.Unchecked;
            _wasHand = Hand;
        }

        /// <summary>What the family tabs set now, for the game shown.</summary>
        private Dictionary<string, string> TabValues(NoGbaKind kind)
        {
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _tabs)
                foreach (var kv in ReadTab(t))
                {
                    var s = SettingOf(kv.Key);
                    if (s != null && (s.Kinds & kind) != 0) own[kv.Key] = kv.Value;
                }
            return own;
        }

        /// <summary>Every key the window shows for this kind - null for one on its default, Various for one the games do
        /// not agree on - and the text set by hand, for LbipSelection.MergeFor.</summary>
        private Dictionary<string, string> ShownValues(NoGbaKind kind)
        {
            var shown = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _tabs)
            {
                foreach (var kv in t.Combos)
                    if (Applies(kv.Key, kind)) shown[kv.Key] = (kv.Value.SelectedItem as Choice)?.Raw;
                if (t.Step50 != null && Applies(SizingKey, kind))
                    shown[SizingKey] = t.SizingOverride.CheckState == CheckState.Indeterminate ? Various
                                     : t.SizingOverride.CheckState == CheckState.Checked ? SizingOf(t) : null;
            }
            shown[HandOnKey] = _handOn.CheckState == CheckState.Indeterminate ? Various : Hand ? "1" : null;
            var text = Hand ? _handText.Text : _keptHand;
            shown[HandTextKey] = _handTextVarious ? Various : string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            return shown;
        }

        // What the window showed once filled from its source, per kind - see LbipGameEdit.
        private readonly Dictionary<NoGbaKind, Dictionary<string, string>> _atOpen = new Dictionary<NoGbaKind, Dictionary<string, string>>();

        private Entry SourceGame()
            => _sel.Source != null && _sel.Source.SelectedIndex > 0 ? (Entry)_sel.Groups[_sel.Source.SelectedIndex - 1][0].Tag : _games[0];

        private string Generated() => NoGbaGameSettings.Fragment(TabValues(SourceGame().Kind));

        private void ShowGenerated()
        {
            _loading = true;
            _handText.Text = Lines(Generated());
            _loading = false;
            HandChanged();
        }

        private void HandChanged()
        {
            bool on = Hand;
            _handText.ReadOnly = !on;
            // Read-only said by its text, not its background: the dark theme maps a text colour each time it is set, a background
            // only once (NixxTheme) - a background set here later would bring Windows' white back.
            _handText.ForeColor = on ? SystemColors.WindowText : SystemColors.GrayText;
            RefreshEnabled();
            RefreshMarks();
            if (!on)
            {
                _handStatus.ForeColor = SystemColors.GrayText;
                _handStatus.Text = _handOn.CheckState == CheckState.Indeterminate ? "Each game keeps its own." : "Generated from the other pages.";
                return;
            }
            if (_handTextVarious)
            {
                _handStatus.ForeColor = SystemColors.GrayText;
                _handStatus.Text = VariousText(HandTextKey) + ": each game keeps its own text. Type to set one for all.";
                return;
            }
            var warnings = NoGbaGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var error);
            _handStatus.ForeColor = error != null ? Color.Firebrick : warnings.Count > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            _handStatus.Text = error != null ? "Not valid: " + error
                             : warnings.Count > 0 ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? "\n(and " + (warnings.Count - 3) + " more)" : "")
                             : "Valid.";
        }

        private void PreviewResult()
        {
            var g = SourceGame();
            List<KeyValuePair<string, string>> keys;
            if (Hand)
            {
                keys = NoGbaGameSettings.ParseHand(_handText.Text, out var error);
                if (keys == null) { MessageBox.Show(this, "Not valid: " + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            else keys = TabValues(g.Kind).ToList();
            var result = NoGbaGameSettings.Preview(g.Layout, keys, out var before, out var why);
            if (result == null) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var left = keys.Where(kv => NoGbaGameSettings.IsManaged(kv.Key)).Select(kv => kv.Key).ToList();
            ConfigPreviewWindow.Show(this, "NO$GBA.INI - for a session of " + g.Title,
                "NO$GBA.INI as no$gba will read it for the session; it comes back as it is now when no$gba quits."
                + (left.Count > 0 ? " Left out: " + string.Join(", ", left) + "." : ""),
                before, result);
        }

        // ── the source ───────────────────────────────────────────────────────

        /// <summary>The window from the values to show (one game's, a group's, or "All selected games" with Various).</summary>
        private void Show(Dictionary<string, string> shown)
        {
            string At(string key) => Get(shown, key);
            _loading = true;
            foreach (var t in _tabs) ShowTab(t, At);
            _handTextVarious = At(HandTextKey) == Various;
            _keptHand = _handTextVarious ? null : At(HandTextKey);
            ShowHandOn(At(HandOnKey));
            _handText.Text = Hand ? (_handTextVarious ? "" : Lines(_keptHand)) : Lines(Generated());
            _loading = false;
            HandChanged();
            _atOpen.Clear();
            foreach (var kind in _games.Select(g => g.Kind).Distinct()) _atOpen[kind] = ShownValues(kind);
        }

        // ── OK ───────────────────────────────────────────────────────────────

        private void Apply()
        {
            var now = _games.Select(g => g.Kind).Distinct().ToDictionary(k => k, ShownValues);
            if (!now.Any(kv => LbipGameEdit.Changed(_atOpen.TryGetValue(kv.Key, out var b) ? b : null, kv.Value))) { DialogResult = DialogResult.OK; Close(); return; }
            if (Hand && !_handTextVarious)
            {
                var warnings = NoGbaGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var handError);
                if (handError != null) { MessageBox.Show(this, "The settings set by hand are not valid: " + handError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (warnings.Count > 0
                    && MessageBox.Show(this, "The settings set by hand:\n\n- " + string.Join("\n- ", warnings) + "\n\nUse them anyway?",
                                       Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            if (!_sel.Confirm(this, Text)) return;

            int changed = 0;
            foreach (var g in _games)
            {
                // OK changes only what was changed, a Various left as it is nothing (LbipSelection.MergeFor, Mehdi 04/10 and
                // 06/10); a known setting of another kind is not kept.
                var merged = _sel.MergeFor(_member[g], _atOpen.TryGetValue(g.Kind, out var before) ? before : null, now[g.Kind]);
                var handOn = Get(merged, HandOnKey) == "1";
                var handText = Get(merged, HandTextKey);
                merged.Remove(HandOnKey);
                merged.Remove(HandTextKey);
                foreach (var k in merged.Keys.ToList())
                {
                    var s = SettingOf(k);
                    if (s != null && (s.Kinds & g.Kind) == 0) merged.Remove(k);
                }
                var chosen = merged.Count > 0 ? merged : null;
                if (Fragment(g.Own) != Fragment(chosen)) { NoGbaGameSettings.Save(g.Layout, g.GameId, chosen); changed++; }
                // The text set by hand: written only when the user changed it for this game.
                var stored = _member[g].Stored;
                if ((Get(stored, HandOnKey) == "1") != handOn || !string.Equals(Get(stored, HandTextKey), handText, StringComparison.Ordinal))
                { NoGbaGameSettings.SaveAdvanced(g.Layout, g.GameId, handText, handOn); changed++; }
            }
            Log.Info("options window: " + changed + " change(s), of " + _games.Count + " game(s)");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
