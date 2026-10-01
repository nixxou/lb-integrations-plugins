// The files INSIDE an STFS package - a title update's default.xexp, for instance - read without mounting anything.
//
// Ported from Xenia's src\xenia\vfs\devices\xcontent_devices\stfs_container_device.cc and stfs_xbox.h (Copyright Ben
// Vanik, BSD licence): the same block arithmetic, the same hash-table walk, written over a read(offset, length)
// function so that it serves a file on disk and an archive's entry decompressed as far as needed alike.
//
//   the data starts at round_up(header_size, 0x1000), header_size the u32 BE at 0x340;
//   the volume descriptor is at 0x379 (STFS, 0x24 bytes): file table block count (u16 LE) at +3, file table block
//     number (u24 LE) at +5, flags at +2 (bit 0 read-only format: one hash block per table, and only level 0 used),
//     total block count (u32 BE) at +0x1C;
//   a block number becomes an offset through the hash tables interleaved with the data (BlockToOffset): every 170
//     data blocks a level-0 table, every 28 900 a level-1, every 4 913 000 a level-2 - one block each in a read-only
//     package, two otherwise;
//   a file table block holds 0x40 entries of 0x40 bytes: name[40], flags (name length in bits 0-5, contiguous bit 6,
//     directory bit 7), valid blocks u24 LE, allocated blocks u24 LE, start block u24 LE, parent index u16 BE (0xFFFF
//     for the root), length u32 BE;
//   a file's blocks are a chain: the next one is in its block's hash entry (the low 24 bits of the u32 BE at +0x14 of
//     each 0x18-byte entry), 0xFFFFFF ending it.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xenia
{
    internal sealed class StfsFileEntry
    {
        public string Name;          // its path in the package, "/" between folders
        public bool IsDirectory;
        public uint StartBlock;
        public uint Length;
        public override string ToString() => Name + (IsDirectory ? "/" : " (" + Length + " bytes)");
    }

    internal sealed class StfsFiles
    {
        private const int BlockSize = 0x1000;
        private const uint EndOfChain = 0xFFFFFF;
        private static readonly uint[] BlocksPerHashLevel = { 170, 28900, 4913000 };

        private readonly Func<long, int, byte[]> _read;
        private readonly long _dataStart;
        private readonly bool _readOnly;
        private readonly bool _rootActiveIndex;
        private readonly uint _totalBlocks;
        private readonly int _blocksPerHashTable;
        private readonly uint[] _blockStep = new uint[2];
        private readonly Dictionary<long, byte[]> _tables = new Dictionary<long, byte[]>();

        public readonly List<StfsFileEntry> Entries = new List<StfsFileEntry>();

        /// <summary>The package's files, or null when it is not a readable STFS volume (an SVOD package - Games on Demand -
        /// keeps its files elsewhere). <paramref name="read"/> answers in offsets from the start of the package, null
        /// past its end.</summary>
        public static StfsFiles Open(Func<long, int, byte[]> read)
        {
            try
            {
                var head = read(0, 0x3A9 + 4);
                if (head == null || !Stfs.IsStfs(head)) return null;
                if (Xex.BeUInt32(head, 0x3A9) != 0) return null;          // volume type: 0 STFS, 1 SVOD
                if (head[0x379] != 0x24) return null;                       // descriptor_length
                var files = new StfsFiles(read, head);
                return files.ReadTable() ? files : null;
            }
            catch (Exception ex) { Log.Info("STFS: the package's files could not be read (" + ex.Message + ")"); return null; }
        }

        private StfsFiles(Func<long, int, byte[]> read, byte[] head)
        {
            _read = read;
            uint headerSize = Xex.BeUInt32(head, 0x340);
            _dataStart = (headerSize + BlockSize - 1) / BlockSize * BlockSize;
            const int d = 0x379;
            _readOnly = (head[d + 2] & 1) != 0;
            _rootActiveIndex = (head[d + 2] & 2) != 0;
            _fileTableBlocks = (ushort)(head[d + 3] | head[d + 4] << 8);
            _fileTableBlock = U24(head, d + 5);
            _totalBlocks = Xex.BeUInt32(head, d + 0x1C);
            _blocksPerHashTable = _readOnly ? 1 : 2;
            _blockStep[0] = BlocksPerHashLevel[0] + (uint)_blocksPerHashTable;
            _blockStep[1] = BlocksPerHashLevel[1] + (BlocksPerHashLevel[0] + 1) * (uint)_blocksPerHashTable;
        }

        private readonly ushort _fileTableBlocks;
        private readonly uint _fileTableBlock;

        private bool ReadTable()
        {
            var all = new List<StfsFileEntry>();
            uint block = _fileTableBlock;
            for (int n = 0; n < _fileTableBlocks; n++)
            {
                var table = _read(_dataStart + BlockToOffset(block), BlockSize);
                if (table == null || table.Length < BlockSize) return all.Count > 0 && Finish(all);
                for (int m = 0; m < 0x40; m++)
                {
                    int at = m * 0x40;
                    if (table[at] == 0) break;
                    int nameLength = table[at + 0x28] & 0x3F;
                    var name = Encoding.Latin1.GetString(table, at, Math.Min(nameLength, 40));
                    ushort parent = (ushort)(table[at + 0x32] << 8 | table[at + 0x33]);
                    var entry = new StfsFileEntry
                    {
                        IsDirectory = (table[at + 0x28] & 0x80) != 0,
                        StartBlock = U24(table, at + 0x2F),
                        Length = Xex.BeUInt32(table, at + 0x34),
                    };
                    entry.Name = parent == 0xFFFF || parent >= all.Count ? name : all[parent].Name + "/" + name;
                    all.Add(entry);
                }
                var next = NextBlock(block);
                if (next == EndOfChain) break;
                block = next;
            }
            return Finish(all);
        }

        private bool Finish(List<StfsFileEntry> all)
        {
            Entries.AddRange(all);
            return true;
        }

        /// <summary>A file's bytes - at most <paramref name="max"/> of them - or null when its chain breaks.</summary>
        public byte[] ReadFile(StfsFileEntry entry, int max = int.MaxValue)
        {
            if (entry == null || entry.IsDirectory) return null;
            long want = Math.Min(entry.Length, (long)max);
            var output = new byte[want];
            long done = 0;
            uint block = entry.StartBlock;
            while (done < want && block != EndOfChain)
            {
                int size = (int)Math.Min(BlockSize, want - done);
                var data = _read(_dataStart + BlockToOffset(block), size);
                if (data == null || data.Length < size) return null;
                Array.Copy(data, 0, output, done, size);
                done += size;
                if (done < want) block = NextBlock(block);
            }
            return done == want ? output : null;
        }

        public StfsFileEntry Find(string name)
            => Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

        // ── Xenia's arithmetic ───────────────────────────────────────────────

        private long BlockToOffset(uint blockIndex)
        {
            ulong block = blockIndex;
            for (int i = 0; i < BlocksPerHashLevel.Length; i++)
            {
                uint levelBase = BlocksPerHashLevel[i];
                block += ((blockIndex + levelBase) / levelBase) * (ulong)_blocksPerHashTable;
                if (blockIndex < levelBase) break;
            }
            return (long)(block << 12);
        }

        private uint BlockToHashBlockNumber(uint blockIndex, int level)
        {
            if (level == 2) return _blockStep[1];
            if (blockIndex < BlocksPerHashLevel[level]) return level == 0 ? 0 : _blockStep[level - 1];
            uint block = (blockIndex / BlocksPerHashLevel[level]) * _blockStep[level];
            if (level == 0)
            {
                block += ((blockIndex / BlocksPerHashLevel[1]) + 1) * (uint)_blocksPerHashTable;
                if (blockIndex < BlocksPerHashLevel[1]) return block;
            }
            return block + (uint)_blocksPerHashTable;
        }

        private int LevelsToCheck()
        {
            for (int level = 0; level < BlocksPerHashLevel.Length; level++)
                if (_totalBlocks < BlocksPerHashLevel[level]) return level;
            return 0;
        }

        /// <summary>The next block of a chain, from the block's level-0 hash entry - in a writable package, from whichever
        /// of each table's two copies its parent says is the live one (GetBlockHash).</summary>
        private uint NextBlock(uint blockIndex)
        {
            int secondary = _rootActiveIndex ? BlockSize : 0;
            int levels = LevelsToCheck();
            if (_readOnly) { secondary = 0; levels = 0; }
            byte[] table = null;
            for (int level = levels; level >= 0; level--)
            {
                long offset = _dataStart + ((long)BlockToHashBlockNumber(blockIndex, level) << 12) + secondary;
                if (!_tables.TryGetValue(offset, out table))
                {
                    table = _read(offset, BlockSize);
                    if (table == null || table.Length < BlockSize) return EndOfChain;
                    _tables[offset] = table;
                }
                uint record = level == 0 ? blockIndex % BlocksPerHashLevel[0] : (blockIndex / BlocksPerHashLevel[level - 1]) % BlocksPerHashLevel[0];
                uint info = Xex.BeUInt32(table, (int)record * 0x18 + 0x14);
                if (level > 0) secondary = (info & 0x40000000) != 0 ? BlockSize : 0;
                else return info & 0xFFFFFF;
            }
            return EndOfChain;
        }

        private static uint U24(byte[] b, int at) => (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16);
    }
}
