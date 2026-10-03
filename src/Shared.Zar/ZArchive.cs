// ZArchive (.zar) read - the compressed archive format of Exzap's ZArchive (MIT No Attribution, github.com/Exzap/ZArchive),
// Cemu's and Xenia Canary's (Mehdi, 03/10: "indispensable de gérer les zar"). Read from its zarchivecommon.h and
// zarchivereader.cpp, the C# ours; zstd is ZstdSharp's (the managed port SharpCompress already brings).
//
// THE FILE, every number BIG-ENDIAN:
//   [compressed data][offset records][name table][file tree][meta directory][meta data][footer]
//   footer (0x90 bytes, the file's last): six sections {u64 offset, u64 size} - compressed data, offset records, names,
//     file tree, meta directory, meta data -, u8[32] integrity hash, u64 total size (= the file's), u32 version
//     0x61BF3A01, u32 magic 0x169F52D6.
//   The files' bytes are ONE stream, cut in 64 KB blocks, each compressed alone with zstd - stored as is when that does
//     not make it smaller (its stored size is then exactly 64 KB). An offset record (0x28 bytes) holds the offset of
//     16 blocks' first, then each one's stored size minus one (u16): a block is found with one record read.
//   file tree: 16-byte entries, entry 0 the root (a directory, no name). u32 name offset | 0x80000000 for a file, then a
//     file's u32 offset low, u32 size low, u32 (size high << 16 | offset high), or a directory's u32 first child index,
//     u32 child count, u32 reserved. Its children are entries [first, first + count), sorted by name.
//   name table: a name is its length (one byte under 0x80; else two: (b0 & 0x7F) | b1 << 7, as the writer puts it),
//     then its bytes. Names compare without case (ASCII).
//
// A file is a range of the decompressed stream, so it reads at any offset - a block or two decompressed, kept in a
// cache of the last 64 (4 MB, the reader's own size). Safe from several threads: one at a time.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Zar
{
    internal sealed class ZArchiveEntry
    {
        /// <summary>Its path inside the archive, '\' between the parts; "" for the root.</summary>
        public string Path;
        public bool IsDirectory;
        /// <summary>A file's place in the decompressed stream, and its length.</summary>
        public long Offset, Length;
        public int Index;
    }

    internal sealed class ZArchive : IDisposable
    {
        private const int Block = 64 * 1024, PerRecord = 16, RecordSize = 8 + 2 * PerRecord, FooterSize = 16 * 6 + 32 + 8 + 4 + 4, CacheBlocks = 64;
        private const uint Magic = 0x169F52D6, Version1 = 0x61BF3A01;

        private readonly FileStream _file;
        private readonly long _dataOffset, _dataSize;
        private readonly byte[] _records;
        private readonly long _blockCount;
        private readonly object _gate = new object();
        private readonly Dictionary<long, byte[]> _cache = new Dictionary<long, byte[]>();
        private readonly LinkedList<long> _lru = new LinkedList<long>();
        private readonly ZstdSharp.Decompressor _zstd = new ZstdSharp.Decompressor();

        public List<ZArchiveEntry> Entries { get; } = new List<ZArchiveEntry>();
        public string ArchivePath { get; }

        private ZArchive(string path, FileStream file, long dataOffset, long dataSize, byte[] records)
        {
            ArchivePath = path;
            _file = file;
            _dataOffset = dataOffset;
            _dataSize = dataSize;
            _records = records;
            _blockCount = (long)(records.Length / RecordSize) * PerRecord;
        }

        /// <summary>Is this file a ZArchive - by its footer, not its name.</summary>
        public static bool IsZar(string path)
        {
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (f.Length <= FooterSize) return false;
                var tail = new byte[8];
                f.Seek(f.Length - 8, SeekOrigin.Begin);
                if (f.Read(tail, 0, 8) != 8) return false;
                return U32(tail, 0) == Version1 && U32(tail, 4) == Magic;
            }
            catch { return false; }
        }

        /// <summary>The archive opened and its tree read - null when it is not one, or does not read.</summary>
        public static ZArchive Open(string path)
        {
            FileStream f = null;
            try
            {
                f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.RandomAccess);
                long size = f.Length;
                if (size <= FooterSize) { f.Dispose(); return null; }
                var footer = ReadAt(f, size - FooterSize, FooterSize);
                if (footer == null || U32(footer, FooterSize - 4) != Magic || U32(footer, FooterSize - 8) != Version1 || (long)U64(footer, FooterSize - 16) != size)
                { f.Dispose(); return null; }
                (long Offset, long Size) Section(int i) => ((long)U64(footer, i * 16), (long)U64(footer, i * 16 + 8));
                var data = Section(0); var recs = Section(1); var names = Section(2); var tree = Section(3);
                foreach (var s in new[] { data, recs, names, tree })
                    if (s.Offset < 0 || s.Size < 0 || s.Offset + s.Size > size) { f.Dispose(); return null; }
                if (recs.Size % RecordSize != 0 || tree.Size % 16 != 0 || tree.Size == 0 || recs.Size > int.MaxValue || names.Size > int.MaxValue || tree.Size > int.MaxValue)
                { f.Dispose(); return null; }

                var z = new ZArchive(path, f, data.Offset, data.Size, ReadAt(f, recs.Offset, (int)recs.Size));
                var nameTable = ReadAt(f, names.Offset, (int)names.Size);
                var nodes = ReadAt(f, tree.Offset, (int)tree.Size);
                if (nameTable == null || nodes == null || (U32(nodes, 0) & 0x80000000) != 0) { z.Dispose(); return null; }
                z.Walk(nodes, nameTable, 0, "", 0);
                return z;
            }
            catch { f?.Dispose(); return null; }
        }

        private void Walk(byte[] nodes, byte[] names, int index, string path, int depth)
        {
            int count = nodes.Length / 16;
            if (index < 0 || index >= count || depth > 64) return;
            int at = index * 16;
            uint nameAndType = U32(nodes, at);
            bool isFile = (nameAndType & 0x80000000) != 0;
            if (isFile)
            {
                uint offLow = U32(nodes, at + 4), sizeLow = U32(nodes, at + 8), high = U32(nodes, at + 12);
                Entries.Add(new ZArchiveEntry
                {
                    Path = path, Index = index,
                    Offset = (long)offLow | ((long)(high & 0xFFFF) << 32),
                    Length = (long)sizeLow | ((long)(high & 0xFFFF0000) << 16),
                });
                return;
            }
            Entries.Add(new ZArchiveEntry { Path = path, IsDirectory = true, Index = index });
            uint first = U32(nodes, at + 4), children = U32(nodes, at + 8);
            for (uint i = 0; i < children && first + i < count; i++)
            {
                int child = (int)(first + i);
                var name = Name(names, U32(nodes, child * 16) & 0x7FFFFFFF);
                if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '\\', '/' }) >= 0) continue;
                Walk(nodes, names, child, path.Length == 0 ? name : path + "\\" + name, depth + 1);
            }
        }

        private static string Name(byte[] table, uint offset)
        {
            if (offset == 0x7FFFFFFF || offset >= table.Length) return "";
            int length = table[offset] & 0x7F;
            if ((table[offset] & 0x80) != 0)
            {
                if (offset + 1 >= table.Length) return "";
                length |= table[offset + 1] << 7;
                offset += 2;
            }
            else offset++;
            if (offset + length > table.Length) return "";
            return Encoding.UTF8.GetString(table, (int)offset, length);
        }

        /// <summary>A file by its path inside the archive, '/' or '\' alike, without case - null when there is none.</summary>
        public ZArchiveEntry Find(string path)
        {
            var p = (path ?? "").Replace('/', '\\').Trim('\\');
            return Entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Up to <paramref name="count"/> bytes of a file from <paramref name="offset"/> - fewer at its end, 0 past it.</summary>
        public int Read(ZArchiveEntry file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (file == null || file.IsDirectory || offset < 0 || offset >= file.Length || count <= 0) return 0;
            count = (int)Math.Min(count, file.Length - offset);
            lock (_gate)
            {
                long raw = file.Offset + offset;
                int done = 0;
                while (done < count)
                {
                    var block = BlockAt(raw / Block);
                    if (block == null) break;
                    int inBlock = (int)(raw % Block), n = Math.Min(count - done, block.Length - inBlock);
                    if (n <= 0) break;
                    Buffer.BlockCopy(block, inBlock, buffer, bufferOffset + done, n);
                    done += n;
                    raw += n;
                }
                return done;
            }
        }

        /// <summary>A file's first bytes - <paramref name="max"/> at most.</summary>
        public byte[] ReadHead(ZArchiveEntry file, int max)
        {
            if (file == null) return null;
            var b = new byte[(int)Math.Min(max, file.Length)];
            int n = Read(file, 0, b, 0, b.Length);
            if (n < b.Length) Array.Resize(ref b, n);
            return b;
        }

        /// <summary>A file as a read-only stream (to copy it out).</summary>
        public Stream OpenRead(ZArchiveEntry file) => new EntryStream(this, file);

        private byte[] BlockAt(long index)
        {
            if (_cache.TryGetValue(index, out var hit))
            {
                _lru.Remove(index);
                _lru.AddLast(index);
                return hit;
            }
            if (index < 0 || index >= _blockCount) return null;
            int record = (int)(index / PerRecord), sub = (int)(index % PerRecord), at = record * RecordSize;
            long offset = (long)U64(_records, at);
            for (int i = 0; i < sub; i++) offset += U16(_records, at + 8 + i * 2) + 1;
            int stored = U16(_records, at + 8 + sub * 2) + 1;
            if (offset + stored > _dataSize) return null;
            var raw = ReadAt(_file, _dataOffset + offset, stored);
            if (raw == null) return null;
            byte[] block;
            if (stored == Block) block = raw;                               // stored as is
            else
            {
                block = new byte[Block];
                int n = _zstd.Unwrap(raw, 0, stored, block, 0, Block);
                if (n != Block) return null;
            }
            _cache[index] = block;
            _lru.AddLast(index);
            while (_lru.Count > CacheBlocks) { _cache.Remove(_lru.First.Value); _lru.RemoveFirst(); }
            return block;
        }

        private static byte[] ReadAt(FileStream f, long offset, int length)
        {
            var b = new byte[length];
            f.Seek(offset, SeekOrigin.Begin);
            int got = 0, r;
            while (got < length && (r = f.Read(b, got, length - got)) > 0) got += r;
            return got == length ? b : null;
        }

        private static uint U32(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);
        private static int U16(byte[] b, int at) => b[at] << 8 | b[at + 1];
        private static ulong U64(byte[] b, int at) => (ulong)U32(b, at) << 32 | U32(b, at + 4);

        public void Dispose()
        {
            lock (_gate) { _file.Dispose(); _zstd.Dispose(); }
        }

        private sealed class EntryStream : Stream
        {
            private readonly ZArchive _z;
            private readonly ZArchiveEntry _e;
            private long _pos;
            public EntryStream(ZArchive z, ZArchiveEntry e) { _z = z; _e = e; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _e.Length;
            public override long Position { get => _pos; set => _pos = Math.Max(0, value); }
            public override int Read(byte[] buffer, int offset, int count) { int n = _z.Read(_e, _pos, buffer, offset, count); _pos += n; return n; }
            public override long Seek(long offset, SeekOrigin origin)
                => _pos = Math.Max(0, origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _pos + offset : _e.Length + offset);
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
