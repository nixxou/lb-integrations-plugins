// Unpacking the Demul archive.
//
// Demul ships as a solid .7z, flat: demul.exe at the root beside its plugins\ folder, the *.ini files
// of each plugin, nvram\, roms\ and scache\ (measured on demul_251220.7z). SharpCompress picks its
// reader by sniffing the bytes, as in the Xenia plugin, whose canary build is a .7z too.
//
// Extraction is OVER the destination, never after clearing it - and an UPDATE keeps what the user
// made of the install: the archive carries padDemul.ini, gpuDX11.ini, spuDemul.ini... at their
// shipped values, so overwriting them would throw away the user's controls and screen settings, and
// nvram\ holds the arcade games' saves. Those are written only when they are not there yet.

using System;
using System.IO;
using SharpCompress.Archives;

namespace LbIntegrations.Demul
{
    internal static class Archives
    {
        /// <summary>Extract every entry over <paramref name="destinationDir"/>. A file the user may
        /// have changed (see <see cref="KeptIfPresent"/>) is left as it is when it exists. Entries
        /// whose path escapes the destination are refused rather than trusted.</summary>
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            Directory.CreateDirectory(root);

            using var archive = ArchiveFactory.Open(archivePath);
            int files = 0, kept = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key)) continue;

                string target = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive entry escapes the destination: " + key);

                if (KeptIfPresent(key) && File.Exists(target)) { kept++; continue; }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var source = entry.OpenEntryStream())
                using (var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    source.CopyTo(destination);
                files++;
            }
            Log.Info("extracted " + files + " file(s) from " + Path.GetFileName(archivePath)
                     + (kept > 0 ? ", " + kept + " of the user's left as they were" : ""));
        }

        /// <summary>A plugin's settings file (*.ini) or anything under nvram\.</summary>
        internal static bool KeptIfPresent(string key)
        {
            var k = key.Replace('\\', '/');
            return k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("nvram/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
