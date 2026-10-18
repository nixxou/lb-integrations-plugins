// The right-click entry on the games 3dSen can run - one game or a selection: "Nixx-3DSen : Options...". The window,
// dressed as every Nixx window (NixxShell): the game's 3D PROFILE - "Automatic" (matched by its title, ThreeDSenProfiles),
// or one chosen in 3dSen's list (2D Mode included) - and its screen, each "as every game" until changed, kept in
// games\<game id>.ini (ThreeDSenSettings). At the bottom, live, what goes on 3dSen's command line, then the pack's legend,
// "Reset to defaults", OK and Cancel (OptionMarks).
//
// SEVERAL GAMES (the pack's rule, LbipSelection): "All selected games", then the games grouped by identical options; an
// option they disagree on shows "Various (n of N)" (purple) - left so, each keeps its own; OK asks, then writes only what
// was changed.
//
// NOT AN IGameMenuItemPlugin: Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenGameMenu
    {
        public const string Caption = "Nixx-3DSen : Options...";

        public static Image Icon => LbipMenuIcon.Of(ThreeDSenLaunch.IsExe, ThreeDSenPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is a 3dSen, or it is a NES game and a 3dSen is in the library. Asked at
        /// every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && ThreeDSenLaunch.IsExe(own.ApplicationPath)) return true;
                if (!ThreeDSenPlugin.IsNes(game.Platform)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && ThreeDSenLaunch.IsExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new ThreeDSenGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, ThreeDSenPlugin.PackName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class ThreeDSenGameForm : Form
    {
        private sealed class Row
        {
            public string Key, Label;
            public ComboBox Combo;
            public readonly List<string> Values = new List<string>();
            public Panel Bar;
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Values[Combo.SelectedIndex];
        }

        private readonly LbipSelection _sel;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<ThreeDSenProfile> _profiles;
        private readonly IGame[] _games;
        private Dictionary<string, string> _atOpen;
        private readonly Label _count, _line;
        private bool Several => _sel.Several;
        private static IEnumerable<string> Keys => new[] { ThreeDSenSettings.ProfileKey }.Concat(ThreeDSenSettings.All.Select(o => o.Key));

        public ThreeDSenGameForm(IGame[] games)
        {
            _games = games;
            _profiles = ThreeDSenProfiles.All(ThreeDSenLaunch.DataDir());
            _sel = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = Safe(() => g.Title) ?? "?", Stored = StoredOf(Safe(() => g.Id)), Tag = g }));
            var game = games[0];
            Text = !Several ? ThreeDSenPlugin.PackName + " - " + Safe(() => game.Title) : ThreeDSenPlugin.PackName + " - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(720, 560);
            Font = new Font("Segoe UI", 9f);

            var page = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 10, 14, 6) };
            GroupBox Card(string title, params Control[] rows)
            {
                var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(670, 0) };
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
                flow.Controls.AddRange(rows);
                box.Controls.Add(flow);
                return box;
            }
            Label Text_(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Margin = new Padding(0, 2, 0, 2) };
            Control RowOf(string key, string label, string hint)
            {
                var r = new Row { Key = key, Label = label, Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 3, 8, 0) } };
                var line = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                line.Controls.Add(r.Bar);
                line.Controls.Add(new Label { Text = label, Width = 130, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                r.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420, MaxDropDownItems = 20 };
                r.Combo.SelectedIndexChanged += (_, _) => { r.Bar.BackColor = LbipSelection.BarOf(r.Chosen()); ShowLine(); };
                LbipHint.Attach(r.Combo, hint);
                line.Controls.Add(r.Combo);
                _rows.Add(r);
                return line;
            }

            var top = _sel.Top(Keys, Describe, ShowValues);
            if (!Several)
                page.Controls.Add(new Label { Text = Safe(() => game.Title), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 0, 10) });

            page.Controls.Add(Card(!Several ? "Its 3D profile" : "Their 3D profile",
                RowOf(ThreeDSenSettings.ProfileKey, "Profile",
                    "3dSen draws a game in 3D from its profile: Automatic finds it by the game's title. Choose one when the title does not match "
                    + "(3dSen spells some its own way). 3dSen does not check the ROM: a wrong profile plays it with another game's 3D. "
                    + "2D Mode plays any game flat."),
                Text_(!Several ? "File: " + (Safe(() => game.ApplicationPath) ?? "") : "Automatic: each game by its own title.")));

            var screen = new List<Control>();
            foreach (var o in ThreeDSenSettings.All) screen.Add(RowOf(o.Key, o.Label, o.Hint));
            screen.Add(LbipHint.Note("Kept " + (Several ? "per game" : "for this game alone") + " (games\\<game id>.ini).",
                "Every game's values are in the Nixx window, page 3dSen.", 640, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(!Several ? "Its screen" : "Their screen", screen.ToArray()));

            _count = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 2, 0, 2) };
            _line = new Label { AutoSize = true, MaximumSize = new Size(640, 0), UseMnemonic = false, Font = new Font("Consolas", 9f), Margin = new Padding(0, 2, 0, 2) };
            page.Controls.Add(Card("Command line", _count, _line));

            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, ThreeDSenPlugin.PackName); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("3dSen", false, false, Several, "As every game / Automatic"), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            if (top != null) Controls.Add(top);
            Controls.Add(bottom);
            if (_sel.Source != null) _sel.Source.SelectedIndex = 0; else ShowValues(_sel.From(_sel.Members[0], Keys));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private static Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var k in Keys) { var v = ThreeDSenSettings.GameValue(id, k); if (v != null) d[k] = v; }
            return d;
        }

        private string Describe(LbipSelection.Member m)
        {
            var mine = new List<string>();
            if (m.Stored.TryGetValue(ThreeDSenSettings.ProfileKey, out var p)) mine.Add("profile " + (_profiles.FirstOrDefault(x => x.Id == p)?.Title ?? p));
            mine.AddRange(ThreeDSenSettings.All.Where(o => m.Stored.ContainsKey(o.Key)).Select(o => o.Label));
            return mine.Count == 0 ? "as every game" : "its own: " + string.Join(", ", mine);
        }

        private void ShowValues(Dictionary<string, string> shown)
        {
            foreach (var r in _rows) Fill(r, shown.TryGetValue(r.Key, out var v) ? v : null);
            _atOpen = ShownNow();
            ShowLine();
        }

        /// <summary>The automatic profile of the one game shown, or null.</summary>
        private ThreeDSenProfile Automatic()
            => Several ? null : ThreeDSenProfiles.Match(_profiles, Safe(() => _games[0].Title), ThreeDSenPlugin.ResolveFullPathForUi(Safe(() => _games[0].ApplicationPath)));

        private void Fill(Row r, string value)
        {
            r.Combo.Items.Clear(); r.Values.Clear();
            if (value == LbipSelection.Various) { r.Combo.Items.Add(_sel.VariousText(r.Key)); r.Values.Add(LbipSelection.Various); }
            if (r.Key == ThreeDSenSettings.ProfileKey)
            {
                var auto = Automatic();
                r.Combo.Items.Add(Several ? "Automatic (by each game's title)" : auto != null ? "Automatic (" + auto.Title + ")" : "Automatic - no profile matches this title: choose one");
                r.Values.Add(null);
                var two = _profiles.FirstOrDefault(p => p.Id == ThreeDSenProfiles.TwoDModeId);
                if (two != null) { r.Combo.Items.Add("2D Mode - flat, any game"); r.Values.Add(two.Id); }
                foreach (var p in _profiles.Where(p => p.Id != ThreeDSenProfiles.TwoDModeId)) { r.Combo.Items.Add(p.Title + (p.BuiltIn ? "" : "  (custom)")); r.Values.Add(p.Id); }
                if (value != null && value != LbipSelection.Various && !r.Values.Contains(value)) { r.Combo.Items.Add("Profile " + value + " (not in 3dSen any more)"); r.Values.Add(value); }
            }
            else
            {
                var o = ThreeDSenSettings.Option(r.Key);
                r.Combo.Items.Add("As every game (" + o.TextOf(ThreeDSenSettings.Value(o.Key)) + ")"); r.Values.Add(null);
                for (int i = 0; i < o.Values.Length; i++)
                {
                    var text = o.Texts[i];
                    if (o.Key == "Screen" && o.Values[i] == "asis") { var rem = ThreeDSenLaunch.Remembered(); if (rem != null) text += " (" + rem + ")"; }
                    r.Combo.Items.Add(text); r.Values.Add(o.Values[i]);
                }
            }
            r.Combo.SelectedIndex = Math.Max(0, r.Values.IndexOf(value));
            r.Bar.BackColor = LbipSelection.BarOf(r.Chosen());
        }

        /// <summary>What goes on 3dSen's command line with what the window shows - for the one game, or the first of several.</summary>
        private void ShowLine()
        {
            if (_count == null || _rows.Count == 0) return;
            string Shown(string key) { var r = _rows.First(x => x.Key == key); var v = r.Chosen(); return v == LbipSelection.Various ? null : v; }
            var profile = Shown(ThreeDSenSettings.ProfileKey) ?? Automatic()?.Id;
            var screen = Shown("Screen") ?? ThreeDSenSettings.Value("Screen");
            var size = Shown("WindowSize") ?? ThreeDSenSettings.Value("WindowSize");
            var switches = ThreeDSenLaunch.Switches(profile ?? "<profile>", screen, size);
            if (Several && Shown(ThreeDSenSettings.ProfileKey) == null) switches[0] = "-id=<each game's profile>";
            int various = _rows.Count(r => r.Chosen() == LbipSelection.Various);
            _count.Text = switches.Count(s => s.StartsWith("-")) + " option(s) on 3dSen's command line - the game itself goes by its profile, never by its path"
                          + (various > 0 ? " (" + various + " vary, kept per game)" : "") + ":";
            _line.Text = string.Join(" ", switches) + (profile == null && !Several ? "\r\n(no profile: the launch is refused until one is chosen)" : "");
        }

        private void Reset()
        {
            foreach (var r in _rows) Fill(r, null);
            ShowLine();
        }

        private Dictionary<string, string> ShownNow()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in _rows) d[r.Key] = r.Chosen();
            return d;
        }

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
                foreach (var k in Keys)
                {
                    merged.TryGetValue(k, out var v);
                    m.Stored.TryGetValue(k, out var before);
                    if (string.Equals(v, before, StringComparison.Ordinal)) continue;
                    ThreeDSenSettings.SetGameValue(id, k, v);
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
            => games != null && games.Any(ThreeDSenGameMenu.IsOurs) ? new[] { ThreeDSenGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == ThreeDSenGameMenu.Caption) ThreeDSenGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => ThreeDSenGameMenu.Icon;
    }
}
