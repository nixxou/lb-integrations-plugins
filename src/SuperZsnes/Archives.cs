// Unpacking the release archive, whatever kind it turns out to be.
//
// SUPER ZSNES publishes a plain zip today - 26 entries, flat, SUPERZSNES.exe at the root beside
// SUPERZSNES_Data\ and UnityPlayer.dll. SharpCompress picks its reader by sniffing the bytes, so a
// switch of container upstream costs nothing here.
//
// Extraction is always OVER the destination, never after clearing it. The settings live under
// %USERPROFILE%\AppData\LocalLow today (see SuperZsnesPaths), but the emulator's own extension
// table carries a bare ".portable" literal, so a build may one day keep them beside the executable
// - and a user may already keep ROMs, saves or enhancement files in the install folder. Clearing it
// first would be destroying something we have not proved is not there.

using System;
using System.IO;
using SharpCompress.Archives;

namespace LbIntegrations.SuperZsnes
{
    internal static class Archives
    {
        /// <summary>Extract every entry over <paramref name="destinationDir"/>, overwriting. Entries
        /// whose path escapes the destination are refused rather than trusted.</summary>
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            Directory.CreateDirectory(root);

            using var archive = ArchiveFactory.Open(archivePath);
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
    }
}
