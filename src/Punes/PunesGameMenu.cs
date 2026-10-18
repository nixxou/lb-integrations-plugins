// The right-click entry on the games puNES can run - one game or a selection: "Nixx-puNES : Options...". The window,
// dressed as every Nixx window (NixxShell), in tabs (ares's model, AresGameMenu): GAME - the system puNES runs it as (its
// own choice from the file, PunesSystems.SystemFor - nothing to pick), its file, its BIOS, its screen and region; then a
// tab per group of options (Picture, Emulation), an option that means nothing for the selection's systems left out. At the
// bottom, what goes on puNES's command line for it, as the SUPER ZSNES window shows it - updated at every change - then
// the pack's legend, "Reset to defaults", OK and Cancel (OptionMarks). "As set in puNES" says what puNES has: its
// puNES.cfg is read, never written, when the window opens.
//
// SEVERAL GAMES (the pack's rule, memory "multi-game-window-rule"): at the top, "All selected games (N)", the default, then
// the games grouped by identical options. "All selected games" shows an option they agree on as it is, one they do not as
// "Various (n of N)" (purple): left so, each keeps its own; changed, it goes to all. OK asks first, then changes only what
// was changed (LbipGameEdit) - each game keeps the rest of its own.
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

namespace LbIntegrations.Punes
{
    internal static class PunesGameMenu
    {
        public const string Caption = "Nixx-puNES : Options...";

