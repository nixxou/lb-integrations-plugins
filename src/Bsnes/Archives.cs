// Unpacking the bsnes nightly.
//
// THE ZIP (bsnes-windows.zip, read 06/10) holds one folder, bsnes-nightly\, and everything inside it: bsnes.exe,
// settings.bml, Database\, Shaders\, Locale\, GPLv3.txt. It is extracted WITHOUT that folder, OVER the destination,
// never after clearing it - and settings.bml, the pads, hotkeys and paths the user set, is written only when it is not
// there yet: the zip carries the defaults.

using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Bsnes
{
    internal static class Archives
    {
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            Directory.CreateDirectory(root);

            using var archive = ArchiveFactory.Open(archivePath);
            var entries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
            // The one folder everything sits in, when there is one.
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

        internal static bool KeptIfPresent(string key)
            => key.EndsWith(".bml", StringComparison.OrdinalIgnoreCase) && !key.Contains('/');   // settings.bml (Database\*.bml are the project's)
    }
}
