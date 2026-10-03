// The options window of the PPSSPP plugin, opened from the right-click entry on one game or a selection
// (PpssppGameMenu): a game's OWN settings, laid over its PPSSPP config for its session (PpssppGameSettings).
//
// EVERY SETTING IS ON "DEFAULT" UNTIL SET (Mehdi, 29/09 - as the other plugins): a list starts with
// "<Default : value>", a box has three states (the filled one: default), the CPU clock has an "Override
// default" beside it. "Default" is what the game runs on without this plugin - its own PPSSPP game config,
// else ppsspp.ini. Only what is not on default is kept: showing the window changes no game. "Advanced" shows
// the lines the game changes, and takes the user's own instead, "Edit by hand".
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

namespace LbIntegrations.Ppsspp
{
    internal sealed class PpssppOptionsForm : Form
    {
        internal sealed class Entry
        {
            public IGame Game;
            public string Title, GameId, DiscId;
            public PpssppLayout Layout;
            public Dictionary<string, string> Own;       // Section/Key -> value, null for none
            public Dictionary<string, string> Defaults;  // what it runs on without them
            public HashSet<string> GameConfig;           // what its own PPSSPP game config sets
            public string Advanced;
            public bool AdvancedOn;
            public string Key => Fragment(Own) + "|" + (AdvancedOn ? "hand:" : "") + (Advanced ?? "");
        }

        private static string Fragment(Dictionary<string, string> v)
            => v == null ? "" : string.Join(";", v.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value));

        private const string NoteText = "Set by hand in the Advanced tab - untick \"Edit by hand\" there to use this tab again.";

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private ComboBox _source;
        private Dictionary<string, string> _defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _gameConfig = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A setting's control: a list, a three-state box, or a number with its "Override default".</summary>
        private sealed class Field
        {
            public PpssppGameSettings.Setting Setting;
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
        private bool _paused;

