// The right-click entry on the games bsnes can run - one game or a selection: "Nixx-bsnes : Options...". The
// window, dressed as every Nixx window (NixxShell): the game's file and saves, and its own picture options, each "as
// every game" until changed - kept in games\<game id>.ini beside settings.ini (BsnesSettings) and written into
// settings.bml at its launch. At the bottom, the pack's legend, "Reset to defaults", OK and Cancel (OptionMarks).
//
// SEVERAL GAMES (the pack's rule, Mehdi, 06/10 - LbipSelection): at the top, "All selected games", the default, then the
// games grouped by identical options, to start from a group's. "All selected games" shows an option they agree on as it
// is, one they do not as "Various (n of N)" (purple): left so, each keeps its own; changed, it goes to all. OK asks
// first, then writes only what was changed. The file and saves - one game's - are not shown.
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

namespace LbIntegrations.Bsnes
{
    internal static class BsnesGameMenu
    {
        public const string Caption = "Nixx-bsnes : Options...";

        public static Image Icon => LbipMenuIcon.Of(BsnesLaunch.IsBsnesExe, p => p);

        /// <summary>A game of ours: its own emulator is a bsnes, or it is a Super Nintendo game and a bsnes is in the
        /// library ("Launch With" can pick it). Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && BsnesLaunch.IsBsnesExe(own.ApplicationPath)) return true;
                if (!BsnesPlugin.IsSnes(game.Platform)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && BsnesLaunch.IsBsnesExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new BsnesGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, "Nixx-bsnes", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class BsnesGameForm : Form
    {
        /// <summary>One option: its list, what the list's items stand for (Various, null = as every game, a value), its bar.</summary>
        private sealed class Row
        {
            public BsnesOption O;
            public ComboBox Combo;
            public readonly List<string> Values = new List<string>();
            public Panel Bar;
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex];
        }

        private readonly LbipSelection _sel;
        private readonly List<Row> _rows = new List<Row>();
        private Dictionary<string, string> _atOpen;   // what the window showed once filled - OK writes only the differences
        private bool Several => _sel.Several;
        private static IEnumerable<string> Keys => BsnesSettings.All.Select(o => o.Key);

        public BsnesGameForm(IGame[] games)
        {
            _sel = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = Safe(() => g.Title) ?? "?", Stored = StoredOf(Safe(() => g.Id)), Tag = g }));
            var game = games[0];
            Text = !Several ? "Nixx-bsnes - " + Safe(() => game.Title) : "Nixx-bsnes - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(700, 560);
            Font = new Font("Segoe UI", 9f);

