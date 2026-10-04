// FATX, the Xbox hard disk's file system - enough of it to read a game's saves out of E: and to put them back.
//
// THE DISK (no partition table, fixed offsets - the retail layout xemu's dashboard disk keeps): X, Y, Z (caches), C (system)
// and E (data, the saves) at 0xABE80000, 0x1312D6000 bytes. A partition: a 4 KB superblock ("FATX", volume id, sectors
// per cluster, the root directory's first cluster), the FAT from 0x1000 - 16-bit entries under 65525 clusters, else
// 32-bit; its size rounded up to 4 KB - then the clusters, numbered from 1. A directory is a chain of clusters of 64-byte
// entries: name length (0xE5 deleted, 0x00 or 0xFF the end), attributes (0x10 a directory), 42 bytes of name, first
// cluster, size, three FAT dates.
//
// WRITES GO TO A PatchedDisk: the clusters of the underlying disk that change, kept in memory - XemuSaveStore makes a new
// console of them (Qcow2Image.Build). Nothing is ever written where it was read from.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu.Saves
{
    /// <summary>A disk read through, its changed blocks kept here. <see cref="Changed"/>: block index → the block's bytes.</summary>
    internal sealed class PatchedDisk : IVirtualDisk
    {
        private readonly IVirtualDisk _under;
        public readonly int BlockBits;
        public readonly long BlockSize;
        public readonly Dictionary<long, byte[]> Changed = new Dictionary<long, byte[]>();

        public PatchedDisk(IVirtualDisk under, int blockBits) { _under = under; BlockBits = blockBits; BlockSize = 1L << blockBits; }
        public long Length => _under.Length;

        public void Read(long offset, byte[] buffer, int at, int count)
        {
            while (count > 0)
            {
                long block = offset >> BlockBits;
                int inBlock = (int)(offset & (BlockSize - 1));
                int n = (int)Math.Min(count, BlockSize - inBlock);
                if (Changed.TryGetValue(block, out var b)) Array.Copy(b, inBlock, buffer, at, n);
                else _under.Read(offset, buffer, at, n);
                offset += n; at += n; count -= n;
            }
        }

        public void Write(long offset, byte[] data, int at, int count)
        {
            while (count > 0)
            {
                long block = offset >> BlockBits;
                int inBlock = (int)(offset & (BlockSize - 1));
                int n = (int)Math.Min(count, BlockSize - inBlock);
                if (!Changed.TryGetValue(block, out var b))
                {
                    b = new byte[BlockSize];
                    _under.Read(block << BlockBits, b, 0, (int)BlockSize);
                    Changed[block] = b;
                }
                Array.Copy(data, at, b, inBlock, n);
                offset += n; at += n; count -= n;
            }
        }

        public void Dispose() { }
    }

    internal sealed class FatxEntry
    {
        public string Name;
        public bool IsDirectory;
        public uint FirstCluster;
        public uint Size;
        /// <summary>Where its 64 bytes are on the disk.</summary>
        public long At;
        public override string ToString() => (IsDirectory ? "[" + Name + "]" : Name + " (" + Size + ")") + " @" + FirstCluster;
    }

    internal sealed class FatxVolume
    {
        public const long EOffset = 0xABE80000, ESize = 0x1312D6000;
        private const int EntrySize = 64, NameMax = 42;
        private const byte Deleted = 0xE5, DirectoryAttribute = 0x10;

        private readonly IVirtualDisk _disk;
        private readonly PatchedDisk _writable;
        public long Offset { get; }
        public long Size { get; }
        public uint ClusterSize { get; }
        public uint RootCluster { get; }
        public long ClusterCount { get; }
        public bool Fat32 { get; }
        private readonly long _fatAt, _dataAt;
        private readonly uint[] _fat;
        private readonly HashSet<long> _fatDirty = new HashSet<long>();

        /// <summary>The volume at <paramref name="offset"/> - null when there is no FATX there. Writable when the disk is a
        /// <see cref="PatchedDisk"/>.</summary>
        public static FatxVolume Open(IVirtualDisk disk, long offset = EOffset, long size = ESize)
        {
            var sb = new byte[16];
            disk.Read(offset, sb, 0, sb.Length);
            if (Encoding.ASCII.GetString(sb, 0, 4) != "FATX") return null;
            return new FatxVolume(disk, offset, size, BitConverter.ToUInt32(sb, 8), BitConverter.ToUInt32(sb, 12));
        }

        private FatxVolume(IVirtualDisk disk, long offset, long size, uint sectorsPerCluster, uint root)
        {
            _disk = disk; _writable = disk as PatchedDisk;
            Offset = offset; Size = size;
            ClusterSize = sectorsPerCluster * 512;
            if (ClusterSize == 0 || ClusterSize > 1 << 20) throw new InvalidDataException("FATX cluster size " + ClusterSize);
            RootCluster = root;
            ClusterCount = size / ClusterSize;
            Fat32 = ClusterCount >= 0xFFF5;
            long fatBytes = (ClusterCount + 1) * (Fat32 ? 4 : 2);
            fatBytes = (fatBytes + 4095) / 4096 * 4096;
            _fatAt = offset + 0x1000;
            _dataAt = _fatAt + fatBytes;
            var raw = new byte[fatBytes];
            disk.Read(_fatAt, raw, 0, raw.Length);
            _fat = new uint[ClusterCount + 1];
            for (long i = 0; i <= ClusterCount; i++) _fat[i] = Fat32 ? BitConverter.ToUInt32(raw, (int)(i * 4)) : BitConverter.ToUInt16(raw, (int)(i * 2));
        }

        private bool IsEnd(uint v) => Fat32 ? v >= 0xFFFFFFF0 : v >= 0xFFF0;
        private uint End => Fat32 ? 0xFFFFFFFF : 0xFFFF;
        private long ClusterAt(uint cluster) => _dataAt + (long)(cluster - 1) * ClusterSize;

        /// <summary>The clusters of a chain, in order. Stops at a loop or a cluster out of range.</summary>
        public List<uint> Chain(uint first)
        {
            var list = new List<uint>();
            var seen = new HashSet<uint>();
            for (uint c = first; c >= 1 && c <= ClusterCount && seen.Add(c); c = _fat[c])
            {
                list.Add(c);
                if (IsEnd(_fat[c]) || _fat[c] == 0) break;
            }
            return list;
        }

        // ── reading ──────────────────────────────────────────────────────────

        public List<FatxEntry> List(uint dirCluster)
        {
            var entries = new List<FatxEntry>();
            var buf = new byte[ClusterSize];
            foreach (var c in Chain(dirCluster))
            {
                _disk.Read(ClusterAt(c), buf, 0, buf.Length);
                for (int i = 0; i + EntrySize <= buf.Length; i += EntrySize)
                {
                    byte len = buf[i];
                    if (len == 0x00 || len == 0xFF) return entries;
                    if (len == Deleted || len > NameMax) continue;
                    entries.Add(new FatxEntry
                    {
                        Name = Encoding.ASCII.GetString(buf, i + 2, len),
                        IsDirectory = (buf[i + 1] & DirectoryAttribute) != 0,
                        FirstCluster = BitConverter.ToUInt32(buf, i + 0x2C),
                        Size = BitConverter.ToUInt32(buf, i + 0x30),
                        At = ClusterAt(c) + i,
                    });
                }
            }
            return entries;
        }

        /// <summary>The entry at <paramref name="path"/> ('\' separated, any case), or null; "" is the root (null entry, use
        /// RootCluster).</summary>
        public FatxEntry Find(string path)
        {
            uint dir = RootCluster; FatxEntry found = null;
            foreach (var part in path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                found = List(dir).FirstOrDefault(e => string.Equals(e.Name, part, StringComparison.OrdinalIgnoreCase));
                if (found == null) return null;
                dir = found.FirstCluster;
            }
            return found;
        }

        public byte[] ReadFile(FatxEntry e)
        {
            var data = new byte[e.Size];
            if (e.Size == 0) return data;
            long done = 0;
            foreach (var c in Chain(e.FirstCluster))
            {
                int n = (int)Math.Min(ClusterSize, e.Size - done);
                _disk.Read(ClusterAt(c), data, (int)done, n);
                done += n;
                if (done >= e.Size) break;
            }
            if (done < e.Size) throw new InvalidDataException(e.Name + ": its cluster chain is shorter than its size");
            return data;
        }

        /// <summary>Every file under <paramref name="dirCluster"/>, by its path from there ('/' separated) - and the empty
        /// directories, as "path/".</summary>
        public void Walk(uint dirCluster, string prefix, List<(string Path, FatxEntry Entry)> into, int depth = 0)
        {
            if (depth > 16) throw new InvalidDataException("directories nested too deep under " + prefix);
            foreach (var e in List(dirCluster))
            {
                var p = prefix + e.Name;
                if (e.IsDirectory) { int before = into.Count; Walk(e.FirstCluster, p + "/", into, depth + 1); if (into.Count == before) into.Add((p + "/", e)); }
                else into.Add((p, e));
            }
        }

        // ── writing ──────────────────────────────────────────────────────────

        private PatchedDisk W => _writable ?? throw new InvalidOperationException("this volume was opened read-only");

        private void SetFat(uint cluster, uint value)
        {
            _fat[cluster] = value;
            _fatDirty.Add(cluster * (Fat32 ? 4L : 2L) / 4096);
        }

        private uint Allocate(uint after)
        {
            for (uint c = Math.Max(2u, after + 1); c <= ClusterCount; c++) if (_fat[c] == 0) return c;
            for (uint c = 2; c <= after && c <= ClusterCount; c++) if (_fat[c] == 0) return c;
            throw new IOException("the Xbox partition is full");
        }

        /// <summary>A chain of <paramref name="count"/> new clusters (at least one), linked and ended.</summary>
        private List<uint> AllocateChain(int count)
        {
            var list = new List<uint>();
            uint last = 1;
            for (int i = 0; i < Math.Max(1, count); i++)
            {
                uint c = Allocate(last);
                SetFat(c, End);                          // taken at once, so the next search skips it
                if (list.Count > 0) SetFat(list[list.Count - 1], c);
                list.Add(c);
                last = c;
            }
            return list;
        }

        private void Free(uint first)
        {
            foreach (var c in Chain(first)) SetFat(c, 0);
        }

        /// <summary>A directory and all it holds removed: its clusters freed, its entry marked deleted.</summary>
        public void DeleteTree(FatxEntry e)
        {
            if (e.IsDirectory) foreach (var child in List(e.FirstCluster)) DeleteTree(child);
            if (e.FirstCluster != 0) Free(e.FirstCluster);
            W.Write(e.At, new[] { Deleted }, 0, 1);
        }

        public FatxEntry CreateDirectory(uint parentCluster, string name)
        {
            var c = AllocateChain(1)[0];
            var empty = Enumerable.Repeat((byte)0xFF, (int)ClusterSize).ToArray();
            W.Write(ClusterAt(c), empty, 0, empty.Length);
            return AddEntry(parentCluster, name, true, c, 0);
        }

        public FatxEntry CreateFile(uint parentCluster, string name, byte[] data)
        {
            uint first = 0;
            if (data.Length > 0)
            {
                var chain = AllocateChain((int)((data.Length + ClusterSize - 1) / ClusterSize));
                first = chain[0];
                for (int i = 0; i < chain.Count; i++)
                {
                    var block = new byte[ClusterSize];
                    Array.Copy(data, (long)i * ClusterSize, block, 0, Math.Min(ClusterSize, data.Length - (long)i * ClusterSize));
                    W.Write(ClusterAt(chain[i]), block, 0, block.Length);
                }
            }
            return AddEntry(parentCluster, name, false, first, (uint)data.Length);
        }

        /// <summary>A 64-byte entry in the first free slot of the directory (a deleted one, else the end), the directory grown
        /// by a cluster when it is full.</summary>
        private FatxEntry AddEntry(uint dirCluster, string name, bool directory, uint first, uint size)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            if (nameBytes.Length == 0 || nameBytes.Length > NameMax) throw new ArgumentException("a FATX name is 1 to 42 characters: " + name);
            if (List(dirCluster).Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) throw new IOException(name + " is there already");
            var entry = new byte[EntrySize];
            entry[0] = (byte)nameBytes.Length;
            entry[1] = (byte)(directory ? DirectoryAttribute : 0);
            for (int i = 0; i < NameMax; i++) entry[2 + i] = 0xFF;
            nameBytes.CopyTo(entry, 2);
            BitConverter.GetBytes(first).CopyTo(entry, 0x2C);
            BitConverter.GetBytes(size).CopyTo(entry, 0x30);
            var stamp = Stamp(DateTime.Now);
            for (int k = 0; k < 3; k++) { BitConverter.GetBytes(stamp.Time).CopyTo(entry, 0x34 + k * 4); BitConverter.GetBytes(stamp.Date).CopyTo(entry, 0x36 + k * 4); }

            var chain = Chain(dirCluster);
            var buf = new byte[ClusterSize];
            foreach (var c in chain)
            {
                _disk.Read(ClusterAt(c), buf, 0, buf.Length);
                for (int i = 0; i + EntrySize <= buf.Length; i += EntrySize)
                {
                    byte len = buf[i];
                    if (len != Deleted && len != 0x00 && len != 0xFF) continue;
                    long at = ClusterAt(c) + i;
                    W.Write(at, entry, 0, EntrySize);
                    // The end marker moved one entry on, unless this was a deleted slot or the cluster's last - nothing valid
                    // ever follows an end marker, so writing one there clobbers nothing.
                    if ((len == 0x00 || len == 0xFF) && i + EntrySize < buf.Length) W.Write(at + EntrySize, new byte[] { 0xFF }, 0, 1);
                    return new FatxEntry { Name = name, IsDirectory = directory, FirstCluster = first, Size = size, At = at };
                }
            }
            // Full: one more cluster, the entry first in it.
            var more = Allocate(chain[chain.Count - 1]);
            SetFat(chain[chain.Count - 1], more);
            SetFat(more, End);
            var fresh = Enumerable.Repeat((byte)0xFF, (int)ClusterSize).ToArray();
            entry.CopyTo(fresh, 0);
            W.Write(ClusterAt(more), fresh, 0, fresh.Length);
            return new FatxEntry { Name = name, IsDirectory = directory, FirstCluster = first, Size = size, At = ClusterAt(more) };
        }

        /// <summary>The FAT's changed 4 KB pages written to the disk.</summary>
        public void Flush()
        {
            int width = Fat32 ? 4 : 2;
            foreach (var page in _fatDirty)
            {
                var b = new byte[4096];
                long firstEntry = page * 4096 / width;
                for (int i = 0; i < 4096 / width; i++)
                {
                    long e = firstEntry + i;
                    if (e > ClusterCount) break;
                    if (Fat32) BitConverter.GetBytes(_fat[e]).CopyTo(b, i * 4); else BitConverter.GetBytes((ushort)_fat[e]).CopyTo(b, i * 2);
                }
                W.Write(_fatAt + page * 4096, b, 0, b.Length);
            }
            _fatDirty.Clear();
        }

        /// <summary>What is wrong with the volume, empty when nothing: every directory walked from the root, each chain
        /// checked - no cluster in two chains, a file's chain as long as its size needs, a directory's at least one - and no
        /// cluster marked used that nothing reaches (a leak). Cached FAT, so a check after writes sees them.</summary>
        public List<string> Check()
        {
            var problems = new List<string>();
            var owner = new Dictionary<uint, string>();
            void Claim(uint first, string who, long size, bool dir)
            {
                if (first == 0) { if (dir || size > 0) problems.Add(who + ": no cluster but " + (dir ? "a directory" : size + " bytes")); return; }
                var chain = Chain(first);
                if (chain.Count == 0) { problems.Add(who + ": its first cluster " + first + " is out of range"); return; }
                if (!IsEnd(_fat[chain[chain.Count - 1]])) problems.Add(who + ": its chain does not end (cluster " + chain[chain.Count - 1] + " -> " + _fat[chain[chain.Count - 1]] + ")");
                if (!dir && chain.Count != (size + ClusterSize - 1) / ClusterSize) problems.Add(who + ": " + chain.Count + " clusters for " + size + " bytes");
                foreach (var c in chain)
                {
                    if (owner.TryGetValue(c, out var other)) problems.Add("cluster " + c + " is " + other + "'s and " + who + "'s");
                    else owner[c] = who;
                }
            }
            void Walk(uint dir, string path, int depth)
            {
                if (depth > 16) { problems.Add(path + ": nested too deep"); return; }
                foreach (var e in List(dir))
                {
                    var p = path + "\\" + e.Name;
                    Claim(e.FirstCluster, p, e.Size, e.IsDirectory);
                    if (e.IsDirectory && e.FirstCluster != 0) Walk(e.FirstCluster, p, depth + 1);
                }
            }
            Claim(RootCluster, "the root", 0, true);
            Walk(RootCluster, "", 0);
            for (uint c = 1; c <= ClusterCount; c++)
                if (_fat[c] != 0 && !owner.ContainsKey(c)) { problems.Add("cluster " + c + " is marked used and nothing holds it"); if (problems.Count > 50) break; }
            return problems;
        }

        /// <summary>A FAT time and date (the Xbox's: years from 2000).</summary>
        internal static (ushort Time, ushort Date) Stamp(DateTime t)
        {
            int year = Math.Max(0, Math.Min(127, t.Year - 2000));
            return ((ushort)(t.Hour << 11 | t.Minute << 5 | t.Second / 2), (ushort)(year << 9 | t.Month << 5 | t.Day));
        }
    }
}
