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
// A SELECTION THAT DOES NOT AGREE: grouped by identical settings, a combo box names each group and the
// one chosen is what the window starts from; OK applies what is shown to every selected game, after asking.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
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
            public string Key => Fragment(Own) + "|" + (AdvancedOn ? "hand:" : "") + (Advanced ?? "");
        }

        private static string Fragment(Dictionary<string, string> v)
            => v == null ? "" : string.Join(";", v.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));

        private const string SizingKey = "Game Screen Sizing";
        private const string NoteText = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again.";

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private readonly NoGbaKind _kinds;
        private readonly Dictionary<string, string> _global;
        private ComboBox _source;

        /// <summary>One family's tab: its box, and a control per setting.</summary>
        private sealed class TabState
        {
            public string Name;
            public Label HandNote;
            public CheckBox SizingOverride;           // Game Screen Sizing's "Override default"
            public readonly Dictionary<string, ComboBox> Combos = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
            public CheckBox Step50, Aspect;           // Game Screen Sizing, shown as no$gba shows it
        }

        private readonly List<TabState> _tabs = new List<TabState>();
        private CheckBox _handOn;
        private TextBox _handText;
        private Label _handStatus;
        private string _keptHand;
        private bool _loading;

        public NoGbaOptionsForm(List<Entry> games)
        {
            _games = games;
            _groups = games.GroupBy(g => g.Key).Select(g => g.ToList()).ToList();
            _kinds = games.Aggregate(NoGbaKind.None, (k, g) => k | g.Kind);
            _global = NoGbaGameSettings.Current(games.Select(g => g.Layout).FirstOrDefault(l => l != null));

            Text = "Nixx-no$gba - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(580, 520);

            var top = new Panel { Dock = DockStyle.Top, Height = _groups.Count > 1 ? 80 : 52, Padding = new Padding(12, 10, 12, 0) };
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText,
                Text = "This plugin's settings, laid over NO$GBA.INI only while the game runs.",
            });
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20,
                Text = games.Count == 1 ? games[0].Title + "   (" + KindName(games[0].Kind) + ")"
                     : games.Count + " games (" + string.Join(", ", new[] { NoGbaKind.Gba, NoGbaKind.Ds, NoGbaKind.DsiWare }.Where(k => (_kinds & k) != 0).Select(KindName)) + ")"
                       + (_groups.Count > 1 ? " - their settings differ: start from" : ", all with the same settings"),
            });
            if (_groups.Count > 1)
            {
                _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                foreach (var g in _groups)
                    _source.Items.Add(g[0].Title + (g.Count > 1 ? " <and " + (g.Count - 1) + " other" + (g.Count > 2 ? "s" : "") + ">" : "")
                                      + "   -   " + (g[0].AdvancedOn ? "set by hand" : g[0].Own == null ? "no$gba's settings" : "its own settings"));
                _source.SelectedIndexChanged += (_, _) => LoadFrom(_groups[_source.SelectedIndex][0]);
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var name in new[] { NoGbaGameSettings.EmulationTab, NoGbaGameSettings.CartridgeTab, NoGbaGameSettings.DisplayTab, NoGbaGameSettings.LinkTab })
            {
                var page = FamilyTab(name);
                if (page != null) tabs.TabPages.Add(page);
            }
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
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

        internal static string KindName(NoGbaKind k) => k == NoGbaKind.Gba ? "GBA" : k == NoGbaKind.Ds ? "DS cartridge" : k == NoGbaKind.DsiWare ? "DSiWare" : "?";

        private static string KindsName(NoGbaKind kinds)
            => string.Join(" and ", new[] { NoGbaKind.Gba, NoGbaKind.Ds, NoGbaKind.DsiWare }.Where(k => (kinds & k) != 0).Select(KindName));

        // ── a family's tab ───────────────────────────────────────────────────

        private TabPage FamilyTab(string name)
        {
            var settings = NoGbaGameSettings.Offered.Where(s => s.Tab == name && (s.Kinds & _kinds) != 0).ToList();
            if (settings.Count == 0) return null;
            var state = new TabState { Name = name };
            var page = new TabPage(name) { UseVisualStyleBackColor = true, AutoScroll = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 10), Size = new Size(530, 34), ForeColor = SystemColors.GrayText,
                Text = "A <Default> entry or an untouched \"Override default\" leaves the setting as no$gba has it. What is set here is "
                     + "written for the game's session only; no$gba's own come back when it quits.",
            });

            int y = 52;
            foreach (var s in settings)
            {
                bool partial = (s.Kinds & _kinds) != _kinds;   // the selection holds games it does not apply to
                var label = s.Key + (partial ? "   (" + KindsName(s.Kinds & _kinds) + " only)" : "");
                page.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(18, y), UseMnemonic = false });
                if (s.Key == SizingKey)
                {
                    // no$gba's own two boxes over its four words: Free, Force 50% step, Force Aspect Ratio, Strict (both) -
                    // one key, so one "Override default" for the pair.
                    state.SizingOverride = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(200, y - 2) };
                    page.Controls.Add(state.SizingOverride);
                    y += 22;
                    state.Step50 = new CheckBox { Text = "Force 50% step", AutoSize = true, Location = new Point(24, y) };
                    state.Aspect = new CheckBox { Text = "Force Aspect Ratio", AutoSize = true, Location = new Point(200, y) };
                    page.Controls.Add(state.Step50);
                    page.Controls.Add(state.Aspect);
                    state.SizingOverride.CheckedChanged += (_, _) =>
                    {
                        if (_loading) return;
                        if (!state.SizingOverride.Checked) ShowSizing(state, null);
                        RefreshEnabled();
                    };
                    y += 30;
                    continue;
                }
                y += 20;
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, y), Width = 330 };
                page.Controls.Add(combo);
                state.Combos[s.Key] = combo;
                y += 34;
            }
            state.HandNote = new Label { AutoSize = false, Location = new Point(12, y + 4), Size = new Size(520, 34), ForeColor = Color.Firebrick, Visible = false, Text = NoteText };
            page.Controls.Add(state.HandNote);
            _tabs.Add(state);
            return page;
        }

        /// <summary>Fill a list: "&lt;Default : no$gba's value&gt;" first ("&lt;Default&gt;" when NO$GBA.INI does not
        /// hold the key), then no$gba's choices as its window shows them - and a value it does not list (a
        /// newer no$gba's) as one more, so it is shown and kept.</summary>
        private static void Fill(ComboBox box, string key, string defaultValue, string own)
        {
            box.Items.Clear();
            box.Items.Add(new Choice(null, string.IsNullOrEmpty(defaultValue) ? "<Default>" : "<Default : " + NoGbaGameSettings.Shown(defaultValue) + ">"));
            var choices = NoGbaGameSettings.Choices.TryGetValue(key, out var c) ? c : new string[0];
            foreach (var raw in choices) box.Items.Add(new Choice(raw));
            if (own == null) { box.SelectedIndex = 0; return; }
            int at = Array.IndexOf(choices, own);
            if (at < 0) { box.Items.Add(new Choice(own)); box.SelectedIndex = box.Items.Count - 1; }
            else box.SelectedIndex = at + 1;
        }

        private sealed class Choice
        {
            public readonly string Raw;        // null: the default
            private readonly string _text;
            public Choice(string raw, string text = null) { Raw = raw; _text = text; }
            public override string ToString() => _text ?? NoGbaGameSettings.Shown(Raw);
        }

        /// <summary>The tab from no$gba's values (the defaults) and the game's own over them.</summary>
        private void ShowTab(TabState t, Dictionary<string, string> own)
        {
            string Default(string key) => _global.TryGetValue(key, out var g) ? g : null;
            string Own(string key) => own != null && own.TryGetValue(key, out var v) ? v : null;
            foreach (var kv in t.Combos) Fill(kv.Value, kv.Key, Default(kv.Key), Own(kv.Key));
            if (t.Step50 != null)
            {
                t.SizingOverride.Checked = Own(SizingKey) != null;
                ShowSizing(t, Own(SizingKey));
            }
        }

        private void ShowSizing(TabState t, string own)
        {
            var sizing = own ?? (_global.TryGetValue(SizingKey, out var g) ? g : null) ?? "Free";
            t.Step50.Checked = sizing == "Force 50% step" || sizing == "Strict";
            t.Aspect.Checked = sizing == "Force Aspect Ratio" || sizing == "Strict";
        }

        /// <summary>Only what is not on default.</summary>
        private static Dictionary<string, string> ReadTab(TabState t)
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in t.Combos)
                if (kv.Value.SelectedItem is Choice c && c.Raw != null) v[kv.Key] = c.Raw;
            if (t.Step50 != null && t.SizingOverride.Checked)
                v[SizingKey] = t.Step50.Checked && t.Aspect.Checked ? "Strict" : t.Step50.Checked ? "Force 50% step" : t.Aspect.Checked ? "Force Aspect Ratio" : "Free";
            return v;
        }

        private void RefreshEnabled()
        {
            bool hand = _handOn != null && _handOn.Checked;
            foreach (var t in _tabs)
            {
                foreach (var c in t.Combos.Values) c.Enabled = !hand;
                if (t.Step50 != null)
                {
                    t.SizingOverride.Enabled = !hand;
                    t.Step50.Enabled = t.Aspect.Enabled = !hand && t.SizingOverride.Checked;
                }
                t.HandNote.Visible = hand;
            }
        }

        // ── Advanced ─────────────────────────────────────────────────────────

        private TabPage AdvancedTab()
        {
            var page = new TabPage("Advanced") { UseVisualStyleBackColor = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 6), Size = new Size(544, 116), ForeColor = SystemColors.GrayText,
                Text = "Write ONLY the lines you want to change: Key == Value, as NO$GBA.INI writes them - any key.\n"
                     + "- A value must be one of no$gba's own, word for word (a leading \"-\" included): it ignores any other IN SILENCE. "
                     + "The check below says when a value is not one of them.\n"
                     + "- Only these lines are written, for this game's sessions; no$gba's own come back when it quits, and a line that was not there is taken out.\n"
                     + "- Left out: the lines this plugin sets for every launch (NDS Mode/Colors, Reset/Startup Entrypoint, SAV/SNA File Format).",
            });
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 126), Text = "Edit by hand (the other tabs are then not used)" };
            page.Controls.Add(_handOn);
            var preview = new Button { Text = "Preview result...", AutoSize = true, Location = new Point(438, 122) };
            preview.Click += (_, _) => PreviewResult();
            page.Controls.Add(preview);
            _handText = new TextBox
            {
                Location = new Point(12, 152), Size = new Size(544, 176), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 9f), ReadOnly = true,
            };
            page.Controls.Add(_handText);
            _handStatus = new Label { AutoSize = false, Location = new Point(12, 332), Size = new Size(544, 60) };
            page.Controls.Add(_handStatus);

            _handOn.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                _loading = true;
                if (_handOn.Checked) _handText.Text = Lines(_keptHand ?? Generated());
                else { _keptHand = _handText.Text; _handText.Text = Lines(Generated()); }
                _loading = false;
                HandChanged();
            };
            _handText.TextChanged += (_, _) => { if (!_loading) HandChanged(); };
            return page;
        }

        private static string Lines(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        /// <summary>What the family tabs set now, for the game shown.</summary>
        private Dictionary<string, string> TabValues(NoGbaKind kind)
        {
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _tabs)
                foreach (var kv in ReadTab(t))
                {
                    var s = NoGbaGameSettings.Offered.FirstOrDefault(x => x.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (s != null && (s.Kinds & kind) != 0) own[kv.Key] = kv.Value;
                }
            return own;
        }

        private Entry SourceGame() => _source != null ? _groups[_source.SelectedIndex][0] : _games[0];

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
            bool on = _handOn.Checked;
            _handText.ReadOnly = !on;
            _handText.BackColor = on ? SystemColors.Window : SystemColors.Control;
            RefreshEnabled();
            if (!on) { _handStatus.ForeColor = SystemColors.GrayText; _handStatus.Text = "Generated from the other tabs."; return; }
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
            if (_handOn.Checked)
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

        private void LoadFrom(Entry e)
        {
            _loading = true;
            foreach (var t in _tabs) ShowTab(t, e.Own);
            _keptHand = e.Advanced;
            _handOn.Checked = e.AdvancedOn && e.Advanced != null;
            _handText.Text = Lines(_handOn.Checked ? e.Advanced : Generated());
            _loading = false;
            HandChanged();
        }

        // ── OK ───────────────────────────────────────────────────────────────

        private void Apply()
        {
            if (_handOn.Checked)
            {
                var warnings = NoGbaGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var handError);
                if (handError != null) { MessageBox.Show(this, "The settings set by hand are not valid: " + handError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (warnings.Count > 0
                    && MessageBox.Show(this, "The settings set by hand:\n\n- " + string.Join("\n- ", warnings) + "\n\nUse them anyway?",
                                       Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            if (_games.Count > 1
                && MessageBox.Show(this, "These settings will be applied to all " + _games.Count + " selected games - each keeping only what applies to its kind.",
                                   Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            int changed = 0;
            var handText = _handOn.Checked ? _handText.Text.Trim() : _keptHand?.Trim();
            foreach (var g in _games)
            {
                var own = TabValues(g.Kind);
                var chosen = own.Count > 0 ? own : null;
                if (Fragment(g.Own) != Fragment(chosen)) { NoGbaGameSettings.Save(g.Layout, g.GameId, chosen); changed++; }
                if (!(g.AdvancedOn == (_handOn.Checked && !string.IsNullOrEmpty(handText))
                      && string.Equals((g.Advanced ?? "").Trim(), handText ?? "", StringComparison.Ordinal)))
                { NoGbaGameSettings.SaveAdvanced(g.Layout, g.GameId, handText, _handOn.Checked); changed++; }
            }
            Log.Info("options window: " + changed + " change(s), of " + _games.Count + " game(s)");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
