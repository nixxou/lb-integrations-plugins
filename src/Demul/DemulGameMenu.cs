// The right-click entry on the games Demul can run - one game or a selection: "Nixx-Demul : Options...". The window,
// dressed as every Nixx window (NixxShell): the game's compatibility at the top, from the DEmul team's own list
// (DemulCompat, embedded - Mehdi, 06/10: "tu la hardcodes"); what the game is to Demul (its board, its set, whether this
// build knows it, its BIOS set); and its own picture options, each "as every game" until changed - kept in
// games\<game id>.ini beside settings.ini (DemulSettings) and written into the video plugin's .ini at its launch. A bar at
// the left of each option says where it comes from, the pack's legend and "Reset to defaults" at the bottom (OptionMarks).
//
// SEVERAL GAMES (the pack's rule, Mehdi, 06/10 - LbipSelection): at the top "All selected games", the default, then the
// games grouped by identical options. A setting they do not agree on shows "Various (n of N)", purple: left so, each keeps
// its own; changed, it goes to all. OK asks first, then writes only what was changed (LbipGameEdit). What is about one game
// only - its compatibility, its set, its BIOS, its file - is not shown.
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

namespace LbIntegrations.Demul
{
    internal static class DemulGameMenu
    {
        public const string Caption = "Nixx-Demul : Options...";

        public static Image Icon => LbipMenuIcon.Of(Settings.IsDemulExe, p => p);