        public static Image Icon => LbipMenuIcon.Of(PunesLaunch.IsPunesExe, PunesPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is a puNES, or its platform is one puNES runs and a puNES is in the
        /// library ("Launch With" can pick it). Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && PunesLaunch.IsPunesExe(own.ApplicationPath)) return true;
                if (PunesSystems.Platform(game.Platform) == null) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && PunesLaunch.IsPunesExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new PunesGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, "Nixx-puNES", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class PunesGameForm : Form
    {
        private const string Various = "\u0001various";   // "All selected games", the games do not agree

        /// <summary>One option's row: its list, what the list's items stand for, the bar that says where it comes from.</summary>
        private sealed class Row
        {
            public PunesOption O;
            public ComboBox Combo;
            public List<string> Values = new List<string>();   // Various, null (follows), a value
            public Panel Bar;
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex];
        }

        private sealed class Entry { public IGame Game; public string Id, Title; public Dictionary<string, string> Stored; public string Key; }

        private readonly List<Entry> _games;
        private readonly List<List<Entry>> _groups;
        private readonly ComboBox _source;
        private readonly List<Row> _rows = new List<Row>();
        private readonly string _exe;
        private readonly string _system;        // the one system of the selection, or null when they differ
        private readonly PunesCfg _cfg;         // puNES's own settings, read when the window opens - never written here
        private readonly TextBox _line;
        private readonly Label _count;
        private Dictionary<string, string> _atOpen;
        private bool _loading;
        private bool Several => _games.Count > 1;

        /// <summary>For the probe, with no LaunchBox: the puNES whose settings the window reads.</summary>
        internal static string ExeForProbe;

        public PunesGameForm(IGame[] games)
        {
            _games = games.Select(g => new Entry { Game = g, Id = Safe(() => g.Id), Title = Safe(() => g.Title) ?? "?" }).ToList();
            foreach (var e in _games)
            {
                e.Stored = StoredOf(e.Id);
                e.Key = string.Join("\n", e.Stored.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            }
            _groups = _games.GroupBy(e => e.Key).Select(g => g.ToList()).ToList();
            var game = games[0];
            Text = !Several ? "Nixx-puNES - " + _games[0].Title : "Nixx-puNES - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(640, 500);
            Font = new Font("Segoe UI", 9f);

            _exe = ExeForProbe ?? Settings.FirstExe();
            try { if (_exe != null) _cfg = PunesCfg.Load(PunesCfg.MainOf(_exe)); } catch (Exception ex) { Log.Warn("puNES's settings could not be read for the window", ex); }
            // The systems of the selection, as puNES will pick them from the files: a system's options show when one is of it.
            var systems = _games.Select(e => PunesSystems.SystemFor(Safe(() => e.Game.Platform), Safe(() => e.Game.ApplicationPath), InnerExtension(Safe(() => e.Game.ApplicationPath))).System?.Id)
                                .Where(s => s != null).Distinct().ToList();
            _system = systems.Count == 1 ? systems[0] : null;

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
                var rom = Safe(() => game.ApplicationPath) ?? "";
                var full = PunesPlugin.ResolveFullPathForUi(rom);
                AddText(gameTab, "File", rom);
                var (sys, from) = PunesSystems.SystemFor(Safe(() => game.Platform), full, InnerExtension(full));
                AddText(gameTab, "System", sys == null ? "unknown - puNES cannot tell from the file, and its platform is not one this plugin knows"
                                                       : sys.Name + " (by " + from + " - puNES picks it itself)");
                if (sys != null && _exe != null && sys.Bios.Length > 0)
                    AddText(gameTab, "BIOS", string.Join(";  ", sys.Bios.Select(b => b.Name + ": " + (PunesFirmware.Found(_exe, b, full) ?? (b.Required ? "missing - the game will not start" : "-")))));
                if (_exe != null && !PunesLaunch.Givable(full))
                    AddText(gameTab, "Cannot start", "its path holds an '=' sign, which puNES cuts its command line at - rename the file or its folder");
            }
            else AddText(gameTab, "Systems", systems.Count == 0 ? "unknown" : string.Join(", ", systems.Select(s => PunesSystems.System(s).Name)));
            foreach (var o in PunesSettings.All.Where(x => x.Group == "Game")) AddOption(gameTab, o);
            tabs.TabPages.Add(Page("Game", gameTab));

            // ── a tab per group ──
            foreach (var group in PunesSettings.Groups)
            {
                var options = PunesSettings.All.Where(x => x.Group == group && (systems.Count == 0 || systems.Any(x.AppliesTo))).ToList();
                if (options.Count == 0) continue;
                var table = Table();
                foreach (var o in options) AddOption(table, o);
                tabs.TabPages.Add(Page(group, table));
            }

            // ── the bottom: what goes on the line, then the pack's legend, reset, OK and Cancel ──
            var summary = new Panel { Dock = DockStyle.Bottom, Height = 118, Padding = new Padding(12, 6, 12, 2) };
            _count = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window, TabStop = false };
            var note = LbipHint.Note("Given to puNES when the game starts; puNES does not keep them.",
                "Kept in games\\<game id>.ini. Each option is passed as --option=value, before what the platform's line in LaunchBox has (an option "
                + "there wins: puNES takes the last). \"As set in puNES\" passes nothing: what puNES's own menus have is used.", 700, new Padding(0, 2, 0, 0));
            note.Dock = DockStyle.Bottom;
            summary.Controls.Add(_line);
            summary.Controls.Add(_count);
            summary.Controls.Add(note);

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-puNES"); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("puNES", gameConfig: false, hereLoses: false, various: Several), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(summary);
            Controls.Add(bottom);
            if (_source != null) _source.SelectedIndex = 0; else LoadFrom(_games[0]);
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        /// <summary>The extension of the game inside an archive, read from its entries - null for a plain file, or when it
        /// cannot be read (the window must open whatever).</summary>
        private static string InnerExtension(string rom)
        {
            try
            {
                var full = PunesPlugin.ResolveFullPathForUi(rom);
                if (string.IsNullOrEmpty(full) || !PunesSystems.IsArchive(full) || !File.Exists(full)) return null;
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(full);
                var name = Path.GetFileNameWithoutExtension(full);
                var games = archive.Entries.Where(e => !e.IsDirectory && PunesSystems.OfExtension(Path.GetExtension(e.Key ?? "")) != null).ToList();
                var pick = games.Count == 1 ? games[0] : games.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Key), name, StringComparison.OrdinalIgnoreCase));
                return pick == null ? null : Path.GetExtension(pick.Key).ToLowerInvariant();
            }
            catch { return null; }
        }

        // ── a game's stored values ───────────────────────────────────────────

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var o in PunesSettings.All) { var v = PunesSettings.GameValue(id, o.Key); if (v != null) d[o.Key] = v; }
            return d;
        }

        private static string Describe(Entry e)
        {
            var mine = PunesSettings.All.Where(o => e.Stored.ContainsKey(o.Key)).Select(o => o.Label).ToList();
            return mine.Count == 0 ? "puNES's settings" : "its own: " + string.Join(", ", mine.Take(4)) + (mine.Count > 4 ? " and " + (mine.Count - 4) + " more" : "");
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

        /// <summary>A row's list: "Various" only when the games do not agree, then what it follows, then the values.</summary>
        private void Fill(Row r, string value)
        {
            var o = r.O;
            r.Combo.Items.Clear(); r.Values.Clear();
            if (value == Various) { r.Combo.Items.Add(VariousText(o.Key)); r.Values.Add(Various); }
            r.Combo.Items.Add(FollowText(o)); r.Values.Add(null);
            for (int i = 0; i < o.Values.Length; i++)
            {
                // "As set in puNES" is the first item already - but for the screen, whose first item is every game's.
                if (o.Values[i] == PunesSettings.Punes && !o.OnPage) continue;
                var text = o.Values[i] == PunesSettings.Punes ? FollowPunes(o) : o.Texts[i];
                r.Combo.Items.Add(text); r.Values.Add(o.Values[i]);
            }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
        }

        /// <summary>The first choice, which follows: every game's (the screen), or puNES's own - with what puNES has.</summary>
        private string FollowText(PunesOption o)
            => o.OnPage ? "As every game (" + (PunesSettings.Value(o.Key) == PunesSettings.Punes ? FollowPunes(o) : o.TextOf(PunesSettings.Value(o.Key))) + ")" : FollowPunes(o);

        private string FollowPunes(PunesOption o)
        {
            var has = PunesSettings.CfgHas(o, _cfg);
            return "As set in puNES" + (has != null ? " (" + has + ")" : "");
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

        private void AddOption(TableLayoutPanel t, PunesOption o)
        {
            var row = new Row { O = o, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 6, 8, 0) } };
            row.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, MaxDropDownItems = 20 };
            row.Combo.SelectedIndexChanged += (_, _) => Changed();
            LbipHint.Attach(row.Combo, o.Hint);
            AddRow(t, row.Bar, o.Label, row.Combo);
            _rows.Add(row);
        }

        /// <summary>"Reset to defaults": every option back to what it follows - puNES's own, every game's screen. Nothing is
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

        /// <summary>What a game starts with for an option, as shown: its own, else every game's; null when they do not agree.</summary>
        private string Effective(Row row)
        {
            var c = row.Chosen();
            if (c == Various) return null;
            return c ?? PunesSettings.Value(row.O.Key);
        }

        private void Changed()
        {
            if (_loading || _line == null || _count == null) return;
            var byKey = _rows.ToDictionary(r => r.O.Key);
            var parts = PunesSettings.Args(key => byKey.TryGetValue(key, out var r) ? Effective(r) : null, _system);
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
                foreach (var o in PunesSettings.All)
                {
                    merged.TryGetValue(o.Key, out var v);
                    e.Stored.TryGetValue(o.Key, out var before);
                    if (!string.Equals(v, before, StringComparison.Ordinal)) PunesSettings.SetGameValue(e.Id, o.Key, v);
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
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(PunesGameMenu.IsOurs) ? new[] { PunesGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == PunesGameMenu.Caption) PunesGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => PunesGameMenu.Icon;
    }
}
