// A disk Windows sees holding one exFAT volume with a tree of files - each file's bytes read from its IVolumeFile (a source
// read through, a virtual zip), nothing copied, nothing writable. The many-files sibling of ExfatOneFileView (one file, an
// Xbox disc): the same disk, boot region and checksums, a tree instead of one entry. exFAT, not FAT32: no 4 GB limit on a
// file (CHDs of hard disks and DVDs are bigger), and tens of thousands of entries in a directory are fine.
//
// THE LAYOUT, made here, in memory but for the files' bytes: the MBR (one partition at 1 MB, type 07), the boot region and
// its backup, the FAT (computed when asked, never stored), then the cluster heap on a 1 MB boundary of the disk, clusters of
// 64 KB: every file contiguous, in tree order, from cluster 2; then every directory but the root, contiguous; the allocation
// bitmap (every cluster in use), the up-case table (ASCII letters, every other character itself), the root directory last.
// Files and directories are NoFatChain; the FAT holds every run's chain too (the root's is the one exFAT needs). A cluster
// is found by a binary search over the runs.
//
// The MBR caps the disk at 2 TB (sector count on 32 bits): a bigger tree is refused, not truncated.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LbIntegrations.Volume
{
    internal sealed class ExfatVolumeView
    {
        private const int Bps = 512, BpsShift = 9;
        private const int SpcShift = 7, Spc = 1 << SpcShift, ClusterBytes = Bps << SpcShift;   // 64 KB
        private const long PartitionLba = 2048;                     // 1 MB
        private const uint FatOffset = 128;                         // sectors, from the volume's start
        private const uint Eoc = 0xFFFFFFFF;

        private sealed class Dir
        {
            public string Name;
            public readonly SortedDictionary<string, Dir> Dirs = new(StringComparer.OrdinalIgnoreCase);
            public readonly SortedDictionary<string, IVolumeFile> Files = new(StringComparer.OrdinalIgnoreCase);
            public uint First; public long Clusters, Bytes;
            public byte[] Content;
        }

        private struct Run
        {
            public uint First; public long Clusters;
            public IVolumeFile File;   // a file's bytes, or
            public byte[] Bytes;       // made here (a directory, the bitmap, the up-case table)
        }

        public string Label { get; }
        public long Length { get; }
        public int FileCount { get; }
        /// <summary>Paths left out: a name used twice in a directory (case aside).</summary>
        public List<string> Skipped { get; } = new();

        private readonly Run[] _runs;
        private readonly uint _fatLength, _heapOffset, _clusterCount, _rootFirst;
        private readonly long _volumeLength, _fatStart, _heapStart;
        private readonly byte[] _mbr, _bootRegion;
        private readonly uint _serial;

        public ExfatVolumeView(IEnumerable<(string Path, IVolumeFile File)> files, IEnumerable<string> dirs = null, string label = "VOLUME")
        {
            Label = new string((label ?? "VOLUME").Where(c => c >= 32 && c < 127).Take(11).ToArray());
            var g = Guid.NewGuid().ToByteArray();
            _serial = BitConverter.ToUInt32(g, 0) | 1;
            uint volumeSerial = BitConverter.ToUInt32(g, 4) | 1;

            // the tree
            var root = new Dir { Name = "" };
            Dir DirOf(string[] parts, int count)
            {
                var d = root;
                for (int i = 0; i < count; i++)
                {
                    var n = Name(parts[i]);
                    if (!d.Dirs.TryGetValue(n, out var sub)) d.Dirs[n] = sub = new Dir { Name = n };
                    d = sub;
                }
                return d;
            }
            foreach (var d in dirs ?? Enumerable.Empty<string>()) { var p = d.Split('\\', StringSplitOptions.RemoveEmptyEntries); DirOf(p, p.Length); }
            int fileCount = 0;
            foreach (var (path, file) in files)
            {
                var p = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                var dir = DirOf(p, p.Length - 1);
                var n = Name(p[^1]);
                if (dir.Files.ContainsKey(n) || dir.Dirs.ContainsKey(n)) { Skipped.Add(path); continue; }
                dir.Files[n] = file;
                fileCount++;
            }
            FileCount = fileCount;

            // clusters: files, in tree order, from 2
            var runs = new List<Run>();
            uint next = 2;
            var all = new List<Dir>();
            void Walk(Dir d)
            {
                all.Add(d);
                foreach (var f in d.Files.Values)
                {
                    long n = Math.Max(1, (f.Length + ClusterBytes - 1) / ClusterBytes);
                    runs.Add(new Run { First = next, Clusters = n, File = f });
                    next = checked((uint)(next + n));
                }
                foreach (var s in d.Dirs.Values) Walk(s);
            }
            Walk(root);
            // the directories' sizes follow from their entries alone; the root also holds the label, bitmap and table entries
            foreach (var d in all)
            {
                long bytes = (d == root ? 3 * 32 : 0) + d.Files.Keys.Concat(d.Dirs.Keys).Sum(n => 32L * (2 + (n.Length + 14) / 15));
                d.Bytes = Math.Max(bytes, 32);
                d.Clusters = Math.Max(1, (d.Bytes + ClusterBytes - 1) / ClusterBytes);
            }
            foreach (var d in all.Where(d => d != root)) { d.First = next; next = checked((uint)(next + d.Clusters)); }

            var upcase = Upcase();
            long upcaseClusters = (upcase.Length + ClusterBytes - 1) / ClusterBytes;
            long before = next - 2 + upcaseClusters + root.Clusters, bitmapClusters = 1, bitmapLength;
            for (; ; )
            {
                bitmapLength = (before + bitmapClusters + 7) / 8;
                long need = (bitmapLength + ClusterBytes - 1) / ClusterBytes;
                if (need <= bitmapClusters) break;
                bitmapClusters = need;
            }
            uint bitmapFirst = next; next = checked((uint)(next + bitmapClusters));
            uint upcaseFirst = next; next = checked((uint)(next + upcaseClusters));
            root.First = _rootFirst = next; next = checked((uint)(next + root.Clusters));
            _clusterCount = next - 2;

            // the directories' bytes, now that every first cluster is known
            var firstOfFile = new Dictionary<IVolumeFile, uint>(ReferenceEqualityComparer.Instance);
            foreach (var r in runs) firstOfFile[r.File] = r.First;
            foreach (var d in all)
            {
                var content = new byte[d.Clusters * ClusterBytes];
                int at = 0;
                if (d == root)
                {
                    content[0] = 0x83; content[1] = (byte)Label.Length; Encoding.Unicode.GetBytes(Label).CopyTo(content, 2);
                    content[32] = 0x81; BitConverter.GetBytes(bitmapFirst).CopyTo(content, 52); BitConverter.GetBytes((ulong)bitmapLength).CopyTo(content, 56);
                    content[64] = 0x82; BitConverter.GetBytes(TableChecksum(upcase)).CopyTo(content, 68);
                    BitConverter.GetBytes(upcaseFirst).CopyTo(content, 84); BitConverter.GetBytes((ulong)upcase.Length).CopyTo(content, 88);
                    at = 96;
                }
                foreach (var (n, f) in d.Files) { var set = EntrySet(n, false, firstOfFile[f], f.Length); set.CopyTo(content, at); at += set.Length; }
                foreach (var (n, s) in d.Dirs) { var set = EntrySet(n, true, s.First, s.Clusters * ClusterBytes); set.CopyTo(content, at); at += set.Length; }
                d.Content = content;
                runs.Add(new Run { First = d.First, Clusters = d.Clusters, Bytes = content });
            }
            var bitmap = new byte[bitmapClusters * ClusterBytes];
            for (long c = 0; c < _clusterCount; c++) bitmap[c / 8] |= (byte)(1 << (int)(c % 8));
            runs.Add(new Run { First = bitmapFirst, Clusters = bitmapClusters, Bytes = bitmap });
            var table = new byte[upcaseClusters * ClusterBytes];
            upcase.CopyTo(table, 0);
            runs.Add(new Run { First = upcaseFirst, Clusters = upcaseClusters, Bytes = table });
            _runs = runs.OrderBy(r => r.First).ToArray();

            _fatLength = (uint)(((_clusterCount + 2L) * 4 + Bps - 1) / Bps);
            _heapOffset = (uint)((FatOffset + _fatLength + 2047) / 2048 * 2048);
            _volumeLength = _heapOffset + (long)_clusterCount * Spc;
            if (PartitionLba + _volumeLength > uint.MaxValue)
                throw new InvalidOperationException("the volume would be " + (_volumeLength * Bps >> 30) + " GB: an MBR disk stops at 2 TB");
            _fatStart = (PartitionLba + FatOffset) * Bps;
            _heapStart = (PartitionLba + _heapOffset) * Bps;
            Length = (PartitionLba + _volumeLength) * Bps;
            _mbr = Mbr();
            _bootRegion = BootRegion(volumeSerial);
        }

        /// <summary>The disk's bytes at <paramref name="offset"/>. The source stream of the helper's protocol is not used.</summary>
        public int Read(System.IO.Stream unused, long offset, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count && offset + done < Length)
            {
                long at = offset + done;
                int n;
                if (at < _fatStart)
                {
                    n = (int)Math.Min(count - done, Bps - at % Bps);
                    long sector = at / Bps, inVolume = sector - PartitionLba;
                    byte[] s = null; int from = 0;
                    if (sector == 0) s = _mbr;
                    else if (inVolume >= 0 && inVolume < 24) { s = _bootRegion; from = (int)(inVolume % 12) * Bps; }
                    if (s != null) Array.Copy(s, from + at % Bps, buffer, done, n); else Array.Clear(buffer, done, n);
                }
                else if (at < _heapStart)
                {
                    long fatBytes = (long)_fatLength * Bps;
                    if (at >= _fatStart + fatBytes) { n = (int)Math.Min(count - done, _heapStart - at); Array.Clear(buffer, done, n); }
                    else
                    {
                        long inFat = at - _fatStart;
                        n = (int)Math.Min(count - done, fatBytes - inFat);
                        for (int i = 0; i < n;)
                        {
                            long e = (inFat + i) / 4;
                            int k = (int)((inFat + i) % 4), m = Math.Min(4 - k, n - i);
                            var v = BitConverter.GetBytes(Next(e));
                            Array.Copy(v, k, buffer, done + i, m);
                            i += m;
                        }
                    }
                }
                else
                {
                    long heapAt = at - _heapStart;
                    long c = 2 + heapAt / ClusterBytes;
                    int ri = Find(c);
                    if (ri < 0) { n = (int)Math.Min(count - done, ClusterBytes - heapAt % ClusterBytes); Array.Clear(buffer, done, n); }
                    else
                    {
                        var r = _runs[ri];
                        long inRun = heapAt - (long)(r.First - 2) * ClusterBytes;
                        n = (int)Math.Min(count - done, r.Clusters * ClusterBytes - inRun);
                        if (r.Bytes != null) Array.Copy(r.Bytes, inRun, buffer, done, n);
                        else
                        {
                            int want = (int)Math.Max(0, Math.Min(n, r.File.Length - inRun)), got = 0;
                            while (got < want) { int g = r.File.Read(inRun + got, buffer, done + got, want - got); if (g <= 0) break; got += g; }
                            if (got < n) Array.Clear(buffer, done + got, n - got);
                        }
                    }
                }
                done += n;
            }
            return done;
        }

        private int Find(long cluster)
        {
            int lo = 0, hi = _runs.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var r = _runs[mid];
                if (cluster < r.First) hi = mid - 1;
                else if (cluster >= r.First + r.Clusters) lo = mid + 1;
                else return mid;
            }
            return -1;
        }

        private uint Next(long cluster)
        {
            if (cluster == 0) return 0xFFFFFFF8;
            if (cluster == 1) return Eoc;
            if (cluster > uint.MaxValue) return 0;
            int i = Find(cluster);
            if (i < 0) return 0;
            var r = _runs[i];
            return cluster == r.First + r.Clusters - 1 ? Eoc : (uint)cluster + 1;
        }

        // ── entries ──────────────────────────────────────────────────────────

        private static byte[] EntrySet(string name, bool directory, uint first, long length)
        {
            int nameEntries = (name.Length + 14) / 15;
            var set = new byte[32 * (2 + nameEntries)];
            set[0] = 0x85;
            set[1] = (byte)(1 + nameEntries);
            BitConverter.GetBytes((ushort)(directory ? 0x11 : 0x21)).CopyTo(set, 4);   // read-only, and directory / archive
            uint stamp = (uint)((2001 - 1980) << 25 | 11 << 21 | 15 << 16);           // any fixed date does
            BitConverter.GetBytes(stamp).CopyTo(set, 8);
            BitConverter.GetBytes(stamp).CopyTo(set, 12);
            BitConverter.GetBytes(stamp).CopyTo(set, 16);
            int s = 32;
            set[s] = 0xC0;
            set[s + 1] = 0x03;                                      // allocation possible, no FAT chain: contiguous
            set[s + 3] = (byte)name.Length;
            BitConverter.GetBytes(NameHash(name)).CopyTo(set, s + 4);
            BitConverter.GetBytes((ulong)length).CopyTo(set, s + 8);
            BitConverter.GetBytes(length == 0 && !directory ? 0u : first).CopyTo(set, s + 20);
            BitConverter.GetBytes((ulong)length).CopyTo(set, s + 24);
            for (int i = 0; i < nameEntries; i++)
            {
                int e = 64 + 32 * i;
                set[e] = 0xC1;
                Encoding.Unicode.GetBytes(name.Substring(i * 15, Math.Min(15, name.Length - i * 15))).CopyTo(set, e + 2);
            }
            BitConverter.GetBytes(SetChecksum(set)).CopyTo(set, 2);
            return set;
        }

        /// <summary>A name the volume can hold: printable ASCII but \ / : * ? " &lt; &gt; | (each another one _), no space at
        /// either end nor dot at the end, at most 255 characters.</summary>
        internal static string Name(string name)
        {
            var s = new string((name ?? "").Select(c => c < 32 || c > 126 || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.').TrimEnd();
            if (s.Length == 0) s = "_";
            return s.Length > 255 ? s.Substring(0, 255) : s;
        }

        private static ushort SetChecksum(byte[] set)
        {
            ushort sum = 0;
            for (int i = 0; i < set.Length; i++)
            {
                if (i == 2 || i == 3) continue;
                sum = (ushort)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + set[i]);
            }
            return sum;
        }

        private static ushort NameHash(string name)
        {
            ushort hash = 0;
            foreach (var c in name.ToUpperInvariant())
            {
                hash = (ushort)(((hash & 1) != 0 ? 0x8000 : 0) + (hash >> 1) + (c & 0xFF));
                hash = (ushort)(((hash & 1) != 0 ? 0x8000 : 0) + (hash >> 1) + (c >> 8));
            }
            return hash;
        }

        private static uint TableChecksum(byte[] table)
        {
            uint sum = 0;
            foreach (var b in table) sum = ((sum & 1) != 0 ? 0x80000000 : 0) + (sum >> 1) + b;
            return sum;
        }

        private static byte[] Upcase()
        {
            var t = new byte[65536 * 2];
            for (int c = 0; c < 65536; c++)
            {
                int u = c >= 'a' && c <= 'z' ? c - 32 : c;
                t[c * 2] = (byte)u; t[c * 2 + 1] = (byte)(u >> 8);
            }
            return t;
        }

        private byte[] Mbr()
        {
            var b = new byte[Bps];
            BitConverter.GetBytes(_serial).CopyTo(b, 440);
            var p = 446;
            b[p + 1] = 0xFE; b[p + 2] = 0xFF; b[p + 3] = 0xFF;
            b[p + 4] = 0x07;
            b[p + 5] = 0xFE; b[p + 6] = 0xFF; b[p + 7] = 0xFF;
            BitConverter.GetBytes((uint)PartitionLba).CopyTo(b, p + 8);
            BitConverter.GetBytes(checked((uint)_volumeLength)).CopyTo(b, p + 12);
            b[510] = 0x55; b[511] = 0xAA;
            return b;
        }

        private byte[] BootRegion(uint volumeSerial)
        {
            var r = new byte[12 * Bps];
            r[0] = 0xEB; r[1] = 0x76; r[2] = 0x90;
            Encoding.ASCII.GetBytes("EXFAT   ").CopyTo(r, 3);
            BitConverter.GetBytes((ulong)PartitionLba).CopyTo(r, 64);
            BitConverter.GetBytes((ulong)_volumeLength).CopyTo(r, 72);
            BitConverter.GetBytes(FatOffset).CopyTo(r, 80);
            BitConverter.GetBytes(_fatLength).CopyTo(r, 84);
            BitConverter.GetBytes(_heapOffset).CopyTo(r, 88);
            BitConverter.GetBytes(_clusterCount).CopyTo(r, 92);
            BitConverter.GetBytes(_rootFirst).CopyTo(r, 96);
            BitConverter.GetBytes(volumeSerial).CopyTo(r, 100);
            BitConverter.GetBytes((ushort)0x0100).CopyTo(r, 104);
            r[108] = BpsShift;
            r[109] = SpcShift;
            r[110] = 1;
            r[111] = 0x80;
            r[112] = 100;
            for (int i = 120; i < 510; i++) r[i] = 0xF4;
            r[510] = 0x55; r[511] = 0xAA;
            for (int s = 1; s <= 8; s++) { r[s * Bps + 510] = 0x55; r[s * Bps + 511] = 0xAA; }
            uint sum = 0;
            for (int i = 0; i < 11 * Bps; i++)
            {
                if (i == 106 || i == 107 || i == 112) continue;
                sum = ((sum & 1) != 0 ? 0x80000000 : 0) + (sum >> 1) + r[i];
            }
            for (int i = 0; i < Bps / 4; i++) BitConverter.GetBytes(sum).CopyTo(r, 11 * Bps + i * 4);
            return r;
        }
    }
}
