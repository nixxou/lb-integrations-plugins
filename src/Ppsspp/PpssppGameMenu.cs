// The right-click entry on the games one of our PPSSPP can run: "Nixx-PPSSPP : Updates...", opening PpssppUpdatesForm for
// one game (Mehdi, 04/10: the options window kept its updates only - a game's own settings are PPSSPP's "Game settings").
//
// NOT AN IGameMenuItemPlugin. A plugin in LaunchBox 14's Local\Plugins gets its emulator role and nothing
// else (measured on the Vita3K plugin): the entry is shown by Nixx-Menus, a classic plugin in Plugins\ that
// relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.
//
// WHOSE GAME IS IT: its own emulator when that is ours, else an emulator of ours whose platforms name the
// game's (EmulatorFor): "Launch With" can pick it. What the window sets is kept per install and game id,
// and applied whichever of them runs it.

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ppsspp
{
    internal static class PpssppGameMenu
    {
        public const string Caption = "Nixx-PPSSPP : Compatibility & updates...";

        /// <summary>The emulator's own icon, taken from its executable - see LbipMenuIcon.</summary>
        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(PpssppPaths.IsPpssppExecutable, PpssppPlugin.ResolveFullPathOf);

        internal static bool IsOurs(IGame game) => EmulatorFor(game) != null;

        /// <summary>The emulator of ours this game can run on: its own when that is ours, else any of ours
        /// whose platforms name the game's. Asked at every right-click: lookups in memory only.</summary>
        internal static IEmulator EmulatorFor(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return null;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && PpssppPaths.IsPpssppExecutable(own.ApplicationPath)) return own;
                var platform = game.Platform ?? "";
                foreach (var e in dm.GetAllEmulators() ?? new IEmulator[0])
                {
                    if (e == null || !PpssppPaths.IsPpssppExecutable(e.ApplicationPath)) continue;
                    if ((e.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0]).Any(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase)))
                        return e;
                }
            }
            catch { }
            return null;
        }

        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                if (ours.Count == 0) return;
                const string caption = "Nixx-PPSSPP";
                if (ours.Count > 1) { MessageBox.Show(OwnerWindow(), "A game's compatibility and updates are shown one game at a time: select only one.", caption, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                var g = ours[0];
                PpssppLayout layout = null;
                try
                {
                    var exe = PpssppPlugin.ResolveFullPathOf(EmulatorFor(g)?.ApplicationPath);
                    layout = string.IsNullOrEmpty(exe) ? null : PpssppPaths.Resolve(exe);
                }
                catch { }
                string discId = null;
                try { discId = PspDiscId.Of(PpssppPlugin.ResolveFullPathOf(Safe(() => g.ApplicationPath))); } catch { }
                if (layout == null || string.IsNullOrEmpty(discId))
                {
                    MessageBox.Show(OwnerWindow(), layout == null ? "No PPSSPP of this pack runs this game."
                                    : "The game's id could not be read from its file (an .elf, a homebrew, a .cso inside a zip...): its compatibility and updates cannot be found.",
                                    caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using var form = new PpssppUpdatesForm(g, Safe(() => g.Title), discId, layout);
                form.ShowDialog(OwnerWindow());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; } catch { return ""; }
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

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        public static string[] Entries(IGame[] games)
            => games != null && games.Any(PpssppGameMenu.IsOurs) ? new[] { PpssppGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == PpssppGameMenu.Caption) PpssppGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => PpssppGameMenu.Icon;
    }
}
