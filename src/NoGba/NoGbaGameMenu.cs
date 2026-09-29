// The right-click entry on the games whose emulator is ours - one game or a selection: "Nixx-no$gba :
// Options...", opening NoGbaOptionsForm.
//
// NOT AN IGameMenuItemPlugin. A plugin in LaunchBox 14's Local\Plugins gets its emulator role and
// nothing else (measured on the Vita3K plugin): the entry is shown by Nixx-Menus, a classic plugin in
// Plugins\ that relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.
//
// WHOSE GAME IS IT: any game one of our no$gba can run (Mehdi, 29/09) - its own emulator when that is
// ours, else an emulator of ours whose platforms name the game's (EmulatorFor): "Launch With" can pick it.
// no$gba takes nothing on a command line, so nothing of the window depends on which is the game's own.
//
// WHAT KIND OF GAME: the platform says it first (GBA, DS, DSiWare); a DS game is DSiWare when its
// header says so; a game on another platform is told by its file's extension.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Dsi;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.NoGba
{
    internal static class NoGbaGameMenu
    {
        public const string Caption = "Nixx-no$gba : Options...";

        /// <summary>The emulator's own icon, taken from its executable - see LbipMenuIcon.</summary>
        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(NoGbaPaths.IsNoGbaExecutable, NoGbaPlugin.ResolveFullPath);

        internal static bool IsOurs(IGame game) => EmulatorFor(game) != null;

        /// <summary>The emulator of ours this game can run on (Mehdi, 29/09): its OWN emulator when that one
        /// is ours - else any emulator of ours whose platforms name the game's. Null when none: the entry is
        /// not offered. Asked at every right-click: lookups in memory, nothing read from disk.</summary>
        internal static IEmulator EmulatorFor(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return null;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && NoGbaPaths.IsNoGbaExecutable(own.ApplicationPath)) return own;
                var platform = game.Platform ?? "";
                foreach (var e in dm.GetAllEmulators() ?? new IEmulator[0])
                {
                    if (e == null || !NoGbaPaths.IsNoGbaExecutable(e.ApplicationPath)) continue;
                    if ((e.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0]).Any(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase)))
                        return e;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Is <paramref name="emulator"/> the game's OWN emulator - the one its command line is for?</summary>
        internal static bool IsOwnEmulator(IGame game, IEmulator emulator)
        {
            try { return emulator != null && string.Equals(game?.EmulatorId, emulator.Id, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }


        internal static NoGbaKind KindOf(IGame game, string romFull)
        {
            var platform = Safe(() => game.Platform);
            if (platform.Equals("Nintendo Game Boy Advance", StringComparison.OrdinalIgnoreCase)) return NoGbaKind.Gba;
            if (platform.Equals("Nintendo DSiware", StringComparison.OrdinalIgnoreCase)) return NoGbaKind.DsiWare;
            var ext = Path.GetExtension(romFull ?? "").ToLowerInvariant();
            if (!platform.Equals("Nintendo DS", StringComparison.OrdinalIgnoreCase) && (ext == ".gba" || ext == ".agb" || ext == ".mb")) return NoGbaKind.Gba;
            try { var header = NdsHeader.Describe(romFull); if (header.Known) return header.IsDSiWare ? NoGbaKind.DsiWare : NoGbaKind.Ds; } catch { }
            return platform.Equals("Nintendo DS", StringComparison.OrdinalIgnoreCase) ? NoGbaKind.Ds : NoGbaKind.Gba;
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
                    NoGbaLayout layout = null;
                    try
                    {
                        var exe = NoGbaPlugin.ResolveFullPath(EmulatorFor(g)?.ApplicationPath);
                        layout = string.IsNullOrEmpty(exe) ? null : NoGbaPaths.Resolve(exe);
                    }
                    catch { }
                    var gameId = Safe(() => g.Id);
                    return new NoGbaOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), GameId = gameId, Layout = layout,
                        Kind = KindOf(g, NoGbaPlugin.ResolveFullPath(Safe(() => g.ApplicationPath))),
                        Own = NoGbaGameSettings.Load(layout, gameId),
                        Advanced = NoGbaGameSettings.LoadAdvanced(layout, gameId, out var on),
                        AdvancedOn = on,
                    };
                }).ToList();
                using var form = new NoGbaOptionsForm(entries);
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
            => games != null && games.Any(NoGbaGameMenu.IsOurs) ? new[] { NoGbaGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == NoGbaGameMenu.Caption) NoGbaGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => NoGbaGameMenu.Icon;
    }
}
