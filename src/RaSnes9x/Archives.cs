// Unpacking the RASnes9x zip, and a game's .7z.
//
// THE EMULATOR'S ZIP is flat: RASnes9x.exe at the root, its Overlay\ folder (RetroAchievements' sounds and pictures),
// changes.txt, the readme and the licence (measured on RASnes9x-x64.zip of 2024-12-09). Extraction is OVER the
// destination, never after clearing it: snes9x.conf (the user's keys and pads), RAPrefs_RASnes9X.cfg (the
// RetroAchievements login), RA_Integration-x64.dll and the Saves\ folder are not in the zip, so an update cannot touch
// them - and KeptIfPresent keeps the overlay's theme.json too, should the user have edited it.

using System;
using System.IO;
using SharpCompress.Archives;

namespace LbIntegrations.RaSnes9x
{
    internal static class Archives
    {
        /// <summary>Extract every entry over <paramref name="destinationDir"/>. Entries whose path escapes the destination
        /// are refused rather than trusted.</summary>
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
                if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
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

        /// <summary>A file the user may have changed: a .conf/.cfg/.ini, or the overlay's theme.</summary>
        internal static bool KeptIfPresent(string key)
        {
            var k = key.Replace('\\', '/');
            return k.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith("theme.json", StringComparison.OrdinalIgnoreCase);
        }
    }
}
