// The right-click entry on the games ares can run - one game or a selection: "Nixx-ares : Options...". The window, dressed
// as every Nixx window (NixxShell), in tabs (Mehdi, 06/10): GAME - the system ares runs it as (the third level of
// AresLaunch.SystemFor), its file, its BIOS, its saves, its screen; then a tab per group of options, those of one system
// (Nintendo 64...) only for a game of that system. At the bottom, what goes on ares's command line for it, as the SUPER
// ZSNES window shows it - updated at every change - then the pack's legend, "Reset to defaults", OK and Cancel (OptionMarks).
// The shader is picked from a menu built from the install's Shaders folder when it opens, folders as sub-menus - as ares's
// own Settings > Shader menu (presentation.cpp, loadShaders). "As set in ares" says what ares has: its settings.bml is
// read, never written, when the window opens.
//
// SEVERAL GAMES (the pack's rule - melonDS, Vita3K - with Mehdi's change of 06/10): at the top, "All selected games", the
// default, then the games grouped by identical options ("Zelda <and 3 others> - its own: Shader, Scaling"). "All selected
// games" shows a setting they agree on as it is, and one they do not as "Various values" (purple, with how many games have
// their own): left so, each keeps its own; changed, it goes to all. A group shows its first game's. OK asks first, then
// changes only what was changed (LbipGameEdit) - each game keeps the rest of its own.
//
// NOT AN IGameMenuItemPlugin, as for the other plugins of the pack: Nixx-Menus relays to the GameMenu class below by its
// name - src\Menus\Menus.cs has the contract.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ares
{
    internal static class AresGameMenu
    {
        public const string Caption = "Nixx-ares : Options...";

        public static Image Icon => LbipMenuIcon.Of(AresLaunch.IsAresExe, p => p);

        /// <summary>A game of ours: its own emulator is an ares, or its platform is one ares runs and an ares is in the
        /// library. Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && AresLaunch.IsAresExe(own.ApplicationPath)) return true;
                if (AresSystems.Platform(game.Platform) == null) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && AresLaunch.IsAresExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new AresGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, "Nixx-ares", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class AresGameForm : Form
    {
        private const string Various = "\u0001various";   // "All selected games", the games do not agree
        private const string SystemKey = "System";

        /// <summary>One option's row: its list (or, the shader, its menu button), and the bar that says where it comes from.</summary>
        private sealed class Row
        {
            public AresOption O;
            public ComboBox Combo;
            public List<string> Values = new List<string>();   // what the list's items stand for: Various, null (follow), a value
            public Button Shader;
            public string ShaderValue;   // Various, null, "none" or a preset path
            public Panel Bar;

            public string Chosen() => Combo != null ? (Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex]) : ShaderValue;
        }

        private sealed class Entry { public IGame Game; public string Id, Title; public Dictionary<string, string> Stored; public string Key; }

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private readonly ComboBox _source;
        private readonly List<Row> _rows = new List<Row>();
        private readonly ComboBox _system;
        private readonly List<string> _systemValues = new List<string>();   // Various, null (as its platform), or a system
        private readonly Panel _systemBar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) };
        private readonly string _asPlatform, _exe;
        private readonly AresBml _bml;          // ares's own settings, read when the window opens - never written here
        private readonly TextBox _line;
        private readonly Label _count;
        private Dictionary<string, string> _atOpen;
        private bool _loading;
        private bool Several => _games.Count > 1;
        /// <summary>For the probe, with no LaunchBox: the ares whose Shaders folder and settings the window reads.</summary>
        internal static string ExeForProbe;

        public AresGameForm(IGame[] games)
        {
            _games = games.Select(g => new Entry { Game = g, Id = Safe(() => g.Id), Title = Safe(() => g.Title) ?? "?" }).ToList();
            foreach (var e in _games)
            {
                e.Stored = StoredOf(e.Id);
                e.Key = string.Join("\n", e.Stored.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            }
            _groups = _games.GroupBy(e => e.Key).Select(g => g.ToList()).ToList();
            var game = games[0];
            Text = !Several ? "Nixx-ares - " + _games[0].Title : "Nixx-ares - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(780, 700);
            MinimumSize = new Size(660, 520);
            Font = new Font("Segoe UI", 9f);

            var id = _games[0].Id;
            var dm = PluginHelper.DataManager;
            var emu = dm?.GetAllEmulators()?.FirstOrDefault(e => AresLaunch.IsAresExe(Safe(() => e.ApplicationPath)));
            _exe = emu == null ? ExeForProbe : AresPlugin.ResolveFullPathForUi(Safe(() => emu.ApplicationPath));
            try { if (_exe != null) _bml = AresBml.Load(AresLaunch.SettingsOf(_exe)); } catch (Exception ex) { Log.Warn("ares's settings could not be read for the window", ex); }
            var rom = Safe(() => game.ApplicationPath) ?? "";
            _asPlatform = AresPlugin.SystemOfForUi(emu, null, Safe(() => game.Platform), rom);
            var own = !Several ? AresSettings.GameSystem(id) : null;
            var gameSystem = !Several ? (own ?? _asPlatform) : null;
            // The systems of the selection: a system's own tab shows when one of them is of it.
            var systems = _games.Select(e => AresSettings.GameSystem(e.Id) ?? AresPlugin.SystemOfForUi(emu, null, Safe(() => e.Game.Platform), Safe(() => e.Game.ApplicationPath) ?? ""))
                                .Where(s => s != null).Distinct().ToList();

            // ── the top: which games, and where to start from ──
            var top = new Panel { Dock = DockStyle.Top, Height = Several ? 62 : 34, Padding = new Padding(12, 8, 12, 0) };
            top.Controls.Add(new Label
            {
                Dock = DockStyle.Top, Height = 24, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 11f),
                Text = !Several ? _games[0].Title : games.Length + " games" + (_groups.Count > 1 ? " - their options differ" : ""),
            });
            if (Several)
            {
                _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
                _source.Items.Add("All selected games (" + games.Length + ")");
                if (_groups.Count > 1)
                    foreach (var g in _groups)
                        _source.Items.Add(g[0].Title + (g.Count > 1 ? " <and " + (g.Count - 1) + " other" + (g.Count > 2 ? "s" : "") + ">" : "") + "   -   " + Describe(g[0]));
                _source.SelectedIndexChanged += (_, _) => { if (_source.SelectedIndex == 0) LoadAll(); else LoadFrom(_groups[_source.SelectedIndex - 1][0]); };
                top.Controls.Add(_source);
                _source.BringToFront();
            }

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };

            // ── Game ──
            var platform = AresSystems.Platform(Safe(() => game.Platform));
            _system = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
            bool samePlatform = games.All(g => string.Equals(Safe(() => g.Platform), Safe(() => game.Platform), StringComparison.OrdinalIgnoreCase));
            _system.Enabled = samePlatform;
            _system.SelectedIndexChanged += (_, _) => Changed();
            LbipHint.Attach(_system, "As its platform: the --system on the platform's line in LaunchBox (Edit Emulator > Associated Platforms), else the plugin's "
                                   + "choice for the platform and the file's extension. A system chosen here is this game's alone."
                                   + (samePlatform ? "" : " The games selected are of different platforms: each keeps its own."));
            _platformSystems = platform == null ? new List<string>() : platform.Systems.Concat(platform.ByExtension.Values).Distinct().ToList();

            var gameTab = Table();
            AddRow(gameTab, _systemBar, "System", _system);
            if (!Several)
            {
                AddText(gameTab, "File", rom);
                var system = AresSystems.System(gameSystem);
                if (_exe != null && system != null)
                {
                    var (owner, specs) = AresSystems.FirmwareFor(system);
                    if (specs.Length > 0)
                    {
                        var states = AresFirmware.Ensure(_exe, new[] { system }, readOnly: true);
                        AddText(gameTab, "BIOS", owner.Name + ": " + string.Join(";  ", states.Select(st => st.Spec.Region + " " + (st.Path == null ? (st.Spec.Optional ? "-" : "missing") : Path.GetFileName(st.Path))))
                                                 + (AresFirmware.Enough(states) ? "" : "  -  the game will not start without one"));
                    }
                    var list = new List<GameSaveBase>();
                    AresPlugin.Collect(list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), _exe, system, AresPlugin.ResolveFullPathForUi(rom), id ?? "x", null);
                    AddText(gameTab, "Saves", list.Count == 0 ? "none yet" : string.Join(", ", list.Select(l => Path.GetFileName(l.FileLocation))));
                }
                // Its line in ares's compatibility list (AresCompat) - fetched again in the background when a month old.
                AresCompat.Refresh();
                var entry = AresCompat.Find(gameSystem, AresPlugin.ResolveFullPathForUi(rom), _games[0].Title, out var how);
                var compat = new Label
                {
                    AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(3, 7, 0, 4), UseMnemonic = false,
                    Text = entry == null ? "Not in ares's list" : entry.Summary + (string.IsNullOrEmpty(entry.Notes) ? "" : " - " + entry.Notes),
                    ForeColor = entry == null ? SystemColors.GrayText : CompatColor(entry.Status),
                };
                LbipHint.Attach(compat, entry == null ? "ares-emu.net/compatibility has no line for this game (or not tested)."
                                                      : "ares-emu.net/compatibility: " + entry.Title + " - found " + how + "."
                                                        + (entry.Issues > 0 ? " " + entry.Issues + " GitHub issue(s)." : ""));
                AddRow(gameTab, null, "Compatibility", compat);
            }
            foreach (var o in AresSettings.All.Where(x => x.OnPage)) AddOption(gameTab, o);
            tabs.TabPages.Add(Page("Game", gameTab));

            // ── a tab per group ──
            foreach (var group in AresSettings.Groups)
            {
                var options = AresSettings.All.Where(x => x.Group == group && !x.OnPage && (systems.Count == 0 || systems.Any(x.AppliesTo))).ToList();
                if (options.Count == 0) continue;
                var table = Table();
                foreach (var o in options) AddOption(table, o);
                tabs.TabPages.Add(Page(group == "Start and emulation" ? "Emulation" : group == "Colour and effects" ? "Colour" : group, table));
            }

            // ── the bottom: what goes on the line, then the pack's legend, reset, OK and Cancel ──
            var summary = new Panel { Dock = DockStyle.Bottom, Height = 118, Padding = new Padding(12, 6, 12, 2) };
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, TabStop = false };
            var note = LbipHint.Note("Given to ares when the game starts; its own settings come back when it quits.",
                "Kept in games\\<game id>.ini. Each option is passed as --setting \"<key>=<value>\", before what the platform's line in LaunchBox has "
                + "(a --setting there wins). \"As set in ares\" passes nothing: what ares's own menus have is used - every game's settings are ares's.", 720, new Padding(0, 2, 0, 0));
            note.Dock = DockStyle.Bottom;
            summary.Controls.Add(_line);
            summary.Controls.Add(_count);
            summary.Controls.Add(note);

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-ares"); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("ares", gameConfig: false, hereLoses: false, various: Several), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(summary);
            Controls.Add(bottom);
            if (_source != null) _source.SelectedIndex = 0; else LoadFrom(_games[0]);
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private readonly List<string> _platformSystems;

        // ── a game's stored values ───────────────────────────────────────────

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            var sys = AresSettings.GameSystem(id);
            if (sys != null) d[SystemKey] = sys;
            foreach (var o in AresSettings.All) { var v = AresSettings.GameValue(id, o.Key); if (v != null) d[o.Key] = v; }
            return d;
        }

        private static string Describe(Entry e)
        {
            var mine = AresSettings.All.Where(o => e.Stored.ContainsKey(o.Key)).Select(o => o.Label).ToList();
            if (e.Stored.ContainsKey(SystemKey)) mine.Insert(0, "System " + e.Stored[SystemKey]);
            return mine.Count == 0 ? "ares's settings" : "its own: " + string.Join(", ", mine.Take(4)) + (mine.Count > 4 ? " and " + (mine.Count - 4) + " more" : "");
        }

        // ── loading ──────────────────────────────────────────────────────────

        /// <summary>"All selected games": what they agree on as it is, the rest "various values".</summary>
        private void LoadAll()
        {
            var shown = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in new[] { SystemKey }.Concat(_rows.Select(r => r.O.Key)))
            {
                var values = _games.Select(e => e.Stored.TryGetValue(key, out var v) ? v : null).Distinct().ToList();
                shown[key] = values.Count == 1 ? values[0] : Various;
            }
            Show(shown);
        }

        /// <summary>A group: its first game's values.</summary>
        private void LoadFrom(Entry e)
        {
            var shown = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in new[] { SystemKey }.Concat(_rows.Select(r => r.O.Key))) shown[key] = e.Stored.TryGetValue(key, out var v) ? v : null;
            Show(shown);
        }

        private void Show(Dictionary<string, string> shown)
        {
            _loading = true;
            FillSystem(shown[SystemKey]);
            foreach (var r in _rows) Fill(r, shown[r.O.Key]);
            _loading = false;
            _atOpen = Shown();
            Changed();
        }

        /// <summary>How many of the selected games have a value of their own for a key.</summary>
        private int OwnCount(string key) => _games.Count(e => e.Stored.ContainsKey(key));

        private string VariousText(string key) => "Various (" + OwnCount(key) + " of " + _games.Count + ")";

        private void FillSystem(string value)
        {
            _system.Items.Clear(); _systemValues.Clear();
            if (value == Various) { _system.Items.Add(VariousText(SystemKey)); _systemValues.Add(Various); }
            _system.Items.Add("As its platform (" + (_asPlatform ?? "none") + ")"); _systemValues.Add(null);
            foreach (var s in _platformSystems) { _system.Items.Add(s); _systemValues.Add(s); }
            if (value != null && value != Various && !_systemValues.Contains(value)) { _system.Items.Add(value); _systemValues.Add(value); }
            _system.SelectedIndex = Math.Max(0, _systemValues.IndexOf(value));
        }

        /// <summary>A row's list, with what it can show now: "various values" only when the games do not agree.</summary>
        private void Fill(Row r, string value)
        {
            var o = r.O;
            if (r.Shader != null) { r.ShaderValue = value; ShowShader(r); return; }
            r.Combo.Items.Clear(); r.Values.Clear();
            if (value == Various) { r.Combo.Items.Add(VariousText(o.Key)); r.Values.Add(Various); }
            r.Combo.Items.Add(FollowText(o)); r.Values.Add(null);
            for (int i = 0; i < o.Values.Length; i++)
            {
                if (o.Values[i] == AresSettings.Ares) continue;
                r.Combo.Items.Add(o.Texts[i]); r.Values.Add(o.Values[i]);
            }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
        }

        /// <summary>The first choice, which follows: every game's (the screen), or ares's own - with what ares has.</summary>
        private string FollowText(AresOption o)
        {
            if (o.OnPage) return "As every game (" + o.TextOf(AresSettings.Value(o.Key)) + ")";
            var has = AresHas(o);
            return "As set in ares" + (has != null ? " (" + has + ")" : "");
        }

        /// <summary>What ares's settings.bml has for an option, said as the list says it; null when it cannot be read.</summary>
        private string AresHas(AresOption o)
        {
            if (_bml == null) return null;
            try
            {
                if (o.Key == "Output")
                {
                    var output = _bml.Get("Video/Output");
                    if (output == null) return null;
                    if (output == "IntegerFixed") return "Integer, fixed " + (_bml.Get("Video/FixedScale") ?? "?") + "x";
                    var map = new Dictionary<string, string> { ["Scale"] = "scale", ["Integer"] = "integer", ["Stretch"] = "stretch" };
                    return map.TryGetValue(output, out var k) ? o.TextOf(k) : output;
                }
                if (o.Bml == null) return null;
                var raw = _bml.Get(o.Bml);
                if (raw == null) return null;
                if (o.Key == "Shader") return raw.Equals("None", StringComparison.OrdinalIgnoreCase) || raw.Length == 0 ? "None" : AresOption.ShaderText(raw);
                for (int i = 0; i < o.Values.Length; i++)
                {
                    var written = o.Writes?[i] ?? o.Values[i];
                    if (o.Values[i] != AresSettings.Ares && string.Equals(written, raw, StringComparison.OrdinalIgnoreCase))
                        return o.Texts[i].StartsWith("On - ") ? "On" : o.Texts[i];
                }
                // A real number ares wrote its own way (1.0 / 1.00).
                if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    for (int i = 0; i < o.Values.Length; i++)
                        if (double.TryParse(o.Values[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && Math.Abs(v - d) < 1e-6)
                            return o.Texts[i];
                    if (o.Key == "Luminance" || o.Key == "Saturation" || o.Key == "Gamma" || o.Key == "Volume") return Math.Round(d * 100) + "%";
                }
                return raw;
            }
            catch { return null; }
        }

        // ── the rows ─────────────────────────────────────────────────────────

        private static TableLayoutPanel Table()
        {
            var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 8) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static TabPage Page(string text, Control table)
        {
            var page = new TabPage(text) { AutoScroll = true, Padding = new Padding(6) };
            page.Controls.Add(table);
            return page;
        }

        private static void AddRow(TableLayoutPanel t, Panel bar, string label, Control field)
        {
            int r = t.RowCount++;
            t.Controls.Add(bar ?? new Panel { Width = 4, Height = 18 }, 0, r);
            t.Controls.Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(186, 0), Margin = new Padding(0, 7, 4, 0), UseMnemonic = false }, 1, r);
            t.Controls.Add(field, 2, r);
        }

        private static void AddText(TableLayoutPanel t, string label, string text)
            => AddRow(t, null, label, new Label { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(3, 7, 0, 4), UseMnemonic = false });

        private void AddOption(TableLayoutPanel t, AresOption o)
        {
            var row = new Row { O = o, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) } };
            if (o.Key == "Shader")
            {
                row.Shader = new Button { Width = 340, Height = 26, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
                row.Shader.Click += (_, _) => ShaderMenu(row).Show(row.Shader, new Point(0, row.Shader.Height));
                LbipHint.Attach(row.Shader, o.Hint);
                AddRow(t, row.Bar, o.Label, row.Shader);
            }
            else
            {
                row.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, MaxDropDownItems = 20 };
                row.Combo.SelectedIndexChanged += (_, _) => Changed();
                LbipHint.Attach(row.Combo, o.Hint);
                AddRow(t, row.Bar, o.Label, row.Combo);
            }
            _rows.Add(row);
        }

        /// <summary>"Reset to defaults": every setting back to what it follows - ares's own, every game's screen - and the
        /// system to its platform's. Nothing is saved before OK.</summary>
        private void Reset()
        {
            _loading = true;
            FillSystem(null);
            foreach (var r in _rows) Fill(r, null);
            _loading = false;
            Changed();
        }

        // ── the shader, as ares's menu ───────────────────────────────────────

        private void ShowShader(Row row)
        {
            var v = row.ShaderValue;
            row.Shader.Text = (v == Various ? VariousText(row.O.Key) : v == null ? FollowText(row.O) : v == "none" ? "None" : AresOption.ShaderText(v)) + "   ▾";
            Changed();
        }

        /// <summary>The menu, built now from the install's Shaders folder: each folder a sub-menu, each preset an item
        /// (ares: directory::files(location, "*.slangp"), the folders nested by their path).</summary>
        private ContextMenuStrip ShaderMenu(Row row)
        {
            var menu = new ContextMenuStrip();
            void Pick(string value) { row.ShaderValue = value; ShowShader(row); }
            ToolStripMenuItem Item(string text, string value)
            {
                var it = new ToolStripMenuItem(text) { Checked = string.Equals(row.ShaderValue, value, StringComparison.OrdinalIgnoreCase) };
                it.Click += (_, _) => Pick(value);
                return it;
            }
            if (_atOpen != null && _atOpen.TryGetValue(row.O.Key, out var was) && was == Various) menu.Items.Add(Item(VariousText(row.O.Key), Various));
            menu.Items.Add(Item(FollowText(row.O), null));
            menu.Items.Add(Item("None", "none"));
            var presets = AresSettings.Shaders(_exe);
            if (presets.Count == 0) { menu.Items.Add(new ToolStripMenuItem(_exe == null ? "(no ares in the library)" : "(no Shaders folder in " + Path.GetDirectoryName(_exe) + ")") { Enabled = false }); return menu; }
            menu.Items.Add(new ToolStripSeparator());
            // ares's order: the folders sorted by their path parts (a parent before its children), each one's sub-menu made
            // at the end of its parent's, then its presets - so in a folder, its presets come before its sub-folders.
            var folders = new Dictionary<string, ToolStripMenuItem>(StringComparer.OrdinalIgnoreCase);
            ToolStripItemCollection Under(string dir)
            {
                if (string.IsNullOrEmpty(dir)) return menu.Items;
                if (folders.TryGetValue(dir, out var f)) return f.DropDownItems;
                var cut = dir.LastIndexOf('/');
                var parent = Under(cut < 0 ? "" : dir.Substring(0, cut));
                f = new ToolStripMenuItem(cut < 0 ? dir : dir.Substring(cut + 1));
                folders[dir] = f;
                parent.Add(f);
                return f.DropDownItems;
            }
            string DirOf(string p) { var cut = p.LastIndexOf('/'); return cut < 0 ? "" : p.Substring(0, cut); }
            var byDir = presets.GroupBy(DirOf, StringComparer.OrdinalIgnoreCase)
                               .OrderBy(g => g.Key, Comparer<string>.Create((a, b) =>
                               {
                                   var pa = a.Length == 0 ? new string[0] : a.Split('/');
                                   var pb = b.Length == 0 ? new string[0] : b.Split('/');
                                   for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
                                   {
                                       int c = string.CompareOrdinal(pa[i], pb[i]);
                                       if (c != 0) return c;
                                   }
                                   return pa.Length.CompareTo(pb.Length);
                               }));
            foreach (var dir in byDir)
            {
                var items = Under(dir.Key);
                foreach (var p in dir.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var it = Item(AresOption.ShaderText(p.Substring(p.LastIndexOf('/') + 1)), p);
                    items.Add(it);
                    if (it.Checked) for (var o = it.OwnerItem as ToolStripMenuItem; o != null; o = o.OwnerItem as ToolStripMenuItem) o.Checked = true;
                }
            }
            return menu;
        }

        // ── what is shown, what goes on the line ─────────────────────────────

        /// <summary>What the window shows, every key present: Various, null (follows) or a value - for LbipGameEdit.</summary>
        private Dictionary<string, string> Shown()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            d[SystemKey] = _system.SelectedIndex < 0 ? null : _systemValues[_system.SelectedIndex];
            foreach (var r in _rows) d[r.O.Key] = r.Chosen();
            return d;
        }

        /// <summary>What a game starts with for an option, as shown: its own, else every game's (the screen), else ares's
        /// own; null when the games do not agree.</summary>
        private string Effective(Row row)
        {
            var c = row.Chosen();
            if (c == Various) return null;
            return c ?? AresSettings.Value(row.O.Key);
        }

        private void Changed()
        {
            if (_loading || _line == null || _count == null) return;
            var parts = new List<string>();
            var sys = _system.SelectedIndex < 0 ? null : _systemValues[_system.SelectedIndex];
            if (sys == Various) parts.Add("--system <each game's own>");
            else if ((sys ?? _asPlatform) != null) parts.Add("--system \"" + (sys ?? _asPlatform) + "\"");
            _systemBar.BackColor = OptionMarks.ColorOf(sys == Various ? OptionLevel.Various : sys != null ? OptionLevel.Here : OptionLevel.Emulator);
            var byKey = _rows.ToDictionary(r => r.O.Key);
            var screen = byKey.TryGetValue("Screen", out var sr) ? Effective(sr) : null;
            if (screen == "full") parts.Add("--fullscreen"); else if (screen == "pseudo") parts.Add("--pseudofullscreen");
            var settings = AresSettings.SettingArgs(key => byKey.TryGetValue(key, out var r) ? Effective(r) ?? AresSettings.Ares : AresSettings.Ares);
            parts.AddRange(settings.Select(s => "--setting " + (s.Any(char.IsWhiteSpace) ? "\"" + s + "\"" : s)));
            int various = 0;
            foreach (var r in _rows)
            {
                var c = r.Chosen();
                if (c == Various) various++;
                r.Bar.BackColor = OptionMarks.ColorOf(c == Various ? OptionLevel.Various : c != null ? OptionLevel.Here : OptionLevel.Emulator);
            }
            int n = parts.Count;
            _count.Text = (n == 0 ? "Nothing added to the command line" : n + " option(s) added to the command line, before the ROM path")
                        + (various > 0 ? " (" + various + " vary, kept per game)" : "") + ":";
            _line.Text = string.Join(" ", parts);
            _line.Select(0, 0);
        }

        /// <summary>OK: over several games, asked first; then each game gets what the user changed, and keeps the rest of its
        /// own (LbipGameEdit) - a "various values" left as it is changes nothing.</summary>
        private bool Save()
        {
            var now = Shown();
            if (!LbipGameEdit.Changed(_atOpen, now)) return true;
            if (Several && MessageBox.Show(this, "Your changes will be applied to all " + _games.Count + " selected games; what each one has of its own and you did "
                                                 + "not change stays as it is.", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return false;
            var changes = now.Where(kv => kv.Value != Various).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            int written = 0;
            foreach (var e in _games)
            {
                if (string.IsNullOrEmpty(e.Id)) continue;
                var merged = LbipGameEdit.Merge(e.Stored, _atOpen, changes, StringComparer.Ordinal);
                if (LbipGameEdit.Same(e.Stored, merged)) continue;
                merged.TryGetValue(SystemKey, out var sys);
                if (!string.Equals(sys, e.Stored.TryGetValue(SystemKey, out var was) ? was : null, StringComparison.Ordinal)) AresSettings.SetGameSystem(e.Id, sys);
                foreach (var o in AresSettings.All)
                {
                    merged.TryGetValue(o.Key, out var v);
                    e.Stored.TryGetValue(o.Key, out var before);
                    if (!string.Equals(v, before, StringComparison.Ordinal)) AresSettings.SetGameValue(e.Id, o.Key, v);
                }
                written++;
            }
            Log.Info("options saved: " + written + " of " + _games.Count + " game(s) changed");
            return true;
        }

        /// <summary>A status in its colour: green completable, amber in-game, orange starts, red nothing.</summary>
        private static Color CompatColor(string status)
            => status == "Completable" ? Color.FromArgb(0x3C, 0xB3, 0x71) : status == "In-Game" ? Color.FromArgb(0xDE, 0x96, 0x00)
             : status == "Starts" ? Color.FromArgb(0xE0, 0x6C, 0x20) : status == "Nothing" ? Color.FromArgb(0xCD, 0x2D, 0x2D) : SystemColors.GrayText;

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(AresGameMenu.IsOurs) ? new[] { AresGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == AresGameMenu.Caption) AresGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => AresGameMenu.Icon;
    }
}
