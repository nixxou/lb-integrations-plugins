// The right-click entry on the games ZiNc EX can run - one game or a selection: "Nixx-ZiNc-EX : Options...". The window,
// dressed as every Nixx window (NixxShell): what the game is to ZiNc - the game its romset was found to be (ZincGames), the
// zips it is given under ZiNc's names, or why it cannot start - with the ZiNc game it is started as, when telling it by its
// ROMs is not what is wanted; and how its window opens, "as every game" until changed - kept in games\<game id>.ini beside
// settings.ini (ZincExSettings). A bar at the left of each option says where it comes from (OptionMarks).
//
// SEVERAL GAMES (the pack's rule, Mehdi, 06/10 - LbipSelection): at the top "All selected games", the default, then the
// games grouped by identical options. A setting they do not agree on shows "Various (n of N)", purple: left so, each keeps
// its own; changed, it goes to all. OK asks first, then writes only what was changed. What is about one game only - its
// romset, the ZiNc game it is - is not shown.
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

namespace LbIntegrations.ZincEx
{
    internal static class ZincExGameMenu
    {
        public const string Caption = "Nixx-ZiNc-EX : Options...";

        public static Image Icon => LbipMenuIcon.Of(ZincExLaunch.IsZincEx, ZincExPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is a ZiNc EX - Arcade is MAME's platform, and only 71 of its games are
        /// ZiNc's, so the platform alone does not make one. Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null || string.IsNullOrWhiteSpace(game.EmulatorId)) return false;
                var own = dm.GetEmulatorById(game.EmulatorId);
                return own != null && ZincExLaunch.IsZincEx(own.ApplicationPath);
            }
            catch { return false; }
        }

