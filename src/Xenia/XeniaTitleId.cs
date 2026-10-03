// The Xbox 360 title id of whatever a library entry points at.
//
// This is what matches a game to its saves, and reading it from the CONTENT rather than the file
// name is what makes the matching survive renames. Four routes, cheapest first:
//
//   STFS container  CON / LIVE / PIRS - Games on Demand, XBLA, DLC, title updates.
//                   One seek: a big-endian integer at 0x360. Sigil has no STFS reader at all, so
//                   these are games Argosy cannot identify and we can.
//   .xex            the executable itself, or a folder holding default.xex.
//   disc image      XDVDFS - NOT ISO9660. Find default.xex at the root, then read its header.
//   .zar            ZArchive, Xenia's own compressed format (Mehdi, 03/10): its magic in a FOOTER, the
//                   disc's files inside - default.xex read there at any offset (src\Shared.Zar).
//
// Detection is by CONTENT, never by extension - Xenia itself sniffs the file and will happily launch
// a disc image named anything at all. Results are cached per (path, size, mtime), failures included:
// GetSaves runs over the whole library and must not re-read discs.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace LbIntegrations.Xenia
{
    internal static class XeniaTitleId
    {
        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The 8 uppercase hex digits, or null. Never throws.</summary>
        public static string Of(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            // A launcher seen to start another game (Minecraft's disc): that game's title id - XeniaRelaunch.
            var started = XeniaRelaunch.TargetOf(path);
            if (started != null && !string.Equals(started, path, StringComparison.OrdinalIgnoreCase)) { var id = Of(started); if (id != null) return id; }

            string key;
            try
            {
                // A library entry may point at a folder (an extracted disc) rather than a file.
                if (Directory.Exists(path))
                {
                    var inner = FindDefaultXex(path);
                    var own = inner == null ? null : Of(inner);
                    return own == null ? null : XeniaLauncherDisc.GameOf(path, own) ?? own;
                }
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }

            if (Cache.TryGetValue(key, out var cached)) return cached.Length == 0 ? null : cached;

            string found = null;
            // AN ARCHIVE - No-Intro's digital sets zip each package: the game inside it, read by its content (XeniaScan), the
            // scan's cache first. One game in it, or the first.
            if (XeniaScan.IsArchive(path))
            {
                try
                {
                    var full = Path.GetFullPath(path);
                    // The scan's cache - this archive's lines, or those of the same archive in another folder (moved).
                    var games = XeniaScan.CachedFile(full);
                    if (games.Count == 0) games = XeniaScan.ClassifyArchive(full, new FileInfo(full).Length, new FileInfo(full).LastWriteTimeUtc.Ticks);
                    found = games.FirstOrDefault(e => e.Kind == XeniaFileKind.Game && e.TitleId.Length == 8)?.TitleId;
                }
                catch (Exception ex) { Log.Warn("could not read a title id from the archive " + path, ex); }
                Cache[key] = found ?? "";
                if (found != null) Log.Info("title id " + found + " <- " + Path.GetFileName(path) + " (the game in it)");
                return found;
            }
            try { found = Extract(path); }
            catch (Exception ex) { Log.Warn("could not read a title id from " + path, ex); }
            // A disc that only launches the game it carries (Minecraft's): that game's id - XeniaLauncherDisc. A note of a real
            // launch (XeniaRelaunch, above) has already won when there is one.
            if (found != null) { var game = XeniaLauncherDisc.GameOf(path, found); if (game != null) found = game; }

            Cache[key] = found ?? "";
            if (found != null) Log.Info("title id " + found + " <- " + Path.GetFileName(path));
            return found;
        }

        private static string Extract(string path)
        {
            // A ZArchive: its default.xex, read inside it (XeniaScan.FromZar).
            if (Zar.ZArchive.IsZar(path))
            {
                var fi = new FileInfo(path);
                var z = XeniaScan.Classify(path, fi.Length, fi.LastWriteTimeUtc.Ticks);
                return z.TitleId.Length == 8 ? z.TitleId : null;
            }
            byte[] head;
            try
            {
                using var fs = File.OpenRead(path);
                head = Xex.ReadAt(fs, 0, 4);
            }
            catch { return null; }
            if (head == null) return null;

            // By content, in the order that costs least.
            if (Stfs.IsStfs(head))
            {
                var info = Stfs.Read(path);
                return info == null ? null : Xex.Format(info.TitleId);
            }
            if (Xex.IsXex(head))
            {
                var id = Xex.TitleIdOfFile(path);
                return id.HasValue ? Xex.Format(id.Value) : null;
            }
            var fromDisc = Xdvdfs.TitleIdOfImage(path);
            return fromDisc.HasValue ? Xex.Format(fromDisc.Value) : null;
        }

        /// <summary>default.xex inside an extracted disc folder. Shallow on purpose: it sits at the root
        /// of a dump, and a deep walk over a game folder is not worth the seconds.</summary>
        private static string FindDefaultXex(string folder)
        {
            try
            {
                var direct = Path.Combine(folder, "default.xex");
                if (File.Exists(direct)) return direct;
                foreach (var sub in Directory.EnumerateDirectories(folder))
                {
                    var nested = Path.Combine(sub, "default.xex");
                    if (File.Exists(nested)) return nested;
                }
            }
            catch (Exception ex) { Log.Warn("could not look for default.xex under " + folder, ex); }
            return null;
        }
    }
}
