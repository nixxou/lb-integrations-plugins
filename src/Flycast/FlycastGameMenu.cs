// The right-click entry on the games one of our Flycast can run - one game or a selection: "Nixx-Flycast :
// Options...", opening FlycastOptionsForm.
//
// NOT AN IGameMenuItemPlugin. A plugin in LaunchBox 14's Local\Plugins gets its emulator role and nothing
// else (measured on the Vita3K plugin): the entry is shown by Nixx-Menus, a classic plugin in Plugins\ that
// relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.
//
// WHOSE GAME IS IT: its own emulator when that is ours, else an emulator of ours whose platforms name the
// game's (EmulatorFor): "Launch With" can pick it. What the window sets is kept per install and game id,
// and given to whichever of them runs it.

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    internal static class FlycastGameMenu
    {
        public const string Caption = "Nixx-Flycast : Options...";

        /// <summary>The emulator's own icon, taken from its executable - see LbipMenuIcon.</summary>
        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(FlycastPaths.IsFlycastExecutable, FlycastPlugin.ResolveFullPath);

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
                if (own != null && FlycastPaths.IsFlycastExecutable(own.ApplicationPath)) return own;
                var platform = game.Platform ?? "";
                foreach (var e in dm.GetAllEmulators() ?? new IEmulator[0])
                {
                    if (e == null || !FlycastPaths.IsFlycastExecutable(e.ApplicationPath)) continue;
                    if ((e.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0]).Any(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase)))
                        return e;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Dreamcast or arcade, by the game's platform: a Dreamcast unless it is one of the arcade ones
        /// (a homebrew filed under a platform of its own is a Dreamcast disc).</summary>
        internal static FlycastGameSettings.Games KindOf(string platform)
            => FlycastPlatforms.IsArcade(platform) ? FlycastGameSettings.Games.Arcade : FlycastGameSettings.Games.Dreamcast;

        /// <summary>The line the host starts this emulator with for the game's platform, else its own.</summary>
        private static string LineOf(IEmulator emu, string platform)
        {
            try
            {
                var row = (emu?.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0])
                          .FirstOrDefault(p => string.Equals(p?.Platform, platform, StringComparison.OrdinalIgnoreCase));
                var line = row?.CommandLine;
                return string.IsNullOrWhiteSpace(line) ? emu?.CommandLine ?? "" : line;
            }
            catch { return ""; }
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
                    var emu = EmulatorFor(g);
                    FlycastLayout layout = null;
                    try
                    {
                        var exe = FlycastPlugin.ResolveFullPath(emu?.ApplicationPath);
                        layout = string.IsNullOrEmpty(exe) ? null : FlycastPaths.Resolve(exe);
                    }
                    catch { }
                    var gameId = Safe(() => g.Id);
                    var platform = Safe(() => g.Platform);
                    var kind = KindOf(platform);
                    // Its id: kept, else asked of flycast-id.exe - for a selection of a few games only, a larger
                    // one showing what is kept - and a Dreamcast disc's IP.BIN read here when it has no answer.
                    var rom = FlycastPlugin.ResolveFullPath(Safe(() => g.ApplicationPath));
                    string product = ours.Count <= 10 ? FlycastGameIdentity.Of(layout, rom, 8000, out _) : FlycastGameIdentity.Cached(layout, rom);
                    if (product == null && kind == FlycastGameSettings.Games.Dreamcast)
                        try { product = FlycastGameId.Of(rom); } catch { }
                    return new FlycastOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), GameId = gameId, Product = product, Games = kind, Layout = layout,
                        Line = LineOf(emu, platform),
                        Own = FlycastGameSettings.Load(layout, gameId),
                        Defaults = FlycastGameSettings.DefaultsOf(layout, product, out var wins),
                        GameConfig = wins,
                        Advanced = FlycastGameSettings.LoadAdvanced(layout, gameId, out var on),
                        AdvancedOn = on,
                    };
                }).ToList();
                using var form = new FlycastOptionsForm(entries);
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
            => games != null && games.Any(FlycastGameMenu.IsOurs) ? new[] { FlycastGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == FlycastGameMenu.Caption) FlycastGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => FlycastGameMenu.Icon;
    }
}