        /// <summary>A game of ours: its own emulator is a Demul, or its platform is one of Demul's and a Demul is in the
        /// library ("Launch With" can pick it). Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && Settings.IsDemulExe(own.ApplicationPath)) return true;
                // On "Arcade", only a set of Demul's boards - not every game of a MAME library.
                if (!DemulPlatforms.IsOurs(game.Platform)
                    && !(DemulPlatforms.IsArcadeCatchAll(game.Platform) && DemulPlatforms.BoardOf(game.Platform, game.ApplicationPath) != null)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && Settings.IsDemulExe(e.ApplicationPath));
            }
            catch { return false; }
        }

        public static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToArray();
                if (ours.Length == 0) return;
                using var form = new DemulGameForm(ours);
                form.ShowDialog();
            }
            catch (Exception ex) { Log.Warn("the game's window", ex); MessageBox.Show("The window could not open: " + ex.Message, "Nixx-Demul", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class DemulGameForm : Form
    {
        /// <summary>One option: its list, what its items stand for (Various, null = as every game, a stored value), its bar.</summary>
        private sealed class Option
        {
            public string Key, Label, Follow;
            public string[] Texts, Values;
            public ComboBox Combo;
            public Panel Bar;
            public bool OnPage;
            public readonly List<string> Shown = new List<string>();
            public string Chosen() => Combo.SelectedIndex < 0 ? null : Shown[Combo.SelectedIndex];
        }

        private readonly IGame[] _games;
        private readonly LbipSelection _selection;
        private readonly List<Option> _options = new List<Option>();
        private Dictionary<string, string> _atOpen;

        public DemulGameForm(IGame[] games)
        {
            _games = games;
            var game = games[0];
            _options.AddRange(Options());
            _selection = new LbipSelection(games.Select(g => new LbipSelection.Member { Title = Safe(() => g.Title) ?? "?", Stored = StoredOf(Safe(() => g.Id)), Tag = g }).ToList());
            bool several = _selection.Several;
            Text = games.Length == 1 ? "Nixx-Demul - " + Safe(() => game.Title) : "Nixx-Demul - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(700, 640);
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

            // ── the header: its title and its compatibility - one game only; several have the selection's list at the top ──
            var rom = Safe(() => game.ApplicationPath) ?? "";
            var board = DemulPlatforms.BoardOf(Safe(() => game.Platform), rom);
            var compat = board == "Dreamcast" || several ? null : DemulCompat.Of(rom);
            if (!several)
            {
                var header = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 10) };
                header.Controls.Add(new Label { Text = Safe(() => game.Title), AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 12, 2) });
                var (pill, color) = compat != null ? (compat.Verdict, compat.Color)
                                  : board == "Dreamcast" ? ("No list", Color.FromArgb(0x5A, 0x5A, 0x64)) : ("Not listed", Color.FromArgb(0x5A, 0x5A, 0x64));
                var tag = new Label { Text = "Demul: " + pill, AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 4, 6, 2), BackColor = color, ForeColor = Color.White, Font = new Font(Font, FontStyle.Bold), Tag = LbIntegrations.Ui.NixxTheme.Own };
                LbipHint.Attach(tag, compat != null ? "The DEmul team's list (" + DemulCompat.Date + "): " + compat.Meaning + "."
                                     : board == "Dreamcast" ? "The DEmul team kept a list for the arcade boards only."
                                     : "This set is not in the DEmul team's list of " + DemulCompat.Date + " - added since, or not tried then.");
                header.Controls.Add(tag);
                page.Controls.Add(header);

                // ── what the DEmul team says ──
                if (compat != null)
                {
                    var rows = new List<Control>
                    {
                        Line(compat.Meaning.Substring(0, 1).ToUpperInvariant() + compat.Meaning.Substring(1) + ".", true),
                        Line("Test mode: " + (compat.Test ? "yes" : "no") + "   Boots: " + (compat.Boot ? "yes" : "no") + "   Playable: " + (compat.Game ? "yes" : "no")),
                    };
                    var facts = new List<string>();
                    if (!string.IsNullOrEmpty(compat.Players)) facts.Add(compat.Players + " player(s)");
                    if (!string.IsNullOrEmpty(compat.Rotation)) facts.Add(DemulCompat.RotationText(compat.Rotation) + " screen");
                    if (!string.IsNullOrEmpty(compat.Controls)) facts.Add("controls: " + compat.Controls);
                    if (facts.Count > 0) rows.Add(Line(string.Join("  -  ", facts)));
                    if (DemulCompat.BiosText(compat.Bios) is string bios) rows.Add(Line("Wants " + bios + "."));
                    rows.Add(Line("Board: " + compat.Section + ". From the DEmul team's arcade_compat.txt of " + DemulCompat.Date + "."));
                    page.Controls.Add(Card("Compatibility", rows.ToArray()));
                }

                // ── the game to Demul ──
                var gameRows = new List<Control> { Line("Platform: " + Safe(() => game.Platform) + (board != null ? "  -  Demul runs it as " + DemulPlatforms.SystemOfBoard(board) : "")) };
                if (board == "Dreamcast") gameRows.Add(Line("Disc: " + Path.GetFileName(rom) + (DemulLaunchDisc(rom) ? "" : "  -  not a disc Demul reads (.gdi, .cdi, .chd)")));
                else if (rom.Length > 0)
                {
                    DemulSets.All.TryGetValue(Path.GetFileNameWithoutExtension(rom), out var set);
                    gameRows.Add(Line("Set: " + Path.GetFileNameWithoutExtension(rom) + (set != null ? "  -  " + set.Title + " (" + set.Board + ")" : "  -  not a set of Demul's boards by that name")));
                    if (set != null) gameRows.Add(Line(set.Known ? "This build of Demul knows it." : "This build of Demul does not know it: it will most likely not run."));
                }
                var exe = Settings.Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
                var biosSet = DemulBios.For(DemulPlatforms.SystemOfBoard(board), board);
                if (exe != null && biosSet != null)
                {
                    bool there = File.Exists(Path.Combine(Path.GetDirectoryName(exe), DemulLaunch.BiosFolder, biosSet));
                    gameRows.Add(Line("BIOS: " + biosSet + (there ? " is in Demul's roms folder." : " is NOT in Demul's roms folder - the game will not start. It is looked for again at launch.")));
                }
                gameRows.Add(Line("File: " + rom));
                page.Controls.Add(Card("The game", gameRows.ToArray()));
            }

            // ── its own options, each with its bar ──
            var optionRows = new List<Control>();
            foreach (var o in _options)
            {
                // Gaelco scaling: for a Gaelco game, or a selection (as before). Not on the page = not touched at OK.
                o.OnPage = o.Key != "GaelcoScaling" || board == "Gaelco" || several;
                if (!o.OnPage) continue;
                o.Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
                o.Bar = new Panel { Width = 4, Height = 18, Margin = new Padding(0, 5, 8, 0) };
                o.Combo.SelectedIndexChanged += (_, _) => Marks();
                if (o.Key == "VideoPlugin") LbipHint.Attach(o.Combo, "By board: the old DX11 plugin for Gaelco and Cave CV1000, the new one for the rest (Batocera's choice).");
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(o.Bar);
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                row.Controls.Add(o.Combo);
                optionRows.Add(row);
            }
            optionRows.Add(LbipHint.Note("Written into Demul's video plugin settings when the game starts.", "Every game's values are in the Nixx window, page Demul. "
                                         + (several ? "These are kept per game" : "These are kept for this game alone") + " (games\\<game id>.ini).", 620, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(several ? "Their options" : "This game's options", optionRows.ToArray()));

            // ── the top (several games): All selected games, then each group ──
            var top = _selection.Top(_options.Where(o => o.OnPage).Select(o => o.Key), Describe, Fill);

            // ── the bottom: the pack's legend, reset, OK and Cancel ──
            var ok = new Button { Text = "OK", Width = 96, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { if (Save()) { DialogResult = DialogResult.OK; Close(); } } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-Demul"); } };
            var bottom = OptionMarks.Bottom(OptionMarks.Legend("Demul", false, false, various: several), OptionMarks.ResetButton(Reset), ok, cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            if (top != null) Controls.Add(top);
            Controls.Add(bottom);
            if (_selection.Source != null) _selection.Source.SelectedIndex = 0;
            else Fill(_selection.From(_selection.Members[0], _options.Where(o => o.OnPage).Select(o => o.Key)));
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        // ── the options and a game's stored values ───────────────────────────

        /// <summary>The five options, their texts and the values games\<game id>.ini keeps; "as every game" says the Demul page's.</summary>
        private static IEnumerable<Option> Options()
        {
            string OnOff(bool b) => b ? "on" : "off";
            var aspects = new[] { "Stretched", "4:3", "16:9" };
            var video = DemulSettings.VideoPlugin;
            yield return new Option { Key = "Fullscreen", Label = "Screen", Follow = OnOff(DemulSettings.Fullscreen), Texts = new[] { "Full screen", "Windowed" }, Values = new[] { "true", "false" } };
            yield return new Option { Key = "Aspect", Label = "Aspect", Follow = aspects[DemulSettings.Aspect], Texts = aspects, Values = new[] { "0", "1", "2" } };
            yield return new Option { Key = "VSync", Label = "VSync", Follow = OnOff(DemulSettings.VSync), Texts = new[] { "VSync on", "VSync off" }, Values = new[] { "true", "false" } };
            yield return new Option { Key = "GaelcoScaling", Label = "Gaelco scaling", Follow = DemulSettings.GaelcoScaling + "x", Texts = new[] { "1x", "2x", "3x", "4x", "5x", "6x" }, Values = new[] { "1", "2", "3", "4", "5", "6" } };
            yield return new Option { Key = "VideoPlugin", Label = "Video plugin", Follow = video == "new" ? "always new" : video == "old" ? "always old" : "by board",
                                      Texts = new[] { "By board", "The new DX11 plugin", "The old DX11 plugin" }, Values = new[] { "auto", "new", "old" } };
        }

        /// <summary>A game's own values, as its launch reads them: one it cannot use (not a choice of the list) counts as not
        /// set - it follows every game's, and is left as it is in the file unless changed here.</summary>
        private Dictionary<string, string> StoredOf(string id)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(id)) return d;
            foreach (var o in _options)
            {
                var v = DemulSettings.GameValue(id, o.Key);
                if (v != null && int.TryParse(v, out var n) && o.Values.All(x => int.TryParse(x, out _))) v = n.ToString();
                if (v != null && o.Values.Contains(v)) d[o.Key] = v;
            }
            return d;
        }

        private string Describe(LbipSelection.Member m)
        {
            var mine = _options.Where(o => o.OnPage && m.Stored.ContainsKey(o.Key)).Select(o => o.Label).ToList();
            return mine.Count == 0 ? "every game's" : "its own: " + string.Join(", ", mine);
        }

        /// <summary>The lists, with what they show now: "Various (n of N)" first only for a key the games do not agree on.</summary>
        private void Fill(Dictionary<string, string> shown)
        {
            foreach (var o in _options.Where(x => x.OnPage))
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

        /// <summary>What the window shows, each option on the page: Various, null (as every game) or a value.</summary>
        private Dictionary<string, string> ShownNow()
            => _options.Where(o => o.OnPage).ToDictionary(o => o.Key, o => o.Chosen(), StringComparer.Ordinal);

        private void Marks()
        {
            foreach (var o in _options.Where(x => x.OnPage && x.Bar != null)) o.Bar.BackColor = LbipSelection.BarOf(o.Chosen());
        }

        /// <summary>"Reset to defaults": every option back to every game's - a Various too. Nothing is saved before OK.</summary>
        private void Reset()
        {
            foreach (var o in _options.Where(x => x.OnPage)) o.Combo.SelectedIndex = o.Shown.IndexOf(null);
            Marks();
        }

        private static bool DemulLaunchDisc(string rom)
        {
            var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
            return ext == ".gdi" || ext == ".cdi" || ext == ".chd";
        }

        /// <summary>OK: over several games, asked first; then each game gets only what the user changed and keeps the rest
        /// of its own (LbipSelection.MergeFor) - a Various left as it is writes nothing.</summary>
        private bool Save()
        {
            var now = ShownNow();
            if (!LbipSelection.Changed(_atOpen, now)) return true;
            if (!_selection.Confirm(this, Text)) return false;
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
                    DemulSettings.SetGameValue(id, o.Key, v);
                    any = true;
                }
                if (any) written++;
            }
            Log.Info("options saved: " + written + " of " + _games.Length + " game(s) changed");
            return true;
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(DemulGameMenu.IsOurs) ? new[] { DemulGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == DemulGameMenu.Caption) DemulGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => DemulGameMenu.Icon;
    }
}
