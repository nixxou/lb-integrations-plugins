// The right-click entry on DS games whose emulator is ours - one game or a selection: "Nixx-melonDS :
// Options...", opening MelonDsOptionsForm.
//
// NOT AN IGameMenuItemPlugin. A plugin in LaunchBox 14's Local\Plugins gets its emulator role and
// nothing else (measured on the Vita3K plugin): the entry is shown by Nixx-Menus, a classic plugin in
// Plugins\ that relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.
// None may be declared here: LiteBox loads both roots and would show the entry twice.
//
// WHOSE GAME IS IT: any game one of our melonDS can run (Mehdi, 29/09) - its own emulator when that is
// ours, else an emulator of ours whose platforms name the game's (EmulatorFor): "Launch With" can pick it.
// The session option lives in the game's command line, which is its OWN emulator's: it is only offered
// when that emulator is ours. The window's options grow by tabs; the RAM disk one is only for DSiWare.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Dsi;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsGameMenu
    {
        public const string Caption = "Nixx-melonDS : Options...";

        /// <summary>The emulator's own icon, taken from its executable - see LbipMenuIcon.</summary>
        public static Image Icon => LbIntegrations.Lbip.LbipMenuIcon.Of(MelonDsPaths.IsMelonDsExecutable, MelonDsPlugin.ResolveFullPath);

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
                if (own != null && MelonDsPaths.IsMelonDsExecutable(own.ApplicationPath)) return own;
                var platform = game.Platform ?? "";
                foreach (var e in dm.GetAllEmulators() ?? new IEmulator[0])
                {
                    if (e == null || !MelonDsPaths.IsMelonDsExecutable(e.ApplicationPath)) continue;
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


        internal static void Open(IGame[] games)
        {
            try
            {
                var ours = games.Where(IsOurs).ToList();
                Log.Info("game menu: " + ours.Count + " game(s) of ours selected" + (games.Length > ours.Count ? ", " + (games.Length - ours.Count) + " other(s) left out" : ""));
                if (ours.Count == 0) return;

                var entries = ours.Select(g =>
                {
                    var emulator = EmulatorFor(g);
                    var own = Safe(() => g.CommandLine);
                    var inherited = InheritedLine(g, emulator);
                    var rom = MelonDsPlugin.ResolveFullPath(Safe(() => g.ApplicationPath));
                    bool ware = false;
                    try { ware = NdsHeader.Describe(rom).IsDSiWare; } catch { }
                    string exe = null;
                    try { exe = MelonDsPlugin.ResolveFullPath(emulator?.ApplicationPath); } catch { }
                    var install = exe == null ? null : MelonDsPaths.Resolve(exe)?.InstallDir;
                    var gameId = Safe(() => g.Id);
                    return new MelonDsOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), Own = own, Inherited = inherited, IsDSiWare = ware, Exe = exe,
                        LineIsOurs = IsOwnEmulator(g, emulator), EmulatorTitle = Safe(() => PluginHelper.DataManager?.GetEmulatorById(g.EmulatorId)?.Title),
                        InstallDir = install, GameId = gameId,
                        // What the game runs with: the options kept for it, else its line's flags.
                        Options = MelonDsOptions.From(MelonDsSessionStore.Load(install, gameId) ?? (string.IsNullOrWhiteSpace(own) ? inherited : own)),
                        Settings = MelonDsGameSettings.Load(install, gameId),
                        Advanced = MelonDsGameSettings.LoadAdvanced(install, gameId, out var handOn),
                        AdvancedOn = handOn,
                    };
                }).ToList();

                using var form = new MelonDsOptionsForm(entries);
                form.ShowDialog(OwnerWindow());
            }
            catch (Exception ex) { Log.Warn("game menu", ex); }
        }

        /// <summary>The line a game runs with when it has none of its own: its emulator's line for the
        /// game's platform, else the emulator's own.</summary>
        internal static string InheritedLine(IGame game, IEmulator emulator)
        {
            try
            {
                if (emulator == null) return "";
                var platform = Safe(() => game.Platform);
                var forPlatform = (emulator.GetAllEmulatorPlatforms() ?? new IEmulatorPlatform[0])
                    .FirstOrDefault(p => string.Equals(Safe(() => p.Platform), platform, StringComparison.OrdinalIgnoreCase));
                var line = Safe(() => forPlatform?.CommandLine);
                return string.IsNullOrWhiteSpace(line) ? Safe(() => emulator.CommandLine) : line;
            }
            catch { return ""; }
        }

        private static string Safe(Func<string> read)
        {
            try { return read() ?? ""; } catch { return ""; }
        }

        /// <summary>The host's active window, so the dialog is modal to it and comes up in front.</summary>
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
            => games != null && games.Any(MelonDsGameMenu.IsOurs) ? new[] { MelonDsGameMenu.Caption } : new string[0];

        public static void Selected(string entry, IGame[] games)
        {
            if (entry == MelonDsGameMenu.Caption) MelonDsGameMenu.Open(games ?? new IGame[0]);
        }

        public static Image Icon => MelonDsGameMenu.Icon;
    }
}
