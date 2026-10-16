// Demul's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// THE ARCADE BOARDS - a strict list: Demul goes by the file's name alone ("-rom=<set>") and runs only the sets of its own
// game table (DemulSets, Known: the name is in demul.exe).
//   nostart     not a zip or 7z, not a set of Demul's boards by that name, a BIOS, or a set this build does not know
//   then the DEmul team's arcade_compat.txt of 2018 (DemulCompat):
//   playable    "Works": boots, plays, nothing noted
//   glitches    plays, with a note
//   nostart     boots or reaches its test mode only, or does not boot
//   (none)      a set Demul knows that the 2018 list does not have - nothing to say
// DREAMCAST - no list: "nostart" for a file that is no disc Demul reads (.gdi, .cdi, .chd), nothing otherwise.
// The file's name and two dictionaries in memory: nothing is read.

using System;
using System.IO;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Demul
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signature fixed.</summary>
    public static class LaunchWith
    {
        public static string Level(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !Settings.IsDemulExe(emulatorPath)) return null;
                var rom = game.ApplicationPath;
                if (string.IsNullOrWhiteSpace(rom)) return null;
                var ext = Path.GetExtension(rom).ToLowerInvariant();
                if (DemulPlatforms.BoardOf(game.Platform) == "Dreamcast")
                    return ext == ".gdi" || ext == ".cdi" || ext == ".chd" ? null : "nostart";
                if (ext != ".zip" && ext != ".7z") return "nostart";
                if (!DemulSets.All.TryGetValue(Path.GetFileNameWithoutExtension(rom), out var set) || !set.Known || set.Board == DemulSets.Bios) return "nostart";
                var compat = DemulCompat.Of(rom);
                if (compat == null) return null;
                if (compat.Game) return string.IsNullOrEmpty(compat.Notes) ? "playable" : "glitches";
                return "nostart";
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }
    }
}
