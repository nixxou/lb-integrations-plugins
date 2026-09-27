// Which update and which DLC a game is launched with, when the user chose - in its options, the
// "Updates & DLC" tab. A game with no choice gets the automatic one (Vita3kExtras.Choose): the
// highest update, every DLC.
//
//     <emulator folder>\lbip-vita-extras.choice
//
// One line per game, tab-separated: the LaunchBox game ID, then the update ("none", or the archive's
// path), then the CONTENT_IDs of the DLC left out, comma-separated. A DLC is recorded as LEFT OUT,
// not as chosen: one found after the choice was made is installed, as it would be with no choice.
// Choosing the automatic values again removes the game's line.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kExtrasChoice
    {
        public const string FileName = "lbip-vita-extras.choice";
        private const string None = "none";

        public bool NoUpdate;
        public string UpdatePath;
        public HashSet<string> LeftOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The same as no choice at all.</summary>
        public bool Automatic => !NoUpdate && UpdatePath == null && LeftOut.Count == 0;

        public static string PathFor(string installDir) => Path.Combine(installDir, FileName);

        /// <summary>The choice recorded for <paramref name="gameId"/>, or null.</summary>
        public static Vita3kExtrasChoice Load(string installDir, string gameId)
        {
            if (string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(gameId)) return null;
            try
            {
                var file = PathFor(installDir);
                if (!File.Exists(file)) return null;
                foreach (var line in File.ReadAllLines(file))
                {
                    var f = line.Split('\t');
                    if (f.Length < 2 || !string.Equals(f[0], gameId, StringComparison.OrdinalIgnoreCase)) continue;
                    var choice = new Vita3kExtrasChoice();
                    if (string.Equals(f[1], None, StringComparison.OrdinalIgnoreCase)) choice.NoUpdate = true;
                    else if (f[1].Length > 0) choice.UpdatePath = f[1];
                    if (f.Length > 2)
                        foreach (var id in f[2].Split(',', StringSplitOptions.RemoveEmptyEntries)) choice.LeftOut.Add(id.Trim());
                    return choice.Automatic ? null : choice;
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + FileName, ex); }
            return null;
        }

        /// <summary>The game's line written - or removed, for the automatic choice.</summary>
        public static void Save(string installDir, string gameId, Vita3kExtrasChoice choice)
        {
            var file = PathFor(installDir);
            var lines = File.Exists(file)
                ? File.ReadAllLines(file).Where(l => !l.StartsWith(gameId + "\t", StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<string>();
            if (choice != null && !choice.Automatic)
                lines.Add(gameId + "\t" + (choice.NoUpdate ? None : choice.UpdatePath ?? "") + "\t" + string.Join(",", choice.LeftOut.OrderBy(x => x, StringComparer.Ordinal)));
            Directory.CreateDirectory(installDir);
            var tmp = file + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, file, overwrite: true);
            Log.Info("updates and DLC of game " + gameId + ": " + (choice == null || choice.Automatic ? "automatic"
                     : (choice.NoUpdate ? "no update" : choice.UpdatePath != null ? "update " + Path.GetFileName(choice.UpdatePath) : "the highest update")
                       + ", " + choice.LeftOut.Count + " DLC left out"));
        }
    }
}
