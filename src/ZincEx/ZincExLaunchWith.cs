// ZiNc EX's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
//   recommended   the romset is one of ZiNc's 71 games (ZincGames, by its ROMs) and ZiNc's readme says nothing against it
//   playable      one of ZiNc's games with a known fault of its board (ZincCompat: System 11 / 12 sound, Raizing's and
//                 FX-1B's sound boards, Psychic Force's first start, Gallop Racer 3's geometry)
//   nostart       none of ZiNc's games - ZiNc EX would refuse it - or not a zip
// The game's window's choice of ZiNc game counts, as at a launch. Answered from ZincExPlugin.SetOf's cache: a zip read the first
// time a game's menu opens, while its file keeps its date and size; a romset that is none of ZiNc's is read again each time
// (its parent or BIOS may come later) - its directory only, a few milliseconds.

using System;
using System.IO;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.ZincEx
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !ZincExLaunch.IsZincEx(emulatorPath)) return null;
                var exe = ZincExPlugin.ResolveFullPathForUi(emulatorPath);
                var rom = ZincExPlugin.ResolveFullPathForUi(game.ApplicationPath);
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe) || string.IsNullOrWhiteSpace(rom)) return null;
                var set = ZincExPlugin.SetOf(exe, rom, game.Id);
                var zinc = set == null ? null : ZincGames.BySet(set);
                if (zinc == null) return "nostart";
                return ZincCompat.Notes(zinc).Count > 0 ? "playable" : "recommended";
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }
    }
}
