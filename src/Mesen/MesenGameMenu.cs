// The right-click entry on the games Mesen can run - one game or a selection: "Nixx-Mesen : Options...". The window, dressed
// as every Nixx window (NixxShell), in tabs (FceuxGameMenu's model): GAME - its file, the console Mesen runs it as (detected
// by Mesen from the file, Mehdi 19/10; a choice only where Mesen has a model to pick - Game Boy or Color or Super Game Boy,
// PC Engine / TurboGrafx / SuperGrafx, the WonderSwans), its BIOS, its screen; then Picture and Emulation, a few of Mesen's
// options (MesenSettings.All) - only those the game's console has. At the bottom, the switches the launch adds for it -
// updated at every change - then the pack's legend, "Reset to defaults", OK and Cancel (OptionMarks). "As set in Mesen" says
// what Mesen has: its settings.json is read, never written, when the window opens.
//
// A GAME'S OPTIONS ARE FOR ITS SESSION: switches on the line, Mesen's own values put back once it has quit (MesenSession). The
// screen is every game's (the Nixx page) unless a game sets its own.
//
// SEVERAL GAMES (the pack's rule, memory "multi-game-window-rule"): at the top, "All selected games (N)", the default, then
// the games grouped by identical options. "All selected games" shows an option they agree on as it is, one they do not as
// "Various (n of N)" (purple): left so, each keeps its own; changed, it goes to all - a game whose console has no such option
// (a filter for a NES game and a Game Boy one: each its own console's) or not that value (Dendy for a SNES game) is left as
// it is at the launch. OK asks first, then changes only what was changed (LbipGameEdit).
//
// NOT AN IGameMenuItemPlugin, as for the other plugins of the pack: LaunchBox 14 gives a plugin of Local\Plugins its
// emulator role only; Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Mesen
{
    internal static class MesenGameMenu
    {
        public const string Caption = "Nixx-Mesen : Options...";

        /// <summary>Mesen's own icon, from the first install of ours in the library (LbipMenuIcon) - never null.</summary>
        public static Image Icon => LbipMenuIcon.Of(MesenLaunch.IsMesenExe, MesenPlugin.ResolveFullPathForUi);

        private static bool IsMesen(string path) { try { return MesenLaunch.IsMesenExe(MesenPlugin.ResolveFullPathForUi(path)); } catch { return false; } }

        /// <summary>A game of ours: its own emulator is a Mesen, or its platform is one Mesen runs and a Mesen is in the library
        /// ("Launch With" can pick it). Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && IsMesen(own.ApplicationPath)) return true;
                if (MesenSystems.Platform(game.Platform) == null) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && IsMesen(e.ApplicationPath));
            }
            catch { return false; }
        }

        /// <summary>The Mesen whose settings the window reads: the first of the library.</summary>
        internal static string FirstExe()
        {
            try
            {
                var e = (PluginHelper.DataManager?.GetAllEmulators() ?? new IEmulator[0]).FirstOrDefault(x => x != null && IsMesen(x.ApplicationPath));
                return e == null ? null : MesenPlugin.ResolveFullPathForUi(e.ApplicationPath);
            }
            catch { return null; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new MesenGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, "Nixx-Mesen", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class MesenGameForm : Form
    {
        private const string Various = "\u0001various";   // "All selected games", the games do not agree
        internal const string SystemKey = "System";

        /// <summary>One option's row: its list, what the list's items stand for, the bar that says where it comes from.</summary>
        private sealed class Row
        {
            public MesenOption O;
            public ComboBox Combo;
            public List<string> Values = new List<string>();   // Various, null (follows), a value
            public Panel Bar;
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex];
        }

        private sealed class Entry { public IGame Game; public string Id, Title; public MesenSystem Detected, Runs; public Dictionary<string, string> Stored; public string Key; }

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private readonly ComboBox _source;
        private readonly List<Row> _rows = new List<Row>();
        private readonly string _exe;
        private readonly MesenJson _json;       // Mesen's own settings, read when the window opens - never written here
        private readonly MesenOption _system;   // the console row, made for these games (their platforms' models)
        private readonly TextBox _line;
        private readonly Label _count;
        private Dictionary<string, string> _atOpen;
        private bool _loading;
        private bool Several => _games.Count > 1;

        /// <summary>For the probe, with no LaunchBox: the Mesen whose settings the window reads.</summary>
        internal static string ExeForProbe;

        public MesenGameForm(IGame[] games)
        {
            _games = games.Select(g => new Entry { Game = g, Id = Safe(() => g.Id), Title = Safe(() => g.Title) ?? "?" }).ToList();
            foreach (var e in _games)
            {
                e.Detected = SystemOf(e.Game, null);
                e.Runs = SystemOf(e.Game, e.Id);
                e.Stored = StoredOf(e.Id);
                e.Key = string.Join("\n", e.Stored.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            }
            _groups = _games.GroupBy(e => e.Key).Select(g => g.ToList()).ToList();
            Text = !Several ? "Nixx-Mesen - " + _games[0].Title : "Nixx-Mesen - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(760, 600);
            MinimumSize = new Size(640, 480);
            Font = new Font("Segoe UI", 9f);

            _exe = ExeForProbe ?? MesenGameMenu.FirstExe();
            try { if (_exe != null) _json = MesenJson.Load(MesenLaunch.SettingsOf(_exe)); } catch (Exception ex) { Log.Warn("Mesen's settings could not be read for the window", ex); }
            _system = SystemOption(_games);

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
            var gameTab = Table();
            if (!Several)
            {
                var e = _games[0];
                AddText(gameTab, "File", Safe(() => e.Game.ApplicationPath) ?? "");
                // With a console to choose, its row says what Mesen detects.
                if (_system == null || e.Detected == null)
                    AddText(gameTab, "Console", e.Detected == null ? "none Mesen runs - the game will not start" : e.Detected.Name + " (as Mesen tells it from the file)");
                if (e.Runs != null && _exe != null)
                {
                    var needed = MesenBios.Ensure(_exe, new[] { e.Runs }, optional: false, readOnly: true).Where(b => b.Required).ToList();
                    if (needed.Count > 0)
                        AddText(gameTab, "BIOS", string.Join("\n", needed.Select(b => b.Spec.File + ": " + (b.Path != null && b.SizeOk ? b.Path : "missing in " + MesenBios.FolderOf(_exe) + " - the game will not start"))));
                }
            }
            else AddText(gameTab, "Consoles", string.Join(", ", _games.Select(e => e.Detected?.Name ?? "?").Distinct()));
            if (_system != null) AddOption(gameTab, _system);
            foreach (var o in MesenSettings.All.Where(x => x.Group == "Game")) AddOption(gameTab, o);
            tabs.TabPages.Add(Page("Game", gameTab));

            // ── a tab per group: the options at least one of the games' consoles has ──
            foreach (var group in MesenSettings.Groups)
            {
                var options = MesenSettings.All.Where(x => x.Group == group && _games.Any(e => x.AppliesTo(e.Runs))).ToList();
                if (options.Count == 0) continue;
                var table = Table();
                foreach (var o in options) AddOption(table, o);
                tabs.TabPages.Add(Page(group, table));
            }

            // ── the bottom: the switches the launch adds, then the pack's legend, reset, OK and Cancel ──
            var summary = new Panel { Dock = DockStyle.Bottom, Height = 118, Padding = new Padding(12, 6, 12, 2) };
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, TabStop = false };
            var note = LbipHint.Note("Added to Mesen's command line when the game starts; Mesen's own values come back when it quits.",
                "Kept in games\\<game id>.ini. Mesen keeps a command line's settings in its settings.json, so the plugin notes Mesen's own values "
                + "before the launch and puts them back once it has quit. The screen is the exception: it is the Nixx page's, every game's, unless "
                + "a game sets its own. \"As set in Mesen\" adds nothing: what Mesen's own menus have is used. A picture option is Mesen's override "
                + "for the game's console (Settings > the console > Video), so it never reaches another console's games.", 700, new Padding(0, 2, 0, 0));
            note.Dock = DockStyle.Bottom;
            summary.Controls.Add(_line);
            summary.Controls.Add(_count);
            summary.Controls.Add(note);

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-Mesen"); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("Mesen", gameConfig: false, hereLoses: false, various: Several), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(summary);
            Controls.Add(bottom);
            if (_source != null) _source.SelectedIndex = 0; else LoadFrom(_games[0]);
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        /// <summary>The console Mesen runs a game as: with <paramref name="gameId"/>, the game's own choice included; without,
        /// what Mesen tells from the file (an archive's game read from its entries; the window opens whatever).</summary>
        private static MesenSystem SystemOf(IGame g, string gameId)
        {
            try
            {
                var rom = MesenPlugin.ResolveFullPathForUi(Safe(() => g.ApplicationPath) ?? "");
                var ext = Path.GetExtension(MesenLaunch.Inner(rom) ?? "").ToLowerInvariant();
                return MesenLaunch.SystemFor(gameId, Safe(() => g.Platform), ext, "").System;
            }
            catch { return null; }
        }

        /// <summary>The console row: the models of the games' platforms Mesen can be told to run (a switch); null when none has.</summary>
        private static MesenOption SystemOption(List<Entry> games)
        {
            var systems = games.SelectMany(e => MesenSystems.Platform(Safe(() => e.Game.Platform))?.Systems ?? new string[0])
                               .Select(MesenSystems.System).Where(s => s?.Switch != null).Distinct().ToList();
            if (systems.Count < 2) return null;
            return new MesenOption
            {
                Key = SystemKey, Group = "Game", Label = "Console", Values = systems.Select(s => s.Key).ToArray(), Texts = systems.Select(s => s.Name).ToArray(),
                Hint = "The console Mesen runs the game as. Left as detected, Mesen tells it from the file and its own setting (a Game Boy game "
                       + "in colour on the Game Boy Color, say). Chosen, the model is set for the game's session (--gameboy.model=..., --ws.model=..., "
                       + "--pcEngine.consoleType=...).",
            };
        }

        // ── a game's stored values ───────────────────────────────────────────

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            var sys = MesenSettings.GameSystem(id);
            if (sys != null) d[SystemKey] = sys;
            foreach (var o in MesenSettings.All) { var v = MesenSettings.GameValue(id, o.Key); if (v != null) d[o.Key] = v; }
            return d;
        }

        private string Describe(Entry e)
        {
            var labels = MesenSettings.All.Select(o => (Key: o.Key, Label: o.Label)).Append((Key: SystemKey, Label: "Console"));
            var mine = labels.Where(l => e.Stored.ContainsKey(l.Key)).Select(l => l.Label).ToList();
            return mine.Count == 0 ? "Mesen's settings" : "its own: " + string.Join(", ", mine.Take(4)) + (mine.Count > 4 ? " and " + (mine.Count - 4) + " more" : "");
        }

        // ── loading ──────────────────────────────────────────────────────────

        /// <summary>"All selected games": what they agree on as it is, the rest "Various".</summary>
        private void LoadAll()
        {
            var shown = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in _rows.Select(r => r.O.Key))
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
            foreach (var key in _rows.Select(r => r.O.Key)) shown[key] = e.Stored.TryGetValue(key, out var v) ? v : null;
            Show(shown);
        }

        private void Show(Dictionary<string, string> shown)
        {
            _loading = true;
            foreach (var r in _rows) Fill(r, shown[r.O.Key]);
            _loading = false;
            _atOpen = ShownNow();
            Changed();
        }

        private int OwnCount(string key) => _games.Count(e => e.Stored.ContainsKey(key));

        private string VariousText(string key) => "Various (" + OwnCount(key) + " of " + _games.Count + ")";

        /// <summary>The one console of the games, when they all run on the same; null otherwise.</summary>
        private MesenSystem OneSystem()
        {
            var s = _games.Select(e => e.Runs).Distinct().ToList();
            return s.Count == 1 ? s[0] : null;
        }

        /// <summary>A row's list: "Various" only when the games do not agree, then what it follows, then the values - for one
        /// console, only those it takes.</summary>
        private void Fill(Row r, string value)
        {
            var o = r.O;
            var one = OneSystem();
            r.Combo.Items.Clear(); r.Values.Clear();
            if (value == Various) { r.Combo.Items.Add(VariousText(o.Key)); r.Values.Add(Various); }
            r.Combo.Items.Add(FollowText(o)); r.Values.Add(null);
            for (int i = 0; i < o.Values.Length; i++)
            {
                if (o.Key != SystemKey && !o.OnPage && one != null && !o.Takes(one, o.Values[i]) && o.Values[i] != value) continue;
                r.Combo.Items.Add(o.Texts[i]); r.Values.Add(o.Values[i]);
            }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
        }

        /// <summary>The first choice, which follows: every game's (the screen), what Mesen detects (the console), or Mesen's own
        /// setting - with what Mesen has, for one console.</summary>
        private string FollowText(MesenOption o)
        {
            if (o.OnPage) return "As every game (" + o.TextOf(MesenSettings.Value(o.Key)) + ")";
            if (o.Key == SystemKey)
            {
                var detected = _games.Select(e => e.Detected?.Name).Distinct().ToList();
                return "As Mesen detects it" + (detected.Count == 1 && detected[0] != null ? " (" + detected[0] + ")" : "");
            }
            var one = OneSystem();
            if (one != null && o.AppliesTo(one) && MesenSettings.Missing(MesenSettings.Switches(o, one, o.Values.First(v => o.Takes(one, v))), _json) != null)
                return "Not in this Mesen - left out";
            var has = one == null ? null : MesenSettings.InMesen(o, one, _json);
            return "As set in Mesen" + (has != null ? " (" + has + ")" : "");
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

        private void AddOption(TableLayoutPanel t, MesenOption o)
        {
            var row = new Row { O = o, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) } };
            row.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, MaxDropDownItems = 20 };
            row.Combo.SelectedIndexChanged += (_, _) => Changed();
            LbipHint.Attach(row.Combo, o.Hint);
            AddRow(t, row.Bar, o.Label, row.Combo);
            _rows.Add(row);
        }

        /// <summary>"Reset to defaults": every option back to what it follows - Mesen's own, every game's screen. Nothing is
        /// saved before OK.</summary>
        private void Reset()
        {
            _loading = true;
            foreach (var r in _rows) Fill(r, null);
            _loading = false;
            Changed();
        }

        // ── what is shown, what goes on the line ─────────────────────────────

        private Dictionary<string, string> ShownNow()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in _rows) d[r.O.Key] = r.Chosen();
            return d;
        }

        /// <summary>The switches a game gets, as shown: a row's choice, its own where the row is "Various" or not shown.</summary>
        internal static List<string> SwitchesShown(MesenSystem detected, string gameId, Func<string, (bool Shown, string Value)> shown)
        {
            var list = new List<string>();
            string Pick(string key, Func<string> own) { var s = shown(key); return !s.Shown || s.Value == Various ? own() : s.Value; }
            var sys = MesenSystems.System(Pick(SystemKey, () => MesenSettings.GameSystem(gameId)));
            var runs = sys ?? detected;
            if (sys?.Switch != null) list.Add(sys.Switch);
            if ((Pick("Fullscreen", () => MesenSettings.GameValue(gameId, "Fullscreen")) ?? MesenSettings.Value("Fullscreen")) == "true") list.Add("--fullscreen");
            foreach (var o in MesenSettings.All.Where(o => !o.OnPage))
            {
                var v = Pick(o.Key, () => MesenSettings.GameValue(gameId, o.Key));
                if (v != null) list.AddRange(MesenSettings.Switches(o, runs, v));
            }
            return list;
        }

        private void Changed()
        {
            if (_loading || _line == null || _count == null) return;
            var byKey = _rows.ToDictionary(r => r.O.Key);
            var first = _games[0];
            var switches = SwitchesShown(first.Detected, first.Id, key => byKey.TryGetValue(key, out var r) ? (true, r.Chosen()) : (false, null));
            int various = 0;
            foreach (var r in _rows)
            {
                var c = r.Chosen();
                if (c == Various) various++;
                r.Bar.BackColor = OptionMarks.ColorOf(c == Various ? OptionLevel.Various : c != null ? OptionLevel.Here : OptionLevel.Emulator);
            }
            int session = switches.Count(s => s != "--fullscreen");
            _count.Text = (session == 0 ? "Nothing added for this game's session alone" : session + " switch(es) for the game's session, Mesen's own values put back after")
                        + (Several ? " - " + first.Title + "'s" : "") + (various > 0 ? " (" + various + " vary, kept per game)" : "") + ":";
            _line.Text = string.Join("\r\n", switches.Select(s => s + (s == "--fullscreen" ? "   (the screen)" : "")));
            _line.Select(0, 0);
        }

        /// <summary>OK: over several games, asked first; then each game gets what the user changed, and keeps the rest of its
        /// own (LbipGameEdit) - a "Various" left as it is changes nothing.</summary>
        private bool Save()
        {
            var now = ShownNow();
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
                foreach (var key in _rows.Select(r => r.O.Key))
                {
                    merged.TryGetValue(key, out var v);
                    e.Stored.TryGetValue(key, out var before);
                    if (string.Equals(v, before, StringComparison.Ordinal)) continue;
                    if (key == SystemKey) MesenSettings.SetGameSystem(e.Id, v);
                    else MesenSettings.SetGameValue(e.Id, key, v);
                }
                written++;
            }
            Log.Info("options saved: " + written + " of " + _games.Count + " game(s) changed");
            return true;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        internal const string Caption = MesenGameMenu.Caption;

        public static string[] Entries(IGame[] games)
            => games != null && games.Any(MesenGameMenu.IsOurs) ? new[] { MesenGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == MesenGameMenu.Caption) MesenGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => MesenGameMenu.Icon;
    }
}
