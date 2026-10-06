// The right-click entry on the games bsnes can run - one game or a selection: "Nixx-bsnes : Options...". The
// window, dressed as every Nixx window (NixxShell): the game's file and saves, and its own picture options, each "as
// every game" until changed - kept in games\<game id>.ini beside settings.ini (BsnesSettings) and written into
// settings.bml at its launch.
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
                if (!string.Equals((game.Platform ?? "").Trim(), BsnesPlugin.SnesPlatform, StringComparison.OrdinalIgnoreCase)) return false;
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
        private readonly IGame[] _games;
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, int> _initial = new Dictionary<string, int>();
        /// <summary>Several games: the first item keeps each game's own value, and only a combo the user changed is written.</summary>
        private int Offset => _games.Length > 1 ? 1 : 0;

        public BsnesGameForm(IGame[] games)
        {
            _games = games;
            var game = games[0];
            Text = games.Length == 1 ? "Nixx-bsnes - " + Safe(() => game.Title) : "Nixx-bsnes - " + games.Length + " games";
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

            page.Controls.Add(new Label { Text = games.Length == 1 ? Safe(() => game.Title) : games.Length + " games - the options below go to each of them",
                                          AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 0, 10) });

            // ── the game to bsnes ──
            if (games.Length == 1)
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
                }                page.Controls.Add(Card("The game", rows.ToArray()));
            }

            // ── its own options ──
            var id = Safe(() => game.Id);
            var optionRows = new List<Control>();
            foreach (var o in BsnesSettings.All)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = o.Label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
                if (games.Length > 1) c.Items.Add("Each game keeps its own");
                c.Items.Add("As every game (" + o.TextOf(BsnesSettings.Value(o.Key)) + ")");
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                var own = games.Length == 1 ? BsnesSettings.GameValue(id, o.Key) : null;
                c.SelectedIndex = games.Length > 1 ? 0 : own == null ? 0 : Array.IndexOf(o.Values, own) + 1;
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                _initial[o.Key] = c.SelectedIndex;
                row.Controls.Add(c);
                optionRows.Add(row);
            }
            optionRows.Add(LbipHint.Note("Written into settings.bml when the game starts.", "Every game's values are in the Nixx window, page bsnes. "
                                         + "These are kept for this game alone (games\\<game id>.ini).", 620, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(games.Length == 1 ? "This game's options" : "Their options", optionRows.ToArray()));

            // ── the footer ──
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10, 8, 10, 8) };
            var ok = new Button { Text = "OK", Width = 96, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { Save(); } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-bsnes"); DialogResult = DialogResult.None; } };
            bar.Controls.Add(ok);
            bar.Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(page);
            Controls.Add(bar);
            LbIntegrations.Ui.NixxShell.Dress(this);
        }

        private void Save()
        {
            foreach (var g in _games)
            {
                var id = Safe(() => g.Id);
                if (string.IsNullOrEmpty(id)) continue;
                foreach (var o in BsnesSettings.All)
                {
                    int i = _choices[o.Key].SelectedIndex;
                    if (Offset == 1 && i == _initial[o.Key]) continue;   // several games, left as each had it
                    BsnesSettings.SetGameValue(id, o.Key, i <= Offset ? null : o.Values[i - 1 - Offset]);
                }
            }
            Log.Info("options saved for " + _games.Length + " game(s)");
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
