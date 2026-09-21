// Unpacking a release archive, whatever kind it turns out to be.
//
// The PPSSPP plugin could use System.IO.Compression because PPSSPP publishes zips. Xenia canary
// switched its Windows asset from .zip to .7z in June 2026 - and renamed it twice before that - so
// the archive kind is not something to infer from a name. SharpCompress reads both by sniffing the
// bytes, which also means a future switch back costs nothing here.
//
// Extraction is always OVER the destination, never after clearing it: a portable Xenia keeps its
// content\, its config and the user's saves inside the very folder we are extracting into. The cost
// is that a file dropped between builds lingers; the alternative is destroying save data.

using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LbIntegrations.Xenia
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
