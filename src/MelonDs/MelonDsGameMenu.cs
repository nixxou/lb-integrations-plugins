// The right-click entry on DS games whose emulator is ours - one game or a selection: "Nixx-melonDS :
// Options...", opening MelonDsOptionsForm.
//
// NOT AN IGameMenuItemPlugin. A plugin in LaunchBox 14's Local\Plugins gets its emulator role and
// nothing else (measured on the Vita3K plugin): the entry is shown by Nixx-Menus, a classic plugin in
// Plugins\ that relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.
// None may be declared here: LiteBox loads both roots and would show the entry twice.
//
// WHOSE GAME IS IT: the game's OWN emulator (IGame.EmulatorId), the one Play uses - ours when that
// emulator's executable is melonDS's. Shown for EVERY DS game of ours (Mehdi, 29/09): the window's
// options grow by tabs, and the RAM disk one is only there for DSiWare.

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

        public static Image Icon => _icon ??= SystemIcons.Application.ToBitmap();
        private static Image _icon;

        internal static bool IsOurs(IGame game)
        {
            try
            {
                var id = game?.EmulatorId;
                if (string.IsNullOrWhiteSpace(id)) return false;
                var emulator = PluginHelper.DataManager?.GetEmulatorById(id);
                return emulator != null && MelonDsPaths.IsMelonDsExecutable(emulator.ApplicationPath);
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

                var entries = ours.Select(g =>
                {
                    var own = Safe(() => g.CommandLine);
                    var inherited = InheritedLine(g);
                    var rom = MelonDsPlugin.ResolveFullPath(Safe(() => g.ApplicationPath));
                    bool ware = false;
                    try { ware = NdsHeader.Describe(rom).IsDSiWare; } catch { }
                    string exe = null;
                    try { exe = MelonDsPlugin.ResolveFullPath(PluginHelper.DataManager?.GetEmulatorById(g.EmulatorId)?.ApplicationPath); } catch { }
                    var install = exe == null ? null : MelonDsPaths.Resolve(exe)?.InstallDir;
                    var gameId = Safe(() => g.Id);
                    return new MelonDsOptionsForm.Entry
                    {
                        Game = g, Title = Safe(() => g.Title), Own = own, Inherited = inherited, IsDSiWare = ware, Exe = exe,
                        InstallDir = install, GameId = gameId,
                        Options = MelonDsOptions.From(string.IsNullOrWhiteSpace(own) ? inherited : own),
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
        internal static string InheritedLine(IGame game)
        {
            try
            {
                var emulator = PluginHelper.DataManager?.GetEmulatorById(game?.EmulatorId);
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