        public PpssppOptionsForm(List<Entry> games)
        {
            _games = games;
            _groups = games.GroupBy(g => g.Key).Select(g => g.ToList()).ToList();

            Text = "Nixx-PPSSPP - Options" + (games.Count > 1 ? " (" + games.Count + " games)" : " - " + games[0].Title);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(600, 664);

            var top = new Panel { Dock = DockStyle.Top, Height = _groups.Count > 1 ? 116 : 88, Padding = new Padding(12, 10, 12, 0) };
            // What a session costs, and the way round it (Mehdi, 01/10) - see OptionMarks.PauseRow.
            _paused = games.All(g => g.Layout != null && PpssppGameSettings.IsPaused(g.Layout, g.GameId));
            top.Controls.Add(OptionMarks.PauseRow("PPSSPP", _paused, p => _paused = p));
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText,
                Text = "This plugin's settings, laid over the game's PPSSPP config only while it runs.",
            });
            var noId = games.Count(g => string.IsNullOrEmpty(g.DiscId));
            top.Controls.Add(new Label
            {
                AutoSize = false, Dock = DockStyle.Top, Height = 20,
                Text = (games.Count == 1 ? games[0].Title + (games[0].DiscId != null ? "   (" + games[0].DiscId + ")" : "")
                                         : games.Count + " games" + (_groups.Count > 1 ? " - their settings differ: start from" : ", all with the same settings"))
                       + (noId > 0 ? "   - " + (games.Count == 1 ? "no game id: only the Backend applies" : noId + " without a game id: only the Backend applies to them") : ""),
            });
            if (_groups.Count > 1)
            {
                _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                foreach (var g in _groups)
                    _source.Items.Add(g[0].Title + (g.Count > 1 ? " <and " + (g.Count - 1) + " other" + (g.Count > 2 ? "s" : "") + ">" : "")
                                      + "   -   " + (g[0].AdvancedOn ? "set by hand" : g[0].Own == null ? "its PPSSPP settings" : "its own settings"));
                _source.SelectedIndexChanged += (_, _) => LoadFrom(_groups[_source.SelectedIndex][0]);
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            var tabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var name in new[] { PpssppGameSettings.GraphicsTab, PpssppGameSettings.SystemTab, PpssppGameSettings.HacksTab })
                tabs.TabPages.Add(SettingsTab(name));
            var advanced = AdvancedTab();
            tabs.TabPages.Add(advanced);
            // The game's updates (PpssppUpdates) - one game with its disc id: they are installed for that id.
            if (games.Count == 1 && !string.IsNullOrEmpty(games[0].DiscId) && games[0].Layout != null) tabs.TabPages.Add(UpdatesTab(games[0]));
            tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == advanced && !_handOn.Checked) ShowGenerated(); };

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            // Where each value comes from, said by colour - see OptionMarks.
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("PPSSPP", gameConfig: true, hereLoses: false),
                                            OptionMarks.ResetButton(ResetDefaults), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(top);

            if (_source != null) _source.SelectedIndex = 0;
            else LoadFrom(_groups[0][0]);
        }

        // ── the game's updates ───────────────────────────────────────────────

        /// <summary>The updates found for the game, and the one installed - installed or removed at once, by its buttons:
        /// nothing is installed unasked (Mehdi, 03/10). See PpssppUpdates.</summary>
        private TabPage UpdatesTab(Entry g)
        {
            var page = new TabPage("Updates") { UseVisualStyleBackColor = true };
            string rom = null;
            try { rom = LbIntegrations.Lbip.LbipImportWatch.Full(g.Game?.ApplicationPath); } catch { }
            var discVersion = rom == null ? null : PspDiscId.SfoOf(rom)?.GetString("DISC_VERSION")?.Trim();
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 8), Size = new Size(560, 48), ForeColor = SystemColors.GrayText,
                Text = "A game update is a PBOOT.PBP that PPSSPP starts in place of the disc's executable, when it is made for this disc's "
                     + "version (" + (discVersion ?? "?") + "). Installed into the memory stick (PSP\\GAME\\" + g.DiscId + ") once, and kept there: "
                     + "never installed unasked.",
            });
            var installed = new Label { AutoSize = false, Location = new Point(12, 60), Size = new Size(560, 34) };
            var list = new ListView { Location = new Point(12, 98), Size = new Size(560, 200), View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
            list.Columns.Add("Update", 250);
            list.Columns.Add("For disc version", 110);
            list.Columns.Add("File", 190);
            var install = new Button { Text = "Install the selected", AutoSize = true, Location = new Point(12, 306) };
            var remove = new Button { Text = "Remove the installed one", AutoSize = true, Location = new Point(170, 306) };
            void Show()
            {
                list.Items.Clear();
                foreach (var u in PpssppUpdates.For(g.DiscId, rom))
                {
                    var item = new ListViewItem(new[] { (u.Title ?? "update") + " - version " + (u.AppVer ?? "?"), u.DiscVersion ?? "?", System.IO.Path.GetFileName(u.Path) }) { Tag = u };
                    if (discVersion != null && u.DiscVersion != null && u.DiscVersion != discVersion) { item.ForeColor = Color.DarkGoldenrod; item.ToolTipText = "made for another version of the disc: PPSSPP would not start it"; }
                    list.Items.Add(item);
                }
                if (list.Items.Count == 0) list.Items.Add(new ListViewItem(new[] { "No update found for " + g.DiscId, "", "" }) { ForeColor = SystemColors.GrayText });
                var now = PpssppUpdates.Installed(g.Layout, g.DiscId);
                installed.Text = now == null ? "Installed: none - the game runs as on its disc."
                    : "Installed: " + now.Value.Update + (now.Value.Ours ? "" : " - put there by hand: this window leaves it alone.");
                remove.Enabled = now != null && now.Value.Ours;
            }
            install.Click += (_, _) =>
            {
                if (list.SelectedItems.Count == 0 || !(list.SelectedItems[0].Tag is PspUpdate u)) return;
                var why = PpssppUpdates.Install(g.Layout, u);
                if (why != null) MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show();
            };
            remove.Click += (_, _) =>
            {
                var why = PpssppUpdates.Remove(g.Layout, g.DiscId);
                if (why != null) MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Show();
            };
            page.Controls.AddRange(new Control[] { installed, list, install, remove });
            Show();
            return page;
        }

        // ── a tab of settings ────────────────────────────────────────────────

        private TabPage SettingsTab(string name)
        {
            var page = new TabPage(name) { UseVisualStyleBackColor = true, AutoScroll = true };
            page.Controls.Add(new Label
            {
                AutoSize = false, Location = new Point(12, 8), Size = new Size(550, 34), ForeColor = SystemColors.GrayText,
                Text = name == PpssppGameSettings.HacksTab
                    ? "PPSSPP already applies its own fixes to known games (its compat.ini). A <Default> entry or a filled box leaves the setting as the game has it."
                    : "A <Default> entry, a filled box or an untouched \"Override default\" leaves the setting as the game has it without this plugin: its own PPSSPP config, else PPSSPP's settings.",
            });
            // Two columns: the lists and the number on the left, the boxes on the right.
            int y = 48, yRight = 52;
            foreach (var s in PpssppGameSettings.Offered.Where(x => x.Tab == name))
            {
                var f = new Field { Setting = s };
                if (s.Kind == PpssppGameSettings.Kind.Bool)
                {
                    f.Box = new CheckBox { Text = s.Label, Tag = s.Label, AutoSize = false, Size = new Size(270, 36), Location = new Point(300, yRight), ThreeState = true };
                    f.Box.CheckStateChanged += (_, _) => { if (!_loading) { RefreshTexts(); RefreshMarks(); } };
                    page.Controls.Add(f.Box);
                    yRight += 40;
                }
                else if (s.Kind == PpssppGameSettings.Kind.Choice)
                {
                    page.Controls.Add(new Label { Text = s.Label + (s.Section == PpssppGameSettings.CliSection ? "   (given at launch)" : ""), AutoSize = true, Location = new Point(18, y) });
                    y += 20;
                    f.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, y), Width = 250 };
                    f.Combo.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshMarks(); };
                    page.Controls.Add(f.Combo);
                    y += 32;
                }
                else
                {
                    page.Controls.Add(new Label { Text = s.Label, AutoSize = false, Size = new Size(270, 32), Location = new Point(18, y) });
                    y += 34;
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
            var note = new Label { AutoSize = false, Location = new Point(12, y + 4), Size = new Size(550, 34), ForeColor = Color.Firebrick, Visible = false, Text = NoteText };
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

        private static string ChoiceText(PpssppGameSettings.Setting s, string v)
            => s.Choices.FirstOrDefault(c => c.Value == v).Text ?? v;

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
                    f.Combo.Items.Add(new Item { Value = null, Text = string.IsNullOrEmpty(d) ? "<Default>" : "<Default : " + ChoiceText(s, d) + ">" });
                    foreach (var c in s.Choices) f.Combo.Items.Add(new Item { Value = c.Value, Text = c.Text });
                    int at = mine == null ? 0 : f.Combo.Items.Cast<Item>().ToList().FindIndex(i => i.Value == mine);
                    if (at < 0) { f.Combo.Items.Add(new Item { Value = mine, Text = mine }); at = f.Combo.Items.Count - 1; }
                    f.Combo.SelectedIndex = at;
                }
                else if (f.Box != null)
                    f.Box.CheckState = mine == null ? CheckState.Indeterminate : mine.Equals("True", StringComparison.OrdinalIgnoreCase) ? CheckState.Checked : CheckState.Unchecked;
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

        /// <summary>Each setting's bar: set here, from the game's own PPSSPP config, or none - see OptionMarks.
        /// A text set by hand in use: the tabs are not used, so not marked.</summary>
        private void RefreshMarks()
        {
            bool hand = _handOn != null && _handOn.Checked;
            var mine = ReadValues();
            foreach (var f in _fields)
            {
                var id = f.Setting.Id;
                bool used = !hand || f.Setting.Section == PpssppGameSettings.CliSection;
                var level = !used ? OptionLevel.Unused
                          : mine.ContainsKey(id) ? (_gameConfig.Contains(id) ? OptionLevel.Here : OptionLevel.Here)
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
                           : (string)f.Box.Tag + "   (default: " + (d == null ? "not set" : d.Equals("True", StringComparison.OrdinalIgnoreCase) ? "on" : d.Equals("False", StringComparison.OrdinalIgnoreCase) ? "off" : d) + ")";
            }
        }

        private void RefreshEnabled()
        {
            bool hand = _handOn != null && _handOn.Checked;
            foreach (var f in _fields)
            {
                // The renderer is given on the command line: a text set by hand does not replace it.
                bool on = !hand || f.Setting.Section == PpssppGameSettings.CliSection;
                if (f.Combo != null) f.Combo.Enabled = on;
                if (f.Box != null) f.Box.Enabled = on;
                if (f.Override != null) { f.Override.Enabled = on; f.Number.Enabled = on && f.Override.Checked; }
            }
            foreach (var n in _handNotes) n.Visible = hand;
        }

        /// <summary>Only what is not on default, Section/Key -> value.</summary>
        private Dictionary<string, string> ReadValues()
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _fields)
            {
                if (f.Combo != null && f.Combo.SelectedItem is Item i && i.Value != null) v[f.Setting.Id] = i.Value;
                else if (f.Box != null && f.Box.CheckState != CheckState.Indeterminate) v[f.Setting.Id] = f.Box.Checked ? "True" : "False";
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
                Text = "Write ONLY what you want to change: [Section], then Key = Value, as ppsspp.ini writes them - any key PPSSPP keeps per game.\n"
                     + "- Only these keys are written, into the game's own PPSSPP config (<GAME ID>_ppsspp.ini), for its sessions only: "
                     + "that config as it was comes back when PPSSPP quits; a key you do not name keeps its value.\n"
                     + "- Left out: [Achievements] and [ControlMapping], which this plugin keeps elsewhere. The Backend is not an ini key: "
                     + "it stays in the Graphics tab, given to PPSSPP at launch.",
            });
            _handOn = new CheckBox { AutoSize = true, Location = new Point(12, 134), Text = "Edit by hand (the other tabs are then not used - the Backend excepted)" };
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

        private string Generated() => PpssppGameSettings.Fragment(PpssppGameSettings.ToRaw(ReadValues()));

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
            var warnings = PpssppGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var error);
            _handStatus.ForeColor = error != null ? Color.Firebrick : warnings.Count > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
            _handStatus.Text = error != null ? "Not valid: " + error
                             : warnings.Count > 0 ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? "\n(and " + (warnings.Count - 3) + " more)" : "")
                             : "Valid.";
        }

        private void PreviewResult()
        {
            var g = SourceGame();
            List<PpssppGameSettings.Raw> keys;
            if (_handOn.Checked)
            {
                keys = PpssppGameSettings.ParseHand(_handText.Text, out var error);
                if (keys == null) { MessageBox.Show(this, "Not valid: " + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            else keys = PpssppGameSettings.ToRaw(ReadValues());
            var result = PpssppGameSettings.Preview(g.Layout, g.DiscId, keys, out var before, out var why);
            if (result == null) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var left = keys.Where(k => PpssppGameSettings.IsManaged(k.Section)).Select(k => "[" + k.Section + "] " + k.Key).ToList();
            ConfigPreviewWindow.Show(this, g.DiscId + "_ppsspp.ini - for a session of " + g.Title,
                "The game's PPSSPP config as PPSSPP will read it for the session; it comes back as it is now when PPSSPP quits."
                + (left.Count > 0 ? " Left out: " + string.Join(", ", left) + "." : ""),
                before, result);
        }

        // ── the source ───────────────────────────────────────────────────────

        private void LoadFrom(Entry e)
        {
            _defaults = e.Defaults ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _gameConfig = e.GameConfig ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                var warnings = PpssppGameSettings.CheckHand(SourceGame().Layout, _handText.Text, out var handError);
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
                if (Fragment(g.Own) != Fragment(chosen)) { PpssppGameSettings.Save(g.Layout, g.GameId, chosen); changed++; }
                if (!(g.AdvancedOn == (_handOn.Checked && !string.IsNullOrEmpty(handText))
                      && string.Equals((g.Advanced ?? "").Trim(), handText ?? "", StringComparison.Ordinal)))
                { PpssppGameSettings.SaveAdvanced(g.Layout, g.GameId, handText, _handOn.Checked); changed++; }
                if (g.Layout != null && PpssppGameSettings.IsPaused(g.Layout, g.GameId) != _paused) { PpssppGameSettings.SetPaused(g.Layout, g.GameId, _paused); changed++; }
            }
            Log.Info("options window: " + changed + " change(s), of " + _games.Count + " game(s)");
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
