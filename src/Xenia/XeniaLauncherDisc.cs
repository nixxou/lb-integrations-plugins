// A disc that only LAUNCHES the game it carries - Minecraft's (Mehdi, 04/10): its default.xex (4D530A81) starts an Arcade
// package on the disc, Content\0000000000000000\584111F7\000D0000\49AAD8..., and that package is the game - its title id
// is what its patches, its updates and its saves go by. XeniaRelaunch learns it at the first launch, from Xenia; this
// tells it from the disc ALONE, before any launch, by its shape:
//
//   1. ONE game package (Content\<16 hex>\<title id>\000D0000\<name>) on the disc - several is a compilation: no choice made;
//   2. of another title id than the disc's own;
//   3. at least TWO THIRDS of the disc's own files - $SystemUpdate, $TitleUpdate, AvatarAssetPack and any other package
//      (DLC...) left out. A launcher is a wrapper round its package (Minecraft: 118.7 MB of 130.6, 91 %); a full game
//      carrying a bonus Arcade game has its own gigabytes beside it (under 20 %) - the case that must NOT be redirected,
//      or its saves would be looked for under the bonus's id.
//   And a veto, only when Xenia's compatibility list is there (a local copy, no network here): the disc's own id listed
//   as a game is a game, not a launcher.
//
// What Xenia does at a launch stays the authority: once XeniaRelaunch has a note for the disc, it wins (XeniaTitleId).
// Read: a .zar (its file table), an XDVDFS image (its tree), a folder (an extracted disc). An archive holding a disc is
// not opened for this - its first launch tells.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LbIntegrations.Xenia
{
    internal static class XeniaLauncherDisc
    {
        private static readonly Regex GamePackage = new Regex(@"^content\\[0-9a-f]{16}\\([0-9a-f]{8})\\000d0000\\[^\\]+$", RegexOptions.IgnoreCase);
        private static readonly Regex AnyPackage = new Regex(@"^content\\[0-9a-f]{16}\\", RegexOptions.IgnoreCase);
        private static readonly string[] SystemFolders = { "$systemupdate\\", "$titleupdate\\", "avatarassetpack" };

        // The disc's stamp -> the game's title id ("" for none), and why, for the menu and the log.
        private static readonly ConcurrentDictionary<string, (string Id, string Why)> Seen = new ConcurrentDictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The title id of the game this disc only launches - null when it is not such a disc (see the header).
        /// <paramref name="discId"/>: the disc's own default.xex title id. Never throws.</summary>
        public static string GameOf(string path, string discId)
        {
            string stamp;
            try
            {
                if (Directory.Exists(path)) stamp = Path.GetFullPath(path) + "|dir";
                else { var fi = new FileInfo(path); if (!fi.Exists) return null; stamp = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; }
            }
            catch { return null; }
            if (Seen.TryGetValue(stamp, out var known)) return known.Id.Length == 0 ? null : known.Id;
            string id = null, why;
            try { id = Judge(Files(path), discId, out why); }
            catch (Exception ex) { why = "the disc could not be read (" + ex.Message + ")"; }
            Seen[stamp] = (id ?? "", why);
            if (why != null) Log.Info("launcher disc: " + Path.GetFileName(path) + " - " + why);
            return id;
        }

        /// <summary>Is this title id one taken from the game a disc carries (not yet confirmed by a launch) - for the menu's
        /// "deduced from the disc".</summary>
        public static bool IsDeduced(string titleId)
            => !string.IsNullOrEmpty(titleId) && Seen.Values.Any(k => string.Equals(k.Id, titleId, StringComparison.OrdinalIgnoreCase));

        public const string DeducedNote = " (deduced from the disc: it only launches the game it carries - confirmed at its first launch)";

        internal static string Judge(List<(string Path, long Length)> files, string discId, out string why)
        {
            why = null;
            if (files == null || files.Count == 0) return null;
            var norm = files.Select(f => (Path: f.Path.Replace('/', '\\').TrimStart('\\'), f.Length)).ToList();
            var games = norm.Select(f => (f.Path, f.Length, M: GamePackage.Match(f.Path))).Where(x => x.M.Success).ToList();
            if (games.Count == 0) return null;                                   // the common case: said nothing
            if (games.Count > 1) { why = games.Count + " game packages on the disc - a compilation, its own id kept"; return null; }
            var game = games[0];
            var id = game.M.Groups[1].Value.ToUpperInvariant();
            if (string.Equals(id, discId, StringComparison.OrdinalIgnoreCase)) return null;
            long own = norm.Where(f => !SystemFolders.Any(s => f.Path.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                                       && !(AnyPackage.IsMatch(f.Path) && f.Path != game.Path)).Sum(f => f.Length);
            double share = own == 0 ? 0 : (double)game.Length / own;
            var figures = (game.Length / 1048576.0).ToString("0.#") + " MB of " + (own / 1048576.0).ToString("0.#") + " MB (" + (int)(share * 100) + " %)";
            if (share < 2.0 / 3)
            { why = "carries the game " + id + " but it is only " + figures + " of the disc - a game of its own, its id " + discId + " kept"; return null; }
            if (discId != null && XeniaCompat.Lookup(discId).Count > 0)
            { why = "carries the game " + id + " (" + figures + "), but " + discId + " is a game in Xenia's compatibility list - its own id kept"; return null; }
            why = "only launches the game it carries, " + id + " (" + figures + " of the disc) - its title id is " + id
                  + " (the disc's own is " + (discId ?? "?") + ")";
            return id;
        }

        private static List<(string Path, long Length)> Files(string path)
        {
            if (Directory.Exists(path))
            {
                var root = Path.GetFullPath(path).TrimEnd('\\') + "\\";
                return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(20000)
                                .Select(f => (f.Substring(root.Length), new FileInfo(f).Length)).ToList();
            }
            if (Zar.ZArchive.IsZar(path))
            {
                using var z = Zar.ZArchive.Open(path);
                return z == null ? null : z.Entries.Where(e => !e.IsDirectory).Select(e => (e.Path, e.Length)).ToList();
            }
            using var fs = File.OpenRead(path);
            return Xdvdfs.AllFiles((offset, length) => Xex.ReadAt(fs, offset, length));
        }
    }
}
