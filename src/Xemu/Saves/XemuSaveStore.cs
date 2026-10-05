// A game's saves out of its xemu console and back in, IN CXBX'S FORMAT (Mehdi, 04/10): the folder E:\UDATA\<title id> of the
// game's disk (hdd\games\<title id>.qcow2 over hdd\base.qcow2) packed as Cxbx's plugin packs <data>\EmuDisk\Partition1\UDATA\
// <title id> - a zip of the folder's files, sorted ordinal, every entry stamped 1980-01-01, stored, no zip64 (CxbxSaves.Pack)
// - so the same saves make the same bytes in either emulator, and one emulator's backup restores into the other.
//
// EXTRACT reads the disk through its chain (Qcow2Image) and E: (FatxVolume); nothing written.
// INSERT lays a packed save into the game's console - the folder's content REPLACED, not merged, as Cxbx's restore does: the
// title's folder removed (its clusters freed), made again, every file written. The changes are kept apart (PatchedDisk) and a
// NEW console made of them and of what the old one held (Qcow2Image.Build), through a temporary file: the old console is
// whole until the new one replaces it. A game with no console yet gets one, over the pristine base, holding its save.
//
// NOT HANDLED (Mehdi, 04/10: "on verra plus tard, ça concerne peu de jeux"): saves a game signs with the console's key -
// moved to another console (Cxbx's, or another xemu's EEPROM) such a game may call them damaged. TDATA (E:\TDATA\<title id>,
// the game's own cache and settings) is not part of the save, as for Cxbx.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace LbIntegrations.Xemu.Saves
{
    internal static class XemuSaveStore
    {
        public const string Extension = ".cxbxsave";
        private static readonly DateTime Stamp = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The files of E:\UDATA\<paramref name="titleId"/> on <paramref name="console"/>, by their path in that folder
        /// ('/' separated) - empty when the game has no save there.</summary>
        public static List<(string Name, byte[] Data)> Extract(string console, string titleId)
        {
            var files = new List<(string, byte[])>();
            using var disk = Qcow2Image.Open(console);
            var e = FatxVolume.Open(disk) ?? throw new InvalidDataException("no FATX partition E: on " + console);
            var title = e.Find("UDATA\\" + titleId);
            if (title == null || !title.IsDirectory) return files;
            var walked = new List<(string Path, FatxEntry Entry)>();
            e.Walk(title.FirstCluster, "", walked);
            foreach (var (path, entry) in walked)
                if (!path.EndsWith("/")) files.Add((path, e.ReadFile(entry)));
            return files;
        }

        /// <summary>The saves packed as Cxbx's plugin packs them, to <paramref name="target"/> through a .part file. False when
        /// there is no save: no file is made, and one there is left alone.</summary>
        public static bool Capture(string console, string titleId, string target)
        {
            var files = Extract(console, titleId);
            if (files.Count == 0) return false;
            Pack(files, target);
            return true;
        }

        /// <summary>CxbxSaves.Pack's zip, from files in memory: sorted ordinal, 1980-01-01, stored, no zip64.</summary>
        public static void Pack(IEnumerable<(string Name, byte[] Data)> files, string target)
        {
            var partial = target + ".part";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new ZipWriter(output, new ZipWriterOptions(CompressionType.None)))
                    foreach (var (name, data) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
                    {
                        using var source = new MemoryStream(data, writable: false);
                        writer.Write(name, source, new ZipWriterEntryOptions { CompressionType = CompressionType.None, ModificationDateTime = Stamp, EnableZip64 = false });
                    }
                File.Move(partial, target, overwrite: true);
            }
            finally { try { if (File.Exists(partial)) File.Delete(partial); } catch { } }
        }

        /// <summary>A packed save's files - refused when an entry would leave the title's folder, or a name does not fit FATX.</summary>
        public static List<(string Name, byte[] Data)> Unpack(string pack)
        {
            var files = new List<(string, byte[])>();
            using var archive = ZipArchive.Open(pack);
            foreach (var en in archive.Entries)
            {
                if (en.IsDirectory || string.IsNullOrEmpty(en.Key) || LbIntegrations.Xbox.XboxSaveKeys.IsKeysEntry(en.Key)) continue;
                var name = en.Key.Replace('\\', '/').TrimStart('/');
                var parts = name.Split('/');
                if (parts.Any(p => p.Length == 0 || p == "." || p == "..")) throw new InvalidDataException("an entry escapes the save's folder: " + en.Key);
                if (parts.Any(p => p.Length > 42 || p.Any(c => c < 0x20 || c > 0x7E))) throw new InvalidDataException("a name the Xbox cannot hold: " + en.Key);
                using var s = en.OpenEntryStream();
                using var m = new MemoryStream();
                s.CopyTo(m);
                files.Add((name, m.ToArray()));
            }
            return files;
        }

        /// <summary>E:\UDATA\<paramref name="titleId"/> removed from the console - the rest of it (the game's cache, its TDATA,
        /// another title's save) kept. False when there was nothing to remove.</summary>
        public static bool Remove(string console, string titleId)
        {
            if (!File.Exists(console)) return false;
            string tmp;
            using (var disk = Qcow2Image.Open(console))
            {
                var q = disk as Qcow2Image ?? throw new InvalidDataException(console + " is not a qcow2");
                if (q.BackingPath == null) throw new InvalidDataException(console + " has no base under it");
                var patched = new PatchedDisk(disk, q.ClusterBits);
                var e = FatxVolume.Open(patched) ?? throw new InvalidDataException("no FATX partition E: on " + console);
                var udata = e.Find("UDATA");
                var old = udata == null || !udata.IsDirectory ? null
                        : e.List(udata.FirstCluster).FirstOrDefault(x => string.Equals(x.Name, titleId, StringComparison.OrdinalIgnoreCase));
                if (old == null) return false;
                e.DeleteTree(old);
                e.Flush();
                var problems = FatxVolume.Open(patched).Check();
                if (problems.Count > 0) throw new InvalidDataException("the console would be damaged - nothing written: " + string.Join("; ", problems.Take(5)));
                // Written again WITH its savestates (Qcow2Rebuild), checked before it replaces anything.
                tmp = Qcow2Rebuild.WithChanges(console, q, patched.Changed);
            }
            Qcow2Rebuild.Commit(tmp, console);
            return true;
        }

        /// <summary>The packed save <paramref name="pack"/> laid into the game's console <paramref name="console"/> (made over
        /// <paramref name="baseDisk"/> when it is not there) - E:\UDATA\<paramref name="titleId"/> replaced by the save's files.
        /// Never while xemu runs: the caller checks.</summary>
        public static void Insert(string console, string baseDisk, string titleId, string pack)
        {
            var files = Unpack(pack);
            bool exists = File.Exists(console);
            string tmp;
            long size; int clusterBits;
            string backing;
            using (var disk = Qcow2Image.Open(exists ? console : baseDisk))
            {
                var q = disk as Qcow2Image ?? throw new InvalidDataException((exists ? console : baseDisk) + " is not a qcow2");
                size = q.Length; clusterBits = q.ClusterBits;
                backing = exists ? (q.BackingPath ?? throw new InvalidDataException(console + " has no base under it")) : baseDisk;
                var patched = new PatchedDisk(disk, clusterBits);
                var e = FatxVolume.Open(patched) ?? throw new InvalidDataException("no FATX partition E: on " + (exists ? console : baseDisk));

                var udata = e.Find("UDATA") ?? e.CreateDirectory(e.RootCluster, "UDATA");
                if (!udata.IsDirectory) throw new InvalidDataException("E:\\UDATA is a file");
                var old = e.List(udata.FirstCluster).FirstOrDefault(x => string.Equals(x.Name, titleId, StringComparison.OrdinalIgnoreCase));
                if (old != null) e.DeleteTree(old);
                // The folder's name as the game wrote it, else the title id as the Xbox writes one: lower case hex.
                var title = e.CreateDirectory(udata.FirstCluster, old?.Name ?? titleId.ToLowerInvariant());
                var dirs = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { [""] = title.FirstCluster };
                uint DirOf(string path)
                {
                    if (dirs.TryGetValue(path, out var c)) return c;
                    int cut = path.LastIndexOf('/');
                    var parent = DirOf(cut < 0 ? "" : path.Substring(0, cut));
                    var made = e.CreateDirectory(parent, cut < 0 ? path : path.Substring(cut + 1));
                    return dirs[path] = made.FirstCluster;
                }
                foreach (var (name, data) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    int cut = name.LastIndexOf('/');
                    e.CreateFile(DirOf(cut < 0 ? "" : name.Substring(0, cut)), cut < 0 ? name : name.Substring(cut + 1), data);
                }
                e.Flush();
                // Never a broken console: the volume checked as it now is, before anything is written.
                var problems = FatxVolume.Open(patched).Check();
                if (problems.Count > 0) throw new InvalidDataException("the console would be damaged - nothing written: " + string.Join("; ", problems.Take(5)));

                // The new console: what the old one held - its savestates too (Qcow2Rebuild) - then what changed; checked before
                // it replaces anything. A console not made yet: what changed, over the base.
                if (exists) tmp = Qcow2Rebuild.WithChanges(console, q, patched.Changed);
                else
                {
                    var active = new RebuildTable();
                    foreach (var kv in patched.Changed) active.Clusters[kv.Key] = ClusterSource.New(kv.Value);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(console)));
                    tmp = Qcow2Rebuild.Write(console, backing, size, clusterBits, active, new List<RebuildTable>());
                }
            }
            Qcow2Rebuild.Commit(tmp, console);
        }
    }
}
