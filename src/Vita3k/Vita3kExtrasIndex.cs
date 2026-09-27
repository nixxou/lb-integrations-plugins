// What the import found beside the games: the updates and DLC that came in the same batch of files,
// recorded so that a launch can find them even when they sit nowhere its own rules look.
//
//     <emulator folder>\lbip-vita-extras.tsv
//
// One line per extra, tab-separated, a header line first:
//
//     game  game_size  title_id  kind  version  content_id  size  mtime  path  title
//
// "game" is the game archive it was found with, by FILE NAME and size - not its path: a library moved
// elsewhere is the same library (empty when the batch held none of that title id). "kind" is update or
// dlc. "path" is RELATIVE TO THE GAME'S FOLDER when the extra is in it or under it - resolved at a
// launch against where the game is NOW, so a folder moved whole keeps its index - and full otherwise.
// Plain text on purpose: somebody can read it, and fix it.
//
// NO DUPLICATES. A line is the game (name + size) and the extra (title id + file name + size): an
// import replaces the lines of every game archive it looked at, whatever path it found them under, and
// the same extra twice in one batch is one line.
//
// HINTS, NEVER THE TRUTH (Mehdi's rule). A line only ADDS a candidate to Vita3kExtras' own rules, which
// keep running; a file that is not there is passed over; and every candidate is still read and kept
// only when its own param.sfo says it belongs to the game. The lines of other archives of the same
// title id are proposed too - another dump, another region: the param.sfo decides.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Vita3k
{
    internal sealed class IndexedExtra
    {
        public string Game = "", TitleId = "", Kind = "", Version = "", ContentId = "", Path = "", Title = "";
        public long GameSize, Size, Ticks;

        public const string Update = "update", Dlc = "dlc";

        /// <summary>The game it was found with: name and size.</summary>
        public string GameKey => (Game ?? "").ToLowerInvariant() + "|" + GameSize.ToString(CultureInfo.InvariantCulture);

        /// <summary>The line itself: its game, and the extra by title id, file name and size.</summary>
        public string Key => GameKey + "|" + (TitleId ?? "").ToUpperInvariant() + "|"
                             + System.IO.Path.GetFileName(Path ?? "").ToLowerInvariant() + "|" + Size.ToString(CultureInfo.InvariantCulture);
    }

    internal static class Vita3kExtrasIndex
    {
        public const string FileName = "lbip-vita-extras.tsv";
        private const string Header = "game\tgame_size\ttitle_id\tkind\tversion\tcontent_id\tsize\tmtime\tpath\ttitle";

        public static string PathFor(string installDir) => System.IO.Path.Combine(installDir, FileName);

        public static List<IndexedExtra> Read(string installDir)
        {
            var rows = new List<IndexedExtra>();
            try
            {
                var file = PathFor(installDir);
                if (!File.Exists(file)) return rows;
                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.Length == 0 || line.StartsWith("game\t", StringComparison.Ordinal) || line.StartsWith("#")) continue;
                    var f = line.Split('\t');
                    if (f.Length < 10) continue;
                    rows.Add(new IndexedExtra
                    {
                        Game = f[0], GameSize = Long(f[1]), TitleId = f[2], Kind = f[3], Version = f[4], ContentId = f[5],
                        Size = Long(f[6]), Ticks = Long(f[7]), Path = f[8], Title = f[9],
                    });
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + FileName, ex); }
            return rows;
        }

        /// <summary>An import's findings: every line of the game archives in <paramref name="games"/> -
        /// by name and size - is replaced by <paramref name="found"/>'s, the other lines kept.</summary>
        public static void Record(string installDir, IEnumerable<string> games, IEnumerable<IndexedExtra> found)
        {
            try
            {
                var fresh = found.GroupBy(r => r.Key).Select(g => g.First()).ToList();
                // The games looked at: their lines are this import's now. Not the lines of NO game - an
                // extra imported without its game: those only go when the same one is found again (Key).
                var redone = new HashSet<string>(games.Select(g => new IndexedExtra { Game = System.IO.Path.GetFileName(g), GameSize = SizeOf(g) }.GameKey)
                                                      .Concat(fresh.Where(r => r.Game.Length > 0).Select(r => r.GameKey)));
                var keep = Read(installDir).Where(r => !redone.Contains(r.GameKey));
                var rows = keep.Concat(fresh)
                    .GroupBy(r => r.Key).Select(g => g.Last())
                    .OrderBy(r => r.TitleId, StringComparer.Ordinal).ThenBy(r => r.Game, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var file = PathFor(installDir);
                Directory.CreateDirectory(installDir);
                var tmp = file + ".tmp";
                File.WriteAllLines(tmp, new[] { Header }.Concat(rows.Select(Line)));
                File.Move(tmp, file, overwrite: true);
                Log.Info("import: " + fresh.Count + " update(s)/DLC recorded in " + file + " (" + rows.Count + " line(s) in all)");
            }
            catch (Exception ex) { Log.Warn("could not write " + FileName, ex); }
        }

        /// <summary>The files the index proposes for <paramref name="titleId"/>, the game being
        /// <paramref name="romPath"/>: a relative path resolved against its folder, only those still there.
        /// Hints - Vita3kExtras reads each one before keeping it.</summary>
        public static List<string> For(string installDir, string titleId, string romPath)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(installDir) || string.IsNullOrEmpty(titleId)) return paths;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Read(installDir))
            {
                if (!string.Equals(r.TitleId, titleId, StringComparison.OrdinalIgnoreCase)) continue;
                var path = Resolve(r.Path, romPath);
                if (path != null && File.Exists(path) && seen.Add(path)) paths.Add(path);
            }
            return paths;
        }

        /// <summary>A line of the index for an extra found with <paramref name="game"/> (null: with none).</summary>
        public static IndexedExtra From(string game, string path, VitaContent content)
        {
            long size = 0, ticks = 0;
            try { var i = new FileInfo(path); size = i.Length; ticks = i.LastWriteTimeUtc.Ticks; } catch { }
            return new IndexedExtra
            {
                Game = string.IsNullOrEmpty(game) ? "" : System.IO.Path.GetFileName(game), GameSize = string.IsNullOrEmpty(game) ? 0 : SizeOf(game),
                TitleId = content.TitleId ?? "", Kind = content.IsPatch ? IndexedExtra.Update : IndexedExtra.Dlc,
                Version = content.AppVer ?? "", ContentId = content.ContentId ?? "", Size = size, Ticks = ticks,
                Path = Relative(game, path), Title = content.FullTitle ?? content.Title ?? "",
            };
        }

        /// <summary>The extra's path as the index keeps it: relative to the game's folder when it is in
        /// it or under it, full otherwise.</summary>
        internal static string Relative(string game, string path)
        {
            var full = Full(path);
            if (string.IsNullOrEmpty(game)) return full;
            var dir = System.IO.Path.GetDirectoryName(Full(game));
            if (string.IsNullOrEmpty(dir)) return full;
            var rel = System.IO.Path.GetRelativePath(dir, full);
            return rel.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(rel) ? full : rel;
        }

        internal static string Resolve(string stored, string romPath)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            try
            {
                if (System.IO.Path.IsPathRooted(stored)) return System.IO.Path.GetFullPath(stored);
                var dir = System.IO.Path.GetDirectoryName(Full(romPath));
                return dir == null ? null : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, stored));
            }
            catch { return null; }
        }

        private static string Line(IndexedExtra r)
            => string.Join("\t", new[] { r.Game, r.GameSize.ToString(CultureInfo.InvariantCulture), r.TitleId, r.Kind, r.Version, r.ContentId,
                                         r.Size.ToString(CultureInfo.InvariantCulture), r.Ticks.ToString(CultureInfo.InvariantCulture),
                                         r.Path, r.Title }.Select(Clean));

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static long Long(string s)
            => long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static long SizeOf(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }

        private static string Full(string p) { try { return System.IO.Path.GetFullPath(p); } catch { return p ?? ""; } }
    }
}
