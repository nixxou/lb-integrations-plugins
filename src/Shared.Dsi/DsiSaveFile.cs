// A DSiWare save, as one file: <title id>\state.dsisave.
//
// IT IS A ZIP, and the extension is a label rather than a format - rename it and any tool opens it.
// Inside is exactly what used to be a folder: files.txt, one flat-named file per captured entry,
// and base.zip / base.txt, the recipe of the console it was made on.
//
// WHY A FILE AT ALL. The host has two shapes for a save and only one of them is well trodden. A
// container is extracted by the plugin into a temp folder and lands in the vault as a FOLDER with no
// extension; a file is copied straight in and lands as <game name><extension>. Every other plugin in
// this repository that manages a single save takes the second path, and every defect this one had -
// backups that made empty folders, restores refused with "this backup is not a file", a Delete Save
// that did nothing and said nothing - came from the first.
//
// AND IT IS DETERMINISTIC, which is not decoration: on the file path the host fingerprints a save by
// hashing its BYTES. A zip rewritten differently from identical content would make the freshness dot
// flicker at every launch, and a sync see a change that never happened. Three rules get there:
//
//   sorted     entries in ordinal order of their name, whatever order the folder hands them back
//   dated      one constant, 1980-01-01 - the source's own timestamps are never read
//   stored     no compression at all
//
// STORED RATHER THAN A FIXED COMPRESSION LEVEL. This assembly targets net9.0-windows, and .NET 9
// moved to zlib-ng: the same bytes deflated by two runtimes are not the same bytes. Storing removes
// the variable instead of pinning it. The cost is nothing at this size - a measured state is about
// 70 KB, the largest seen 4.2 MB - and the one bulky member, base.zip, is already compressed.
//
// THE WRITER IS SharpCompress, DELIBERATELY. It is pinned at 0.41.0 and ILRepack folds it into this
// DLL, so its header layout ships with us and cannot drift when the host's runtime is upgraded.
// System.IO.Compression's writer is code we do not ship.
//
// Nothing here trusts that any of the above is true: MelonDsCheck packs the same content twice, in
// two orders and with different file timestamps, and compares the sha256.