            var page = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 10, 14, 6) };
            GroupBox Card(string title, params Control[] rows)
            {
                var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(650, 0) };
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
                flow.Controls.AddRange(rows);
                box.Controls.Add(flow);
                return box;
            }
            Label Line(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 2) };

            // ── the top: one game's title, or the list of where to start from ──
            var top = _sel.Top(Keys, Describe, ShowValues);
            if (!Several)
                page.Controls.Add(new Label { Text = Safe(() => game.Title), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 0, 10) });

            // ── the game to bsnes: one game's, not shown for several ──
            if (!Several)
            {
                var rom = Safe(() => game.ApplicationPath) ?? "";
                var rows = new List<Control> { Line("File: " + rom) };
                var exe = Settings.FirstExe();
                if (exe != null && rom.Length > 0)
                {
                    var full = BsnesPlugin.ResolveFullPathForUi(rom);
                    var folders = BsnesPlugin.FoldersFor(exe, full);
                    var name = BsnesPlugin.NameOf(full);
                    var srm = folders.SaveDir == null ? null : Path.Combine(folders.SaveDir, name + ".srm");
                    var bsz = folders.StateDir == null ? null : Path.Combine(folders.StateDir, name + ".bsz");
                    rows.Add(Line("Cartridge save: " + (srm != null && File.Exists(srm) ? srm : "none yet - " + (srm ?? "beside the game"))));
                    rows.Add(Line("Save states: " + (bsz != null && File.Exists(bsz) ? bsz + " (every slot in one file)" : "none yet")));
                }
                page.Controls.Add(Card("The game", rows.ToArray()));
            }

            // ── the options ──
            var optionRows = new List<Control>();
            foreach (var o in BsnesSettings.All)
            {
                var r = new Row { O = o, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 3, 8, 0) } };
                var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                line.Controls.Add(r.Bar);
                line.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                r.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
                r.Combo.SelectedIndexChanged += (_, _) => r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
                LbipHint.Attach(r.Combo, o.Hint);
                line.Controls.Add(r.Combo);
                _rows.Add(r);
                optionRows.Add(line);
            }
            optionRows.Add(LbipHint.Note("Written into settings.bml when the game starts.", "Every game's values are in the Nixx window, page bsnes. "
                                         + (Several ? "These are kept per game" : "These are kept for this game alone") + " (games\\<game id>.ini).", 620, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(!Several ? "This game's options" : "Their options", optionRows.ToArray()));

            // ── the bottom: the pack's legend, reset, OK and Cancel ──
            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-bsnes"); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("bsnes", false, false, Several, "As every game"), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            if (top != null) Controls.Add(top);
            Controls.Add(bottom);
            if (_sel.Source != null) _sel.Source.SelectedIndex = 0; else ShowValues(_sel.From(_sel.Members[0], Keys));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        // ── a game's stored values ───────────────────────────────────────────

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var o in BsnesSettings.All) { var v = BsnesSettings.GameValue(id, o.Key); if (v != null) d[o.Key] = v; }
            return d;
        }

        private static string Describe(LbipSelection.Member m)
        {
            var mine = BsnesSettings.All.Where(o => m.Stored.ContainsKey(o.Key)).Select(o => o.Label).ToList();
            return mine.Count == 0 ? "as every game" : "its own: " + string.Join(", ", mine.Take(4)) + (mine.Count > 4 ? " and " + (mine.Count - 4) + " more" : "");
        }

        // ── filling ──────────────────────────────────────────────────────────

        private void ShowValues(Dictionary<string, string> shown)
        {
            foreach (var r in _rows) Fill(r, shown.TryGetValue(r.O.Key, out var v) ? v : null);
            _atOpen = ShownNow();
        }

        /// <summary>A row's list: "Various (n of N)" first only when the games do not agree, then "as every game", then the values.</summary>
        private void Fill(Row r, string value)
        {
            var o = r.O;
            r.Combo.Items.Clear(); r.Values.Clear();
            if (value == LbipSelection.Various) { r.Combo.Items.Add(_sel.VariousText(o.Key)); r.Values.Add(LbipSelection.Various); }
            r.Combo.Items.Add("As every game (" + o.TextOf(BsnesSettings.Value(o.Key)) + ")"); r.Values.Add(null);
            for (int i = 0; i < o.Values.Length; i++) { r.Combo.Items.Add(o.Texts[i]); r.Values.Add(o.Values[i]); }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
            r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
        }

        /// <summary>"Reset to defaults": every option back to "as every game". Nothing is saved before OK.</summary>
        private void Reset()
        {
            foreach (var r in _rows) Fill(r, null);
        }

        /// <summary>What the window shows, every key present: Various, null (as every game) or a value.</summary>
        private Dictionary<string, string> ShownNow()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in _rows) d[r.O.Key] = r.Chosen();
            return d;
        }

        /// <summary>OK: over several games, asked first; then each game gets only what the user changed - a "Various" left
        /// as it is writes nothing.</summary>
        private bool Save()
        {
            var now = ShownNow();
            if (!LbipSelection.Changed(_atOpen, now)) return true;
            if (!_sel.Confirm(this, Text)) return false;
            int written = 0;
            foreach (var m in _sel.Members)
            {
                var id = Safe(() => ((IGame)m.Tag).Id);
                if (string.IsNullOrEmpty(id)) continue;
                var merged = _sel.MergeFor(m, _atOpen, now);
                bool any = false;
                foreach (var o in BsnesSettings.All)
                {
                    merged.TryGetValue(o.Key, out var v);
                    m.Stored.TryGetValue(o.Key, out var before);
                    if (string.Equals(v, before, StringComparison.Ordinal)) continue;
                    BsnesSettings.SetGameValue(id, o.Key, v);
                    any = true;
                }
                if (any) written++;
            }
            Log.Info("options saved: " + written + " of " + _sel.Members.Count + " game(s) changed");
            return true;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(BsnesGameMenu.IsOurs) ? new[] { BsnesGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == BsnesGameMenu.Caption) BsnesGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => BsnesGameMenu.Icon;
    }
}
