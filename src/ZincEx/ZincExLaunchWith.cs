// ZiNc EX's verdict in "Launch With" - what Nixx-Menus shows on its line for a game (src\Menus\LaunchWith\Labels.cs has the
// contract, by name: LbIntegrations.<assembly name>.LaunchWith.Level).
//
//   recommended   the romset is one of ZiNc's 71 games (ZincGames, by its ROMs) and ZiNc's readme says nothing against it
//   playable      one of ZiNc's games with a known fault of its board (ZincCompat: System 11 / 12 sound, Raizing's and
//                 FX-1B's sound boards, Psychic Force's first start, Gallop Racer 3's geometry)
//   missing       one of ZiNc's games whose BIOS zip or parent zip is not there - ZiNc EX would refuse it until it is
//                 (ZincGames.Identify's Miss: the game it comes closest to, what it lacks); or its file is not there
//   version       some of a ZiNc game's own ROMs but not all: MAME's other dump of it (its sfex is the Euro set, ZiNc's is
//                 MAME's sfexu) - or an incomplete zip; the two are not told apart
//   unsupported   none of ZiNc's games - a BIOS set, another game of a ZN board, any other zip - or not a zip at all
//   invalid       a .zip that does not read as one
// The game's window's choice of ZiNc game counts, as at a launch. A game found is kept while its zip keeps its date and size
// (StampedCache); a romset lacking something is read again at each opening (its parent or BIOS may come any time) - its
// directory only, a few milliseconds.

using System;
using System.IO;
using LbIntegrations.Lbip;
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
                if (!File.Exists(rom)) return "missing:file";
                if (!rom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return "unsupported";
                var chosen = ZincGames.BySet(ZincExSettings.SetFor(game.Id));
                var exeDir = Path.GetDirectoryName(exe);

                string last = null;
                var kept = StampedCache.Get(rom, "zincex.launchwith|" + (chosen?.Set ?? "") + "|" + exeDir, () =>
                {
                    last = Classify(rom, exeDir, chosen);
                    return last.StartsWith("missing", StringComparison.Ordinal) ? null : last;
                }, keepNull: false);
                return kept ?? last ?? Classify(rom, exeDir, chosen);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }

        private static string Classify(string rom, string exeDir, ZincGame chosen)
        {
            var match = ZincGames.Identify(rom, ZincExLaunch.Folders(exeDir, rom), out var miss, chosen);
            if (match != null) return ZincCompat.Notes(match.Game).Count > 0 ? "playable" : "recommended";
            if (miss?.Closest != null && miss.Lacking.Length > 0)
            {
                var g = miss.Closest;
                if (!string.IsNullOrEmpty(g.Bios) && miss.BiosZip == null) return "missing:BIOS";
                if (!string.IsNullOrEmpty(g.Parent) && miss.ParentZip == null) return "missing:parent";
                return "version";
            }
            if (miss?.Reason == "it could not be read as a zip") return "invalid";
            return "unsupported";
        }
    }
}
