// Unpacking the ZiNc EX release.
//
// THE ZIP (ZiNc-EX-v1.0.0.zip, read 15/10) holds one folder, "ZiNc EX\", and everything inside it. It is extracted WITHOUT
// that folder, OVER the destination, never after clearing it: what ZiNc EX and ZiNc write beside themselves stays -
// zinc-settings.cfg, zinc-games.cfg, renderer.cfg, zinc-input.cfg, the profiles\ and game-settings\ folders (none of them
// in the zip: the launcher makes them), cfg\ (ZiNc's own files, the zip has only its dummy.txt), the user's bezels.
// A .cfg the zip would carry one day is still written only when it is not there yet.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace LbIntegrations.ZincEx
{
    internal static class Archives
    {
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            Directory.CreateDirectory(root);

            using var archive = ZipFile.OpenRead(archivePath);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            // The one folder everything sits in, when there is one.
            var first = entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
            string strip = first.Count == 1 && entries.All(e => e.FullName.Replace('\\', '/').Contains('/')) ? first[0] + "/" : null;

            int files = 0, kept = 0;
            foreach (var entry in entries)
            {
                var key = entry.FullName.Replace('\\', '/');
                if (strip != null) key = key.Substring(strip.Length);
                if (key.Length == 0) continue;

                string target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive entry escapes the destination: " + entry.FullName);
                if (KeptIfPresent(key) && File.Exists(target)) { kept++; continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
                files++;
            }
            Log.Info("extracted " + files + " file(s) from " + Path.GetFileName(archivePath) + (strip != null ? " (out of " + strip.TrimEnd('/') + "\\)" : "")
                     + (kept > 0 ? ", " + kept + " of the user's left as they were" : ""));
        }

        /// <summary>A settings file (*.cfg) or anything under cfg\ (ZiNc's own files).</summary>
        internal static bool KeptIfPresent(string key)
            => key.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || key.StartsWith("cfg/", StringComparison.OrdinalIgnoreCase);
    }
}
