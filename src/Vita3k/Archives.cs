// Unpacking a release archive, whatever kind it turns out to be.
//
// Vita3K publishes a .zip today - windows-latest.zip, flat, with Vita3K.exe at its root, measured on
// build 4098 - so System.IO.Compression would do. SharpCompress is used anyway, because the archive
// kind is not something to infer from a name: the neighbouring Xenia plugin watched its asset be
// renamed twice and then change format outright, and this reader picks its decoder by sniffing the
// bytes rather than trusting the extension.
//
// Extraction is always OVER the destination, never after clearing it. For Vita3K that is not a
// nicety: portable\ sits inside the very folder being extracted into, and it holds the firmware,
// the config, the installed apps and the saves. Clearing first would destroy all of it on every
// update. The cost is that a file dropped between builds lingers.

using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace LbIntegrations.Vita3k
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
