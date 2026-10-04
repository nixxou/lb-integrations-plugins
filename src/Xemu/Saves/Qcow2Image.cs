// A qcow2 disk read through its backing chain, and a new one written from a set of clusters - the two halves of a game's
// console as the saves see it (XemuSaveStore).
//
// READ: version 2 or 3, any cluster size, L1 / L2 tables; a cluster the image does not hold is its backing file's (a qcow2
// again, or raw), zeros past the chain. The v3 "zero" flag reads zeros; a compressed cluster (zlib, raw deflate, qcow2's
// one compression unless the header says zstd) is inflated. Refused: encryption, an external data file, extended L2
// entries, zstd - none of which xemu or its dashboard disk use (measured 04/10: base.qcow2 and the game disks are plain v3,
// 64 KB clusters, no incompatible feature).
//
// WRITE (Build): a qcow2 v3 whose backing file is given, holding exactly the clusters passed - header and backing format
// extension, the backing name, a refcount table and its blocks (16-bit refcounts, every cluster counted once), the L1 table,
// the L2 tables, then the data. The same layout Qcow2Overlay makes, grown to hold data. Written to a temporary file and
// moved into place, so a cut write never leaves half a console.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu.Saves
{
    /// <summary>A disk's bytes, read at any offset.</summary>
    internal interface IVirtualDisk : IDisposable
    {
        long Length { get; }
        void Read(long offset, byte[] buffer, int at, int count);
    }

    internal sealed class RawDisk : IVirtualDisk
    {
        private readonly FileStream _f;
        public RawDisk(string path) { _f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
        public long Length => _f.Length;
        public void Read(long offset, byte[] buffer, int at, int count)
        {
            Array.Clear(buffer, at, count);
            if (offset >= _f.Length) return;
            _f.Seek(offset, SeekOrigin.Begin);
            int got = 0, n;
            while (got < count && (n = _f.Read(buffer, at + got, count - got)) > 0) got += n;
        }
        public void Dispose() => _f.Dispose();
    }

    internal sealed class Qcow2Image : IVirtualDisk
    {
        private const uint Magic = 0x514649FB;
        private const ulong OffsetMask = 0x00FFFFFFFFFFFE00UL;
        private readonly FileStream _f;
        private readonly IVirtualDisk _backing;
        private readonly int _clusterBits, _l2Bits;
        private readonly long _clusterSize;
        private readonly ulong[] _l1;
        private readonly Dictionary<long, ulong[]> _l2 = new Dictionary<long, ulong[]>();
        private readonly Dictionary<long, byte[]> _inflated = new Dictionary<long, byte[]>();

        public long Length { get; }
        public string Path { get; }
        public string BackingPath { get; }
        public int ClusterBits => _clusterBits;
        public long ClusterSize => _clusterSize;

        public static bool IsQcow2(string path)
        {
            try { using var f = File.OpenRead(path); var h = new byte[4]; return f.Read(h, 0, 4) == 4 && BE32(h, 0) == Magic; }
            catch { return false; }
        }

        public static IVirtualDisk Open(string path) => IsQcow2(path) ? new Qcow2Image(path) : (IVirtualDisk)new RawDisk(path);

        private Qcow2Image(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
            _f = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            try
            {
                var h = new byte[112];
                _f.Read(h, 0, h.Length);
                uint version = BE32(h, 4);
                if (BE32(h, 0) != Magic || version < 2 || version > 3) throw new InvalidDataException("not a qcow2 v2/v3: " + Path);
                _clusterBits = (int)BE32(h, 20);
                if (_clusterBits < 9 || _clusterBits > 21) throw new InvalidDataException("cluster_bits " + _clusterBits);
                _clusterSize = 1L << _clusterBits;
                _l2Bits = _clusterBits - 3;
                Length = (long)BE64(h, 24);
                if (BE32(h, 32) != 0) throw new NotSupportedException("an encrypted qcow2");
                if (version == 3)
                {
                    ulong incompatible = BE64(h, 72);
                    // Read through: dirty (0), corrupt (1), compression type (3). Not: external data file (2), extended L2 (4), others.
                    if ((incompatible & 4) != 0) throw new NotSupportedException("a qcow2 with an external data file");
                    if ((incompatible & 16) != 0) throw new NotSupportedException("a qcow2 with extended L2 entries");
                    if ((incompatible & ~0x0BUL) != 0) throw new NotSupportedException("qcow2 incompatible features 0x" + incompatible.ToString("X"));
                    if ((incompatible & 8) != 0 && BE32(h, 100) > 104 && h[104] != 0) throw new NotSupportedException("a zstd-compressed qcow2");
                }
                uint l1Size = BE32(h, 36);
                long l1Offset = (long)BE64(h, 40);
                _l1 = new ulong[l1Size];
                var l1 = ReadAt(l1Offset, (int)l1Size * 8);
                for (int i = 0; i < l1Size; i++) _l1[i] = BE64(l1, i * 8);

                long backingOffset = (long)BE64(h, 8);
                int backingSize = (int)BE32(h, 16);
                if (backingOffset != 0 && backingSize > 0)
                {
                    var name = Encoding.UTF8.GetString(ReadAt(backingOffset, backingSize));
                    BackingPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), name.Replace('/', '\\')));
                    if (!File.Exists(BackingPath)) throw new FileNotFoundException("the backing file of " + Path + " is missing", BackingPath);
                    _backing = Open(BackingPath);
                }
            }
            catch { _f.Dispose(); throw; }
        }

        /// <summary>Does this image itself hold the cluster at <paramref name="clusterIndex"/> (data, compressed or zero)? A
        /// cluster it does not hold reads from its backing file.</summary>
        public bool Holds(long clusterIndex) => L2Entry(clusterIndex) is ulong e && ((e & OffsetMask) != 0 || (e & 1) != 0 || (e & (1UL << 62)) != 0);

        /// <summary>The indices of the clusters this image itself holds.</summary>
        public IEnumerable<long> HeldClusters()
        {
            long perL2 = 1L << _l2Bits;
            for (long i = 0; i < _l1.Length; i++)
            {
                if ((_l1[i] & OffsetMask) == 0) continue;
                for (long j = 0; j < perL2; j++)
                {
                    long c = i * perL2 + j;
                    if (c * _clusterSize >= Length) yield break;
                    if (Holds(c)) yield return c;
                }
            }
        }

        public void Read(long offset, byte[] buffer, int at, int count)
        {
            while (count > 0)
            {
                long cluster = offset >> _clusterBits;
                int inCluster = (int)(offset & (_clusterSize - 1));
                int n = (int)Math.Min(count, _clusterSize - inCluster);
                if (offset >= Length) { Array.Clear(buffer, at, count); return; }
                n = (int)Math.Min(n, Length - offset);
                ReadCluster(cluster, inCluster, buffer, at, n);
                offset += n; at += n; count -= n;
            }
        }

        private void ReadCluster(long cluster, int inCluster, byte[] buffer, int at, int n)
        {
            var e = L2Entry(cluster);
            if (e is ulong entry)
            {
                if ((entry & (1UL << 62)) != 0)
                {
                    var data = Inflate(cluster, entry);
                    Array.Copy(data, inCluster, buffer, at, n);
                    return;
                }
                if ((entry & 1) != 0) { Array.Clear(buffer, at, n); return; }
                long host = (long)(entry & OffsetMask);
                if (host != 0)
                {
                    var b = ReadAt(host + inCluster, n);
                    Array.Copy(b, 0, buffer, at, n);
                    return;
                }
            }
            if (_backing != null && (cluster << _clusterBits) + inCluster < _backing.Length) _backing.Read((cluster << _clusterBits) + inCluster, buffer, at, n);
            else Array.Clear(buffer, at, n);
        }

        private ulong? L2Entry(long cluster)
        {
            long l1Index = cluster >> _l2Bits;
            if (l1Index >= _l1.Length) return null;
            long l2Offset = (long)(_l1[l1Index] & OffsetMask);
            if (l2Offset == 0) return null;
            if (!_l2.TryGetValue(l2Offset, out var table))
            {
                var raw = ReadAt(l2Offset, (int)_clusterSize);
                table = new ulong[_clusterSize / 8];
                for (int i = 0; i < table.Length; i++) table[i] = BE64(raw, i * 8);
                _l2[l2Offset] = table;
            }
            return table[cluster & ((1L << _l2Bits) - 1)];
        }

        private byte[] Inflate(long cluster, ulong entry)
        {
            if (_inflated.TryGetValue(cluster, out var done)) return done;
            int x = 62 - (_clusterBits - 8);
            long host = (long)(entry & ((1UL << x) - 1));
            long sectors = (long)((entry >> x) & ((1UL << (_clusterBits - 8)) - 1)) + 1;
            int length = (int)(sectors * 512 - (host & 511));
            var compressed = ReadAt(host, length);
            var data = new byte[_clusterSize];
            using (var z = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress))
            {
                int got = 0, r;
                while (got < data.Length && (r = z.Read(data, got, data.Length - got)) > 0) got += r;
            }
            if (_inflated.Count > 64) _inflated.Clear();
            _inflated[cluster] = data;
            return data;
        }

        private byte[] ReadAt(long offset, int count)
        {
            var b = new byte[count];
            _f.Seek(offset, SeekOrigin.Begin);
            int got = 0, n;
            while (got < count && (n = _f.Read(b, got, count - got)) > 0) got += n;
            return b;
        }

        public void Dispose() { _f.Dispose(); _backing?.Dispose(); }

        // ── write ────────────────────────────────────────────────────────────

        /// <summary>A new qcow2 at <paramref name="path"/> over <paramref name="backing"/> (a relative name is written, as
        /// Qcow2Overlay does), <paramref name="virtualSize"/> bytes, 2^<paramref name="clusterBits"/> clusters, holding the
        /// clusters <paramref name="clusters"/> gives by index - each <c>2^clusterBits</c> bytes. Through a temporary file.</summary>
        public static void Build(string path, string backing, long virtualSize, int clusterBits, IReadOnlyDictionary<long, byte[]> clusters)
        {
            long cs = 1L << clusterBits;
            int l2Bits = clusterBits - 3;
            long perL2 = 1L << l2Bits;
            long totalClusters = (virtualSize + cs - 1) / cs;
            long l1Size = (totalClusters + perL2 - 1) / perL2;
            if (l1Size * 8 > cs) throw new NotSupportedException("the disk is too big for a one-cluster L1 table");
            var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            var name = System.IO.Path.GetRelativePath(dir, System.IO.Path.GetFullPath(backing)).Replace('\\', '/');
            var nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > 1023) throw new NotSupportedException("the backing file's path is too long");

            // Layout: 0 header, 1 L1, then the L2 tables, then the data, then the refcount table and its blocks.
            var data = clusters.Where(kv => kv.Key >= 0 && kv.Key < totalClusters).OrderBy(kv => kv.Key).ToList();
            var l2Needed = data.Select(kv => kv.Key / perL2).Distinct().OrderBy(i => i).ToList();
            long next = 2;
            var l2At = new Dictionary<long, long>();
            foreach (var i in l2Needed) l2At[i] = next++;
            var dataAt = new Dictionary<long, long>();
            foreach (var kv in data) dataAt[kv.Key] = next++;
            // The refcount structures cover every cluster of the file, themselves included: grown until they do.
            long refEntriesPerBlock = cs / 2;
            long blocks = 1, tableClusters = 1;
            for (; ; )
            {
                long fileClusters = next + tableClusters + blocks;
                long needBlocks = (fileClusters + refEntriesPerBlock - 1) / refEntriesPerBlock;
                long needTable = (needBlocks * 8 + cs - 1) / cs;
                if (needBlocks <= blocks && needTable <= tableClusters) break;
                blocks = Math.Max(blocks, needBlocks); tableClusters = Math.Max(tableClusters, needTable);
            }
            long refTableAt = next, refBlocksAt = next + tableClusters;
            long fileClusterCount = refBlocksAt + blocks;

            var tmp = path + ".lbip-tmp";
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                f.SetLength(fileClusterCount * cs);
                var h = new byte[cs];
                PutBE32(h, 0, Magic); PutBE32(h, 4, 3);
                PutBE32(h, 20, (uint)clusterBits);
                PutBE64(h, 24, (ulong)virtualSize);
                PutBE32(h, 36, (uint)l1Size);
                PutBE64(h, 40, (ulong)(1 * cs));
                PutBE64(h, 48, (ulong)(refTableAt * cs));
                PutBE32(h, 56, (uint)tableClusters);
                PutBE32(h, 96, 4);                               // refcount_order: 16-bit
                PutBE32(h, 100, 112);
                int at = 112;
                var fmt = Encoding.ASCII.GetBytes("qcow2");
                PutBE32(h, at, 0xE2792ACA); PutBE32(h, at + 4, (uint)fmt.Length); Buffer.BlockCopy(fmt, 0, h, at + 8, fmt.Length);
                at += 8 + ((fmt.Length + 7) / 8) * 8;
                at += 8;                                         // end of extensions: zeros
                Buffer.BlockCopy(nameBytes, 0, h, at, nameBytes.Length);
                PutBE64(h, 8, (ulong)at);
                PutBE32(h, 16, (uint)nameBytes.Length);
                Write(f, 0, h);

                var l1 = new byte[cs];
                foreach (var i in l2Needed) PutBE64(l1, (int)(i * 8), (ulong)(l2At[i] * cs) | (1UL << 63));
                Write(f, 1 * cs, l1);
                foreach (var i in l2Needed)
                {
                    var l2 = new byte[cs];
                    foreach (var kv in data.Where(kv => kv.Key / perL2 == i))
                        PutBE64(l2, (int)((kv.Key % perL2) * 8), (ulong)(dataAt[kv.Key] * cs) | (1UL << 63));
                    Write(f, l2At[i] * cs, l2);
                }
                foreach (var kv in data)
                {
                    if (kv.Value.Length != cs) throw new ArgumentException("cluster " + kv.Key + " is " + kv.Value.Length + " bytes, not " + cs);
                    Write(f, dataAt[kv.Key] * cs, kv.Value);
                }
                var table = new byte[tableClusters * cs];
                for (long b = 0; b < blocks; b++) PutBE64(table, (int)(b * 8), (ulong)((refBlocksAt + b) * cs));
                Write(f, refTableAt * cs, table);
                var counts = new byte[blocks * cs];
                for (long c = 0; c < fileClusterCount; c++) { counts[c * 2] = 0; counts[c * 2 + 1] = 1; }
                Write(f, refBlocksAt * cs, counts);
            }
            File.Move(tmp, path, overwrite: true);
        }

        private static void Write(FileStream f, long at, byte[] b) { f.Seek(at, SeekOrigin.Begin); f.Write(b, 0, b.Length); }

        internal static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        internal static ulong BE64(byte[] b, int o) => (ulong)BE32(b, o) << 32 | BE32(b, o + 4);
        private static void PutBE32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static void PutBE64(byte[] b, int o, ulong v) { PutBE32(b, o, (uint)(v >> 32)); PutBE32(b, o + 4, (uint)v); }
    }
}
