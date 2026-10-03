// SPDX-License-Identifier: MPL-2.0
//
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of
// the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// The Xenia plugin's src\Xenia\Xdvdfs.cs (itself derived from sigil's src/xdvdfs.c, MPL-2.0): the same volume
// descriptor, the same two-magic test, the same partition bases. What is added here is the WHOLE TREE - Xenia only
// ever needs default.xex at the root, Cxbx-Reloaded needs every file of the disc on the host's disk, because its
// loader opens an XBE and nothing else. (Its GUI can mount a loose ISO since 19/04/2026 - commit 535c8c9 - through
// the Dokany driver, installed separately: the same reading as here, exposed as a drive. Not used: an extra driver,
// the GUI around the game, and nothing gained for an image in a zip. Mehdi, 03/10.)
//
// ---
//
//   volume descriptor, at <partition base> + 0x10000:
//     +0x000  char[20]  "MICROSOFT*XBOX*MEDIA"
//     +0x014  u32 LE    root directory sector, counted from the partition base
//     +0x018  u32 LE    root directory size, in bytes
//     +0x7EC  char[20]  the same magic again
//
//   directory entry, at <table> + ordinal * 4:
//     +0x00  u16 LE  left child ordinal   (0 = none, 0xFFFF = padding)
//     +0x02  u16 LE  right child ordinal
//     +0x04  u32 LE  first sector
//     +0x08  u32 LE  length in bytes
//     +0x0C  u8      attributes           (0x10 = directory)
//     +0x0D  u8      name length
//     +0x0E  char[]  name
//
// A subdirectory's entry points at ITS table (sector, size) - the same layout again. An entry never crosses a sector.
//
// TWO STEPS, LIST THEN EXTRACT (Mehdi, 03/10). A disc image in a zip or a 7z cannot be seeked: SharpCompress hands it
// over as a stream, front to back. Listing and unpacking in one walk made the stream be read again every time a table
// turned out to lie behind the files it names - three times over for GTA San Andreas. So:
//
//   LIST    the descriptors, then the tables, in ascending order, nothing written. The stream is read only as far as
//           the last table. Every sector skipped on the way that LOOKS like a table (its first entry parses: sane
//           children, sane attributes, a printable name, a sector inside the image) is kept in memory, 64 MB at most -
//           so a table found to lie behind the read position is usually taken from there instead of a new pass. What
//           is kept is the exact bytes at their exact offset: a block kept for nothing costs memory, never correctness.
//   EXTRACT every file, in ascending offset, in one pass - the volume descriptor checked first, so a listing kept from
//           an earlier launch is never applied to a disc it does not describe. Two entries sharing their data are
//           copied from the first one written.
//
// The listing gives the exact size of the game before anything is mounted, and is kept by the caller (CxbxListing):
// the next launches extract at once.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal sealed class XdvdfsFile
    {
        public string Path;        // relative, '\' separated
        public long Offset;        // absolute in the image
        public long Length;
    }

    internal sealed class XdvdfsResult
    {
        public long PartitionBase = -1;
        public List<XdvdfsFile> Files = new List<XdvdfsFile>();
        public List<string> Dirs = new List<string>();
        public long Bytes => Files.Sum(f => f.Length);
        public int Passes;              // over the image, listing and extracting
        public int TablesFromMemory;    // tables found behind the read position and taken from the kept blocks
        /// <summary>default.xbe's first bytes, when the listing read could take them on its way (it lay ahead) - null else.</summary>
        public byte[] XbeHead;
        public string Error;
        public bool Found => PartitionBase >= 0;
        public XdvdfsFile Root(string name)
            => Files.FirstOrDefault(f => string.Equals(f.Path, name, StringComparison.OrdinalIgnoreCase));
    }

    internal static class Xdvdfs
    {
        private const int SectorSize = 2048;
        private const long VolumeDescriptorOffset = 0x10000;
        private const int MagicTrailingOffset = 0x7EC;
        private const int MagicLength = 20;
        private const string Magic = "MICROSOFT*XBOX*MEDIA";
        private const byte AttributeDirectory = 0x10;
        private const int MaxPasses = 6;
        private const int MaxTable = 32 * 1024 * 1024;
        private const long MaxKept = 64L * 1024 * 1024;

        /// <summary>The Xenia plugin's list, ascending: a trimmed image (XISO) first, then the redump layouts.</summary>
        private static readonly long[] PartitionBases =
        {
            0,            // already trimmed
            0x0000FB20,
            0x00020600,
            0x02080000,   // XGD3
            0x0FD90000,   // XGD2
            0x18300000,   // XGD1 - the original Xbox's redump images
        };

        private enum Kind { Probe, Table, Head }

        private sealed class Region
        {
            public Kind Kind;
            public long Offset, Length, Base;
            public string Path;
        }

        /// <summary>The tree of a loose image - plain, CSO, CCI or CHD (DiscImages) - nothing written.</summary>
        public static XdvdfsResult List(string image)
        {
            try
            {
                using var disc = LbIntegrations.Disc.DiscImages.Open(image);
                return List(() => LbIntegrations.Disc.DiscImages.Shared(disc), true, disc.Length, null);
            }
            catch (Exception ex) { return new XdvdfsResult { Error = ex.Message }; }
        }

        /// <summary>The tree, nothing written. <paramref name="open"/> opens the image from its start - once per pass
        /// for a stream that cannot seek. <paramref name="length"/>: its size, 0 when not known.
        /// <paramref name="progress"/>: bytes of the image read so far, and which pass.</summary>
        public static XdvdfsResult List(Func<Stream> open, bool seekable, long length, Action<long, int> progress)
        {
            var result = new XdvdfsResult();
            try { Walk(result, open, seekable, length, progress); }
            catch (Exception ex) { result.Error = ex.GetType().Name + ": " + ex.Message; }
            return result;
        }

        /// <summary>List, then extract - in one call.</summary>
        public static XdvdfsResult Extract(Func<Stream> open, bool seekable, long length, string destination, Action<long, int> progress)
        {
            var listing = List(open, seekable, length, progress);
            if (listing.Error != null || !listing.Found) return listing;
            listing.Error = ExtractListed(listing, open, seekable, destination, progress);
            return listing;
        }

        /// <summary>Every file of <paramref name="listing"/> written under <paramref name="destination"/>, in one pass
        /// when nothing shares its data. Null when done, else what went wrong.</summary>
        public static string ExtractListed(XdvdfsResult listing, Func<Stream> open, bool seekable, string destination, Action<long, int> progress)
        {
            try
            {
                var root = Path.GetFullPath(destination);
                Directory.CreateDirectory(root);
                foreach (var d in listing.Dirs) Directory.CreateDirectory(Path.Combine(root, d));
                var pending = listing.Files.Where(f => f.Length > 0).OrderBy(f => f.Offset).ToList();
                foreach (var f in listing.Files.Where(f => f.Length == 0))
                {
                    var p = Path.Combine(root, f.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllBytes(p, Array.Empty<byte>());
                }
                var written = new List<XdvdfsFile>();
                var buffer = new byte[1 << 20];
                bool checkedDescriptor = false;

                while (pending.Count > 0)
                {
                    if (++listing.Passes > MaxPasses + 3) return pending.Count + " file(s) still unread";
                    var later = new List<XdvdfsFile>();
                    using var s = open();
                    long pos = 0;

                    // THE DISC IS THE ONE LISTED: its descriptor, where the listing says, before any file.
                    if (!checkedDescriptor)
                    {
                        long at = listing.PartitionBase + VolumeDescriptorOffset;
                        pos += Skip(s, at, seekable, buffer, null);
                        var desc = ReadExact(s, SectorSize);
                        pos += desc.Length;
                        if (desc.Length < SectorSize || !MagicAt(desc, 0) || !MagicAt(desc, MagicTrailingOffset))
                            return "the disc is not the one listed (no volume descriptor at 0x" + at.ToString("X") + ")";
                        checkedDescriptor = true;
                    }

                    foreach (var f in pending)
                    {
                        var target = Path.Combine(root, f.Path);
                        if (f.Offset < pos)
                        {
                            // Its data was written already, as another entry's: copied from there.
                            var host = written.FirstOrDefault(w => w.Offset <= f.Offset && f.Offset + f.Length <= w.Offset + w.Length);
                            if (host != null) { CopyRange(Path.Combine(root, host.Path), f.Offset - host.Offset, f.Length, target, buffer); written.Add(f); continue; }
                            if (seekable) { s.Seek(f.Offset, SeekOrigin.Begin); pos = f.Offset; }
                            else { later.Add(f); continue; }
                        }
                        else if (f.Offset > pos)
                        {
                            pos += Skip(s, f.Offset - pos, seekable, buffer, null);
                            if (pos < f.Offset) return "the image ends before " + f.Path;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        long left = f.Length;
                        using (var o = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                            while (left > 0)
                            {
                                var n = s.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                                if (n <= 0) return "the image ended inside " + f.Path;
                                o.Write(buffer, 0, n);
                                left -= n; pos += n;
                                progress?.Invoke(pos, listing.Passes);
                            }
                        written.Add(f);
                    }
                    pending = later;
                }
                return null;
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        private static void CopyRange(string from, long offset, long length, string to, byte[] buffer)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            using var i = File.OpenRead(from);
            using var o = File.Create(to);
            i.Seek(offset, SeekOrigin.Begin);
            while (length > 0) { var n = i.Read(buffer, 0, (int)Math.Min(buffer.Length, length)); if (n <= 0) break; o.Write(buffer, 0, n); length -= n; }
        }

        /// <summary>A file's first bytes from an image that may be a stream - an XBE's header, after a listing. The stream is
        /// read only as far as the file's start plus <paramref name="max"/>.</summary>
        public static byte[] ReadHead(Func<Stream> open, bool seekable, XdvdfsFile file, int max)
        {
            using var s = open();
            var buffer = new byte[1 << 20];
            if (Skip(s, file.Offset, seekable, buffer, null) < file.Offset) return null;
            return ReadExact(s, (int)Math.Min(max, file.Length));
        }

        /// <summary>A file's first bytes from a loose image (plain, CSO, CCI or CHD) - an XBE's header.</summary>
        public static byte[] ReadHead(string image, XdvdfsFile file, int max)
        {
            using var fs = LbIntegrations.Disc.DiscImages.Open(image);
            fs.Seek(file.Offset, SeekOrigin.Begin);
            var n = (int)Math.Min(max, file.Length);
            var buffer = new byte[n];
            int got = 0, r;
            while (got < n && (r = fs.Read(buffer, got, n - got)) > 0) got += r;
            return got == n ? buffer : buffer.Take(got).ToArray();
        }

        // ── the listing walk ─────────────────────────────────────────────────

        private static void Walk(XdvdfsResult result, Func<Stream> open, bool seekable, long length, Action<long, int> progress)
        {
            var pending = PartitionBases.Select(b => new Region { Kind = Kind.Probe, Offset = b + VolumeDescriptorOffset, Length = SectorSize, Base = b }).ToList();
            var seenTables = new HashSet<long>();
            var kept = new Dictionary<long, byte[]>();          // sector offset -> its bytes: blocks that look like tables
            long keptBytes = 0;
            // The first 64 KB of every executable passed on the way - an XBE starts on a sector with "XBEH" - so that
            // default.xbe's header is there even when the root table naming it comes after it (Batman, measured 03/10).
            var heads = new Dictionary<long, System.IO.MemoryStream>();
            long headAt = -1, headNext = -1;
            var buffer = new byte[1 << 20];

            while (pending.Count > 0)
            {
                if (result.Passes >= MaxPasses) { result.Error = pending.Count + " table(s) still unread after " + MaxPasses + " passes"; return; }
                result.Passes++;
                var queue = new PriorityQueue<Region, long>();
                foreach (var p in pending) queue.Enqueue(p, p.Offset);
                pending.Clear();

                using var s = open();
                long pos = 0;
                bool ended = false;
                int deferred = 0, handled = 0;
                while (queue.TryDequeue(out var r, out _))
                {
                    if (r.Kind == Kind.Probe && result.Found) continue;
                    // The XBE's header only on the way: never a pass of its own (the caller reads it after, when it must).
                    if (r.Kind == Kind.Head && r.Offset < pos && !seekable && heads.TryGetValue(r.Offset, out var head) && head.Length >= r.Length)
                    { result.XbeHead = head.ToArray().Take((int)r.Length).ToArray(); continue; }
                    if (r.Kind == Kind.Head && (ended || (r.Offset < pos && !seekable))) continue;
                    if (ended) { if (r.Kind != Kind.Probe) { pending.Add(r); deferred++; } continue; }
                    if (length > 0 && r.Offset + r.Length > length)
                    {
                        if (r.Kind == Kind.Probe) continue;
                        result.Error = "the image is shorter than its tables say (" + (r.Path.Length == 0 ? "the root" : r.Path) + ")";
                        return;
                    }

                    byte[] data = null;
                    if (r.Offset < pos)
                    {
                        if (seekable) { s.Seek(r.Offset, SeekOrigin.Begin); pos = r.Offset; }
                        else if ((data = FromKept(kept, r.Offset, r.Length)) != null) result.TablesFromMemory++;
                        else { pending.Add(r); deferred++; continue; }
                    }
                    else if (r.Offset > pos)
                    {
                        Action<long, byte[], int> look = !seekable && result.Found ? (at, b, i) =>
                        {
                            if (keptBytes < MaxKept && LooksLikeTable(b, i, result.PartitionBase, length))
                            { var copy = new byte[SectorSize]; Array.Copy(b, i, copy, 0, SectorSize); kept[at] = copy; keptBytes += SectorSize; }
                            // An executable's start: its next 64 KB kept with it, sector by sector.
                            if (headAt >= 0 && at == headNext && heads[headAt].Length < 64 * 1024) { heads[headAt].Write(b, i, SectorSize); headNext += SectorSize; }
                            else if (b[i] == 'X' && b[i + 1] == 'B' && b[i + 2] == 'E' && b[i + 3] == 'H' && heads.Count < 64)
                            { headAt = at; headNext = at + SectorSize; heads[at] = new System.IO.MemoryStream(); heads[at].Write(b, i, SectorSize); }
                        } : (Action<long, byte[], int>)null;
                        pos += Skip(s, r.Offset - pos, seekable, buffer, look, pos, result.Found ? result.PartitionBase : 0);
                        progress?.Invoke(pos, result.Passes);
                        if (pos < r.Offset) { ended = true; if (r.Kind != Kind.Probe) { pending.Add(r); deferred++; } continue; }
                    }

                    if (data == null)
                    {
                        data = ReadExact(s, (int)r.Length);
                        pos += data.Length;
                        if (data.Length < r.Length) { ended = true; if (r.Kind != Kind.Probe) { pending.Add(r); deferred++; } continue; }
                    }
                    handled++;

                    if (r.Kind == Kind.Head) { result.XbeHead = data; continue; }

                    if (r.Kind == Kind.Probe)
                    {
                        if (!MagicAt(data, 0) || !MagicAt(data, MagicTrailingOffset)) continue;
                        uint sector = LeUInt32(data, 0x14), size = LeUInt32(data, 0x18);
                        if (size < 0x0E || size > MaxTable) continue;
                        result.PartitionBase = r.Base;
                        Log.Info("XDVDFS partition at 0x" + r.Base.ToString("X") + ", root " + size + " bytes");
                        Add(queue, new Region { Kind = Kind.Table, Offset = r.Base + (long)sector * SectorSize, Length = size, Base = r.Base, Path = "" }, seenTables);
                        continue;
                    }

                    foreach (var e in Entries(data))
                    {
                        var name = SafeName(e.Name);
                        if (name == null) { Log.Info("XDVDFS: entry \"" + e.Name + "\" skipped - not a usable file name"); continue; }
                        var rel = r.Path.Length == 0 ? name : r.Path + "\\" + name;
                        long at = r.Base + (long)e.Sector * SectorSize;
                        if (e.IsDirectory)
                        {
                            result.Dirs.Add(rel);
                            if (e.Length >= 0x0E && e.Length <= MaxTable)
                                Add(queue, new Region { Kind = Kind.Table, Offset = at, Length = e.Length, Base = r.Base, Path = rel }, seenTables);
                        }
                        else
                        {
                            if (length > 0 && at + e.Length > length) { result.Error = "the image is shorter than its tables say (" + rel + ")"; return; }
                            result.Files.Add(new XdvdfsFile { Path = rel, Offset = at, Length = e.Length });
                            // The XBE's header, on the way when it lies ahead - its certificate (title, region, version).
                            if (r.Path.Length == 0 && rel.Equals("default.xbe", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
                                queue.Enqueue(new Region { Kind = Kind.Head, Offset = at, Length = Math.Min(e.Length, 64 * 1024), Base = r.Base, Path = rel }, at);
                        }
                    }
                }
                if (!result.Found) return;          // not an Xbox disc: no pass will change that
                if (pending.Count > 0 && handled == 0)
                { result.Error = "the image ends before " + pending.Count + " table(s) it names"; return; }
            }
        }

        private static void Add(PriorityQueue<Region, long> queue, Region table, HashSet<long> seen)
        {
            if (seen.Add(table.Offset)) queue.Enqueue(table, table.Offset);     // a corrupt tree can point back at itself
        }

        /// <summary>A table behind the read position, from the blocks kept on the way - all of its sectors, or nothing.</summary>
        private static byte[] FromKept(Dictionary<long, byte[]> kept, long offset, long length)
        {
            var data = new byte[length];
            for (long done = 0; done < length; done += SectorSize)
            {
                if (!kept.TryGetValue(offset + done, out var sector)) return null;
                Array.Copy(sector, 0, data, done, Math.Min(SectorSize, length - done));
            }
            return data;
        }

        /// <summary>Does this sector start with a directory entry? Strict enough to keep few blocks, and harmless when
        /// wrong: a kept block is only ever used at the very offset it was read from.</summary>
        private static bool LooksLikeTable(byte[] b, int o, long partitionBase, long length)
        {
            ushort left = (ushort)(b[o] | (b[o + 1] << 8)), right = (ushort)(b[o + 2] | (b[o + 3] << 8));
            if (left == 0xFFFF && right == 0xFFFF) return false;
            if ((left != 0xFFFF && left > 0x4000) || (right != 0xFFFF && right > 0x4000)) return false;
            byte attributes = b[o + 0x0C];
            if ((attributes & ~0xB7) != 0) return false;
            int n = b[o + 0x0D];
            if (n == 0 || 0x0E + n > SectorSize) return false;
            for (int i = 0; i < n; i++) { var c = b[o + 0x0E + i]; if (c < 0x20 || c > 0x7E) return false; }
            long data = partitionBase + (long)LeUInt32(b, o + 4) * SectorSize;
            return length <= 0 || data + LeUInt32(b, o + 8) <= length;
        }

        private struct Entry { public string Name; public uint Sector, Length; public bool IsDirectory; }

        /// <summary>Every node of one directory table. The tree is sorted, but by a collation that is not ours, so it is
        /// walked whole rather than descended - one table is a sector or two.</summary>
        private static List<Entry> Entries(byte[] table)
        {
            var found = new List<Entry>();
            var pending = new Stack<uint>();
            var seen = new HashSet<uint>();
            pending.Push(0);
            while (pending.Count > 0)
            {
                uint ordinal = pending.Pop();
                if (!seen.Add(ordinal) || seen.Count > 65536) continue;
                long at = (long)ordinal * 4;
                if (at < 0 || at + 0x0E > table.Length) continue;

                ushort left = (ushort)(table[at] | (table[at + 1] << 8));
                ushort right = (ushort)(table[at + 2] | (table[at + 3] << 8));
                if (left == 0xFFFF && right == 0xFFFF) continue;               // padding
                int nameLength = table[at + 0x0D];
                if (nameLength > 0 && at + 0x0E + nameLength <= table.Length)
                    found.Add(new Entry
                    {
                        Name = Encoding.ASCII.GetString(table, (int)at + 0x0E, nameLength),
                        Sector = LeUInt32(table, at + 4),
                        Length = LeUInt32(table, at + 8),
                        IsDirectory = (table[at + 0x0C] & AttributeDirectory) != 0,
                    });
                if (left != 0 && left != 0xFFFF) pending.Push(left);
                if (right != 0 && right != 0xFFFF) pending.Push(right);
            }
            return found;
        }

        /// <summary>A name that is one plain path segment, or null.</summary>
        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") return null;
            if (name.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            return name;
        }

        /// <summary><paramref name="count"/> bytes passed over. On a stream with <paramref name="look"/>, each whole
        /// sector passed - counted from <paramref name="alignBase"/> - is shown to it with its absolute offset and its
        /// place in the buffer (<paramref name="from"/> is where the skip starts).</summary>
        private static long Skip(Stream s, long count, bool seekable, byte[] buffer, Action<long, byte[], int> look, long from = 0, long alignBase = 0)
        {
            if (seekable) { s.Seek(count, SeekOrigin.Current); return count; }
            long done = 0;
            // To the next sector boundary first, unlooked: that part belongs to whatever was read before.
            long lead = look == null || from < alignBase ? count : Math.Min(count, (SectorSize - (from - alignBase) % SectorSize) % SectorSize);
            while (done < lead)
            {
                var n = s.Read(buffer, 0, (int)Math.Min(buffer.Length, lead - done));
                if (n <= 0) return done;
                done += n;
            }
            while (done < count)
            {
                int want = (int)Math.Min(buffer.Length, count - done), got = 0, n;
                while (got < want && (n = s.Read(buffer, got, want - got)) > 0) got += n;
                for (int at = 0; at + SectorSize <= got; at += SectorSize) look(from + done + at, buffer, at);
                done += got;
                if (got < want) break;
            }
            return done;
        }

        private static byte[] ReadExact(Stream s, int count)
        {
            var b = new byte[count];
            int got = 0, n;
            while (got < count && (n = s.Read(b, got, count - got)) > 0) got += n;
            return got == count ? b : b.Take(got).ToArray();
        }

        private static bool MagicAt(byte[] buffer, int offset)
        {
            if (offset + MagicLength > buffer.Length) return false;
            for (int i = 0; i < MagicLength; i++)
                if (buffer[offset + i] != (byte)Magic[i]) return false;
            return true;
        }

        private static uint LeUInt32(byte[] b, long offset)
            => (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
    }
}
