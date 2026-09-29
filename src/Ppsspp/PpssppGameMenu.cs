// The right-click entry on the games one of our PPSSPP can run - one game or a selection: "Nixx-PPSSPP :
// Options...", opening PpssppOptionsForm.
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
        public const string Caption = "Nixx-PPSSPP : Options...";

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
                Log.Info("game menu: " + ours.Count + " game(s) of ours selected" + (games.Length > ours.Count ? ", " + (games.Length - ours.Count) + " other(s) left out" : ""));
                if (ours.Count == 0) return;
                var entries = ours.Select(g =>
                {
                    PpssppLayout layout = null;
                    try
                    {
                        var exe = PpssppPlugin.ResolveFullPathOf(EmulatorFor(g)?.ApplicationPath);
                        layout = string.IsNullOrEmpty(exe) ? null : PpssppPaths.Resolve(exe);
                    }
                    catch { }
                    var gameId = Safe(() => g.Id);
                    string discId = null;
                    try { discId = PspDiscId.Of(PpssppPlugin.ResolveFullPathOf(Safe(() => g.ApplicationPath))); } catch { }
                    return new PpssppOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), GameId = gameId, DiscId = discId, Layout = layout,
                        Own = PpssppGameSettings.Load(layout, gameId),
                        Defaults = PpssppGameSettings.DefaultsOf(layout, discId, out var fromGame),
                        GameConfig = fromGame,
                        Advanced = PpssppGameSettings.LoadAdvanced(layout, gameId, out var on),
                        AdvancedOn = on,
                    };
                }).ToList();
                using var form = new PpssppOptionsForm(entries);
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
