// Unpacking archives: Ymir's release, and a game in a .zip / .7z (Ymir opens neither - docs\ymir.md §4).
//
// THE RELEASE (ymir-windows-x86_64-AVX2-v0.4.1.zip, measured 20/10) holds its files at the root - ymir-sdl3.exe,
// dxcompiler.dll, dxil.dll, gamecontrollerdb.txt, licenses\, the README and CHANGELOG - nothing of the user's. It is
// extracted OVER the destination, never after clearing it: Ymir.toml, roms\, backup\, savestates\ and everything else Ymir
// made in its profile stay. A single top folder, should a later release put one, is taken off; a path that would leave the
// destination is refused.

using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Ymir
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

        /// <summary>The user's settings, should a release ever carry one.</summary>
        internal static bool KeptIfPresent(string key) => string.Equals(key, YmirToml.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
