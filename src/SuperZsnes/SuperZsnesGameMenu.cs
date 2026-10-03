// The right-click entry on the games our SUPER ZSNES can run - one game or a selection: "Nixx-SuperZSNES :
// Options...", the game's own options (window, display, gameplay, audio - the catalogue's Game scope), kept
// in games\<game id>.ini beside settings.ini and passed on its command line at launch (Mehdi, 01/10). Like
// every option of this pack, applied IN MEMORY by the in-process plugin: never written into the emulator's
// own settings file.
//
// NOT AN IGameMenuItemPlugin, as for PPSSPP: a plugin in LaunchBox 14's Local\Plugins gets its emulator role and
// nothing else; Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesGameMenu
    {
        public const string Caption = "Nixx-SuperZSNES : Options...";

        private const string SnesPlatform = "Super Nintendo Entertainment System";

        /// <summary>The emulator's own icon, taken from its executable - see LbipMenuIcon.</summary>
        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(SuperZsnesPaths.IsSuperZsnesExecutable, SuperZsnesPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is a SUPER ZSNES, or it is a SNES game and a SUPER ZSNES is in the
        /// library ("Launch With" can pick it). Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && SuperZsnesPaths.IsSuperZsnesExecutable(own.ApplicationPath)) return true;
                if (!string.Equals(game.Platform, SnesPlatform, StringComparison.OrdinalIgnoreCase)) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && SuperZsnesPaths.IsSuperZsnesExecutable(e.ApplicationPath));
            }
            catch { return false; }
        }

        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                Log.Info("game menu: " + ours.Count + " game(s) of ours selected" + (games.Length > ours.Count ? ", " + (games.Length - ours.Count) + " other(s) left out" : ""));
                if (ours.Count == 0) return;
                using var form = new SuperZsnesGameOptionsForm(ours);
                form.ShowDialog(OwnerWindow());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }

        private static IWin32Window OwnerWindow()
        {
            var handle = GetForegroundWindow();
            return handle == IntPtr.Zero ? null : new Owner(handle);
        }

        private sealed class Owner : IWin32Window
        {
            public Owner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }

    /// <summary>One game's options - or a selection's, which then all get the same.</summary>
    internal sealed class SuperZsnesGameOptionsForm : Form
    {
        private readonly List<(string Id, string Title)> _games;
        private readonly SuperZsnesSettingsPage _page;

        public SuperZsnesGameOptionsForm(List<IGame> games)
        {
            _games = games.Select(g => (Safe(() => g.Id), Safe(() => g.Title))).Where(g => g.Item1.Length > 0).ToList();
            var first = _games.FirstOrDefault();
            var saved = SuperZsnesSettings.ReadGame(first.Id);
            bool differ = _games.Skip(1).Any(g => !Same(SuperZsnesSettings.ReadGame(g.Id), saved));

            Text = "Nixx-SuperZSNES - Options" + (_games.Count > 1 ? " (" + _games.Count + " games)" : " - " + first.Title);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(700, 780);

            var intro = (_games.Count == 1 ? first.Title + "'s own options" : _games.Count + " games" + (differ ? " - their options differ: shown from " + first.Title : ", all with the same options"))
                        + ". " + "Each stays SUPER ZSNES's own until set here; set, it goes on the game's command line at launch, "
                        + "and is never written into the emulator's settings.";
            // What SUPER ZSNES runs on without these: its settings file and Unity's registry values (SuperZsnesCurrent).
            var exe = ExecutableFor(games.FirstOrDefault());
            var current = exe != null ? SuperZsnesCurrent.Read(exe) : null;
            _page = new SuperZsnesSettingsPage(o => o.Scope == OptionScope.Game, saved, intro, deploy: false,
                                               where: _games.Count == 1 ? "Options file: " + SuperZsnesSettings.GamePath(first.Id) : "", running: current);
            _atOpen = _page.Shown();
            _page.Dock = DockStyle.Fill;

            var ok = new Button { Text = "OK", Width = 90 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (_, _) => Apply();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 8, 8, 4) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(_page);
            Controls.Add(buttons);
        }

        /// <summary>The SUPER ZSNES this game runs on: its own emulator when that is one, else the first in the library.</summary>
        private static string ExecutableFor(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return null;
                IEmulator emu = null;
                var id = game == null ? null : Safe(() => game.EmulatorId);
                if (!string.IsNullOrWhiteSpace(id)) emu = dm.GetEmulatorById(id);
                if (emu == null || !SuperZsnesPaths.IsSuperZsnesExecutable(emu.ApplicationPath))
                    emu = (dm.GetAllEmulators() ?? new IEmulator[0]).FirstOrDefault(e => e != null && SuperZsnesPaths.IsSuperZsnesExecutable(e.ApplicationPath));
                return emu == null ? null : SuperZsnesPlugin.ResolveFullPathForUi(emu.ApplicationPath);
            }
            catch { return null; }
        }

        // What the page showed once built - see LbipGameEdit.
        private readonly Dictionary<string, string> _atOpen;

        private void Apply()
        {
            var problem = _page.Problem();
            if (problem != null) { MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (_games.Count > 1
                && MessageBox.Show(this, "Your changes will be applied to all " + _games.Count + " selected games; what each one has of its own and you did not change stays as it is.", Text,
                                   MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            // OK changes only what was changed (LbipGameEdit, Mehdi 04/10): a key the page does not show, a choice no longer
            // in its list, stays as the game has it.
            var shown = _page.Shown();
            int changed = 0;
            foreach (var g in _games)
            {
                var stored = SuperZsnesSettings.ReadGame(g.Id);
                var values = LbIntegrations.Lbip.LbipGameEdit.Merge(stored, _atOpen, shown);
                if (Same(stored, values)) continue;
                SuperZsnesSettings.WriteGame(g.Id, values);
                changed++;
            }
            Log.Info("game options window: " + changed + " change(s), of " + _games.Count + " game(s)");
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool Same(IDictionary<string, string> a, IDictionary<string, string> b)
            => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.Ordinal));

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; } catch { return ""; }
        }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(SuperZsnesGameMenu.IsOurs) ? new[] { SuperZsnesGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == SuperZsnesGameMenu.Caption) SuperZsnesGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => SuperZsnesGameMenu.Icon;
    }
}