using System;
using System.Collections.Generic;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace LbIntegrations.Dsi
{
    internal static class DsiSaveFile
    {
        /// <summary>What a DSiWare save is called, here and in the vault alike. The host derives the
        /// vault name from the game and this extension (SaveVault.Extension reads it off the active
        /// path), so this string is what the player ends up seeing.</summary>
        public const string Extension = ".dsisave";

        /// <summary>The one timestamp every entry gets. 1980-01-01 is the earliest a zip can express
        /// - a DOS date has no room for anything before it - which makes it the obvious constant and
        /// an obviously fake one, so nobody reads meaning into it.</summary>
        private static readonly DateTime Stamp = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // ── writing ──────────────────────────────────────────────────────────

        /// <summary>Pack a flat folder into one save file. Written beside the target and moved into
        /// place, so an interrupted pack never leaves something that looks like a save.
        ///
        /// Top level only, and that is not a shortcut: a state folder is flat by construction -
        /// DsiDelta.FlatName turns 0:/shared1/TWLCFG0.dat into 0__shared1_TWLCFG0.dat precisely
        /// so that no subdirectory can ever exist.</summary>
        public static bool Pack(string folder, string target, out string error)
        {
            error = null;
            string partial = null;
            try
            {
                if (folder == null || !Directory.Exists(folder))
                { error = "there is nothing to pack"; return false; }
                if (target == null) { error = "no target to pack into"; return false; }

                var names = new List<string>();
                foreach (var file in Directory.GetFiles(folder)) names.Add(Path.GetFileName(file));
                names.Sort(StringComparer.Ordinal);       // THE order, not the filesystem's

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                partial = target + ".part";
                try { if (File.Exists(partial)) File.Delete(partial); } catch { }

                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new ZipWriter(output, new ZipWriterOptions(CompressionType.None)))
                {
                    foreach (var name in names)
                    {
                        using var source = File.OpenRead(Path.Combine(folder, name));
                        writer.Write(name, source, new ZipWriterEntryOptions
                        {
                            CompressionType = CompressionType.None,
                            ModificationDateTime = Stamp,      // never the source's own
                            EnableZip64 = false,               // no extra field to vary
                        });
                    }
                }

                if (File.Exists(target)) File.Delete(target);
                File.Move(partial, target);
                partial = null;
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
            finally { if (partial != null) try { File.Delete(partial); } catch { } }
        }

        // ── reading ──────────────────────────────────────────────────────────

        /// <summary>Lay a save file out as a folder, for the code that works on folders - the delta
        /// engine, and the native NAND shim, which only takes paths.</summary>
        public static bool Unpack(string savePath, string folder, out string error)
        {
            error = null;
            try
            {
                if (savePath == null || !File.Exists(savePath))
                { error = "there is no save file to open"; return false; }

                Directory.CreateDirectory(folder);
                using var archive = ZipArchive.Open(savePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var name = NameOf(entry.Key);
                    if (name == null) continue;

                    using var source = entry.OpenEntryStream();
                    using var destination = new FileStream(Path.Combine(folder, name),
                                                           FileMode.Create, FileAccess.Write, FileShare.None);
                    source.CopyTo(destination);
                }
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>Is this a save file? It is, when it carries an index - the same question the
        /// folder form answered with File.Exists(dir\files.txt).</summary>
        public static bool Holds(string savePath) => Bytes(savePath, DsiDelta.IndexName) != null;

        /// <summary>One entry, in memory. A zip is random-access through its central directory, so
        /// this reads that entry and nothing else - which is what makes reading base.txt on every
        /// launch cost 400 bytes out of a 70 KB file rather than an unpack.</summary>
        public static byte[] Bytes(string savePath, string entry)
        {
            try
            {
                if (savePath == null || !File.Exists(savePath) || entry == null) return null;
                using var archive = ZipArchive.Open(savePath);
                return Read(archive, entry);
            }
            catch (Exception ex)
            {
                DsiLog.Verbose("could not read " + entry + " out of " + savePath + " - " + ex.Message);
                return null;
            }
        }

        /// <summary>One entry of an archive that is ITSELF already in memory - a save's base.zip,
        /// read straight out of the save without either of them reaching the disk. A MemoryStream
        /// seeks, which is all a zip needs to be opened by its central directory.</summary>
        public static byte[] BytesIn(byte[] archiveBytes, string entry)
        {
            try
            {
                if (archiveBytes == null || archiveBytes.Length == 0 || entry == null) return null;
                using var stream = new MemoryStream(archiveBytes, writable: false);
                using var archive = ZipArchive.Open(stream);
                return Read(archive, entry);
            }
            catch (Exception ex)
            {
                DsiLog.Verbose("could not read " + entry + " out of an archive in memory - " + ex.Message);
                return null;
            }
        }

        private static byte[] Read(ZipArchive archive, string entry)
        {
            foreach (var item in archive.Entries)
            {
                if (item.IsDirectory) continue;
                if (!string.Equals(NameOf(item.Key), entry, StringComparison.OrdinalIgnoreCase)) continue;

                using var source = item.OpenEntryStream();
                using var buffer = new MemoryStream();
                source.CopyTo(buffer);
                return buffer.ToArray();
            }
            return null;
        }

        /// <summary>One entry, to a file. Used where the native NAND shim is going to be handed a
        /// path, which is the only reason anything here ever touches the disk.</summary>
        public static bool Extract(string savePath, string entry, string target, out string error)
        {
            error = null;
            var bytes = Bytes(savePath, entry);
            if (bytes == null) { error = entry + " is not in this save"; return false; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, bytes);
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>Every entry, by name in ordinal order, with its bytes. For the receipt, which
        /// fingerprints the CONTENT and never the container - so that the receipt keeps working
        /// whatever a zip writer decides to do with its headers.</summary>
        public static List<KeyValuePair<string, byte[]>> Entries(string savePath)
        {
            var found = new List<KeyValuePair<string, byte[]>>();
            try
            {
                if (savePath == null || !File.Exists(savePath)) return found;

                using (var archive = ZipArchive.Open(savePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory) continue;
                        var name = NameOf(entry.Key);
                        if (name == null) continue;

                        using var source = entry.OpenEntryStream();
                        using var buffer = new MemoryStream();
                        source.CopyTo(buffer);
                        found.Add(new KeyValuePair<string, byte[]>(name, buffer.ToArray()));
                    }
                }
            }
            catch (Exception ex) { DsiLog.Verbose("could not list " + savePath + " - " + ex.Message); }

            found.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
            return found;
        }

        /// <summary>An entry's name, with any folder an archive from elsewhere might carry dropped.
        /// Ours are always flat; this is what keeps a hand-made zip from writing outside the
        /// destination.</summary>
        private static string NameOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var name = Path.GetFileName(key.Replace('/', Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }
}
