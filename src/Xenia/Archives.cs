// Unpacking a release archive, whatever kind it turns out to be.
//
// The PPSSPP plugin could use System.IO.Compression because PPSSPP publishes zips. Xenia canary
// switched its Windows asset from .zip to .7z in June 2026 - and renamed it twice before that - so
// the archive kind is not something to infer from a name. SharpCompress reads both by sniffing the
// bytes, which also means a future switch back costs nothing here.
//
// Extraction is always OVER the destination, never after clearing it: a portable Xenia keeps its
// content\, its config and the user's saves inside the very folder we are extracting into. The cost
// is that a file dropped between builds lingers; the alternative is destroying save data.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LbIntegrations.Xenia
{
    internal static class Archives
    {
        private static readonly System.Text.RegularExpressions.Regex PartVolume =
            new System.Text.RegularExpressions.Regex("^(?<stem>.+)\\.part(?<n>\\d+)\\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>An archive opened whole - a RAR in volumes with all of them (Mehdi, 03/10: .rar as .zip and .7z):
        /// "game.part1.rar" with its .part2.rar..., "game.rar" with its .r00, .r01... SharpCompress reads the volumes in
        /// the order given. Anything else as ArchiveFactory opens it, by its bytes.</summary>
        public static IArchive Open(string path)
        {
            var volumes = Volumes(path);
            if (volumes.Count > 1) return ArchiveFactory.Open(volumes.Select(v => new FileInfo(v)));
            return ArchiveFactory.Open(path);
        }

        /// <summary>The volumes of a RAR set, first first - just the file when it is not one.</summary>
        public static List<string> Volumes(string path)
        {
            var list = new List<string> { path };
            try
            {
                var dir = Path.GetDirectoryName(path) ?? ".";
                var name = Path.GetFileName(path);
                var m = PartVolume.Match(name);
                if (m.Success)
                {
                    var stem = m.Groups["stem"].Value;
                    var parts = Directory.EnumerateFiles(dir, stem + ".part*.rar")
                        .Select(f => (File: f, M: PartVolume.Match(Path.GetFileName(f))))
                        .Where(x => x.M.Success && string.Equals(x.M.Groups["stem"].Value, stem, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => int.Parse(x.M.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
                        .Select(x => x.File).ToList();
                    if (parts.Count > 1) return parts;
                }
                else if (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    var stem = name.Substring(0, name.Length - 4);
                    var old = Directory.EnumerateFiles(dir, stem + ".r??")
                        .Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), "\\.r\\d\\d$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    if (old.Count > 0) { list.AddRange(old); return list; }
                }
            }
            catch { }
            return list;
        }

        /// <summary>A volume that is not its set's first - "game.part2.rar": not an archive on its own, the first one stands
        /// for the set.</summary>
        public static bool IsLaterVolume(string path)
        {
            var m = PartVolume.Match(Path.GetFileName(path ?? ""));
            return m.Success && int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) > 1;
        }

        /// <summary>Extract every entry over <paramref name="destinationDir"/>, overwriting. Entries
        /// whose path escapes the destination are refused rather than trusted.</summary>
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            Directory.CreateDirectory(root);

            using var archive = Open(archivePath);
            int files = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key)) continue;

                string target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive entry escapes the destination: " + key);

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var source = entry.OpenEntryStream())
                using (var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);
                files++;
            }
            Log.Info("extracted " + files + " file(s) from " + Path.GetFileName(archivePath));
        }

        /// <summary>What kind of archive this is, for a log line. Never throws.</summary>
        public static string KindOf(string archivePath)
        {
            try
            {
                using var archive = Open(archivePath);
                return archive.Type.ToString();
            }
            catch { return "unreadable"; }
        }
    }
}
