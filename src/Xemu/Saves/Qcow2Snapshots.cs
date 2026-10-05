// xemu's savestates, as a qcow2 holds them - and a game's console written again WITH them (Mehdi, 05/10).
//
// xemu's snapshots are QEMU internal snapshots (ui/xemu-snapshots.c: save_snapshot, load_snapshot) stored in the HDD image
// xemu was started with: the game's console, hdd\games\<title id>.qcow2. What one is, read in QEMU (block/qcow2.h,
// block/qcow2-snapshot.c, block/qcow2-refcount.c of xemu v0.8.136):
//   the snapshot table      header 60 nb_snapshots, 64 snapshots_offset; per entry, 8-byte aligned: l1_table_offset u64,
//                           l1_size u32, id_str_size u16, name_size u16, date_sec u32, date_nsec u32, vm_clock_nsec u64,
//                           vm_state_size u32, extra_data_size u32 - then the extra data (vm_state_size_large u64,
//                           disk_size u64, icount u64, and what a newer QEMU adds), the id, the name
//   its own L1 table        a copy of the active one when it was taken - the disk as it was - plus the entries past the
//                           disk's end, from l1_vm_state_index = size_to_l1(disk size): the VM state (RAM, devices, xemu's
//                           thumbnail and disc path) is stored there, in the snapshot's virtual space
//   refcounts               qemu-img check counts every L1 walk: an L2 table once per L1 pointing at it, a data cluster
//                           once per L2 table entry pointing at it - a cluster shared by the disk and two snapshots counts 3.
//                           QCOW_OFLAG_COPIED (bit 63) on an ACTIVE entry says its refcount is exactly 1 (QEMU then writes
//                           it in place): set only there, and only then.
//
// WHY A REBUILD: putting a save into a console (XemuSaveStore.Insert / Remove) used to write a new qcow2 of the active table
// alone - every savestate of the game gone. Qcow2Rebuild writes it again from nothing - the active table with the clusters
// changed, and every snapshot's table as it was - counting every refcount itself (none is patched), the data the tables share
// still shared. It is checked before it replaces anything: each table read back cluster by cluster against what it must
// hold, every refcount counted again from the tables and compared, the COPIED flags; a single difference and the console is
// left as it was.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu.Saves
{
    /// <summary>A snapshot of a qcow2, as its table entry holds it.</summary>
    internal sealed class Qcow2Snapshot
    {
        public string Id, Name;
        public uint DateSec, DateNsec, VmStateSize32;
        public ulong VmClockNsec;
        public byte[] Extra = new byte[0];       // the extra data as it is - written back the same
        public ulong[] L1 = new ulong[0];
        public ulong VmStateSize => Extra.Length >= 8 ? Qcow2Image.BE64(Extra, 0) : VmStateSize32;
        public DateTime Date => DateTimeOffset.FromUnixTimeSeconds(DateSec).LocalDateTime;
    }

    internal sealed partial class Qcow2Image
    {
        public IReadOnlyList<Qcow2Snapshot> Snapshots { get; private set; } = new List<Qcow2Snapshot>();
        internal ulong[] ActiveL1 => _l1;
        internal int L2Bits => _l2Bits;

        /// <summary>The number of snapshots of a qcow2 - from its header alone. 0 for anything that is not one.</summary>
        public static int SnapshotCount(string path)
        {
            try
            {
                using var f = File.OpenRead(path);
                var h = new byte[72];
                if (f.Read(h, 0, h.Length) != h.Length || BE32(h, 0) != Magic) return 0;
                return (int)BE32(h, 60);
            }
            catch { return 0; }
        }

        private List<Qcow2Snapshot> ReadSnapshotTable(uint count, long offset)
        {
            var list = new List<Qcow2Snapshot>();
            if (count == 0) return list;
            if (count > 65536) throw new InvalidDataException("a snapshot table of " + count + " entries");
            for (int i = 0; i < count; i++)
            {
                offset = (offset + 7) & ~7L;
                var h = ReadAt(offset, 40);
                offset += 40;
                var sn = new Qcow2Snapshot
                {
                    DateSec = BE32(h, 16), DateNsec = BE32(h, 20), VmClockNsec = BE64(h, 24), VmStateSize32 = BE32(h, 32),
                };
                long l1Offset = (long)BE64(h, 0);
                uint l1Size = BE32(h, 8);
                int idSize = (h[12] << 8) | h[13], nameSize = (h[14] << 8) | h[15];
                uint extraSize = BE32(h, 36);
                if (extraSize > 1024 || l1Size > (32u << 20) / 8) throw new InvalidDataException("snapshot table entry " + i + " is not one QEMU writes");
                sn.Extra = ReadAt(offset, (int)extraSize); offset += extraSize;
                sn.Id = Encoding.UTF8.GetString(ReadAt(offset, idSize)); offset += idSize;
                sn.Name = Encoding.UTF8.GetString(ReadAt(offset, nameSize)); offset += nameSize;
                var l1 = ReadAt(l1Offset, (int)l1Size * 8);
                sn.L1 = new ulong[l1Size];
                for (int j = 0; j < l1Size; j++) sn.L1[j] = BE64(l1, j * 8);
                list.Add(sn);
            }
            return list;
        }

        /// <summary>The picture xemu keeps in a snapshot's VM state - its thumbnail, a PNG (xemu-snapshots.c,
        /// xemu_snapshots_create_framebuffer_thumbnail_png) - found by its signature and cut at its IEND. Null when there is none.
        /// The VM state is read past the disk's end, where QEMU puts it (qcow2_vm_state_offset: the first L1 entry past the disk);
        /// the whole of it at worst - some 35 MB - so never on a window's thread.</summary>
        internal byte[] SnapshotThumbnail(Qcow2Snapshot s, int maxBytes = 4 << 20)
        {
            long perL1 = 1L << (_clusterBits + _l2Bits);
            long start = (Length + perL1 - 1) / perL1 * perL1;
            long end = start + (long)s.VmStateSize;
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            MemoryStream png = null;
            var tail = new byte[0];
            for (long off = start; off < end; off += _clusterSize)
            {
                var e = EntryIn(s.L1, off >> _clusterBits);
                var data = e == null || e.Value == 0 ? new byte[_clusterSize] : ClusterData(e.Value);
                if (png == null)
                {
                    // The signature, maybe across two clusters: looked for in the last bytes of one and this one.
                    var window = new byte[tail.Length + data.Length];
                    Buffer.BlockCopy(tail, 0, window, 0, tail.Length);
                    Buffer.BlockCopy(data, 0, window, tail.Length, data.Length);
                    int at = IndexOf(window, sig);
                    if (at < 0) { tail = window.AsSpan(window.Length - (sig.Length - 1)).ToArray(); continue; }
                    png = new MemoryStream();
                    png.Write(window, at, window.Length - at);
                }
                else png.Write(data, 0, data.Length);
                if (png.Length > maxBytes) return null;
                if (Cut(png.GetBuffer(), (int)png.Length) is int whole) return png.GetBuffer().AsSpan(0, whole).ToArray();
            }
            return null;
        }

        private static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = hay.AsSpan().IndexOf(needle[0]); i >= 0 && i <= hay.Length - needle.Length; )
            {
                if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
                int next = hay.AsSpan(i + 1).IndexOf(needle[0]);
                i = next < 0 ? -1 : i + 1 + next;
            }
            return -1;
        }

        /// <summary>The PNG's length once its IEND chunk is there, else null: its chunks walked (length, type, data, CRC).</summary>
        private static int? Cut(byte[] b, int length)
        {
            int pos = 8;
            while (pos + 12 <= length)
            {
                long chunk = BE32(b, pos);
                if (chunk > 64L << 20) return null;
                long next = pos + 12 + chunk;
                if (next > length) return null;
                if (b[pos + 4] == 'I' && b[pos + 5] == 'E' && b[pos + 6] == 'N' && b[pos + 7] == 'D') return (int)next;
                pos = (int)next;
            }
            return null;
        }

        /// <summary>A data cluster's bytes from its L2 entry: inflated when compressed, zeros for the zero flag.</summary>
        internal byte[] ClusterData(ulong entry)
        {
            if ((entry & (1UL << 62)) != 0) return (byte[])InflateEntry(entry).Clone();
            long host = (long)(entry & OffsetMask);
            if (host == 0) return new byte[_clusterSize];
            return ReadAt(host, (int)_clusterSize);
        }
    }

    /// <summary>What a table's virtual cluster is in a rebuilt image.</summary>
    internal readonly struct ClusterSource
    {
        public readonly Qcow2Image Image;   // with Entry: that image's cluster - else Data, else zero
        public readonly ulong Entry;
        public readonly byte[] Data;
        public readonly bool Zero;
        private ClusterSource(Qcow2Image image, ulong entry, byte[] data, bool zero) { Image = image; Entry = entry; Data = data; Zero = zero; }
        public static ClusterSource Of(Qcow2Image image, ulong entry) => new ClusterSource(image, entry, null, false);
        public static ClusterSource New(byte[] data) => new ClusterSource(null, 0, data, false);
        public static readonly ClusterSource Zeros = new ClusterSource(null, 0, null, true);
    }

    /// <summary>One L1 table of a rebuilt image - the active one, or a snapshot's - by virtual cluster.</summary>
    internal sealed class RebuildTable
    {
        public int L1Size;
        public readonly SortedDictionary<long, ClusterSource> Clusters = new SortedDictionary<long, ClusterSource>();
        public Qcow2Snapshot Snapshot;           // null: the active table

        /// <summary>The clusters an L1 table of <paramref name="image"/> holds, as they are (a shared one stays shared).</summary>
        public static RebuildTable From(Qcow2Image image, ulong[] l1, Qcow2Snapshot snapshot = null)
        {
            var t = new RebuildTable { L1Size = l1.Length, Snapshot = snapshot };
            const ulong mask = 0x00FFFFFFFFFFFE00UL;
            long perL2 = 1L << image.L2Bits;
            for (long i = 0; i < l1.Length; i++)
            {
                long l2 = (long)(l1[i] & mask);
                if (l2 == 0) continue;
                var table = image.L2Table(l2);
                for (long j = 0; j < perL2; j++)
                {
                    ulong e = table[j];
                    bool compressed = (e & (1UL << 62)) != 0;
                    if (!compressed && (e & 1) != 0) { t.Clusters[i * perL2 + j] = ClusterSource.Zeros; continue; }
                    if (compressed || (e & mask) != 0) t.Clusters[i * perL2 + j] = ClusterSource.Of(image, e & ~(1UL << 63));
                }
            }
            return t;
        }
    }

    internal static class Qcow2Rebuild
    {
        private const ulong Copied = 1UL << 63;

        /// <summary>A console written again over <paramref name="backing"/>: <paramref name="active"/>, and each of
        /// <paramref name="snapshots"/> with its table entry - into a temporary file beside <paramref name="path"/>, checked.
        /// Returns that file, for the caller to move into place once the source image is closed (Commit). Throws, nothing left,
        /// when the check finds anything - <paramref name="path"/> untouched.</summary>
        public static string Write(string path, string backing, long virtualSize, int clusterBits, RebuildTable active, IReadOnlyList<RebuildTable> snapshots)
        {
            long cs = 1L << clusterBits;
            int l2Bits = clusterBits - 3;
            long perL2 = 1L << l2Bits;
            long diskL1 = ((virtualSize + cs - 1) / cs + perL2 - 1) / perL2;
            active.L1Size = (int)Math.Max(active.L1Size, diskL1);
            var tables = new List<RebuildTable> { active };
            tables.AddRange(snapshots);
            foreach (var t in tables)
            {
                if (t.Clusters.Count > 0) t.L1Size = (int)Math.Max(t.L1Size, t.Clusters.Keys.Max() / perL2 + 1);
                foreach (var kv in t.Clusters)
                    if (kv.Value.Data != null && kv.Value.Data.Length != cs) throw new ArgumentException("cluster " + kv.Key + " is " + kv.Value.Data.Length + " bytes, not " + cs);
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            // No backing (a savestate's file): its tables reach only the clusters it holds.
            var nameBytes = backing == null ? new byte[0] : Encoding.UTF8.GetBytes(Path.GetRelativePath(dir, Path.GetFullPath(backing)).Replace('\\', '/'));
            if (nameBytes.Length > 1023) throw new NotSupportedException("the backing file's path is too long");

            // ── layout, in clusters: 0 the header, the L1 tables, the L2 tables, the data, the snapshot table, the refcounts ──
            long next = 1;
            var l1At = new long[tables.Count];
            for (int t = 0; t < tables.Count; t++) { l1At[t] = next; next += (tables[t].L1Size * 8 + cs - 1) / cs; }
            var l2At = new Dictionary<(int Table, long L1Index), long>();
            for (int t = 0; t < tables.Count; t++)
                foreach (var i in tables[t].Clusters.Keys.Select(c => c / perL2).Distinct().OrderBy(i => i)) l2At[(t, i)] = next++;
            // A source cluster shared by several tables is written once: keyed by its image and its L2 entry.
            var shared = new Dictionary<(Qcow2Image, ulong), long>();
            var dataAt = new Dictionary<(int Table, long Cluster), long>();
            var refs = new Dictionary<long, int>();
            for (int t = 0; t < tables.Count; t++)
                foreach (var kv in tables[t].Clusters)
                {
                    if (kv.Value.Zero) continue;
                    long at;
                    if (kv.Value.Image != null) { if (!shared.TryGetValue((kv.Value.Image, kv.Value.Entry), out at)) shared[(kv.Value.Image, kv.Value.Entry)] = at = next++; }
                    else at = next++;
                    dataAt[(t, kv.Key)] = at;
                    refs[at] = refs.TryGetValue(at, out var r) ? r + 1 : 1;
                }
            var snapTable = SnapshotTable(snapshots, l1At, cs);
            long snapAt = snapshots.Count > 0 ? next : 0;
            if (snapshots.Count > 0) next += (snapTable.Length + cs - 1) / cs;
            long refEntriesPerBlock = cs / 2, blocks = 1, tableClusters = 1;
            for (; ; )
            {
                long fileClusters = next + tableClusters + blocks;
                long needBlocks = (fileClusters + refEntriesPerBlock - 1) / refEntriesPerBlock;
                long needTable = (needBlocks * 8 + cs - 1) / cs;
                if (needBlocks <= blocks && needTable <= tableClusters) break;
                blocks = Math.Max(blocks, needBlocks); tableClusters = Math.Max(tableClusters, needTable);
            }
            long refTableAt = next, refBlocksAt = next + tableClusters, total = refBlocksAt + blocks;
            var counts = new int[total];
            for (long c = 0; c < total; c++) counts[c] = 1;              // header, tables, snapshot table, refcounts
            foreach (var kv in refs) counts[kv.Key] = kv.Value;
            if (counts.Any(c => c > 0xFFFF)) throw new NotSupportedException("a cluster shared by more than 65535 tables");

            var tmp = path + ".lbip-tmp";
            bool ok = false;
            try
            {
                using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    f.SetLength(total * cs);
                    var h = new byte[cs];
                    PutBE32(h, 0, 0x514649FB); PutBE32(h, 4, 3);
                    PutBE32(h, 20, (uint)clusterBits);
                    PutBE64(h, 24, (ulong)virtualSize);
                    PutBE32(h, 36, (uint)active.L1Size);
                    PutBE64(h, 40, (ulong)(l1At[0] * cs));
                    PutBE64(h, 48, (ulong)(refTableAt * cs));
                    PutBE32(h, 56, (uint)tableClusters);
                    PutBE32(h, 60, (uint)snapshots.Count);
                    PutBE64(h, 64, (ulong)(snapAt * cs));
                    PutBE32(h, 96, 4);                                   // refcount_order: 16-bit
                    PutBE32(h, 100, 112);
                    if (nameBytes.Length > 0)
                    {
                        int at = 112;
                        var fmt = Encoding.ASCII.GetBytes("qcow2");
                        PutBE32(h, at, 0xE2792ACA); PutBE32(h, at + 4, (uint)fmt.Length); Buffer.BlockCopy(fmt, 0, h, at + 8, fmt.Length);
                        at += 8 + ((fmt.Length + 7) / 8) * 8;
                        at += 8;                                         // end of extensions
                        Buffer.BlockCopy(nameBytes, 0, h, at, nameBytes.Length);
                        PutBE64(h, 8, (ulong)at);
                        PutBE32(h, 16, (uint)nameBytes.Length);
                    }
                    Put(f, 0, h);

                    for (int t = 0; t < tables.Count; t++)
                    {
                        bool isActive = t == 0;
                        var l1 = new byte[((tables[t].L1Size * 8 + cs - 1) / cs) * cs];
                        foreach (var kv in l2At.Where(k => k.Key.Table == t))
                            PutBE64(l1, (int)(kv.Key.L1Index * 8), (ulong)(kv.Value * cs) | (isActive ? Copied : 0));   // an L2 table is never shared
                        Put(f, l1At[t] * cs, l1);
                        foreach (var group in tables[t].Clusters.GroupBy(kv => kv.Key / perL2))
                        {
                            var l2 = new byte[cs];
                            foreach (var kv in group)
                            {
                                ulong e = kv.Value.Zero ? 1UL : (ulong)(dataAt[(t, kv.Key)] * cs);
                                if (!kv.Value.Zero && isActive && counts[dataAt[(t, kv.Key)]] == 1) e |= Copied;
                                PutBE64(l2, (int)((kv.Key % perL2) * 8), e);
                            }
                            Put(f, l2At[(t, group.Key)] * cs, l2);
                        }
                    }
                    var written = new HashSet<long>();
                    for (int t = 0; t < tables.Count; t++)
                        foreach (var kv in tables[t].Clusters)
                        {
                            if (kv.Value.Zero) continue;
                            long c = dataAt[(t, kv.Key)];
                            if (!written.Add(c)) continue;
                            Put(f, c * cs, kv.Value.Data ?? kv.Value.Image.ClusterData(kv.Value.Entry));
                        }
                    if (snapshots.Count > 0) Put(f, snapAt * cs, snapTable);
                    var table = new byte[tableClusters * cs];
                    for (long b = 0; b < blocks; b++) PutBE64(table, (int)(b * 8), (ulong)((refBlocksAt + b) * cs));
                    Put(f, refTableAt * cs, table);
                    var rc = new byte[blocks * cs];
                    for (long c = 0; c < total; c++) { rc[c * 2] = (byte)(counts[c] >> 8); rc[c * 2 + 1] = (byte)counts[c]; }
                    Put(f, refBlocksAt * cs, rc);
                }
                Check(tmp, tables);
                ok = true;
                return tmp;
            }
            finally { if (!ok) try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        /// <summary>The checked file of Write put in place of <paramref name="path"/>.</summary>
        public static void Commit(string tmp, string path) => File.Move(tmp, path, overwrite: true);

        /// <summary><paramref name="image"/>'s active table with <paramref name="changed"/> clusters over it, and its snapshots
        /// as they are - checked, into a temporary file (Write).</summary>
        public static string WithChanges(string path, Qcow2Image image, IReadOnlyDictionary<long, byte[]> changed)
        {
            var active = RebuildTable.From(image, image.ActiveL1);
            foreach (var kv in changed) active.Clusters[kv.Key] = ClusterSource.New(kv.Value);
            var snaps = image.Snapshots.Select(sn => RebuildTable.From(image, sn.L1, sn)).ToList();
            return Write(path, image.BackingPath, image.Length, image.ClusterBits, active, snaps);
        }

        /// <summary>The snapshot table: each entry as QEMU writes it, its extra data, id and name as they were, its L1 moved.</summary>
        private static byte[] SnapshotTable(IReadOnlyList<RebuildTable> snapshots, long[] l1At, long cs)
        {
            using var m = new MemoryStream();
            for (int i = 0; i < snapshots.Count; i++)
            {
                var sn = snapshots[i].Snapshot ?? throw new ArgumentException("a snapshot table without its entry");
                while (m.Length % 8 != 0) m.WriteByte(0);
                var id = Encoding.UTF8.GetBytes(sn.Id ?? ""); var name = Encoding.UTF8.GetBytes(sn.Name ?? "");
                var h = new byte[40];
                PutBE64(h, 0, (ulong)(l1At[i + 1] * cs));
                PutBE32(h, 8, (uint)snapshots[i].L1Size);
                h[12] = (byte)(id.Length >> 8); h[13] = (byte)id.Length; h[14] = (byte)(name.Length >> 8); h[15] = (byte)name.Length;
                PutBE32(h, 16, sn.DateSec); PutBE32(h, 20, sn.DateNsec); PutBE64(h, 24, sn.VmClockNsec);
                PutBE32(h, 32, sn.VmStateSize32); PutBE32(h, 36, (uint)sn.Extra.Length);
                m.Write(h, 0, h.Length); m.Write(sn.Extra, 0, sn.Extra.Length); m.Write(id, 0, id.Length); m.Write(name, 0, name.Length);
            }
            return m.ToArray();
        }

        /// <summary>The image just written, read back: every table holds exactly its clusters, byte for byte; every refcount is
        /// what the tables count; the active entries' COPIED flags are right. Throws on the first difference.</summary>
        private static void Check(string path, List<RebuildTable> tables)
        {
            using var img = (Qcow2Image)Qcow2Image.Open(path);
            if (img.Snapshots.Count != tables.Count - 1) throw new InvalidDataException("the rebuilt console has " + img.Snapshots.Count + " snapshots, not " + (tables.Count - 1));
            const ulong mask = 0x00FFFFFFFFFFFE00UL;
            long cs = img.ClusterSize, perL2 = 1L << img.L2Bits;
            var counted = new Dictionary<long, int>();
            void Count(long hostOffset, long bytes) { for (long c = hostOffset / cs; c < (hostOffset + bytes + cs - 1) / cs; c++) counted[c] = counted.TryGetValue(c, out var n) ? n + 1 : 1; }
            for (int t = 0; t < tables.Count; t++)
            {
                var l1 = t == 0 ? img.ActiveL1 : img.Snapshots[t - 1].L1;
                var want = tables[t];
                if (t > 0)
                {
                    var a = img.Snapshots[t - 1]; var b = want.Snapshot;
                    if (a.Id != b.Id || a.Name != b.Name || a.DateSec != b.DateSec || a.DateNsec != b.DateNsec || a.VmClockNsec != b.VmClockNsec
                        || a.VmStateSize32 != b.VmStateSize32 || !a.Extra.SequenceEqual(b.Extra))
                        throw new InvalidDataException("snapshot " + b.Name + ": its table entry changed");
                }
                var seen = new HashSet<long>();
                for (long i = 0; i < l1.Length; i++)
                {
                    long l2 = (long)(l1[i] & mask);
                    if (l2 == 0) continue;
                    if (t == 0 && (l1[i] & Copied) == 0) throw new InvalidDataException("an active L1 entry without its COPIED flag");
                    var table = img.L2Table(l2);
                    for (long j = 0; j < perL2; j++)
                    {
                        ulong e = table[j];
                        if (e == 0) continue;
                        long c = i * perL2 + j;
                        seen.Add(c);
                        if (!want.Clusters.TryGetValue(c, out var src)) throw new InvalidDataException("a cluster that should not be there (table " + t + ", cluster " + c + ")");
                        if (src.Zero) { if (e != 1) throw new InvalidDataException("a zero cluster written otherwise (table " + t + ", cluster " + c + ")"); continue; }
                        var got = img.ClusterData(e);
                        var expected = src.Data ?? src.Image.ClusterData(src.Entry);
                        if (!got.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("a cluster that reads otherwise (table " + t + ", cluster " + c + ")");
                    }
                    Count(l2, cs);
                    for (long j = 0; j < perL2; j++)
                        if ((table[j] & mask) != 0) Count((long)(table[j] & mask), cs);
                }
                foreach (var c in want.Clusters.Keys) if (!seen.Contains(c)) throw new InvalidDataException("a cluster missing (table " + t + ", cluster " + c + ")");
            }
            // The refcounts written, against those counted from the tables (and the fixed structures, counted once each).
            var raw = File.ReadAllBytes(path);
            var hdr = raw.AsSpan(0, 112).ToArray();
            long refTable = (long)Qcow2Image.BE64(hdr, 48), refTableClusters = Qcow2Image.BE32(hdr, 56);
            long fileClusters = raw.Length / cs;
            for (long c = 0; c < fileClusters; c++)
            {
                long block = (long)Qcow2Image.BE64(raw, (int)(refTable + (c / (cs / 2)) * 8));
                int written = (raw[block + (c % (cs / 2)) * 2] << 8) | raw[block + (c % (cs / 2)) * 2 + 1];
                int need = counted.TryGetValue(c, out var n) ? n : 0;
                bool structure = need == 0 && written == 1;                  // header, L1 tables, snapshot table, refcounts
                if (written != need && !structure) throw new InvalidDataException("cluster " + c + ": refcount " + written + ", the tables count " + need);
            }
            // Active entries: COPIED exactly when their data's refcount is 1.
            var al1 = img.ActiveL1;
            for (long i = 0; i < al1.Length; i++)
            {
                long l2 = (long)(al1[i] & mask);
                if (l2 == 0) continue;
                foreach (var e in img.L2Table(l2))
                {
                    long host = (long)(e & mask);
                    if (host == 0) continue;
                    bool one = counted.TryGetValue(host / cs, out var n) && n == 1;
                    if (one != ((e & Copied) != 0)) throw new InvalidDataException("an active entry's COPIED flag is wrong (refcount " + n + ")");
                }
            }
        }

        private static void Put(FileStream f, long at, byte[] b) { f.Seek(at, SeekOrigin.Begin); f.Write(b, 0, b.Length); }
        private static void PutBE32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static void PutBE64(byte[] b, int o, ulong v) { PutBE32(b, o, (uint)(v >> 32)); PutBE32(b, o + 4, (uint)v); }
    }
}
