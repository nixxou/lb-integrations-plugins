// An Xbox disc image, seen by Windows as a hard disk holding one FAT32 volume - the CD view's successor (03/10). The CD
// view read right through ImDisk, but Cxbx-Reloaded could not run from it: it resolves the XBE's path with
// std::filesystem::canonical, i.e. GetFinalPathNameByHandleW, which needs the volume known to the mount manager - ImDisk's
// drive letters are not (measured: every normalized / DOS form fails with error 1, the same file on a real CD resolves),
// and Cxbx then maps D: and XeImageFileName to an empty path. A disk of Arsenal Image Mounter is a real PnP disk, the mount
// manager's own - but AIM makes no CD. So a disk, and a file system Windows reads from a disk: FAT32.
//
// FAT32 with 2048-byte clusters: a cluster is an Xbox sector. The data area IS the image, cluster 2 its sector 0, so each
// file is a run of clusters pointing at the very sectors it has in the image - the FAT says where a file goes on, the
// image is never copied. Made here, in memory: the MBR (one partition at 1 MB), the boot sectors, the FAT (computed from
// the files' runs when asked, never stored) and the directories, in clusters after the image's last one. Long names in
// VFAT entries (an XDVDFS name is 42 characters at most).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal sealed class XisoFatView
    {
        private const int Bps = 512, Cluster = 2048, Spc = Cluster / Bps;
        private const long PartitionLba = 2048;                     // 1 MB
        private const uint Eoc = 0x0FFFFFFF;

        public long ImageLength { get; }
        public long Length { get; }
        public string Label { get; }

        private readonly long _imageClusters;
        private readonly uint _fatSectors, _reserved, _totalSectors;
        private readonly long _fatStart, _dataStart;                // bytes, from the disk's start
        private readonly uint[] _runFirst, _runLast;                // clusters, sorted - every file's and directory's run
        private readonly Dictionary<long, byte[]> _dirClusters = new Dictionary<long, byte[]>();
        private readonly byte[] _mbr, _boot, _fsInfo;
        private readonly uint _serial;

        private sealed class Dir
        {
            public string Name;
            public Dir Parent;
            public List<Dir> Dirs = new List<Dir>();
            public List<XdvdfsFile> Files = new List<XdvdfsFile>();
            public uint First;                                      // its first cluster
            public int Clusters;
        }

        public XisoFatView(XdvdfsResult listing, long imageLength, string label)
        {
            ImageLength = imageLength;
            Label = label;
            // A new disk signature at every attach - measured 03/10: the same disc attached twice had the same one, and
            // Windows took the second disk offline ("signature collision"), unable to rewrite it on a read-only disk.
            _serial = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) | 1;

            var root = new Dir { Name = "" };
            var dirs = new Dictionary<string, Dir>(StringComparer.OrdinalIgnoreCase) { [""] = root };
            Dir DirOf(string path)
            {
                if (dirs.TryGetValue(path, out var d)) return d;
                var at = path.LastIndexOf('\\');
                var parent = DirOf(at < 0 ? "" : path.Substring(0, at));
                d = new Dir { Name = at < 0 ? path : path.Substring(at + 1), Parent = parent };
                parent.Dirs.Add(d);
                dirs[path] = d;
                return d;
            }
            foreach (var d in listing.Dirs) DirOf(d);
            foreach (var f in listing.Files)
            {
                if (f.Offset % Cluster != 0) throw new NotSupportedException(f.Path + " does not start on a sector");
                if (f.Length > uint.MaxValue) throw new NotSupportedException(f.Path + " is 4 GB or more");
                var at = f.Path.LastIndexOf('\\');
                DirOf(at < 0 ? "" : f.Path.Substring(0, at)).Files.Add(f);
            }

            // The directories, after the image: their sizes first (they depend on the names only), then their clusters.
            _imageClusters = (imageLength + Cluster - 1) / Cluster;
            long next = 2 + _imageClusters;
            var all = new List<Dir>();
            void Place(Dir d)
            {
                all.Add(d);
                d.Clusters = Math.Max(1, (Entries(d).Count * 32 + Cluster - 1) / Cluster);
                d.First = (uint)next;
                next += d.Clusters;
                foreach (var sub in d.Dirs) Place(sub);
            }
            Place(root);

            // The runs: files where their sectors are, directories where placed. Two files on the same sectors cannot be
            // told apart by a FAT.
            var runs = listing.Files.Where(f => f.Length > 0)
                .Select(f => (First: (uint)(2 + f.Offset / Cluster), Last: (uint)(2 + (f.Offset + f.Length - 1) / Cluster)))
                .Concat(all.Select(d => (d.First, Last: (uint)(d.First + d.Clusters - 1))))
                .OrderBy(r => r.First).ToList();
            for (int i = 1; i < runs.Count; i++)
                if (runs[i].First <= runs[i - 1].Last) throw new NotSupportedException("two files share sectors (cluster " + runs[i].First + ")");
            _runFirst = runs.Select(r => r.First).ToArray();
            _runLast = runs.Select(r => r.Last).ToArray();

            // The volume: at least 65525 clusters or Windows takes it for FAT16 - a small XISO gets free clusters after.
            long clusters = Math.Max(next - 2, 65525 + 16);
            _fatSectors = (uint)(((clusters + 2) * 4 + Bps - 1) / Bps);
            // Reserved sectors: 32 at least, more so that the data area starts on a 1 MB boundary - the image then reads
            // aligned as the disk is.
            long head = PartitionLba + 32 + 2L * _fatSectors;
            _reserved = (uint)(32 + (2048 - head % 2048) % 2048);
            _totalSectors = checked((uint)(_reserved + 2L * _fatSectors + clusters * Spc));
            _fatStart = (PartitionLba + _reserved) * Bps;
            _dataStart = _fatStart + 2L * _fatSectors * Bps;
            Length = (PartitionLba + _totalSectors) * Bps;

            foreach (var d in all)
            {
                var bytes = new byte[d.Clusters * Cluster];
                int at = 0;
                foreach (var e in Entries(d)) { e.CopyTo(bytes, at); at += 32; }
                for (int i = 0; i < d.Clusters; i++)
                {
                    var c = new byte[Cluster];
                    Array.Copy(bytes, i * Cluster, c, 0, Cluster);
                    _dirClusters[d.First + i] = c;
                }
            }

            _mbr = Mbr();
            _boot = Boot(root.First);
            _fsInfo = FsInfo();
        }

        /// <summary>The disk's bytes at <paramref name="offset"/>: the structure where it is, else <paramref name="image"/>'s own
        /// (zeros past its end).</summary>
        public int Read(Stream image, long offset, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count && offset + done < Length)
            {
                long at = offset + done;
                int n;
                if (at < _fatStart)
                {
                    n = (int)Math.Min(count - done, Bps - at % Bps);
                    long sector = at / Bps;
                    byte[] s = sector == 0 ? _mbr
                             : sector == PartitionLba || sector == PartitionLba + 6 ? _boot
                             : sector == PartitionLba + 1 || sector == PartitionLba + 7 ? _fsInfo
                             : null;
                    if (s != null) Array.Copy(s, at % Bps, buffer, done, n); else Array.Clear(buffer, done, n);
                }
                else if (at < _dataStart)
                {
                    // The FAT, both copies alike: entry by entry, each from the runs.
                    long fatBytes = (long)_fatSectors * Bps;
                    long inFat = (at - _fatStart) % fatBytes;
                    n = (int)Math.Min(count - done, fatBytes - inFat);
                    n = (int)Math.Min(n, _dataStart - at);
                    for (int i = 0; i < n;)
                    {
                        long e = (inFat + i) / 4;
                        int k = (int)((inFat + i) % 4), m = Math.Min(4 - k, n - i);
                        var v = BitConverter.GetBytes(Next(e));
                        Array.Copy(v, k, buffer, done + i, m);
                        i += m;
                    }
                }
                else
                {
                    long c = 2 + (at - _dataStart) / Cluster;
                    int inCluster = (int)((at - _dataStart) % Cluster);
                    if (c < 2 + _imageClusters)
                    {
                        // A run of image clusters - read in one go, up to the image's end.
                        long imageAt = (c - 2) * Cluster + inCluster;
                        long end = Math.Min(_imageClusters * (long)Cluster, imageAt + (count - done));
                        n = (int)(end - imageAt);
                        int want = (int)Math.Max(0, Math.Min(n, ImageLength - imageAt)), got = 0, r;
                        image.Seek(imageAt, SeekOrigin.Begin);
                        while (got < want && (r = image.Read(buffer, done + got, want - got)) > 0) got += r;
                        if (got < n) Array.Clear(buffer, done + got, n - got);
                    }
                    else
                    {
                        n = Math.Min(count - done, Cluster - inCluster);
                        if (_dirClusters.TryGetValue(c, out var d)) Array.Copy(d, inCluster, buffer, done, n); else Array.Clear(buffer, done, n);
                    }
                }
                done += n;
            }
            return done;
        }

        /// <summary>FAT entry <paramref name="cluster"/>: the next cluster of its run, the run's end, or free.</summary>
        private uint Next(long cluster)
        {
            if (cluster == 0) return 0x0FFFFFF8;                    // media byte
            if (cluster == 1) return Eoc;
            if (cluster > uint.MaxValue) return 0;
            int i = Array.BinarySearch(_runFirst, (uint)cluster);
            if (i < 0) i = ~i - 1;
            if (i < 0 || cluster > _runLast[i]) return 0;
            return cluster == _runLast[i] ? Eoc : (uint)cluster + 1;
        }

        // ── directories ──────────────────────────────────────────────────────

        /// <summary>A directory's 32-byte entries: the label (root) or "." and ".." (others), then each child - its long-name
        /// entries, its short one.</summary>
        private List<byte[]> Entries(Dir d)
        {
            var list = new List<byte[]>();
            if (d.Parent == null) list.Add(Short(LabelName(), 0x08, 0, 0));
            else
            {
                list.Add(Short(Encoding.ASCII.GetBytes(".          "), 0x10, d.First, 0));
                list.Add(Short(Encoding.ASCII.GetBytes("..         "), 0x10, d.Parent.Parent == null ? 0 : d.Parent.First, 0));
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            var children = d.Dirs.Select(x => (x.Name, Dir: x, File: (XdvdfsFile)null))
                .Concat(d.Files.Select(f => (Name: FileName(f), Dir: (Dir)null, File: f)))
                .OrderBy(x => x.Name.ToUpperInvariant(), StringComparer.Ordinal);
            foreach (var (name, sub, file) in children)
            {
                var clean = Sanitize(name);
                var shortName = ShortName(clean, sub != null, used, out bool exact);
                if (!exact) list.AddRange(LongEntries(clean, Checksum(shortName)));
                if (sub != null) list.Add(Short(shortName, 0x10, sub.First, 0));
                else list.Add(Short(shortName, 0x01, file.Length == 0 ? 0 : (uint)(2 + file.Offset / Cluster), (uint)file.Length));
            }
            return list;
        }

        private static string FileName(XdvdfsFile f) { var at = f.Path.LastIndexOf('\\'); return at < 0 ? f.Path : f.Path.Substring(at + 1); }

        /// <summary>A name as FAT allows it: no \ / : * ? " &lt; &gt; |, no trailing dot or space.</summary>
        private static string Sanitize(string name)
        {
            var s = new string(name.Select(c => c < 32 || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c).ToArray()).TrimEnd('.', ' ');
            return s.Length == 0 ? "_" : s;
        }

        /// <summary>The 11-byte short name, unique in its directory; <paramref name="exact"/> when it is the name itself (an
        /// upper-case 8.3 name needs no long entry).</summary>
        private static byte[] ShortName(string name, bool dir, HashSet<string> used, out bool exact)
        {
            string Clean(string s, int max) => new string(s.ToUpperInvariant().Select(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' ? c : '_').Take(max).ToArray());
            var dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name, ext = dot > 0 ? name.Substring(dot + 1) : "";
            string s8 = Clean(stem, 8), e3 = Clean(ext, 3);
            if (s8.Length == 0) s8 = "_";
            exact = s8 == stem && e3 == ext && (dot < 0 || dot > 0) && name.Count(c => c == '.') <= 1 && used.Add(s8 + "." + e3);
            if (!exact)
            {
                var cut = Clean(stem.Replace(".", "").Replace(" ", ""), 6);
                if (cut.Length == 0) cut = "_";
                for (int i = 1; ; i++)
                {
                    var tag = "~" + i;
                    s8 = cut.Substring(0, Math.Min(cut.Length, 8 - tag.Length)) + tag;
                    if (used.Add(s8 + "." + e3)) break;
                }
            }
            return Encoding.ASCII.GetBytes(s8.PadRight(8) + e3.PadRight(3));
        }

        private byte[] LabelName()
            => Encoding.ASCII.GetBytes(new string((Label ?? "XBOX").ToUpperInvariant().Select(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' ? c : '_').Take(11).ToArray()).PadRight(11));

        private static byte[] Short(byte[] name11, byte attr, uint cluster, uint size)
        {
            var e = new byte[32];
            name11.CopyTo(e, 0);
            e[11] = attr;
            // 15/11/2001 00:00, the Xbox's launch: any fixed date does.
            ushort date = (ushort)(((2001 - 1980) << 9) | (11 << 5) | 15);
            BitConverter.GetBytes(date).CopyTo(e, 16);             // created
            BitConverter.GetBytes(date).CopyTo(e, 18);             // accessed
            BitConverter.GetBytes((ushort)(cluster >> 16)).CopyTo(e, 20);
            BitConverter.GetBytes(date).CopyTo(e, 24);             // written
            BitConverter.GetBytes((ushort)cluster).CopyTo(e, 26);
            BitConverter.GetBytes(size).CopyTo(e, 28);
            return e;
        }

        private static byte Checksum(byte[] name11)
        {
            byte sum = 0;
            foreach (var b in name11) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + b);
            return sum;
        }

        /// <summary>The long name's entries, last part first as they are laid: 13 UTF-16 units each, ended by 0 then 0xFFFF.</summary>
        private static IEnumerable<byte[]> LongEntries(string name, byte checksum)
        {
            int parts = (name.Length + 1 + 12) / 13;               // with its terminator, unless it fills the last part
            if (name.Length % 13 == 0) parts = name.Length / 13;
            var units = new ushort[parts * 13];
            for (int i = 0; i < units.Length; i++) units[i] = i < name.Length ? name[i] : i == name.Length ? (ushort)0 : (ushort)0xFFFF;
            int[] at = { 1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30 };
            for (int p = parts; p >= 1; p--)
            {
                var e = new byte[32];
                e[0] = (byte)(p | (p == parts ? 0x40 : 0));
                e[11] = 0x0F;
                e[13] = checksum;
                for (int i = 0; i < 13; i++) BitConverter.GetBytes(units[(p - 1) * 13 + i]).CopyTo(e, at[i]);
                yield return e;
            }
        }

        // ── the disk's first sectors ─────────────────────────────────────────

        private byte[] Mbr()
        {
            var b = new byte[Bps];
            BitConverter.GetBytes(_serial).CopyTo(b, 440);         // disk signature
            var p = 446;
            b[p] = 0;
            b[p + 1] = 0xFE; b[p + 2] = 0xFF; b[p + 3] = 0xFF;      // CHS: beyond, use the LBA
            b[p + 4] = 0x0C;                                         // FAT32, LBA
            b[p + 5] = 0xFE; b[p + 6] = 0xFF; b[p + 7] = 0xFF;
            BitConverter.GetBytes((uint)PartitionLba).CopyTo(b, p + 8);
            BitConverter.GetBytes(_totalSectors).CopyTo(b, p + 12);
            b[510] = 0x55; b[511] = 0xAA;
            return b;
        }

        private byte[] Boot(uint rootCluster)
        {
            var b = new byte[Bps];
            b[0] = 0xEB; b[1] = 0x58; b[2] = 0x90;
            Encoding.ASCII.GetBytes("MSWIN4.1").CopyTo(b, 3);
            BitConverter.GetBytes((ushort)Bps).CopyTo(b, 11);
            b[13] = Spc;
            BitConverter.GetBytes((ushort)_reserved).CopyTo(b, 14);
            b[16] = 2;                                              // FATs
            b[21] = 0xF8;                                           // fixed disk
            BitConverter.GetBytes((ushort)63).CopyTo(b, 24);
            BitConverter.GetBytes((ushort)255).CopyTo(b, 26);
            BitConverter.GetBytes((uint)PartitionLba).CopyTo(b, 28);
            BitConverter.GetBytes(_totalSectors).CopyTo(b, 32);
            BitConverter.GetBytes(_fatSectors).CopyTo(b, 36);
            BitConverter.GetBytes(rootCluster).CopyTo(b, 44);
            BitConverter.GetBytes((ushort)1).CopyTo(b, 48);        // FSInfo
            BitConverter.GetBytes((ushort)6).CopyTo(b, 50);        // backup boot sector
            b[64] = 0x80;
            b[66] = 0x29;
            BitConverter.GetBytes(_serial).CopyTo(b, 67);
            LabelName().CopyTo(b, 71);
            Encoding.ASCII.GetBytes("FAT32   ").CopyTo(b, 82);
            b[510] = 0x55; b[511] = 0xAA;
            return b;
        }

        private static byte[] FsInfo()
        {
            var b = new byte[Bps];
            BitConverter.GetBytes(0x41615252u).CopyTo(b, 0);
            BitConverter.GetBytes(0x61417272u).CopyTo(b, 484);
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(b, 488);     // free clusters: unknown
            BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(b, 492);
            BitConverter.GetBytes(0xAA550000u).CopyTo(b, 508);
            return b;
        }
    }
}
