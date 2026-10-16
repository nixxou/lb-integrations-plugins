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
        private sealed class Answer { public string Level, Note; }

        public static string Level(IGame game, string emulatorPath) => Ask(game, emulatorPath)?.Level;

        /// <summary>The tooltip: the known faults of the game's board (ZiNc's readme), what is missing, or which game ZiNc runs.</summary>
        public static string Note(IGame game, string emulatorPath) => Ask(game, emulatorPath)?.Note;

        private static Answer Ask(IGame game, string emulatorPath)
        {
            try
            {
                if (game == null || !ZincExLaunch.IsZincEx(emulatorPath)) return null;
                var exe = ZincExPlugin.ResolveFullPathForUi(emulatorPath);
                var rom = ZincExPlugin.ResolveFullPathForUi(game.ApplicationPath);
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe) || string.IsNullOrWhiteSpace(rom)) return null;
                if (!File.Exists(rom)) return new Answer { Level = "missing:file", Note = "The game's file is not there: " + rom };
                if (!rom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return new Answer { Level = "unsupported", Note = "ZiNc EX reads its games as MAME zips." };
                var chosen = ZincGames.BySet(ZincExSettings.SetFor(game.Id));
                var exeDir = Path.GetDirectoryName(exe);

                Answer last = null;
                var kept = StampedCache.Get(rom, "zincex.launchwith|" + (chosen?.Set ?? "") + "|" + exeDir, () =>
                {
                    last = Classify(rom, exeDir, chosen);
                    return last.Level.StartsWith("missing", StringComparison.Ordinal) ? null : last;
                }, keepNull: false);
                return kept ?? last ?? Classify(rom, exeDir, chosen);
            }
            catch (Exception ex) { Log.Warn("LaunchWith.Level", ex); return null; }
        }

        /// <summary>One of ZiNc's games: recommended, or playable with its board's known faults.</summary>
        private static Answer Runs(ZincGame g, string head)
        {
            var notes = ZincCompat.Notes(g);
            head += g.Title + " (" + g.Set + ")";
            return new Answer { Level = notes.Count > 0 ? "playable" : "recommended", Note = notes.Count > 0 ? head + "\n" + string.Join("\n", notes) : head + "." };
        }

        private static Answer Classify(string rom, string exeDir, ZincGame chosen)
        {
            var match = ZincGames.Identify(rom, ZincExLaunch.Folders(exeDir, rom), out var miss, chosen);
            if (match != null) return Runs(match.Game, "ZiNc runs it as ");
            if (miss?.Closest != null && miss.Lacking.Length > 0)
            {
                var g = miss.Closest;
                // What keeps it from starting, then how it runs once it is there - two badges (16/10).
                if (!string.IsNullOrEmpty(g.Bios) && miss.BiosZip == null)
                {
                    var then = Runs(g, "Once there, ZiNc runs it as ");
                    return new Answer { Level = "missing:BIOS;" + then.Level, Note = g.Title + " needs its BIOS set, " + g.Bios + ".zip (or MAME's name for it), beside the game or in ZiNc's roms folder.\n" + then.Note };
                }
                if (!string.IsNullOrEmpty(g.Parent) && miss.ParentZip == null)
                {
                    var then = Runs(g, "Once there, ZiNc runs it as ");
                    return new Answer { Level = "missing:parent;" + then.Level, Note = g.Title + " needs its parent set, " + g.Parent + ".zip, beside the game.\n" + then.Note };
                }
                return new Answer { Level = "version", Note = "Not the dump of " + g.Title + " ZiNc knows (ZiNc's " + g.Set + "): " + (miss.Lacking.Length == 1 ? "one of its ROMs is" : miss.Lacking.Length + " of its ROMs are") + " not in this zip - another version of the game, or an incomplete set." };
            }
            if (miss?.Reason == "it could not be read as a zip") return new Answer { Level = "invalid", Note = "This file does not read as a zip." };
            return new Answer { Level = "unsupported", Note = "Not one of ZiNc's 71 games: " + (miss?.Reason ?? "unknown") + "." };
        }
    }
}
