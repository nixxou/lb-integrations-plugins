// The options window of the Flycast plugin, opened from the right-click entry on one game or a selection
// (FlycastGameMenu): a game's OWN settings, given to Flycast on its command line for its session
// (FlycastGameSettings) - nothing of Flycast's is written.
//
// EVERY SETTING IS ON "DEFAULT" UNTIL SET (Mehdi, 29/09 - as the other plugins): a list starts with
// "<Default : value>", a box has three states (the filled one: default), a number has an "Override default"
// beside it. "Default" is what the game runs on without this plugin - its own Flycast game config, else
// emu.cfg, else Flycast's built-in value. Only what is not on default is kept: showing the window changes no
// game. "Advanced" shows the keys the game is given, and takes the user's own instead, "Edit by hand".
//
// DREAMCAST AND ARCADE: a setting that is one's only is marked so when the selection holds both, and is not
// given to the other at launch.
//
// A SELECTION THAT DOES NOT AGREE: grouped by identical settings, a combo box names each group and the one
// chosen is what the window starts from; OK applies what is shown to every selected game, after asking.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins.Data;
using Games = LbIntegrations.Flycast.FlycastGameSettings.Games;

namespace LbIntegrations.Flycast
{
    internal sealed class FlycastOptionsForm : Form
    {
        internal sealed class Entry
        {
            public IGame Game;
            public string Title, GameId, Product, Line;
            public Games Games = Games.Dreamcast;
            public FlycastLayout Layout;
            public Dictionary<string, string> Own;       // section:key -> value, null for none
            public Dictionary<string, string> Defaults;  // what it runs on without them
            public HashSet<string> GameConfig;           // what its own Flycast game config sets - that wins
            public string Advanced;
            public bool AdvancedOn;
            public string Key => Fragment(Own) + "|" + (AdvancedOn ? "hand:" : "") + (Advanced ?? "");
        }

        private static string Fragment(Dictionary<string, string> v)
            => v == null ? "" : string.Join(";", v.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));

        private const string NoteText = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again.";

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private readonly Games _kinds;
        private ComboBox _source;
        private Label _wins;
        private Dictionary<string, string> _defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _gameConfig = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _product;

        /// <summary>A setting's control: a list, a three-state box, or a number with its "Override default".</summary>
        private sealed class Field
        {
            public FlycastGameSettings.Setting Setting;
            public ComboBox Combo;
            public CheckBox Box, Override;
            public NumericUpDown Number;
        }

        private readonly List<Field> _fields = new List<Field>();
        private readonly OptionMarks _marks = new OptionMarks();
        private readonly List<Label> _handNotes = new List<Label>();
        private CheckBox _handOn;
        private TextBox _handText;
        private Label _handStatus;
        private string _keptHand;
        private bool _loading;

