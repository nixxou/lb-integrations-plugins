// Unpacking FCEUX's release, and reading what is inside a game's archive.
//
// THE RELEASE ZIP is flat (fceux-2.7.0-win64.zip, 99 entries, no top folder - measured 18/10): extracted OVER the install,
// never after clearing it. It carries no fceux.cfg (FCEUX writes it at its first exit); one found in a zip anyway (a later
// release) never replaces the user's: the pads, hotkeys and folders are theirs. A single top folder, should a release
// get one, is taken off as for bsnes.
//
// A GAME'S ARCHIVE is opened by FCEUX itself (7z_64.dll: zip, 7z, rar...; gzip in the core - docs\fceux.md §9): it is passed
// as it is (AutoExtract false). Its saves take the name of the ROM INSIDE it, not the archive's (measured: Zelda2-zip.zip
// and Zelda2-7z.7z wrote "Zelda II - The Adventure of Link (Europe).sav" / ".fc1"), so the plugin reads the entries: the
// ROM's name for the saves, its extension for the system (a .fds inside is a disk game).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Fceux
{
    internal static class Archives
    {
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            Directory.CreateDirectory(root);

            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
            var first = entries.Select(e => e.Key.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
            string strip = first.Count == 1 && entries.All(e => e.Key.Replace('\\', '/').Contains('/')) ? first[0] + "/" : null;

            int files = 0, kept = 0;
            foreach (var entry in entries)
            {
                var key = entry.Key.Replace('\\', '/');
                if (strip != null) key = key.Substring(strip.Length);
                if (key.Length == 0) continue;

                string target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive entry escapes the destination: " + entry.Key);
                if (KeptIfPresent(key) && File.Exists(target)) { kept++; continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var source = entry.OpenEntryStream())
                using (var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);
                files++;
            }
            Log.Info("extracted " + files + " file(s) from " + Path.GetFileName(archivePath) + (strip != null ? " (out of " + strip.TrimEnd('/') + "\\)" : "")
                     + (kept > 0 ? ", " + kept + " of the user's left as they were" : ""));
        }

        /// <summary>The user's config, beside the exe: fceux.cfg.</summary>
        internal static bool KeptIfPresent(string key)
            => string.Equals(key, FceuxCfg.FileName, StringComparison.OrdinalIgnoreCase);

        public static bool IsArchive(string path)
            => FceuxSystems.Archives.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>The ROMs of a game's archive: the entries FCEUX looks at (.nes, .fds, .nsf - fceu.cpp romextensions), and
        /// apart the UNIF ones it does NOT look at inside an archive. Null when the archive cannot be read.</summary>
        public static (List<string> Seen, List<string> Unif) RomEntries(string archivePath)
        {
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                var keys = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).Select(e => e.Key).ToList();
                var seen = keys.Where(k => FceuxSystems.ArchiveRomExtensions.Contains(Path.GetExtension(k).ToLowerInvariant())).ToList();
                var unif = keys.Where(k => new[] { ".unf", ".unif" }.Contains(Path.GetExtension(k).ToLowerInvariant())).ToList();
                return (seen, unif);
            }
            catch (Exception ex) { Log.Info("could not read the archive " + archivePath + " - " + ex.Message); return (null, null); }
        }

        /// <summary>The ROM FCEUX plays from an archive, when there is exactly one it looks at - the name of its saves; null
        /// otherwise (none, or several: FCEUX then asks which, fceu.cpp:444).</summary>
        public static string RomInside(string archivePath)
        {
            if (!IsArchive(archivePath)) return null;
            var (seen, _) = RomEntries(archivePath);
            return seen != null && seen.Count == 1 ? Path.GetFileName(seen[0].Replace('\\', '/')) : null;
        }

        /// <summary>One entry unpacked to <paramref name="target"/> (written only when it is not already there with the
        /// archive's size and a date not older than the archive's).</summary>
        public static bool Unpack(string archivePath, string entryKey, string target)
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.Key == entryKey);
            if (entry == null) return false;
            if (File.Exists(target) && new FileInfo(target).Length == entry.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(archivePath)) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var src = entry.OpenEntryStream())
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                LbIntegrations.Lbip.LbipLaunchStatus.CopyStream(src, dst, entry.Size, "Extracting " + Path.GetFileName(entryKey) + "…");
            return true;
        }
    }
}
