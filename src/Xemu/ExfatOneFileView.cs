// A disc for xemu, seen by Windows as a hard disk holding one exFAT volume that holds ONE file - the game's XISO, whose bytes
// are read where they are: a redump's game partition (its bytes from 0x18300000 on), a CSO, CCI or CHD read through its
// container. Nothing copied.
//
// WHY A FILE ON A VOLUME, not the raw disk (measured 04/10): xemu opens -dvd_path with CreateFile, and a \\.\PhysicalDriveN is
// "access denied" to a process that is not elevated - which xemu, started by LaunchBox, is not. A file on a volume with a
// letter opens for anyone. exFAT, not the FAT32 of the Cxbx view (XisoFatView): a FAT32 file stops at 4 GB, an Xbox disc
// goes to 7.
//
// THE LAYOUT, made here, in memory but for the file: the MBR (one partition at 1 MB, type 07), the boot region and its
// backup, the FAT (computed from the runs when asked, never stored), then the cluster heap, starting on a 1 MB boundary of
// the disk. Clusters of 64 KB. Cluster 2 is the file's first: the file is the heap's start, so the disk reads it aligned as
// it is laid. After its last cluster, the allocation bitmap (every cluster in use), the up-case table (uncompressed, ASCII
// letters, every other character itself) and the root directory: the label, the bitmap's and the table's entries, the file's.
// The file is contiguous (NoFatChain) and read-only. Checksums as the exFAT specification gives them (boot region, up-case
// table, entry set, name hash).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu
{
    internal sealed class ExfatOneFileView
    {
        private const int Bps = 512, BpsShift = 9;
        private const int SpcShift = 7, Spc = 1 << SpcShift, ClusterBytes = Bps << SpcShift;   // 64 KB
        private const long PartitionLba = 2048;                     // 1 MB
        private const uint FatOffset = 128;                         // sectors, from the volume's start
        private const uint Eoc = 0xFFFFFFFF;

        public long FileBase { get; }
        public long FileLength { get; }
        public string FileName { get; }
        public string Label { get; }
        public long Length { get; }

        private readonly long _fileClusters;
        private readonly uint _fatLength, _heapOffset, _clusterCount, _bitmapFirst, _upcaseFirst, _rootFirst;
        private readonly long _volumeLength, _fatStart, _heapStart;                 // bytes, from the disk's start
        private readonly uint[] _runFirst, _runLast;
        private readonly Dictionary<long, byte[]> _meta = new Dictionary<long, byte[]>();
        private readonly byte[] _mbr, _bootRegion;
        private readonly uint _serial;

        /// <param name="fileBase">where the file's bytes start in the stream Read is given (a redump's game partition)</param>
        /// <param name="fileLength">how many bytes the file has from there</param>
        public ExfatOneFileView(long fileBase, long fileLength, string fileName = "game.iso", string label = "XBOXDISC")
        {
            if (fileLength <= 0) throw new ArgumentException("the file is empty");
            FileBase = fileBase;
            FileLength = fileLength;
            FileName = Name(fileName);
            Label = new string((label ?? "XBOXDISC").Where(c => c >= 32 && c < 127).Take(11).ToArray());
            // A new disk signature and volume serial at every attach - XisoFatView's lesson (03/10): the same disc attached twice
            // with one signature, and Windows took the second disk offline.
            var g = Guid.NewGuid().ToByteArray();
            _serial = BitConverter.ToUInt32(g, 0) | 1;
            uint volumeSerial = BitConverter.ToUInt32(g, 4) | 1;

            var upcase = Upcase();
            var bitmapLength = 0L;
            _fileClusters = (fileLength + ClusterBytes - 1) / ClusterBytes;
            long upcaseClusters = (upcase.Length + ClusterBytes - 1) / ClusterBytes, rootClusters = 1, bitmapClusters = 1;
            // The bitmap covers itself: grown until it does.
            for (; ; )
            {
                long count = _fileClusters + bitmapClusters + upcaseClusters + rootClusters;
                bitmapLength = (count + 7) / 8;
                long need = (bitmapLength + ClusterBytes - 1) / ClusterBytes;
                if (need <= bitmapClusters) break;
                bitmapClusters = need;
            }
            _clusterCount = checked((uint)(_fileClusters + bitmapClusters + upcaseClusters + rootClusters));
            _bitmapFirst = checked((uint)(2 + _fileClusters));
            _upcaseFirst = checked((uint)(_bitmapFirst + bitmapClusters));
            _rootFirst = checked((uint)(_upcaseFirst + upcaseClusters));

            _fatLength = (uint)(((_clusterCount + 2L) * 4 + Bps - 1) / Bps);
            // The heap on a 1 MB boundary of the DISK - the partition is at 1 MB, so of the volume too.
            _heapOffset = (uint)((FatOffset + _fatLength + 2047) / 2048 * 2048);
            _volumeLength = _heapOffset + (long)_clusterCount * Spc;
            _fatStart = (PartitionLba + FatOffset) * Bps;
            _heapStart = (PartitionLba + _heapOffset) * Bps;
            Length = (PartitionLba + _volumeLength) * Bps;

            _runFirst = new[] { 2u, _bitmapFirst, _upcaseFirst, _rootFirst };
            _runLast = new[] { _bitmapFirst - 1, _upcaseFirst - 1, _rootFirst - 1, (uint)(_rootFirst + rootClusters - 1) };

            // The bitmap: clusters 2 .. 2+count-1, every one in use.
            var bitmap = new byte[bitmapClusters * ClusterBytes];
            for (long c = 0; c < _clusterCount; c++) bitmap[c / 8] |= (byte)(1 << (int)(c % 8));
            Place(_bitmapFirst, bitmap);
            var table = new byte[upcaseClusters * ClusterBytes];
            upcase.CopyTo(table, 0);
            Place(_upcaseFirst, table);
            Place(_rootFirst, Root(bitmapLength, upcase));

            _mbr = Mbr();
            _bootRegion = BootRegion(volumeSerial);
        }

        private void Place(uint first, byte[] bytes)
        {
            for (int i = 0; i * ClusterBytes < bytes.Length; i++)
            {
                var c = new byte[ClusterBytes];
                Array.Copy(bytes, (long)i * ClusterBytes, c, 0, Math.Min(ClusterBytes, bytes.Length - (long)i * ClusterBytes));
                _meta[first + i] = c;
            }
        }

        /// <summary>The disk's bytes at <paramref name="offset"/>: the structures where they are, the file's bytes from
        /// <paramref name="source"/> (from <see cref="FileBase"/> on), zeros elsewhere.</summary>
        public int Read(Stream source, long offset, byte[] buffer, int count)
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
                    if (at >= _fatStart + fatBytes)
                    {
                        n = (int)Math.Min(count - done, _heapStart - at);
                        Array.Clear(buffer, done, n);
                    }
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
                    long c = 2 + (at - _heapStart) / ClusterBytes;
                    int inCluster = (int)((at - _heapStart) % ClusterBytes);
                    if (c < 2 + _fileClusters)
                    {
                        // The file's clusters - read in one go, up to its last cluster; zeros past its end.
                        long fileAt = (c - 2) * ClusterBytes + inCluster;
                        long end = Math.Min(_fileClusters * ClusterBytes, fileAt + (count - done));
                        n = (int)(end - fileAt);
                        int want = (int)Math.Max(0, Math.Min(n, FileLength - fileAt)), got = 0, r;
                        source.Seek(FileBase + fileAt, SeekOrigin.Begin);
                        while (got < want && (r = source.Read(buffer, done + got, want - got)) > 0) got += r;
                        if (got < n) Array.Clear(buffer, done + got, n - got);
                    }
                    else
                    {
                        n = Math.Min(count - done, ClusterBytes - inCluster);
                        if (_meta.TryGetValue(c, out var d)) Array.Copy(d, inCluster, buffer, done, n); else Array.Clear(buffer, done, n);
                    }
                }
                done += n;
            }
            return done;
        }

        /// <summary>FAT entry <paramref name="cluster"/>: the next cluster of its run, the run's end, or free.</summary>
        private uint Next(long cluster)
        {
            if (cluster == 0) return 0xFFFFFFF8;                    // media descriptor
            if (cluster == 1) return Eoc;
            if (cluster > uint.MaxValue) return 0;
            for (int i = 0; i < _runFirst.Length; i++)
                if (cluster >= _runFirst[i] && cluster <= _runLast[i]) return cluster == _runLast[i] ? Eoc : (uint)cluster + 1;
            return 0;
        }

        // ── the root directory ───────────────────────────────────────────────

        private byte[] Root(long bitmapLength, byte[] upcase)
        {
            var root = new List<byte>();
            var label = new byte[32];
            label[0] = 0x83;
            label[1] = (byte)Label.Length;
            Encoding.Unicode.GetBytes(Label).CopyTo(label, 2);
            root.AddRange(label);

            var bm = new byte[32];
            bm[0] = 0x81;
            BitConverter.GetBytes(_bitmapFirst).CopyTo(bm, 20);
            BitConverter.GetBytes((ulong)bitmapLength).CopyTo(bm, 24);
            root.AddRange(bm);

            var up = new byte[32];
            up[0] = 0x82;
            BitConverter.GetBytes(TableChecksum(upcase)).CopyTo(up, 4);
            BitConverter.GetBytes(_upcaseFirst).CopyTo(up, 20);
            BitConverter.GetBytes((ulong)upcase.Length).CopyTo(up, 24);
            root.AddRange(up);

            root.AddRange(FileSet());
            return root.ToArray();
        }

        private byte[] FileSet()
        {
            int nameEntries = (FileName.Length + 14) / 15;
            var set = new byte[32 * (2 + nameEntries)];
            set[0] = 0x85;
            set[1] = (byte)(1 + nameEntries);
            BitConverter.GetBytes((ushort)0x21).CopyTo(set, 4);     // read-only, archive
            // 15/11/2001 00:00, the Xbox's launch: any fixed date does.
            uint stamp = (uint)((2001 - 1980) << 25 | 11 << 21 | 15 << 16);
            BitConverter.GetBytes(stamp).CopyTo(set, 8);
            BitConverter.GetBytes(stamp).CopyTo(set, 12);
            BitConverter.GetBytes(stamp).CopyTo(set, 16);

            int s = 32;
            set[s] = 0xC0;
            set[s + 1] = 0x03;                                      // allocation possible, no FAT chain: contiguous
            set[s + 3] = (byte)FileName.Length;
            BitConverter.GetBytes(NameHash(FileName)).CopyTo(set, s + 4);
            BitConverter.GetBytes((ulong)FileLength).CopyTo(set, s + 8);    // valid data length
            BitConverter.GetBytes(2u).CopyTo(set, s + 20);
            BitConverter.GetBytes((ulong)FileLength).CopyTo(set, s + 24);

            for (int i = 0; i < nameEntries; i++)
            {
                int e = 64 + 32 * i;
                set[e] = 0xC1;
                var part = FileName.Substring(i * 15, Math.Min(15, FileName.Length - i * 15));
                Encoding.Unicode.GetBytes(part).CopyTo(set, e + 2);
            }
            BitConverter.GetBytes(SetChecksum(set)).CopyTo(set, 2);
            return set;
        }

        /// <summary>A name the volume can hold and the up-case table here knows: printable ASCII, none of \ / : * ? " &lt; &gt; |,
        /// at most 255 characters.</summary>
        private static string Name(string name)
        {
            var s = new string((name ?? "").Select(c => c < 32 || c > 126 || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (s.Length == 0) s = "game.iso";
            return s.Length > 255 ? s.Substring(0, 255) : s;
        }

        // ── checksums (exFAT specification, 6.3.3, 7.2.2, 6.3.4, 7.6.4) ─────

        internal static ushort SetChecksum(byte[] set)
        {
            ushort sum = 0;
            for (int i = 0; i < set.Length; i++)
            {
                if (i == 2 || i == 3) continue;
                sum = (ushort)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + set[i]);
            }
            return sum;
        }

        internal static ushort NameHash(string name)
        {
            ushort hash = 0;
            foreach (var c in name.ToUpperInvariant())
            {
                hash = (ushort)(((hash & 1) != 0 ? 0x8000 : 0) + (hash >> 1) + (c & 0xFF));
                hash = (ushort)(((hash & 1) != 0 ? 0x8000 : 0) + (hash >> 1) + (c >> 8));
            }
            return hash;
        }

        internal static uint TableChecksum(byte[] table)
        {
            uint sum = 0;
            foreach (var b in table) sum = ((sum & 1) != 0 ? 0x80000000 : 0) + (sum >> 1) + b;
            return sum;
        }

        /// <summary>Every character itself but a-z, upper-cased: 65536 entries, uncompressed.</summary>
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

        // ── the disk's first sectors ─────────────────────────────────────────

        private byte[] Mbr()
        {
            var b = new byte[Bps];
            BitConverter.GetBytes(_serial).CopyTo(b, 440);
            var p = 446;
            b[p + 1] = 0xFE; b[p + 2] = 0xFF; b[p + 3] = 0xFF;      // CHS: beyond, use the LBA
            b[p + 4] = 0x07;                                         // exFAT
            b[p + 5] = 0xFE; b[p + 6] = 0xFF; b[p + 7] = 0xFF;
            BitConverter.GetBytes((uint)PartitionLba).CopyTo(b, p + 8);
            BitConverter.GetBytes(checked((uint)_volumeLength)).CopyTo(b, p + 12);
            b[510] = 0x55; b[511] = 0xAA;
            return b;
        }

        /// <summary>The 12 sectors of the boot region: the boot sector, 8 extended boot sectors, the OEM parameters, a reserved
        /// sector and the checksum sector.</summary>
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
            BitConverter.GetBytes((ushort)0x0100).CopyTo(r, 104);  // revision 1.00
            r[108] = BpsShift;
            r[109] = SpcShift;
            r[110] = 1;                                             // one FAT
            r[111] = 0x80;
            r[112] = 100;                                           // percent in use: all of it
            for (int i = 120; i < 510; i++) r[i] = 0xF4;            // boot code: halt
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