        internal static string ExeOf(IGame game)
        {
            try
            {
                var own = PluginHelper.DataManager?.GetEmulatorById(game.EmulatorId);
                var path = ZincExPlugin.ResolveFullPathForUi(own?.ApplicationPath);
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new ZincExGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, ZincExPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class ZincExGameForm : Form
    {
        /// <summary>One option: its list, what its items stand for (Various, null = as every game, a stored value), its bar.</summary>
        private sealed class Option
        {
            public string Key, Label, Follow;
            public string[] Texts, Values;
            public ComboBox Combo;
            public Panel Bar;
            public readonly List<string> Shown = new List<string>();
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Shown[Combo.SelectedIndex];
        }

        private readonly IGame[] _games;
        private readonly LbipSelection _selection;
        private readonly List<Option> _options = new List<Option>();
        private Dictionary<string, string> _atOpen;
        private ComboBox _set;
        private string _setAtOpen;

        public ZincExGameForm(IGame[] games)
        {
            _games = games;
            var game = games[0];
            _options.AddRange(Options());
            _selection = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = Safe(() => g.Title) ?? "?", Stored = StoredOf(Safe(() => g.Id)), Tag = g }).ToList());
            bool several = _selection.Several;
            Text = games.Length == 1 ? ZincExPlugin.PackName + " - " + Safe(() => game.Title) : ZincExPlugin.PackName + " - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(700, several ? 360 : 560);
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
            Label Line(string text, bool bold = false) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 2), Font = bold ? new Font(Font, FontStyle.Bold) : Font };

            // ── one game: its title, and what it is to ZiNc ──
            if (!several)
            {
                page.Controls.Add(new Label { Text = Safe(() => game.Title), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 12, 10) });
                var rom = ZincExPlugin.ResolveFullPathForUi(Safe(() => game.ApplicationPath)) ?? "";
                var exe = ZincExGameMenu.ExeOf(game);
                var rows = new List<Control> { Line("File: " + rom) };
                if (exe == null) rows.Add(Line("Its ZiNc EX is not there - the emulator's path in LaunchBox points nowhere."));
                else
                {
                    var match = ZincExLaunch.Identify(exe, rom, Safe(() => game.Id), out var problem);
                    if (match == null) rows.Add(Line(problem, true));
                    else
                    {
                        rows.Add(Line("ZiNc's " + match.Game.Title + " - set " + match.Game.Set + ", game " + match.Game.Id
                                      + (ZincExSettings.SetFor(Safe(() => game.Id)) != null ? " (chosen below)" : match.ByName ? "" : " (told by its ROMs: the file's name is not ZiNc's)"), true));
                        rows.Add(Line("Given to ZiNc EX as: " + string.Join(", ", match.Zips.Select(kv => kv.Key + ".zip" + (ZincGames.SamePath(kv.Value, rom) ? (kv.Key == match.Game.Set ? "" : " (this romset)") : " <- " + kv.Value)))));
                    }
                }
                // The ZiNc game it is started as.
                _set = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
                _set.Items.Add("Told by its ROMs (recommended)");
                foreach (var g in ZincGames.All.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)) _set.Items.Add(g.Title + "  [" + g.Set + "]");
                _setAtOpen = ZincExSettings.SetFor(Safe(() => game.Id));
                var sorted = ZincGames.All.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
                _set.SelectedIndex = _setAtOpen == null ? 0 : 1 + sorted.FindIndex(g => g.Set == _setAtOpen);
                LbipHint.Attach(_set, "ZiNc finds a ROM by its CRC32, so the game a romset is is told by what it holds - every ROM of one of ZiNc's 71 games "
                                    + "in it, its parent's or its BIOS's zip. Choose one here only when that is not the game you want (a merged set holds several).");
                var setRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
                setRow.Controls.Add(new Label { Text = "Start it as", Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                setRow.Controls.Add(_set);
                rows.Add(setRow);
                page.Controls.Add(Card("The game to ZiNc", rows.ToArray()));
            }

            // ── its own options, each with its bar ──
            var optionRows = new List<Control>();
            foreach (var o in _options)
            {
                o.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
                o.Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 5, 8, 0) };
                o.Combo.SelectedIndexChanged += (_, _) => Marks();
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(o.Bar);
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                row.Controls.Add(o.Combo);
                optionRows.Add(row);
            }
            optionRows.Add(LbipHint.Note("Given to ZiNc EX for the launch only.", "Every game's value is in the Nixx window, page ZiNc EX. "
                                         + (several ? "These are kept per game" : "This one is kept for this game alone") + " (games\\<game id>.ini). "
                                         + "The picture, sound and controls of a game are ZiNc EX's own: its Game > Game Settings.", 620, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(several ? "Their options" : "This game's options", optionRows.ToArray()));

            // ── the top (several games): All selected games, then each group ──
            var top = _selection.Top(_options.Select(o => o.Key), Describe, Fill);

            // ── the bottom: the pack's legend, reset, OK and Cancel ──
            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, ZincExPlugin.PackName); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("ZiNc EX", false, false, various: several), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            if (top != null) Controls.Add(top);
            Controls.Add(bottom);
            if (_selection.Source != null) _selection.Source.SelectedIndex = 0;
            else Fill(_selection.From(_selection.Members[0], _options.Select(o => o.Key)));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        // ── the options and a game's stored values ───────────────────────────

        private static IEnumerable<Option> Options()
        {
            var every = ZincExSettings.Screen;
            yield return new Option
            {
                Key = "Screen", Label = "Screen", Follow = every == "windowed" ? "windowed" : every == "zincex" ? "as set in ZiNc EX" : "full screen",
                Texts = new[] { "Full screen", "Windowed", "As set in ZiNc EX" }, Values = new[] { "fullscreen", "windowed", "zincex" },
            };
        }

        /// <summary>A game's own values, as its launch reads them: one it cannot use counts as not set.</summary>
        private Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var o in _options)
            {
                var v = ZincExSettings.GameValue(id, o.Key);
                if (v != null && o.Values.Contains(v)) d[o.Key] = v;
            }
            return d;
        }

        private string Describe(LbipSelection.Member m)
        {
            var mine = _options.Where(o => m.Stored.ContainsKey(o.Key)).Select(o => o.Label).ToList();
            return mine.Count == 0 ? "every game's" : "its own: " + string.Join(", ", mine);
        }

        /// <summary>The lists, with what they show now: "Various (n of N)" first only for a key the games do not agree on.</summary>
        private void Fill(Dictionary<string, string> shown)
        {
            foreach (var o in _options)
            {
                shown.TryGetValue(o.Key, out var value);
                o.Combo.Items.Clear(); o.Shown.Clear();
                if (value == LbipSelection.Various) { o.Combo.Items.Add(_selection.VariousText(o.Key)); o.Shown.Add(LbipSelection.Various); }
                o.Combo.Items.Add("As every game (" + o.Follow + ")"); o.Shown.Add(null);
                for (int i = 0; i < o.Values.Length; i++) { o.Combo.Items.Add(o.Texts[i]); o.Shown.Add(o.Values[i]); }
                o.Combo.SelectedIndex = Math.Max(0, o.Shown.IndexOf(value));
            }
            _atOpen = ShownNow();
            Marks();
        }

        private Dictionary<string, string> ShownNow() => _options.ToDictionary(o => o.Key, o => o.Chosen(), StringComparer.Ordinal);

        private void Marks()
        {
            foreach (var o in _options.Where(x => x.Bar != null)) o.Bar.BackColor = LbipSelection.BarOf(o.Chosen());
        }

        /// <summary>"Reset to defaults": every option back to every game's - a Various too - and the game told by its ROMs.
        /// Nothing is saved before OK.</summary>
        private void Reset()
        {
            foreach (var o in _options) o.Combo.SelectedIndex = o.Shown.IndexOf(null);
            if (_set != null) _set.SelectedIndex = 0;
            Marks();
        }

        private string SetChosen()
        {
            if (_set == null || _set.SelectedIndex <= 0) return null;
            return ZincGames.All.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ElementAt(_set.SelectedIndex - 1).Set;
        }

        /// <summary>OK: over several games, asked first; then each game gets only what the user changed and keeps the rest of
        /// its own (LbipSelection.MergeFor) - a Various left as it is writes nothing.</summary>
        private bool Save()
        {
            var now = ShownNow();
            var set = SetChosen();
            bool setChanged = _set != null && !string.Equals(set, _setAtOpen, StringComparison.OrdinalIgnoreCase);
            if (!LbipSelection.Changed(_atOpen, now) && !setChanged) return true;
            if (!_selection.Confirm(this, Text)) return false;
            if (setChanged)
            {
                var id = Safe(() => _games[0].Id);
                if (!string.IsNullOrEmpty(id)) ZincExSettings.SetGameValue(id, "Set", set);
            }
            int written = 0;
            foreach (var m in _selection.Members)
            {
                var id = Safe(() => ((IGame)m.Tag).Id);
                if (string.IsNullOrEmpty(id)) continue;
                var merged = _selection.MergeFor(m, _atOpen, now);
                bool any = false;
                foreach (var o in _options)
                {
                    merged.TryGetValue(o.Key, out var v);
                    m.Stored.TryGetValue(o.Key, out var before);
                    if (string.Equals(v, before, StringComparison.Ordinal)) continue;
                    ZincExSettings.SetGameValue(id, o.Key, v);
                    any = true;
                }
                if (any) written++;
            }
            Log.Info("options saved: " + written + " of " + _games.Length + " game(s) changed" + (setChanged ? ", started as " + (set ?? "told by its ROMs") : ""));
            return true;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(ZincExGameMenu.IsOurs) ? new[] { ZincExGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == ZincExGameMenu.Caption) ZincExGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => ZincExGameMenu.Icon;
    }
}
