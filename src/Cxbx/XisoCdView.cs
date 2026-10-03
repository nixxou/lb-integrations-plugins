// An Xbox disc image, seen by Windows as a CD it can read (Mehdi, 03/10: users keep their games as XISO; no copy, no
// Dokany). Windows has no XDVDFS driver; it has CDFS. Both describe a file as a run of 2048-byte sectors - so a CD's
// structure (ISO 9660, with a Joliet volume for the long names) is made here, in memory, whose every file points at the
// very sectors that file has in the Xbox image. Read through this view, the image is a CD: a sector is either the
// structure made here, or the image's own bytes, untouched.
//
// WHERE THE STRUCTURE GOES: sectors 16, 17, 18 (the volume descriptors: primary, Joliet, end) - unused in an XISO, whose
// XDVDFS descriptor is at sector 32, and in a redump the start of its video partition, which no Xbox game reads - and the
// directories and path tables after the image's last sector. The view is the image, those sectors overlaid, and longer.
//
// ISO 9660 as Windows' CDFS reads it: names in the primary volume are made unique 8.3 names (any CD reader gets a tree);
// the Joliet volume carries the real names (64 UCS-2 characters - an XDVDFS name is 42 at most). One extent per file:
// an Xbox file is one run of sectors and under 4 GB.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal sealed class XisoCdView
    {
        private const int Sector = 2048;
        private readonly Dictionary<long, byte[]> _synth = new Dictionary<long, byte[]>();   // sector -> its bytes
        public long ImageLength { get; }
        public long Length { get; }
        public string Label { get; }

        private sealed class Dir
        {
            public string Path, Name;
            public Dir Parent;
            public List<Dir> Dirs = new List<Dir>();
            public List<XdvdfsFile> Files = new List<XdvdfsFile>();
            public int Number;                      // in the path table, from 1
            public long[] Lba = new long[2];        // primary, Joliet
            public int[] Size = new int[2];
            public string[] ShortNames;             // primary volume: per child, 8.3
        }

        public XisoCdView(XdvdfsResult listing, long imageLength, string label)
        {
            ImageLength = imageLength;
            Label = label;
            if (listing.PartitionBase % Sector != 0) throw new NotSupportedException("the game partition is not on a sector boundary");

            // The tree.
            var root = new Dir { Path = "", Name = "" };
            var dirs = new Dictionary<string, Dir>(StringComparer.OrdinalIgnoreCase) { [""] = root };
            Dir DirOf(string path)
            {
                if (dirs.TryGetValue(path, out var d)) return d;
                var at = path.LastIndexOf('\\');
                var parent = DirOf(at < 0 ? "" : path.Substring(0, at));
                d = new Dir { Path = path, Name = at < 0 ? path : path.Substring(at + 1), Parent = parent };
                parent.Dirs.Add(d);
                dirs[path] = d;
                return d;
            }
            foreach (var d in listing.Dirs) DirOf(d);
            foreach (var f in listing.Files)
            {
                if (f.Offset % Sector != 0) throw new NotSupportedException(f.Path + " does not start on a sector");
                if (f.Length > uint.MaxValue) throw new NotSupportedException(f.Path + " is 4 GB or more");
                var at = f.Path.LastIndexOf('\\');
                DirOf(at < 0 ? "" : f.Path.Substring(0, at)).Files.Add(f);
            }

            // Path table order: breadth first, by parent, then by name (ISO 9660 9.4).
            var ordered = new List<Dir> { root };
            for (int i = 0; i < ordered.Count; i++)
                ordered.AddRange(ordered[i].Dirs.OrderBy(d => d.Name.ToUpperInvariant(), StringComparer.Ordinal));
            for (int i = 0; i < ordered.Count; i++) ordered[i].Number = i + 1;
            foreach (var d in ordered) d.ShortNames = ShortNames(d);

            // Where the structure goes: after the image.
            long next = (imageLength + Sector - 1) / Sector;
            long Reserve(int bytes) { var at = next; next += (bytes + Sector - 1) / Sector; return at; }

            var pathTables = new byte[2][];
            var pathLba = new long[2, 2];      // [volume, 0 = L, 1 = M]
            for (int v = 0; v < 2; v++)
            {
                foreach (var d in ordered) d.Size[v] = DirSize(d, v);
                foreach (var d in ordered) d.Lba[v] = Reserve(d.Size[v]);
                var l = PathTable(ordered, v, bigEndian: false);
                var m = PathTable(ordered, v, bigEndian: true);
                pathTables[v] = l;
                pathLba[v, 0] = Reserve(l.Length);
                pathLba[v, 1] = Reserve(m.Length);
                Put(pathLba[v, 0], l);
                Put(pathLba[v, 1], m);
            }
            // A margin of empty sectors after the structure, the whole rounded to 1 MB: a reader reading ahead in big
            // blocks never runs past the end on the last directories (measured 03/10 on Commandos 2: the last ones written,
            // in the final 64 KB of the view, came out "corrupted" through ImDisk).
            Length = ((next + 64) * Sector + (1 << 20) - 1) / (1 << 20) * (1 << 20);
            for (int v = 0; v < 2; v++)
                foreach (var d in ordered) Put(d.Lba[v], DirBytes(d, v));

            Put(16, Descriptor(1, root, pathTables[0].Length, pathLba[0, 0], pathLba[0, 1]));
            Put(17, Descriptor(2, root, pathTables[1].Length, pathLba[1, 0], pathLba[1, 1]));
            var end = new byte[Sector];
            end[0] = 255; Encoding.ASCII.GetBytes("CD001").CopyTo(end, 1); end[6] = 1;
            Put(18, end);
        }

        /// <summary>For a diagnosis: the structure made, read back from the view itself - each directory of the Joliet volume
        /// walked from the root as a CD reader would, its records listed.</summary>
        public string Dump(Stream image)
        {
            var sb = new StringBuilder();
            var buffer = new byte[Sector];
            byte[] SectorAt(long lba) { var b = new byte[Sector]; Read(image, lba * Sector, b, Sector); return b; }
            var svd = SectorAt(17);
            uint Le32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
            void Walk(long lba, uint size, string path, int depth)
            {
                for (long s = 0; s * Sector < size; s++)
                {
                    var b = SectorAt(lba + s);
                    for (int at = 0; at < Sector && b[at] != 0;)
                    {
                        int len = b[at], idLen = b[at + 32];
                        uint elba = Le32(b, at + 2), esize = Le32(b, at + 10);
                        bool dir = (b[at + 25] & 2) != 0;
                        string name = idLen == 1 && b[at + 33] <= 1 ? (b[at + 33] == 0 ? "." : "..") : Encoding.BigEndianUnicode.GetString(b, at + 33, idLen);
                        sb.AppendLine(new string(' ', depth * 2) + path + "\\" + name + (dir ? "\\" : "") + "  lba " + elba + " size " + esize + "  (record " + len + " at " + (s * Sector + at) + ")");
                        if (dir && name != "." && name != ".." && depth < 6) Walk(elba, esize, path + "\\" + name, depth + 1);
                        at += len;
                    }
                }
            }
            sb.AppendLine("Joliet root: lba " + Le32(svd, 156 + 2) + " size " + Le32(svd, 156 + 10) + ", volume " + Le32(svd, 80) + " sectors, path table " + Le32(svd, 132) + " bytes at " + Le32(svd, 140));
            Walk(Le32(svd, 156 + 2), Le32(svd, 156 + 10), "", 0);
            return sb.ToString();
        }

        /// <summary>The view's bytes at <paramref name="offset"/>: the structure where it is, else <paramref name="image"/>'s
        /// own (zeros past its end).</summary>
        public int Read(Stream image, long offset, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count && offset + done < Length)
            {
                long at = offset + done, sector = at / Sector;
                int inSector = (int)(at % Sector), n = Math.Min(count - done, Sector - inSector);
                if (_synth.TryGetValue(sector, out var s)) Array.Copy(s, inSector, buffer, done, n);
                else if (at < ImageLength)
                {
                    // The run of image sectors from here with nothing synthesized in it - read in one go.
                    long end = at + n;
                    while (end < offset + count && end < ImageLength && !_synth.ContainsKey(end / Sector)) end = Math.Min(end + Sector, offset + count);
                    n = (int)(Math.Min(end, ImageLength) - at);
                    image.Seek(at, SeekOrigin.Begin);
                    int got = 0, r;
                    while (got < n && (r = image.Read(buffer, done + got, n - got)) > 0) got += r;
                    if (got < n) Array.Clear(buffer, done + got, n - got);
                }
                else Array.Clear(buffer, done, n);
                done += n;
            }
            return done;
        }

        private void Put(long lba, byte[] bytes)
        {
            for (int i = 0; i * Sector < bytes.Length; i++)
            {
                var s = new byte[Sector];
                Array.Copy(bytes, i * Sector, s, 0, Math.Min(Sector, bytes.Length - i * Sector));
                _synth[lba + i] = s;
            }
        }

        // ── names ────────────────────────────────────────────────────────────

        /// <summary>Unique 8.3 names for a directory's children, primary volume: A-Z, 0-9, _ ; a number at the end when two meet.</summary>
        private static string[] ShortNames(Dir d)
        {
            var names = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            string Clean(string s, int max) => new string(s.ToUpperInvariant().Select(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_').Take(max).ToArray());
            foreach (var (name, dir) in Children(d))
            {
                var dot = dir ? -1 : name.LastIndexOf('.');
                var stem = Clean(dot > 0 ? name.Substring(0, dot) : name, 8);
                var ext = dot > 0 ? Clean(name.Substring(dot + 1), 3) : "";
                if (stem.Length == 0) stem = "_";
                string candidate = stem + (ext.Length > 0 ? "." + ext : "");
                for (int i = 1; !used.Add(candidate); i++)
                {
                    var tag = "~" + i;
                    candidate = stem.Substring(0, Math.Min(stem.Length, 8 - tag.Length)) + tag + (ext.Length > 0 ? "." + ext : "");
                }
                names.Add(candidate);
            }
            return names.ToArray();
        }

        /// <summary>A directory's children in record order: sorted by name, directories and files together.</summary>
        private static List<(string Name, bool Dir)> Children(Dir d)
            => d.Dirs.Select(x => (x.Name, true)).Concat(d.Files.Select(f => (FileName(f), false)))
                .OrderBy(x => x.Item1.ToUpperInvariant(), StringComparer.Ordinal).ToList();

        private static string FileName(XdvdfsFile f) { var at = f.Path.LastIndexOf('\\'); return at < 0 ? f.Path : f.Path.Substring(at + 1); }

        private static byte[] Id(string name, int volume, bool dir)
            => volume == 0 ? Encoding.ASCII.GetBytes(name + (dir ? "" : ";1")) : Encoding.BigEndianUnicode.GetBytes(name.Length > 64 ? name.Substring(0, 64) : name);

        // ── directories ──────────────────────────────────────────────────────

        private IEnumerable<byte[]> Records(Dir d, int v)
        {
            yield return Record(new byte[] { 0 }, d.Lba[v], d.Size[v], true);
            var parent = d.Parent ?? d;
            yield return Record(new byte[] { 1 }, parent.Lba[v], parent.Size[v], true);
            var children = Children(d);
            for (int i = 0; i < children.Count; i++)
            {
                var (name, isDir) = children[i];
                var id = v == 0 ? Id(d.ShortNames[i], 0, isDir) : Id(name, 1, isDir);
                if (isDir) { var sub = d.Dirs.First(x => x.Name == name); yield return Record(id, sub.Lba[v], sub.Size[v], true); }
                else { var f = d.Files.First(x => FileName(x) == name); yield return Record(id, f.Length == 0 ? 0 : f.Offset / Sector, f.Length, false); }
            }
        }

        /// <summary>A directory's records laid in sectors: a record never crosses one.</summary>
        private byte[] DirBytes(Dir d, int v)
        {
            var bytes = new List<byte>();
            foreach (var r in Records(d, v))
            {
                int used = bytes.Count % Sector;
                if (used + r.Length > Sector) bytes.AddRange(new byte[Sector - used]);
                bytes.AddRange(r);
            }
            if (bytes.Count % Sector != 0) bytes.AddRange(new byte[Sector - bytes.Count % Sector]);
            return bytes.ToArray();
        }

        private int DirSize(Dir d, int v)
        {
            // The sizes before the extents are known: the records' lengths do not depend on them.
            int total = 0;
            foreach (var r in Records(d, v))
            {
                int used = total % Sector;
                if (used + r.Length > Sector) total += Sector - used;
                total += r.Length;
            }
            return (total + Sector - 1) / Sector * Sector;
        }

        private static byte[] Record(byte[] id, long lba, long size, bool dir)
        {
            int len = 33 + id.Length + (id.Length % 2 == 0 ? 1 : 0);
            var r = new byte[len];
            r[0] = (byte)len;
            Both32(r, 2, (uint)lba);
            Both32(r, 10, (uint)size);
            Date7(r, 18);
            r[25] = (byte)(dir ? 2 : 0);
            Both16(r, 28, 1);
            r[32] = (byte)id.Length;
            id.CopyTo(r, 33);
            return r;
        }

        private static byte[] PathTable(List<Dir> ordered, int v, bool bigEndian)
        {
            var bytes = new List<byte>();
            foreach (var d in ordered)
            {
                var id = d.Parent == null ? new byte[] { 0 } : v == 0 ? Encoding.ASCII.GetBytes(d.Parent.ShortNames[Children(d.Parent).FindIndex(c => c.Dir && c.Name == d.Name)]) : Id(d.Name, 1, true);
                var e = new byte[8 + id.Length + (id.Length % 2)];
                e[0] = (byte)id.Length;
                var lba = (uint)d.Lba[v];
                var parent = (ushort)(d.Parent?.Number ?? 1);
                if (bigEndian) { e[2] = (byte)(lba >> 24); e[3] = (byte)(lba >> 16); e[4] = (byte)(lba >> 8); e[5] = (byte)lba; e[6] = (byte)(parent >> 8); e[7] = (byte)parent; }
                else { BitConverter.GetBytes(lba).CopyTo(e, 2); BitConverter.GetBytes(parent).CopyTo(e, 6); }
                id.CopyTo(e, 8);
                bytes.AddRange(e);
            }
            return bytes.ToArray();
        }

        private byte[] Descriptor(byte type, Dir root, int pathTableSize, long lLba, long mLba)
        {
            var b = new byte[Sector];
            b[0] = type;
            Encoding.ASCII.GetBytes("CD001").CopyTo(b, 1);
            b[6] = 1;
            for (int i = 8; i < 40; i++) b[i] = (byte)' ';
            var label = new string((Label ?? "XBOX").ToUpperInvariant().Select(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_').Take(type == 2 ? 16 : 32).ToArray());
            if (type == 1)
            {
                for (int i = 40; i < 72; i++) b[i] = (byte)' ';
                Encoding.ASCII.GetBytes(label).CopyTo(b, 40);
            }
            else
            {
                for (int i = 40; i < 72; i += 2) { b[i] = 0; b[i + 1] = (byte)' '; }
                Encoding.BigEndianUnicode.GetBytes(label).CopyTo(b, 40);
                b[88] = 0x25; b[89] = 0x2F; b[90] = 0x45;               // "%/E": Joliet, UCS-2 level 3
            }
            Both32(b, 80, (uint)(Length / Sector));
            Both16(b, 120, 1);
            Both16(b, 124, 1);
            Both16(b, 128, Sector);
            Both32(b, 132, (uint)pathTableSize);
            BitConverter.GetBytes((uint)lLba).CopyTo(b, 140);
            var m = (uint)mLba;
            b[148] = (byte)(m >> 24); b[149] = (byte)(m >> 16); b[150] = (byte)(m >> 8); b[151] = (byte)m;
            Record(new byte[] { 0 }, root.Lba[type - 1], root.Size[type - 1], true).CopyTo(b, 156);
            for (int i = 190; i < 813; i++) b[i] = (byte)' ';           // set, publisher, preparer, application ids, files
            if (type == 2) for (int i = 190; i < 813; i += 2) b[i] = 0;
            for (int at = 813; at < 881; at += 17) { for (int i = 0; i < 16; i++) b[at + i] = (byte)'0'; }   // dates: unset
            b[881] = 1;                                                 // file structure version
            return b;
        }

        private static void Both16(byte[] b, int at, ushort v) { BitConverter.GetBytes(v).CopyTo(b, at); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
        private static void Both32(byte[] b, int at, uint v) { BitConverter.GetBytes(v).CopyTo(b, at); b[at + 4] = (byte)(v >> 24); b[at + 5] = (byte)(v >> 16); b[at + 6] = (byte)(v >> 8); b[at + 7] = (byte)v; }

        private static void Date7(byte[] b, int at)
        {
            var d = new DateTime(2001, 11, 15, 0, 0, 0);    // the Xbox's launch: any fixed date does
            b[at] = (byte)(d.Year - 1900); b[at + 1] = (byte)d.Month; b[at + 2] = (byte)d.Day;
        }
    }
}
