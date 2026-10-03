// Unpacking a release archive, whatever kind it turns out to be.
//
// Taken from the Xenia plugin. Cxbx-Reloaded publishes a zip today; SharpCompress sniffs the bytes, so a
// change of kind costs nothing here.
//
// Extraction is always OVER the destination, never after clearing it: a portable Cxbx-Reloaded keeps its
// settings.ini and its EmuDisk (the user's saves) inside the very folder we are extracting into. The cost
// is that a file dropped between builds lingers; the alternative is destroying save data.

using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LbIntegrations.Cxbx
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

        /// <summary>What kind of archive this is, for a log line. Never throws.</summary>
        public static string KindOf(string archivePath)
        {
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                return archive.Type.ToString();
            }
            catch { return "unreadable"; }
        }
    }
}
