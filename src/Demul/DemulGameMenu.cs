// The right-click entry on the games Demul can run - one game or a selection: "Nixx-Demul : Options...". The window,
// dressed as every Nixx window (NixxShell): the game's compatibility at the top, from the DEmul team's own list
// (DemulCompat, embedded - Mehdi, 06/10: "tu la hardcodes"); what the game is to Demul (its board, its set, whether this
// build knows it, its BIOS set); and its own picture options, each "as every game" until changed - kept in
// games\<game id>.ini beside settings.ini (DemulSettings) and written into the video plugin's .ini at its launch.
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
                if (!DemulPlatforms.IsOurs(game.Platform)) return false;
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
        private readonly IGame[] _games;
        private readonly ComboBox _fullscreen, _aspect, _vsync, _scaling, _video;

        public DemulGameForm(IGame[] games)
        {
            _games = games;
            var game = games[0];
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
            Control Row(string label, Control field)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                row.Controls.Add(field);
                return row;
            }

            // ── the header: its title and its compatibility ──
            var rom = Safe(() => game.ApplicationPath) ?? "";
            var board = DemulPlatforms.BoardOf(Safe(() => game.Platform));
            var compat = board == "Dreamcast" ? null : DemulCompat.Of(rom);
            var header = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 10) };
            header.Controls.Add(new Label { Text = games.Length == 1 ? Safe(() => game.Title) : games.Length + " games - the options below go to each of them", AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 12, 2) });
            if (games.Length == 1)
            {
                var (pill, color) = compat != null ? (compat.Verdict, compat.Color)
                                  : board == "Dreamcast" ? ("No list", Color.FromArgb(0x5A, 0x5A, 0x64)) : ("Not listed", Color.FromArgb(0x5A, 0x5A, 0x64));
                var tag = new Label { Text = "Demul: " + pill, AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 4, 6, 2), BackColor = color, ForeColor = Color.White, Font = new Font(Font, FontStyle.Bold), Tag = LbIntegrations.Ui.NixxTheme.Own };
                LbipHint.Attach(tag, compat != null ? "The DEmul team's list (" + DemulCompat.Date + "): " + compat.Meaning + "."
                                     : board == "Dreamcast" ? "The DEmul team kept a list for the arcade boards only."
                                     : "This set is not in the DEmul team's list of " + DemulCompat.Date + " - added since, or not tried then.");
                header.Controls.Add(tag);
            }
            page.Controls.Add(header);

            if (games.Length == 1)
            {
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
                var gameRows = new List<Control> { Line("Platform: " + Safe(() => game.Platform) + (board != null ? "  -  Demul runs it as " + DemulPlatforms.SystemOf(Safe(() => game.Platform)) : "")) };
                if (board == "Dreamcast") gameRows.Add(Line("Disc: " + Path.GetFileName(rom) + (DemulLaunchDisc(rom) ? "" : "  -  not a disc Demul reads (.gdi, .cdi, .chd)")));
                else if (rom.Length > 0)
                {
                    DemulSets.All.TryGetValue(Path.GetFileNameWithoutExtension(rom), out var set);
                    gameRows.Add(Line("Set: " + Path.GetFileNameWithoutExtension(rom) + (set != null ? "  -  " + set.Title + " (" + set.Board + ")" : "  -  not a set of Demul's boards by that name")));
                    if (set != null) gameRows.Add(Line(set.Known ? "This build of Demul knows it." : "This build of Demul does not know it: it will most likely not run."));
                }
                var exe = Settings.Emulators().Select(e => e.Split('\t').Last()).FirstOrDefault(File.Exists);
                var biosSet = DemulBios.For(DemulPlatforms.SystemOf(Safe(() => game.Platform)), board);
                if (exe != null && biosSet != null)
                {
                    bool there = File.Exists(Path.Combine(Path.GetDirectoryName(exe), DemulLaunch.BiosFolder, biosSet));
                    gameRows.Add(Line("BIOS: " + biosSet + (there ? " is in Demul's roms folder." : " is NOT in Demul's roms folder - the game will not start. It is looked for again at launch.")));
                }
                gameRows.Add(Line("File: " + rom));
                page.Controls.Add(Card("The game", gameRows.ToArray()));
            }

            // ── its own options ──
            ComboBox Choice(string everyText, params string[] items)
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
                c.Items.Add("As every game (" + everyText + ")");
                c.Items.AddRange(items);
                return c;
            }
            string OnOff(bool b) => b ? "on" : "off";
            var aspects = new[] { "Stretched", "4:3", "16:9" };
            _fullscreen = Choice(OnOff(DemulSettings.Fullscreen), "Full screen", "Windowed");
            _aspect = Choice(aspects[DemulSettings.Aspect], aspects);
            _vsync = Choice(OnOff(DemulSettings.VSync), "VSync on", "VSync off");
            _scaling = Choice(DemulSettings.GaelcoScaling + "x", "1x", "2x", "3x", "4x", "5x", "6x");
            _video = Choice(DemulSettings.VideoPlugin == "new" ? "always new" : DemulSettings.VideoPlugin == "old" ? "always old" : "by board",
                            "By board", "The new DX11 plugin", "The old DX11 plugin");
            var id = Safe(() => game.Id);
            _fullscreen.SelectedIndex = DemulSettings.GameValue(id, "Fullscreen") switch { "true" => 1, "false" => 2, _ => 0 };
            _aspect.SelectedIndex = int.TryParse(DemulSettings.GameValue(id, "Aspect"), out var a) && a >= 0 && a <= 2 ? a + 1 : 0;
            _vsync.SelectedIndex = DemulSettings.GameValue(id, "VSync") switch { "true" => 1, "false" => 2, _ => 0 };
            _scaling.SelectedIndex = int.TryParse(DemulSettings.GameValue(id, "GaelcoScaling"), out var s) && s >= 1 && s <= 6 ? s : 0;
            _video.SelectedIndex = DemulSettings.GameValue(id, "VideoPlugin") switch { "auto" => 1, "new" => 2, "old" => 3, _ => 0 };
            LbipHint.Attach(_video, "By board: the old DX11 plugin for Gaelco and Cave CV1000, the new one for the rest (Batocera's choice).");
            var optionRows = new List<Control> { Row("Screen", _fullscreen), Row("Aspect", _aspect), Row("VSync", _vsync) };
            if (board == "Gaelco" || games.Length > 1) optionRows.Add(Row("Gaelco scaling", _scaling));
            optionRows.Add(Row("Video plugin", _video));
            optionRows.Add(LbipHint.Note("Written into Demul's video plugin settings when the game starts.", "Every game's values are in the Nixx window, page Demul. "
                                         + "These are kept for this game alone (games\\<game id>.ini).", 620, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(games.Length == 1 ? "This game's options" : "Their options", optionRows.ToArray()));

            // ── the footer ──
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10, 8, 10, 8) };
            var ok = new Button { Text = "OK", Width = 96, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { Save(); } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-Demul"); DialogResult = DialogResult.None; } };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            Controls.Add(bar);
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private static bool DemulLaunchDisc(string rom)
        {
            var ext = Path.GetExtension(rom ?? "").ToLowerInvariant();
            return ext == ".gdi" || ext == ".cdi" || ext == ".chd";
        }

        private void Save()
        {
            foreach (var g in _games)
            {
                var id = Safe(() => g.Id);
                if (string.IsNullOrEmpty(id)) continue;
                DemulSettings.SetGameValue(id, "Fullscreen", _fullscreen.SelectedIndex switch { 1 => "true", 2 => "false", _ => null });
                DemulSettings.SetGameValue(id, "Aspect", _aspect.SelectedIndex > 0 ? (_aspect.SelectedIndex - 1).ToString() : null);
                DemulSettings.SetGameValue(id, "VSync", _vsync.SelectedIndex switch { 1 => "true", 2 => "false", _ => null });
                DemulSettings.SetGameValue(id, "GaelcoScaling", _scaling.SelectedIndex > 0 ? _scaling.SelectedIndex.ToString() : null);
                DemulSettings.SetGameValue(id, "VideoPlugin", _video.SelectedIndex switch { 1 => "auto", 2 => "new", 3 => "old", _ => null });
            }
            Log.Info("options saved for " + _games.Length + " game(s)");
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
