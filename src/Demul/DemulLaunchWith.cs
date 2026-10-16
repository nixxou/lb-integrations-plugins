// Demul's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
// THE ARCADE BOARDS - a strict list: Demul goes by the file's name alone ("-rom=<set>") and runs only the sets of its own
// game table (DemulSets, Known: the name is in demul.exe).
//   unsupported not a zip or 7z, not a set of Demul's boards by that name, a BIOS, or a set this build does not know
//   missing     file: the file is not there; CHD: a Naomi GD-ROM set whose disc is in none of the folders Demul reads -
//               <folder>\<set or parent>\<gdrom>.chd, MAME's layout, in Demul's roms\, the game's folder and the paths of
//               Demul.ini (as DemulLaunch.PrepareIni lays them out)
//   then the DEmul team's arcade_compat.txt of 2018 (DemulCompat):
//   perfect     "Works": boots, plays, nothing noted - the file's own "fully working"
//   playable    plays, with a note
//   nostart     boots or reaches its test mode only, or does not boot
//   (none)      a set Demul knows that the 2018 list does not have - nothing to say
// DREAMCAST - no list: "unsupported" for a file that is no disc Demul reads (.gdi, .cdi, .chd), nothing otherwise.
// No "missing BIOS": the launch looks for the BIOS set in the library itself (DemulBios.Import) - missing now is not missing then.
// The file's name, its existence and two dictionaries in memory: nothing is read.

using System;
using System.Collections.Generic;
using System.Linq;
using LbIntegrations.Lbip;
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
                var full = DemulPlugin.ResolveFullPathForUi(rom);
                if (full != null && !File.Exists(full)) return "missing:file";
                if (DemulPlatforms.BoardOf(game.Platform) == "Dreamcast")
                    return ext == ".gdi" || ext == ".cdi" || ext == ".chd" ? null : "unsupported";
                if (ext != ".zip" && ext != ".7z") return "unsupported";
                if (!DemulSets.All.TryGetValue(Path.GetFileNameWithoutExtension(rom), out var set) || !set.Known || set.Board == DemulSets.Bios) return "unsupported";
                if (set.GdRom != null && !HasGdRom(set, full, Path.GetDirectoryName(DemulPlugin.ResolveFullPathForUi(emulatorPath)))) return "missing:CHD";
                var compat = DemulCompat.Of(rom);
                if (compat == null) return null;
                if (compat.Game) return string.IsNullOrEmpty(compat.Notes) ? "perfect" : "playable";
                return "nostart";
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }

        private static bool HasGdRom(DemulSet set, string rom, string emuDir)
        {
            var folders = new List<string> { Path.GetDirectoryName(rom) };
            if (!string.IsNullOrEmpty(emuDir))
            {
                folders.Add(Path.Combine(emuDir, DemulLaunch.BiosFolder));
                folders.AddRange(RomPaths(Path.Combine(emuDir, DemulLaunch.IniName)));
            }
            foreach (var dir in folders.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var owner in new[] { set.Name, set.Parent })
                    if (owner != null && File.Exists(Path.Combine(dir, owner, set.GdRom + ".chd"))) return true;
            return false;
        }

        /// <summary>The romsN paths of Demul.ini - read again only when the file changes.</summary>
        private static List<string> RomPaths(string ini) => StampedCache.Get(ini, "demul.launchwith.roms", () =>
        {
            var paths = new List<string>();
            if (!File.Exists(ini)) return paths;
            var d = DemulIni.Load(ini);
            int.TryParse(d.Get("files", "romsPathsCount"), out var count);
            for (int i = 0; i < count; i++) { var p = d.Get("files", "roms" + i); if (!string.IsNullOrWhiteSpace(p)) paths.Add(p.Trim()); }
            return paths;
        });
    }
}
