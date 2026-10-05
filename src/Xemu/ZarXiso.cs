// A ZArchive (.zar) as the XISO xemu opens (Mehdi, 05/10: "je vois pas de raison pour laquelle ça marcherait pas de la même
// façon que tu gères les chd"): a CHD holds a disc, its sectors read through it; a ZArchive holds the game's FILES - so the
// disc is BUILT here, an XDVDFS volume around them, and read at any offset, nothing unpacked. What serves a disc's XISO
// (the RAM disk helper's view=xiso, the copy into the cache, the media patch) takes it as it takes a CHD's.
//
// THE VOLUME, 2048-byte sectors, every number little-endian (the layout extract-xiso and xdvdfs write):
//   sectors 0-31     zeros
//   sector 32        the volume descriptor: "MICROSOFT*XBOX*MEDIA", the root table's sector (u32) and size (u32), a FILETIME,
//                    zeros, the magic again at its byte 0x7EC
//   sector 33 on     the directory tables, one after another: in each, a node per file or directory, a BINARY SEARCH
//                    TREE by name without case (upper-cased bytes compared) - u16 left, u16 right (offsets in 4-byte
//                    units from the table's start, 0 for none), u32 sector, u32 size, u8 attributes (0x10 directory, 0x20
//                    file), u8 name length, the name; each node on 4 bytes, never across a sector: the rest padded 0xFF.
//                    An empty directory: one sector of 0xFF.
//   then the files   each on its own sector, in the archive's order (its stream read forward), zeros up to the next
//
// The tables are kept in memory (a few KB, one sector per ~60 names); the files are read through the archive. A file of 4
// GB or more does not fit XDVDFS (u32 sizes): refused. Names must be printable ASCII, as the Xbox's.
// This is the repository's own code - the format is the Xbox's; no line of extract-xiso's or xdvdfs's.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LbIntegrations.Cxbx;
using LbIntegrations.Zar;

namespace LbIntegrations.Xemu
{
    internal sealed class ZarXiso : Stream
    {
        private const int Sector = 2048;
        private const int DescriptorSector = 32;
        private const byte AttrDirectory = 0x10, AttrFile = 0x20;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

        private readonly ZArchive _zar;
        private readonly bool _ownsZar;
        private readonly byte[] _head;                 // sectors 0 to the tables' end
        private readonly long[] _starts;               // each file's offset, ascending
        private readonly ZArchiveEntry[] _files;
        private long _position;

        /// <summary>The volume as Xdvdfs.List would read it: its files' offsets in this image, its directories.</summary>
        public XdvdfsResult Listing { get; }
        public override long Length { get; }

        private sealed class Node
        {
            public string Name;
            public ZArchiveEntry File;                 // null for a directory
            public List<Node> Children = new List<Node>();
            public byte[] Table;                       // a directory's
            public uint TableSector;
            public uint DataSector;
            public bool IsDirectory => File == null;
            public uint Size => IsDirectory ? (uint)Table.Length : (uint)File.Length;
        }

        /// <summary>The XISO of the ZArchive at <paramref name="path"/>. Throws with the reason when it cannot be built.</summary>
        public static ZarXiso Open(string path)
        {
            var z = ZArchive.Open(path) ?? throw new InvalidDataException("the ZArchive does not read");
            try { return new ZarXiso(z, true); }
            catch { z.Dispose(); throw; }
        }

