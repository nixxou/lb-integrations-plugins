// Unpacking Flycast's release archive.
//
// The Xenia plugin needs SharpCompress because canary's Windows asset became a .7z; Flycast's has
// been a plain .zip throughout, and its release carries exactly one Windows file
// (flycast-win64-2.7.zip), so System.IO.Compression is enough and there is one fewer library to
// merge. If upstream ever changes format this is the file to revisit.
//
// Extraction is always OVER the destination, never after clearing it. Flycast is portable: emu.cfg
// sits beside the executable and data\ - every VMU, every save state, all the arcade NVRAM - is
// inside the folder we are extracting into. The cost is that a file dropped between releases
// lingers; the alternative is destroying save data.

using System;
using System.IO;
using System.IO.Compression;

namespace LbIntegrations.Flycast
{
    internal static class Archives
    {
        /// <summary>Extract every entry over <paramref name="destinationDir"/>, overwriting. Entries
        /// whose path escapes the destination are refused rather than trusted.</summary>
        public static void ExtractOver(string archivePath, string destinationDir)
        {
            string root = Path.GetFullPath(destinationDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);

            using var archive = ZipFile.OpenRead(archivePath);
            int files = 0;
            foreach (var entry in archive.Entries)
            {
                // A directory entry has an empty name; its path is created with its children anyway.
                if (string.IsNullOrEmpty(entry.Name)) continue;

                string target = Path.GetFullPath(
                    Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive entry escapes the destination: " + entry.FullName);

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, overwrite: true);
                files++;
            }
            Log.Info("extracted " + files + " file(s) from " + Path.GetFileName(archivePath));
        }
    }
}
