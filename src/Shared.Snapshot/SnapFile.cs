// A state folder packed into one file, and the same bytes every time.
//
// IT IS A ZIP, and whatever extension the caller gives it is a label rather than a format - rename it
// and any tool opens it.
//
// DETERMINISM IS THE WHOLE POINT, and it is not tidiness. On the file path the host fingerprints a
// save by hashing its BYTES. A zip rewritten differently from identical content would make the
// freshness dot flicker at every launch and make a sync see a change that never happened. So:
//
//   sorted    ordinal, not the filesystem's order
//   dated     every entry stamped 1980-01-01, the source's own timestamps never read
//   stored    no compression, on the writer and on each entry
//   no zip64  one less extra field to vary
//
// Stored rather than a pinned compression level, for a reason worth keeping: this targets net9.0 and
// .NET 9 moved to zlib-ng, so the same bytes deflated by two runtimes are not the same bytes. Storing
// removes the variable instead of pinning it. At these sizes it costs nothing.
//
// SharpCompress rather than System.IO.Compression, deliberately: it is pinned and ILRepack folds it
// into the plugin, so its header layout ships with us and cannot drift when the host's runtime is
// upgraded. The writer we do not ship is the writer we cannot promise anything about.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace LbIntegrations.Snapshot
{
    internal static class SnapFile
    {
        /// <summary>The one timestamp every entry gets. 1980-01-01 is the earliest a zip can express -
        /// a DOS date has no room for anything before it - which makes it the obvious constant and an
        /// obviously fake one, so nobody reads meaning into it.</summary>
        private static readonly DateTime Stamp = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Pack a flat folder into one file. Written beside the target and moved into place,
        /// so an interrupted pack never leaves something that looks like a save.
        ///
        /// Top level only, and that is not a shortcut: a state folder is flat by construction -
        /// SnapDelta.FlatName exists precisely so that no subdirectory can ever appear in one.</summary>
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

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
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
                            EnableZip64 = false,
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

        /// <summary>Lay a packed save out as a folder, for the code that works on folders.</summary>
        public static bool Unpack(string savePath, string folder, out string error)
        {
            error = null;
            try
            {
                if (savePath == null || !File.Exists(savePath))
                { error = "there is no save at " + savePath; return false; }

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

        /// <summary>Does this file look like one of ours? The packed equivalent of asking whether the
        /// folder had an index in it.</summary>
        public static bool Holds(string savePath)
        {
            try
            {
                if (savePath == null || !File.Exists(savePath)) return false;
                using var archive = ZipArchive.Open(savePath);
                foreach (var entry in archive.Entries)
                    if (string.Equals(NameOf(entry.Key), SnapDelta.IndexName, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>Every entry, ordinal by name, as CONTENT rather than container - so a receipt
        /// taken from this keeps working whatever a zip writer decides to do with its headers.</summary>
        public static List<KeyValuePair<string, byte[]>> Entries(string savePath)
        {
            var found = new List<KeyValuePair<string, byte[]>>();
            try
            {
                if (savePath == null || !File.Exists(savePath)) return found;
                using var archive = ZipArchive.Open(savePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var name = NameOf(entry.Key);
                    if (name == null) continue;
                    using var source = entry.OpenEntryStream();
                    using var memory = new MemoryStream();
                    source.CopyTo(memory);
                    found.Add(new KeyValuePair<string, byte[]>(name, memory.ToArray()));
                }
                found.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            }
            catch (Exception ex) { SnapLog.Warn("could not read " + savePath, ex); }
            return found;
        }

        /// <summary>Reduce any entry key to a bare file name. This only has to be unique and legible -
        /// and it is what keeps a hand-made zip from writing outside the destination.</summary>
        private static string NameOf(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return null;
                var name = Path.GetFileName(key.Replace('/', Path.DirectorySeparatorChar));
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch { return null; }
        }
    }
}