        public ZarXiso(ZArchive zar, bool ownsZar = false)
        {
            _zar = zar;
            _ownsZar = ownsZar;

            // The tree, from the archive's paths.
            var root = new Node { Name = "" };
            var dirs = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase) { { "", root } };
            Node Dir(string p)
            {
                if (dirs.TryGetValue(p, out var d)) return d;
                int cut = p.LastIndexOf('\\');
                var parent = Dir(cut < 0 ? "" : p.Substring(0, cut));
                d = new Node { Name = Check(cut < 0 ? p : p.Substring(cut + 1)) };
                parent.Children.Add(d);
                dirs[p] = d;
                return d;
            }
            foreach (var e in zar.Entries.Where(x => x.IsDirectory && x.Path.Length > 0)) Dir(e.Path.Trim('\\'));
            var files = zar.Entries.Where(x => !x.IsDirectory).ToList();
            foreach (var e in files)
            {
                if (e.Length >= 1L << 32) throw new InvalidDataException(e.Path + " is 4 GB or more - XDVDFS cannot hold it");
                var p = e.Path.Trim('\\');
                int cut = p.LastIndexOf('\\');
                Dir(cut < 0 ? "" : p.Substring(0, cut)).Children.Add(new Node { Name = Check(cut < 0 ? p : p.Substring(cut + 1)), File = e });
            }

            // The tables: their sizes first (a node's place does not depend on the sectors it points to), then their sectors.
            var all = new List<Node>();
            void Walk(Node d) { all.Add(d); foreach (var c in d.Children.Where(c => c.IsDirectory)) Walk(c); }
            Walk(root);
            foreach (var d in all) d.Table = new byte[Layout(d, null)];
            uint sector = DescriptorSector + 1;
            foreach (var d in all) { d.TableSector = sector; sector += (uint)(d.Table.Length / Sector); }
            uint tablesEnd = sector;

            // The files, in the archive's stream order.
            var ordered = all.SelectMany(d => d.Children.Where(c => !c.IsDirectory)).OrderBy(c => c.File.Offset).ToList();
            var starts = new List<long>();
            var entries = new List<ZArchiveEntry>();
            Listing = new XdvdfsResult { PartitionBase = 0 };
            var pathOf = new Dictionary<ZArchiveEntry, string>();
            foreach (var d in all) foreach (var c in d.Children.Where(c => !c.IsDirectory)) pathOf[c.File] = c.File.Path.Trim('\\');
            foreach (var c in ordered)
            {
                c.DataSector = c.File.Length == 0 ? 0 : sector;
                if (c.File.Length > 0)
                {
                    starts.Add((long)sector * Sector);
                    entries.Add(c.File);
                    sector += (uint)((c.File.Length + Sector - 1) / Sector);
                }
                Listing.Files.Add(new XdvdfsFile { Path = pathOf[c.File], Offset = (long)c.DataSector * Sector, Length = c.File.Length });
            }
            foreach (var p in dirs.Keys.Where(k => k.Length > 0)) Listing.Dirs.Add(p);
            _starts = starts.ToArray();
            _files = entries.ToArray();
            Length = Math.Max((long)sector, tablesEnd) * Sector;

            // The head: zeros, the descriptor, the tables.
            _head = new byte[(long)tablesEnd * Sector];
            int at = DescriptorSector * Sector;
            Magic.CopyTo(_head, at);
            BitConverter.GetBytes(root.TableSector).CopyTo(_head, at + 0x14);
            BitConverter.GetBytes((uint)root.Table.Length).CopyTo(_head, at + 0x18);
            BitConverter.GetBytes(new DateTime(2001, 11, 15, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()).CopyTo(_head, at + 0x1C);
            Magic.CopyTo(_head, at + 0x7EC);
            foreach (var d in all) { Layout(d, d.Table); d.Table.CopyTo(_head, (long)d.TableSector * Sector); }
        }

        private static string Check(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 255 || name.Any(c => c < 0x20 || c > 0x7E))
                throw new InvalidDataException("the name \"" + name + "\" cannot go on an Xbox disc (printable ASCII only)");
            return name;
        }

        /// <summary>The upper-cased bytes of a name, as the Xbox compares them.</summary>
        private static int Compare(string a, string b) => string.CompareOrdinal(a.ToUpperInvariant(), b.ToUpperInvariant());

