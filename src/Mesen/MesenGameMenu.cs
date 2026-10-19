// The right-click entry of a Mesen game - contract BY NAME with the relay Nixx-Menus (src\Menus\Menus.cs):
//
//     public static class LbIntegrations.<assembly name>.GameMenu
//         string[] Entries(IGame[] games)  /  void Selected(string entry, IGame[] games)  /  Image Icon { get; }
//
// FIRST LOT (18/10): the icon only - Mesen's own, from the exe of the first install of ours in the library (LbipMenuIcon),
// never null. The per-game window (the system, the options on the line, "As set in Mesen (value)" read from settings.json,
// the multi-game rule) is the next lot: Entries offers nothing until then, so no entry shows.

using System.Drawing;
using Unbroken.LaunchBox.Plugins.Data;
using LbIntegrations.Lbip;

namespace LbIntegrations.Mesen
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class GameMenu
    {
        internal const string Caption = "Nixx-Mesen : Options...";

        public static string[] Entries(IGame[] games) => new string[0];

        public static void Selected(string entry, IGame[] games) { }

        public static Image Icon => LbipMenuIcon.Of(MesenLaunch.IsMesenExe, MesenPlugin.ResolveFullPathForUi);
    }
}
