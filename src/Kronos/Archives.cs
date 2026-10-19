// Unpacking archives: Kronos's build, and a game in a .zip / .7z (docs\kronos.md §6).
//
// THE BUILD (Kronos_2.7.5_-_64_Bits.zip, Tradu-France, read 19/10) holds its files at the root - kronos.exe, the Qt and SDL2
// DLLs, its translations, vc_redist.x64.exe, the read-me files - and an EMPTY kronos.ini (Kronos's portable mode). It is
// extracted OVER the destination, never after clearing it: kronos.ini (KeptIfPresent), bios\, Saves\ and everything else
// stay. A single top folder, should a later build put one, is taken off; a path that would leave the destination is refused.

using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Kronos
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

        /// <summary>The user's settings: Tradu-France's zip carries an EMPTY kronos.ini (Kronos's portable mode) - an update never
        /// writes it over the one Kronos and the plugin filled.</summary>
        internal static bool KeptIfPresent(string key) => string.Equals(Path.GetFileName((key ?? "").Replace('/', '\\')), KronosLaunch.IniName, StringComparison.OrdinalIgnoreCase);
    }
}
