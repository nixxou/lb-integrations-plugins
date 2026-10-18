// The right-click entry on the games FCEUX can run - FIRST LOT: the contract and its icon; the game's window comes next.
//
// THE NEXT LOT (docs\fceux.md, the plan): "Nixx-FCEUX : Options..." opens the rich window of the pack (model
// SuperZsnesSettingsPage: tabs, "All selected games (N)" and groups, "Various (n of N)", "As set in FCEUX (<value>)",
// Reset to defaults, and at the bottom "N option(s) added to the command line, before the ROM path:" + the line) with the
// per-game switches FCEUX has (-pal, -dendy, -gg, -no8lim, -clipsides). Until then no entry is offered.
//
// NOT AN IGameMenuItemPlugin, as for the other plugins of the pack: LaunchBox 14 gives a plugin of Local\Plugins its
// emulator role only; Nixx-Menus relays to the GameMenu class below by its name - src\Menus\Menus.cs has the contract.

using System;
using System.Drawing;
using System.Linq;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Fceux
{
    internal static class FceuxGameMenu
    {
        public const string Caption = "Nixx-FCEUX : Options...";

        /// <summary>FCEUX's own icon, from the first install of ours in the library (LbipMenuIcon) - never null.</summary>
        public static Image Icon => LbipMenuIcon.Of(FceuxLaunch.IsFceuxExe, FceuxPlugin.ResolveFullPathForUi);

        /// <summary>A game of ours: its own emulator is an FCEUX, or its platform is one FCEUX runs and an FCEUX is in the
        /// library ("Launch With" can pick it). Asked at every right-click: lookups in memory only.</summary>
        internal static bool IsOurs(IGame game)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null || game == null) return false;
                var own = string.IsNullOrWhiteSpace(game.EmulatorId) ? null : dm.GetEmulatorById(game.EmulatorId);
                if (own != null && FceuxLaunch.IsFceuxExe(own.ApplicationPath)) return true;
                if (FceuxSystems.Platform(game.Platform) == null) return false;
                return (dm.GetAllEmulators() ?? new IEmulator[0]).Any(e => e != null && FceuxLaunch.IsFceuxExe(e.ApplicationPath));
            }
            catch { return false; }
        }
    }

    /// <summary>What Nixx-Menus calls, by name - see src\Menus\Menus.cs. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        /// <summary>None yet: the game's window is the next lot (see the header).</summary>
        public static string[] Entries(IGame[] games) => new string[0];

        public static void Selected(string entry, IGame[] games) { }

        public static Image Icon => FceuxGameMenu.Icon;
    }
}
