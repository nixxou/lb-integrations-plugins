// What a launch of ZiNc EX needs: which of ZiNc's games the romset is, a folder holding its zips under ZiNc's names, that
// folder in ZiNc EX's settings, and the command line.
//
// ZINC EX'S COMMAND LINE (its readme, "Frontends"; settings.c run_game_cli):
//
//     ZiNc-EX.exe --game <set name or ROM file> [--fullscreen | --windowed]
//
// It finds the game by the set's NAME only, starts ZiNc.exe with the global and the game's own settings of ZiNc EX (its
// renderer, controls profile, bezel...) without showing its window, waits for ZiNc to end and exits with it: 0 played, 2
// unknown game, 3 ROM files missing, 4 ZiNc.exe could not be started, 5 no game given - the reason written to
// ZiNc-EX_launch.log beside it. --fullscreen / --windowed are for that start only.
//
// So the line is always "--game <ZiNc's set>": the set the romset was found to be (ZincGames), whatever its file is called
// - never the path, whose name may be another game's (MAME's sfex.zip is not ZiNc's sfex).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExLaunch
    {
        public const string ExecutableName = "ZiNc-EX.exe";
        public const string Executable32Name = "ZiNc-EX-32bit.exe";
        public const string ZincName = "ZiNc.exe";
        public const string LaunchLogName = "ZiNc-EX_launch.log";

        /// <summary>ZiNc EX's 64-bit or 32-bit launcher - not ZiNc.exe alone, which starts a game only by its number.</summary>
        public static bool IsZincEx(string path)
        {
            try
            {
                var name = Path.GetFileName((path ?? "").Trim().Trim('"'));
                return string.Equals(name, ExecutableName, StringComparison.OrdinalIgnoreCase) || string.Equals(name, Executable32Name, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Where a game's parent and BIOS zips are looked for, in order: the game's folder, ZiNc EX's own roms\ (where
        /// a BIOS can be put once for every game), the ROMs folder of ZiNc EX's settings.</summary>
        public static List<string> Folders(string exeDir, string rom)
        {
            var list = new List<string>();
            void Add(string d) { if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d) && !list.Any(x => ZincExConfig.SameFolder(x, d))) list.Add(d); }
            try { Add(Path.GetDirectoryName(Path.GetFullPath(rom))); } catch { }
            Add(Path.Combine(exeDir, ZincExConfig.DefaultFolder));
            Add(ZincExConfig.UserFolder(exeDir));
            return list;
        }

        /// <summary>Which of ZiNc's games <paramref name="rom"/> is - the game's window's choice when it made one - or null with
        /// <paramref name="problem"/> said for the user. Reads zips, writes nothing. Never throws.</summary>
        public static ZincGames.Match Identify(string exe, string rom, string gameId, out string problem)
        {
            problem = null;
            try
            {
                if (string.IsNullOrWhiteSpace(rom) || !File.Exists(rom)) { problem = "The game's file is not there: " + rom; return null; }
                if (!rom.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    problem = "ZiNc reads its games as .zip romsets, and " + Path.GetFileName(rom) + " is not one.\n\n"
                              + "Turn off \"Extract ROMs\" for this emulator in LaunchBox: the zip is given as it is.";
                    return null;
                }
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
                var chosen = ZincGames.BySet(ZincExSettings.SetFor(gameId));
                var folders = Folders(exeDir, rom);
                var match = ZincGames.Identify(rom, folders, out var miss, chosen);
                if (match != null)
                {
                    Log.Info(Path.GetFileName(rom) + " is ZiNc's " + match.Game.Set + " (" + match.Game.Title + ")"
                             + (chosen != null ? ", as chosen in its window" : match.ByName ? "" : ", told by its ROMs"));
                    return match;
                }
                problem = Explain(rom, miss, chosen, folders);
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("could not tell which game " + rom + " is", ex);
                problem = "The romset could not be read: " + ex.Message;
                return null;
            }
        }

        private static string Explain(string rom, ZincGames.Miss miss, ZincGame chosen, List<string> folders)
        {
            var file = Path.GetFileName(rom);
            if (miss?.Closest == null)
                return file + " is none of the 71 games ZiNc 1.1 runs: " + (miss?.Reason ?? "it holds none of their ROMs") + ".";
            var g = miss.Closest;
            var text = (chosen != null ? file + " cannot be started as " + g.Title + " (" + g.Set + "), chosen in its window:"
                                       : file + " is not a set ZiNc 1.1 can run. The closest is " + g.Title + " (" + g.Set + "), and")
                       + " ZiNc's ROM" + (miss.Lacking.Length == 1 ? " " : "s ") + string.Join(", ", miss.Lacking.Take(6)) + (miss.Lacking.Length > 6 ? "..." : "")
                       + (miss.Lacking.Length == 1 ? " is" : " are") + " in none of its zips.";
            var others = new List<string>();
            // The parent and BIOS zips not found - not the romset itself, never looked for as its own parent.
            var self = Path.GetFileNameWithoutExtension(rom);
            if (!string.IsNullOrEmpty(g.Parent) && miss.ParentZip == null && !string.Equals(g.Parent, self, StringComparison.OrdinalIgnoreCase))
                others.Add("its parent " + g.Parent + ".zip");
            if (!string.IsNullOrEmpty(g.Bios) && miss.BiosZip == null)
                others.Add("its BIOS " + g.Bios + ".zip" + (ZincGames.BiosAliases.TryGetValue(g.Bios, out var a) && a.Length > 0 ? " (" + string.Join(", ", a.Select(x => x + ".zip")) + " in MAME)" : ""));
            if (others.Count > 0)
                text += "\n\nNot found: " + string.Join(", nor ", others) + " - looked for in:\n" + string.Join("\n", folders);
            var sibling = chosen == null ? Sibling(rom, g, folders) : null;
            if (sibling != null)
                text += "\n\n" + Path.GetFileName(sibling) + ", in the same folder, IS ZiNc's " + g.Set + ": give this game that file (Edit > ROM File) to play it.";
            text += "\n\nZiNc's sets are those of MAME 0.87 (2004). A set from a recent MAME is found by its ROMs whatever its name, "
                    + "but a few of them hold other revisions than ZiNc's.";
            return text;
        }

        /// <summary>Another zip of the romset's folder that is <paramref name="game"/> - MAME names a game's versions after it
        /// (tekken3 -> tekken3je1, sfex -> sfexu), so only the zips whose name starts like the game's set or the romset's are
        /// read. Null when none is.</summary>
        private static string Sibling(string rom, ZincGame game, List<string> folders)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(rom));
                var stems = new[] { game.Set, Path.GetFileNameWithoutExtension(rom) }.Where(x => x.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var stem in stems)
                    foreach (var zip in Directory.EnumerateFiles(dir, stem + "*.zip").Take(40))
                        if (!ZincGames.SamePath(zip, rom) && ZincGames.Identify(zip, folders, out _, game) != null) return zip;
            }
            catch (Exception ex) { Log.Verbose("no sibling looked for - " + ex.Message); }
            return null;
        }

        /// <summary>"--game &lt;set&gt;", the window flag for this launch, and whatever else the user put on the line - the game's
        /// path and an earlier --game / screen flag taken out.</summary>
        public static string CommandLine(string current, string rom, string set, string screen)
        {
            var head = "--game " + set + (screen == "fullscreen" ? " --fullscreen" : screen == "windowed" ? " --windowed" : "");
            var kept = new List<string>();
            // The game's path out first, as a whole: with "no quotes" on the emulator, a path with spaces is several tokens.
            var line = current ?? "";
            if (!string.IsNullOrWhiteSpace(rom))
                foreach (var form in new[] { "\"" + rom + "\"", rom })
                    for (int at; (at = line.IndexOf(form, StringComparison.OrdinalIgnoreCase)) >= 0;) line = line.Remove(at, form.Length).Insert(at, " ");
            var tokens = Tokenize(line);
            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Length == 0 || IsTheGame(t, rom)) continue;
                if (t.Equals("--game", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                if (t.StartsWith("--game=", StringComparison.OrdinalIgnoreCase) || t.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase)
                    || t.Equals("--windowed", StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(t.Any(char.IsWhiteSpace) ? "\"" + t + "\"" : t);
            }
            return kept.Count == 0 ? head : head + " " + string.Join(" ", kept);
        }

        private static bool IsTheGame(string token, string rom)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(rom)) return false;
            try { if (Path.IsPathRooted(token) && string.Equals(Path.GetFullPath(token), Path.GetFullPath(rom), StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            return string.Equals(token, Path.GetFileName(rom), StringComparison.OrdinalIgnoreCase);
        }

        internal static List<string> Tokenize(string line)
        {
            var list = new List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line ?? "")
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted) { if (any || cur.Length > 0) list.Add(cur.ToString()); cur.Clear(); any = false; continue; }
                cur.Append(c);
            }
            if (any || cur.Length > 0) list.Add(cur.ToString());
            return list;
        }
    }
}
