// Reading and unpacking archives, whatever kind they turn out to be.
//
// TWO USES, and the second is the unusual one.
//
// The release asset is a plain zip holding a single melonDS.exe, so System.IO.Compression would do
// for the installer. But the GAMES are the other use: melonDS loads a DS ROM straight out of a zip,
// 7z or rar - libarchive is a required dependency and ARCHIVE_SUPPORT_ENABLED is defined without a
// condition (src/frontend/qt_sdl/CMakeLists.txt:86-92) - and when it does, the save file is named
// after the entry INSIDE the archive, not after the archive. So this plugin has to look inside a
// .7z to know what a save will be called, which System.IO.Compression cannot do.
//
// One library for both, sniffing the bytes rather than trusting an extension.
//
// Extraction is always OVER the destination, never after clearing it: a portable melonDS keeps its
// melonDS.toml, its saves and its savestates inside the very folder we are extracting into. The cost
// is that a file dropped between builds lingers; the alternative is destroying save data.

using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpCompress.Common;

namespace LbIntegrations.NoGba
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

        /// <summary>The name of the first entry whose extension is one of <paramref name="extensions"/>,
        /// and its bytes - up to <paramref name="maxBytes"/>, because the only reason to open a game
        /// archive here is to read a 512-byte header.
        ///
        /// Order is the archive's own, which is what melonDS uses too: its archive picker lists
        /// entries in that order and takes the first when only one matches. Returns false rather than
        /// throwing, because a ROM that cannot be opened must never take a save listing down.</summary>
        public static bool TryReadFirstEntry(string archivePath, string[] extensions, int maxBytes,
                                             out string entryName, out byte[] head)
        {
            entryName = null;
            head = null;
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var key = entry.Key;
                    if (string.IsNullOrEmpty(key)) continue;

                    var ext = Path.GetExtension(key);
                    bool wanted = false;
                    foreach (var candidate in extensions)
                        if (string.Equals(ext, candidate, StringComparison.OrdinalIgnoreCase)) { wanted = true; break; }
                    if (!wanted) continue;

                    using var source = entry.OpenEntryStream();
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while (buffer.Length < maxBytes && (read = source.Read(chunk, 0, chunk.Length)) > 0)
                        buffer.Write(chunk, 0, read);

                    // The name melonDS uses is the entry's own file name, with any folder inside the
                    // archive dropped - it takes romname after the last separator.
                    entryName = Path.GetFileName(key.Replace('/', Path.DirectorySeparatorChar));
                    head = buffer.ToArray();
                    return true;
                }
            }
            catch (Exception ex) { Log.Verbose("could not look inside " + archivePath + " - " + ex.Message); }

            // ArchiveFactory wants a container with a directory it can seek around in, which a
            // gzipped tar is not - measured: .tar opens, .tar.gz does not. ReaderFactory reads a
            // stream forwards instead, which is exactly what a compressed tar is, so it picks up
            // what the other one cannot.
            return TryReadStreaming(archivePath, extensions, maxBytes, out entryName, out head);
        }

        private static bool TryReadStreaming(string archivePath, string[] extensions, int maxBytes,
                                             out string entryName, out byte[] head)
        {
            entryName = null;
            head = null;
            try
            {
                using var file = File.OpenRead(archivePath);
                using var reader = ReaderFactory.Open(file);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory) continue;
                    var key = reader.Entry.Key;
                    if (!Wanted(key, extensions)) continue;

                    using var source = reader.OpenEntryStream();
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while (buffer.Length < maxBytes && (read = source.Read(chunk, 0, chunk.Length)) > 0)
                        buffer.Write(chunk, 0, read);

                    entryName = Path.GetFileName(key.Replace('/', Path.DirectorySeparatorChar));
                    head = buffer.ToArray();
                    return true;
                }
            }
            catch (Exception ex) { Log.Verbose("could not stream " + archivePath + " - " + ex.Message); }
            return false;
        }

        /// <summary>Does this entry's name end in one of the extensions we are after?</summary>
        private static bool Wanted(string key, string[] extensions)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var ext = Path.GetExtension(key);
            foreach (var candidate in extensions)
                if (string.Equals(ext, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Write the first matching entry out to a file, and answer with the name it had
        /// inside the archive.
        ///
        /// FOR EVERY ROM, here, because no$gba cannot open an archive AT ALL. Handed a .zip it puts up
        /// a modal "Cartridge not found" box and waits for somebody to click it - measured. From a
        /// frontend that is not a failed launch, it is a hung one. So every archived ROM is unpacked
        /// before the emulator ever sees it; see NoGbaRoms.</summary>
        public static bool ExtractFirstEntry(string archivePath, string[] extensions, string destPath,
                                             out string entryName)
        {
            entryName = null;
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var key = entry.Key;
                    if (string.IsNullOrEmpty(key)) continue;

                    var ext = Path.GetExtension(key);
                    bool wanted = false;
                    foreach (var candidate in extensions)
                        if (string.Equals(ext, candidate, StringComparison.OrdinalIgnoreCase)) { wanted = true; break; }
                    if (!wanted) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    using (var source = entry.OpenEntryStream())
                    using (var target = File.Create(destPath))
                        source.CopyTo(target);

                    entryName = Path.GetFileName(key.Replace('/', Path.DirectorySeparatorChar));
                    return true;
                }
            }
            catch (Exception ex) { Log.Verbose("could not unpack " + archivePath + " - " + ex.Message); }

            return ExtractStreaming(archivePath, extensions, destPath, out entryName);
        }

        private static bool ExtractStreaming(string archivePath, string[] extensions, string destPath,
                                             out string entryName)
        {
            entryName = null;
            try
            {
                using var file = File.OpenRead(archivePath);
                using var reader = ReaderFactory.Open(file);
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory) continue;
                    var key = reader.Entry.Key;
                    if (!Wanted(key, extensions)) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    using (var source = reader.OpenEntryStream())
                    using (var target = File.Create(destPath))
                        source.CopyTo(target);

                    entryName = Path.GetFileName(key.Replace('/', Path.DirectorySeparatorChar));
                    return true;
                }
            }
            catch (Exception ex) { Log.Verbose("could not stream " + archivePath + " - " + ex.Message); }
            return false;
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