        /// <summary>A directory's table: its byte count (sectors) - and, when <paramref name="into"/> is given, the table written.
        /// The children sorted, the tree balanced (the middle one the root of each part), laid out in preorder.</summary>
        private static int Layout(Node dir, byte[] into)
        {
            if (dir.Children.Count == 0)
            {
                if (into != null) for (int i = 0; i < into.Length; i++) into[i] = 0xFF;
                return Sector;
            }
            var sorted = dir.Children.OrderBy(c => c.Name, Comparer<string>.Create(Compare)).ToList();
            for (int i = 1; i < sorted.Count; i++)
                if (Compare(sorted[i - 1].Name, sorted[i].Name) == 0) throw new InvalidDataException("two names differ only by case: " + sorted[i].Name);

            var offset = new Dictionary<Node, int>();
            var order = new List<(Node n, int lo, int hi)>();
            int cursor = 0;
            // Places first: preorder, each node on 4 bytes, moved to the next sector when it would cross one.
            void Place(int lo, int hi)
            {
                if (lo > hi) return;
                int mid = (lo + hi) / 2;
                var n = sorted[mid];
                int size = 0x0E + n.Name.Length;
                if (cursor / Sector != (cursor + size - 1) / Sector) cursor = (cursor / Sector + 1) * Sector;
                offset[n] = cursor;
                order.Add((n, lo, hi));
                cursor += (size + 3) & ~3;
                Place(lo, mid - 1);
                Place(mid + 1, hi);
            }
            Place(0, sorted.Count - 1);
            int total = (cursor + Sector - 1) / Sector * Sector;
            if (into == null) return total;

            for (int i = 0; i < into.Length; i++) into[i] = 0xFF;
            foreach (var (n, lo, hi) in order)
            {
                int mid = (lo + hi) / 2, o = offset[n];
                ushort left = lo <= mid - 1 ? (ushort)(offset[sorted[(lo + mid - 1) / 2]] / 4) : (ushort)0;
                ushort right = mid + 1 <= hi ? (ushort)(offset[sorted[(mid + 1 + hi) / 2]] / 4) : (ushort)0;
                BitConverter.GetBytes(left).CopyTo(into, o);
                BitConverter.GetBytes(right).CopyTo(into, o + 2);
                BitConverter.GetBytes(n.IsDirectory ? n.TableSector : n.DataSector).CopyTo(into, o + 4);
                BitConverter.GetBytes(n.Size).CopyTo(into, o + 8);
                into[o + 0x0C] = n.IsDirectory ? AttrDirectory : AttrFile;
                into[o + 0x0D] = (byte)n.Name.Length;
                Encoding.ASCII.GetBytes(n.Name).CopyTo(into, o + 0x0E);
                // The node's own padding to 4 bytes: 0xFF already.
            }
            return total;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Position { get => _position; set => _position = Math.Max(0, value); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int done = 0;
            while (done < count && _position < Length)
            {
                if (_position < _head.Length)
                {
                    int n = (int)Math.Min(count - done, _head.Length - _position);
                    Buffer.BlockCopy(_head, (int)_position, buffer, offset + done, n);
                    done += n; _position += n;
                    continue;
                }
                int i = Array.BinarySearch(_starts, _position);
                if (i < 0) i = ~i - 1;
                if (i >= 0 && _position < _starts[i] + _files[i].Length)
                {
                    long inFile = _position - _starts[i];
                    int n = (int)Math.Min(count - done, _files[i].Length - inFile);
                    int got = _zar.Read(_files[i], inFile, buffer, offset + done, n);
                    if (got <= 0) break;
                    done += got; _position += got;
                }
                else
                {
                    // A file's padding to its sector's end - zeros up to the next file or the end.
                    long next = i + 1 < _starts.Length ? _starts[i + 1] : Length;
                    int n = (int)Math.Min(count - done, next - _position);
                    if (n <= 0) break;
                    Array.Clear(buffer, offset + done, n);
                    done += n; _position += n;
                }
            }
            return done;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => _position = Math.Max(0, origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : Length + offset);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownsZar) _zar.Dispose();
            base.Dispose(disposing);
        }
    }
}
