// Unpacking the puNES zip over an install.
//
// THE ZIP (measured 18/10 on punes-0.111-x86_64-win-d3d9.zip, System.IO.Compression listing) is FLAT, no top folder:
// punes64.exe, 7z.dll, the ffmpeg DLLs (avcodec-58, avformat-58, avutil-56, swresample-3, swscale-5), cg.dll, cgD3D9.dll,
// libwinpthread-1.dll, dip.cfg and nes20db.xml - those two are puNES's OWN data (its dip-switch and game databases), so an
// update replaces them like the rest. Extraction is OVER the destination, never after clearing it: puNES.cfg, input.cfg
// and recent.cfg (the user's settings, keys and recent games) and the bios\ prb\ save\ diff\ screenshot\ pgs\ cheat\ shp\
// jsc\ folders are not in the zip and cannot be touched - and should a later zip ship one of those three files, the
// user's is kept (KeptIfPresent).

using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Punes
{
    internal static class Archives
    {
        /// <summary>The user's files, never replaced by a zip's.</summary>
        internal static readonly string[] UsersFiles = { PunesCfg.MainFile, PunesCfg.InputFile, "recent.cfg" };

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

        /// <summary>A file of the user's: puNES.cfg, input.cfg, recent.cfg - at the root, by name. Not dip.cfg: puNES's own.</summary>
        internal static bool KeptIfPresent(string key)
        {
            var name = (key ?? "").Replace('\\', '/');
            return !name.Contains('/') && UsersFiles.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