        public FlycastOptionsForm(List<Entry> games)
        {
            _games = games;
            _groups = games.GroupBy(g => g.Key).Select(g => g.ToList()).ToList();
            _kinds = games.Aggregate((Games)0, (k, g) => k | g.Games);

            Text = "Nixx-Flycast - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(600, 668);

            var top = new Panel { Dock = DockStyle.Top, Height = _groups.Count > 1 ? 100 : 72, Padding = new Padding(12, 10, 12, 0) };
            _wins = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 20, ForeColor = Color.DarkGoldenrod };
            top.Controls.Add(_wins);
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText,
                Text = "This plugin's settings, given to Flycast at launch for the session only - emu.cfg is not touched.",
            });
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20,
                Text = games.Count == 1 ? games[0].Title + (games[0].Product != null ? "   (" + games[0].Product + ")" : "")
                                        : games.Count + " games" + (_groups.Count > 1 ? " - their settings differ: start from" : ", all with the same settings"),
            });
            if (_groups.Count > 1)
            {
                _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                foreach (var g in _groups)
                    _source.Items.Add(g[0].Title + (g.Count > 1 ? " <and " + (g.Count - 1) + " other" + (g.Count > 2 ? "s" : "") + ">" : "")
                                      + "   -   " + (g[0].AdvancedOn ? "set by hand" : g[0].Own == null ? "its Flycast settings" : "its own settings"));
                _source.SelectedIndexChanged += (_, _) => LoadFrom(_groups[_source.SelectedIndex][0]);
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var name in new[] { FlycastGameSettings.VideoTab, FlycastGameSettings.RenderingTab, FlycastGameSettings.SystemTab })
                tabs.TabPages.Add(SettingsTab(name));
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == advanced && !_handOn.Checked) ShowGenerated(); };

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            // Where each value comes from, said by colour - see OptionMarks.
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("Flycast", gameConfig: true, hereLoses: false),
                                            OptionMarks.ResetButton(ResetDefaults), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_source != null) _source.SelectedIndex = 0;
            else LoadFrom(_groups[0][0]);
        }

        // ── a tab of settings ────────────────────────────────────────────────

        /// <summary>"(Dreamcast only)" when the selection also holds games it is not for.</summary>
        private string Only(FlycastGameSettings.Setting s)
            => (s.For & _kinds) == _kinds ? "" : s.For == Games.Dreamcast ? "   (Dreamcast only)" : "   (arcade only)";

        private TabPage SettingsTab(string name)
        {
            var page = new TabPage(name) { UseVisualStyleBackColor = true, AutoScroll = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 8), Size = new Size(560, 34), ForeColor = SystemColors.GrayText,
                Text = name == FlycastGameSettings.RenderingTab
                    ? "Flycast already applies its own fixes to known games, and they win. A <Default> entry, a filled box or an untouched \"Override default\" leaves the setting as the game has it."
                    : "A <Default> entry, a filled box or an untouched \"Override default\" leaves the setting as the game has it without this plugin: its own Flycast game config, else Flycast's settings.",
            });
            // Two columns: the lists and the numbers on the left, the boxes on the right.
            int y = 48, yRight = 52;
            foreach (var s in FlycastGameSettings.Offered.Where(x => x.Tab == name && (x.For & _kinds) != 0))
            {
                var f = new Field { Setting = s };
                if (s.Kind == FlycastGameSettings.Kind.Bool)
                {
                    var label = s.Label + Only(s);
                    // Two lines when the label and its "(default: ...)" do not fit on one.
                    bool two = TextRenderer.MeasureText(label + "   (default: off)", Font).Width > 255;
                    f.Box = new CheckBox { Text = label, Tag = label, AutoSize = false, Size = new Size(280, two ? 36 : 24), Location = new Point(296, yRight), ThreeState = true };
                    f.Box.CheckStateChanged += (_, _) => { if (!_loading) { RefreshTexts(); RefreshMarks(); } };
                    page.Controls.Add(f.Box);
                    yRight += two ? 42 : 32;
                }
                else if (s.Kind == FlycastGameSettings.Kind.Choice)
                {
                    page.Controls.Add(new Label { Text = s.Label + Only(s), AutoSize = true, Location = new Point(18, y) });
                    y += 20;
                    f.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, y), Width = 250 };
                    f.Combo.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshMarks(); };
                    page.Controls.Add(f.Combo);
                    y += 32;
                }
                else
                {
                    page.Controls.Add(new Label { Text = s.Label + Only(s), AutoSize = true, Location = new Point(18, y) });
                    y += 20;
                    f.Override = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(110, y + 2) };
                    f.Number = new NumericUpDown { Minimum = s.Min, Maximum = s.Max, Width = 70, Location = new Point(24, y) };
                    f.Override.CheckedChanged += (_, _) =>
                    {
                        if (_loading) return;
                        if (!f.Override.Checked) f.Number.Value = Number(f, Default(s.Id));
                        RefreshEnabled();
                        RefreshMarks();
                    };
                    page.Controls.Add(f.Number);
                    page.Controls.Add(f.Override);
                    y += 32;
                }
                _fields.Add(f);
            }
            y = Math.Max(y, yRight);
            var note = new Label { AutoSize = false, Location = new Point(12, y + 4), Size = new Size(560, 34), ForeColor = Color.Firebrick, Visible = false, Text = NoteText };
            page.Controls.Add(note);
            _handNotes.Add(note);
            return page;
        }

        private string Default(string id) => _defaults.TryGetValue(id, out var v) ? v : null;

        private static decimal Number(Field f, string v)
            => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(f.Setting.Min, Math.Min(f.Setting.Max, n)) : f.Setting.Min;

        private sealed class Item
        {
            public string Value, Text;
            public override string ToString() => Text;
        }

        private static string ChoiceText(FlycastGameSettings.Setting s, string v)
            => s.Choices.FirstOrDefault(c => c.Value == v).Text ?? v;

        /// <summary>" - its game config" after a default the game's own Flycast config sets.</summary>
        private string Whose(string id) => _gameConfig.Contains(id) ? ", its game config" : "";

        private void ShowValues(Dictionary<string, string> own)
        {
            _loading = true;
            string Own(string id) => own != null && own.TryGetValue(id, out var v) ? v : null;
            foreach (var f in _fields)
            {
                var s = f.Setting;
                var mine = Own(s.Id);
                if (f.Combo != null)
                {
                    f.Combo.Items.Clear();
                    var d = Default(s.Id);
                    f.Combo.Items.Add(new Item { Value = null, Text = string.IsNullOrEmpty(d) ? "<Default>" : "<Default : " + ChoiceText(s, d) + Whose(s.Id) + ">" });
                    foreach (var c in s.Choices) f.Combo.Items.Add(new Item { Value = c.Value, Text = c.Text });
                    int at = mine == null ? 0 : f.Combo.Items.Cast<Item>().ToList().FindIndex(i => i.Value == mine);
                    if (at < 0) { f.Combo.Items.Add(new Item { Value = mine, Text = mine }); at = f.Combo.Items.Count - 1; }
                    f.Combo.SelectedIndex = at;
                }
                else if (f.Box != null)
                    f.Box.CheckState = mine == null ? CheckState.Indeterminate : FlycastGameSettings.IsYes(mine) ? CheckState.Checked : CheckState.Unchecked;
                else
                {
                    f.Override.Checked = mine != null;
                    f.Number.Value = Number(f, mine ?? Default(s.Id));
                }
            }
            _loading = false;
            RefreshTexts();
            RefreshEnabled();
            RefreshMarks();
        }

        /// <summary>Each setting's bar: set here, from the game's own Flycast config, or none - see OptionMarks.
        /// A text set by hand in use: the tabs are not used, so not marked.</summary>
        private void RefreshMarks()
        {
            bool hand = _handOn != null && _handOn.Checked;
            var mine = ReadValues();
            foreach (var f in _fields)
            {
                var id = f.Setting.Id;
                bool used = !hand;
                var level = !used ? OptionLevel.Unused
                          : mine.ContainsKey(id) ? OptionLevel.Here   // over the game's own config too, whatever its id (FlycastGameConfigSession)
                          : _gameConfig.Contains(id) ? OptionLevel.GameConfig : OptionLevel.Emulator;
                _marks.Set((Control)f.Combo ?? (Control)f.Box ?? f.Number, level);
            }
        }

        /// <summary>"Reset to defaults": every setting on default, the text set by hand off and forgotten -
        /// nothing saved before OK.</summary>
        private void ResetDefaults()
        {
            _loading = true;
            _handOn.Checked = false;
            _keptHand = null;
            _loading = false;
            ShowValues(null);
            ShowGenerated();
        }

        private void RefreshTexts()
        {
            foreach (var f in _fields.Where(x => x.Box != null))
            {
                var d = Default(f.Setting.Id);
                f.Box.Text = f.Box.CheckState != CheckState.Indeterminate ? (string)f.Box.Tag
                           : (string)f.Box.Tag + "   (default: " + (d == null ? "not set" : FlycastGameSettings.IsYes(d) ? "on" : "off") + Whose(f.Setting.Id) + ")";
            }
        }

        private void RefreshEnabled()
        {
            bool hand = _handOn != null && _handOn.Checked;
            foreach (var f in _fields)
            {
                if (f.Combo != null) f.Combo.Enabled = !hand;
                if (f.Box != null) f.Box.Enabled = !hand;
                if (f.Override != null) { f.Override.Enabled = !hand; f.Number.Enabled = !hand && f.Override.Checked; }
            }
            foreach (var n in _handNotes) n.Visible = hand;
        }

        /// <summary>Only what is not on default, section:key -> value.</summary>
        private Dictionary<string, string> ReadValues()
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _fields)
            {
                if (f.Combo != null && f.Combo.SelectedItem is Item i && i.Value != null) v[f.Setting.Id] = i.Value;
                else if (f.Box != null && f.Box.CheckState != CheckState.Indeterminate) v[f.Setting.Id] = f.Box.Checked ? "yes" : "no";
                else if (f.Override != null && f.Override.Checked) v[f.Setting.Id] = ((int)f.Number.Value).ToString(CultureInfo.InvariantCulture);
            }
            return v;
        }

        // ── Advanced ─────────────────────────────────────────────────────────

        private TabPage AdvancedTab()
        {
            var page = new TabPage("Advanced") { UseVisualStyleBackColor = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 6), Size = new Size(564, 122), ForeColor = SystemColors.GrayText,
                Text = "Write ONLY what you want to change: [section], then key = value, as emu.cfg writes them - [config], [audio]...\n"
                     + "- They are given to Flycast at launch (-config section:key=value), for the session only: emu.cfg is not written, "
                     + "and a key you do not name keeps its value.\n"
                     + "- Still winning over them: the game's own Flycast game config, and Flycast's fixes for known games. "
                     + "Left out: [achievements], which this plugin keeps. No comma or quote in a value.",
            });
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 134), Text = "Edit by hand (the other tabs are then not used)" };
            page.Controls.Add(_handOn);
            var preview = new Button { Text = "Preview result...", AutoSize = true, Location = new Point(458, 130) };
            preview.Click += (_, _) => PreviewResult();
            page.Controls.Add(preview);
            _handText = new TextBox
            {
                Location = new Point(12, 160), Size = new Size(564, 238), Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 9f), ReadOnly = true,
            };
            page.Controls.Add(_handText);
            _handStatus = new Label { AutoSize = false, Location = new Point(12, 402), Size = new Size(564, 60) };
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

        private string Generated() => FlycastGameSettings.Fragment(FlycastGameSettings.ToRaw(ReadValues()));

        private void ShowGenerated()
        {
            _loading = true;
            _handText.Text = Lines(Generated());
            _loading = false;
            HandChanged();
        }

        private Entry SourceGame() => _source != null ? _groups[_source.SelectedIndex][0] : _games[0];

        private void HandChanged()
        {
            bool on = _handOn.Checked;
            _handText.ReadOnly = !on;
            _handText.BackColor = on ? SystemColors.Window : SystemColors.Control;
            RefreshEnabled();
            RefreshMarks();
            if (!on) { _handStatus.ForeColor = SystemColors.GrayText; _handStatus.Text = "Generated from the other tabs."; return; }
            var warnings = FlycastGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var error);
            _handStatus.ForeColor = error != null ? Color.Firebrick : warnings.Count > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            _handStatus.Text = error != null ? "Not valid: " + error
                             : warnings.Count > 0 ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? "\n(and " + (warnings.Count - 3) + " more)" : "")
                             : "Valid.";
        }

        private void PreviewResult()
        {
            var g = SourceGame();
            List<FlycastGameSettings.Raw> keys;
            if (_handOn.Checked)
            {
                keys = FlycastGameSettings.ParseHand(_handText.Text, out var error);
                if (keys == null) { MessageBox.Show(this, "Not valid: " + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            else keys = FlycastGameSettings.ToRaw(ReadValues());
            var result = FlycastGameSettings.Preview(g.Layout, g.Product, g.Line, keys, _handOn.Checked ? Games.All : g.Games, out var before, out var why);
            if (result == null) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var left = keys.Where(k => FlycastGameSettings.IsManaged(k.Section)).Select(k => k.Id).ToList();
            ConfigPreviewWindow.Show(this, "Flycast's command line - for a session of " + g.Title,
                "The line Flycast will be started with (the game itself comes after it), and what it will use; emu.cfg is not written."
                + (left.Count > 0 ? " Left out: " + string.Join(", ", left) + "." : ""),
                before, result);
        }

        // ── the source ───────────────────────────────────────────────────────

        private void LoadFrom(Entry e)
        {
            _defaults = e.Defaults ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _gameConfig = e.GameConfig ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _product = e.Product;
            _wins.Text = _gameConfig.Count == 0 ? ""
                : "Its own Flycast game config ([" + e.Product + "] in emu.cfg) sets " + _gameConfig.Count + " of these - what is set here goes over it for the session.";
            ShowValues(e.Own);
            _loading = true;
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
                var warnings = FlycastGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var handError);
                if (handError != null) { MessageBox.Show(this, "The settings set by hand are not valid: " + handError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (warnings.Count > 0
                    && MessageBox.Show(this, "The settings set by hand:\n\n- " + string.Join("\n- ", warnings) + "\n\nUse them anyway?",
                                       Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            if (_games.Count > 1
                && MessageBox.Show(this, "These settings will be applied to all " + _games.Count + " selected games.",
                                   Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            int changed = 0;
            var own = ReadValues();
            var chosen = own.Count > 0 ? own : null;
            var handText = _handOn.Checked ? _handText.Text.Trim() : _keptHand?.Trim();
            foreach (var g in _games)
            {
                if (Fragment(g.Own) != Fragment(chosen)) { FlycastGameSettings.Save(g.Layout, g.GameId, chosen); changed++; }
                if (!(g.AdvancedOn == (_handOn.Checked && !string.IsNullOrEmpty(handText))
                      && string.Equals((g.Advanced ?? "").Trim(), handText ?? "", StringComparison.Ordinal)))
                { FlycastGameSettings.SaveAdvanced(g.Layout, g.GameId, handText, _handOn.Checked); changed++; }
            }
            Log.Info("options window: " + changed + " change(s), of " + _games.Count + " game(s)");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
