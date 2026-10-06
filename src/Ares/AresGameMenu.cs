// The right-click entry on the games ares can run - one game or a selection: "Nixx-ares : Options...". The window, dressed
// as every Nixx window (NixxShell): the SYSTEM ares runs it as (the third level of AresLaunch.SystemFor - "as the
// platform", or one of the systems its platform has), its BIOS, its saves, and its own picture options - kept in
// games\<game id>.ini beside settings.ini (AresSettings).
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
        private readonly IGame[] _games;
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, int> _initial = new Dictionary<string, int>();
        /// <summary>Several games: the first item keeps each game's own value, and only a combo the user changed is written.</summary>
        private int Offset => _games.Length > 1 ? 1 : 0;
        private readonly ComboBox _system;
        private readonly List<string> _systems = new List<string>();

        public AresGameForm(IGame[] games)
        {
            _games = games;
            var game = games[0];
            Text = games.Length == 1 ? "Nixx-ares - " + Safe(() => game.Title) : "Nixx-ares - " + games.Length + " games";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ClientSize = new Size(700, 620);
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
            Control Row(string label, Control field)
            {
                var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label { Text = label, Width = 150, Height = 24, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false });
                row.Controls.Add(field);
                return row;
            }

            page.Controls.Add(new Label { Text = games.Length == 1 ? Safe(() => game.Title) : games.Length + " games - the options below go to each of them",
                                          AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 2, 0, 10) });

            // ── the system ──
            var platform = AresSystems.Platform(Safe(() => game.Platform));
            var id = Safe(() => game.Id);
            var dm = PluginHelper.DataManager;
            var emu = dm?.GetAllEmulators()?.FirstOrDefault(e => AresLaunch.IsAresExe(Safe(() => e.ApplicationPath)));
            var exe = emu == null ? null : AresPlugin.ResolveFullPathForUi(Safe(() => emu.ApplicationPath));
            var rom = Safe(() => game.ApplicationPath) ?? "";
            var asPlatform = AresPlugin.SystemOfForUi(emu, null, Safe(() => game.Platform), rom);
            _system = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
            // Several games: a system only when they share a platform - the choice is the platform's systems.
            bool samePlatform = games.All(g => string.Equals(Safe(() => g.Platform), Safe(() => game.Platform), StringComparison.OrdinalIgnoreCase));
            if (games.Length > 1) _system.Items.Add(samePlatform ? "Each game keeps its own" : "Each game keeps its own (they are of different platforms)");
            _system.Items.Add("As its platform (" + (asPlatform ?? "none") + ")");
            if (platform != null)
                foreach (var s in platform.Systems.Concat(platform.ByExtension.Values).Distinct())
                    if (!_systems.Contains(s)) { _systems.Add(s); _system.Items.Add(s); }
            var own = games.Length == 1 ? AresSettings.GameSystem(id) : null;
            if (own != null && !_systems.Contains(own)) { _systems.Add(own); _system.Items.Add(own); }
            _system.SelectedIndex = games.Length > 1 ? 0 : own == null ? 0 : _systems.IndexOf(own) + 1;
            _system.Enabled = samePlatform;
            LbipHint.Attach(_system, "As its platform: the --system on the platform's line in LaunchBox (Edit Emulator > Associated Platforms), else the plugin's "
                                   + "choice for the platform and the file's extension. A system chosen here is this game's alone.");
            var systemRows = new List<Control> { Row("System", _system) };
            if (games.Length == 1)
            {
                systemRows.Add(Line("File: " + rom));
                var system = AresSystems.System(own ?? asPlatform);
                if (exe != null && system != null)
                {
                    var (owner, specs) = AresSystems.FirmwareFor(system);
                    if (specs.Length > 0)
                    {
                        var states = AresFirmware.Ensure(exe, new[] { system }, readOnly: true);
                        systemRows.Add(Line("BIOS (" + owner.Name + "): " + string.Join(";  ", states.Select(st => st.Spec.Region + ": " + (st.Path == null ? (st.Spec.Optional ? "-" : "missing") : Path.GetFileName(st.Path))))
                                             + (AresFirmware.Enough(states) ? "" : "  -  the game will not start without one")));
                    }
                    var full = AresPlugin.ResolveFullPathForUi(rom);
                    var list = new List<GameSaveBase>();
                    AresPlugin.Collect(list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exe, system, full, id ?? "x", null);
                    systemRows.Add(Line("Saves: " + (list.Count == 0 ? "none yet" : string.Join(", ", list.Select(l => Path.GetFileName(l.FileLocation))))));
                }
            }
            page.Controls.Add(Card("The system ares runs it as", systemRows.ToArray()));

            // ── its own options ──
            var optionRows = new List<Control>();
            foreach (var o in AresSettings.All)
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
                if (games.Length > 1) c.Items.Add("Each game keeps its own");
                c.Items.Add("As every game (" + o.TextOf(AresSettings.Value(o.Key)) + ")");
                c.Items.AddRange(o.Texts.Select(t => (object)t).ToArray());
                var mine = games.Length == 1 ? AresSettings.GameValue(id, o.Key) : null;
                c.SelectedIndex = games.Length > 1 ? 0 : mine == null ? 0 : Array.IndexOf(o.Values, mine) + 1;
                LbipHint.Attach(c, o.Hint);
                _choices[o.Key] = c;
                _initial[o.Key] = c.SelectedIndex;
                optionRows.Add(Row(o.Label, c));
            }
            optionRows.Add(LbipHint.Note("Written into settings.bml when the game starts.", "Every game's values are in the Nixx window, page ares. "
                                         + "These are kept for this game alone (games\\<game id>.ini).", 620, new Padding(0, 4, 0, 2)));
            page.Controls.Add(Card(games.Length == 1 ? "This game's options" : "Their options", optionRows.ToArray()));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10, 8, 10, 8) };
            var ok = new Button { Text = "OK", Width = 96, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 28, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => { try { Save(); } catch (Exception ex) { MessageBox.Show(this, "The options could not be written: " + ex.Message, "Nixx-ares"); DialogResult = DialogResult.None; } };
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
                int si = _system.SelectedIndex;
                if (!(Offset == 1 && si == 0)) AresSettings.SetGameSystem(id, si <= Offset ? null : _systems[si - 1 - Offset]);
                foreach (var o in AresSettings.All)
                {
                    int i = _choices[o.Key].SelectedIndex;
                    if (Offset == 1 && i == _initial[o.Key]) continue;   // several games, left as each had it
                    AresSettings.SetGameValue(id, o.Key, i <= Offset ? null : o.Values[i - 1 - Offset]);
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
            => games != null && games.Any(AresGameMenu.IsOurs) ? new[] { AresGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == AresGameMenu.Caption) AresGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => AresGameMenu.Icon;
    }
}
