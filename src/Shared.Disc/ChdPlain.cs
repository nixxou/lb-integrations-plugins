// A CHD as an UNCOMPRESSED CHD v5, built on the fly (Mehdi, 06/10: "détecter un format invalide et faire un proxy"): for an
// emulator whose CHD reader is older than the file - Demul's libchd.dll (MAME's of 2017) reads v3 to v5 with zlib, LZMA,
// Huffman, FLAC and their CD forms, and CRASHES (access violation) on a zstd one (measured 06/10). An uncompressed v5 CHD is
// what every reader since 2012 knows - checked with Demul's own library on a zstd GD-ROM made uncompressed by chdman.
//
// NOTHING OF THE DISC IS REINTERPRETED: the hunks are the source's, decoded (subchannels and all - a CD's hunk is frames of
// 2352 + 96 bytes), its metadata copied entry by entry (tag, flags, data - a GD-ROM's CHGD tracks, their PAD frames, the
// high-density area: as they were), its hunk and unit sizes, its SHA-1s. Only the compression goes.
//
// THE FILE (MAME's chd.h, v5, every number big-endian):
//   [0]   the header, 124 bytes: "MComprHD", 124, 5, four compressors = 0 (none), logical bytes, map offset, metadata
//         offset, hunk bytes, unit bytes, raw SHA-1, SHA-1, parent SHA-1 (zeros)
//   [124] the metadata entries, one after another: tag (u32), flags (u8), length (u24), next entry's offset (u64), data
//   then  the map: per hunk a u32, its offset in the file divided by the hunk size
//   then  the hunks, from the first multiple of the hunk size past the map, in order
// Read-only, seekable; the hunks decoded as they are read, the last few kept.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LbIntegrations.Disc
{
    internal sealed class ChdPlain : Stream
    {
        private const int HeaderSize = 124;
        private readonly CHDSharp.ChdFile _chd;
        private readonly byte[] _head;            // header, metadata, map - everything before the first hunk
        private readonly long _dataStart;
        private readonly int _hunkBytes;
        private readonly long _hunks;
        private readonly Dictionary<long, byte[]> _cache = new Dictionary<long, byte[]>();
        private readonly Queue<long> _order = new Queue<long>();
        private long _position;

        public override long Length { get; }

        public static ChdPlain Open(string path)
        {
            var error = CHDSharp.ChdFile.Open(path, out var chd, CancellationToken.None);
            if (chd == null || error.ToString() != "Chderrnone") { chd?.Dispose(); throw new InvalidDataException("the CHD does not open (" + error + ")"); }
            try { return new ChdPlain(chd); }
            catch { chd.Dispose(); throw; }
        }

        private ChdPlain(CHDSharp.ChdFile chd)
        {
            _chd = chd;
            if (chd.RequiresParent) throw new InvalidDataException("a CHD that needs its parent is not made plain");
            _hunkBytes = (int)chd.HunkBytes;
            _hunks = chd.HunkCount;

            // The metadata, as the source has it.
            var meta = new MemoryStream();
            var entries = (chd.Metadata ?? Array.Empty<CHDSharp.Models.ChdMetadataEntry>()).ToList();
            long at = HeaderSize;
            for (int i = 0; i < entries.Count; i++)
            {
                var data = entries[i].Data ?? Array.Empty<byte>();
                if (data.Length > 0xFFFFFF) throw new InvalidDataException("a metadata entry is too large");
                long next = i + 1 < entries.Count ? at + 16 + data.Length : 0;
                var tag = (entries[i].Tag ?? "").PadRight(4).Substring(0, 4);
                foreach (var c in tag) meta.WriteByte((byte)c);
                meta.WriteByte(entries[i].Flags);
                meta.WriteByte((byte)(data.Length >> 16)); meta.WriteByte((byte)(data.Length >> 8)); meta.WriteByte((byte)data.Length);
                WriteBE(meta, (ulong)next, 8);
                meta.Write(data, 0, data.Length);
                at += 16 + data.Length;
            }
            long metaOffset = entries.Count > 0 ? HeaderSize : 0;
            long mapOffset = (HeaderSize + meta.Length + 15) / 16 * 16;
            long mapEnd = mapOffset + _hunks * 4;
            _dataStart = (mapEnd + _hunkBytes - 1) / _hunkBytes * _hunkBytes;
            if (_dataStart / _hunkBytes + _hunks > uint.MaxValue) throw new InvalidDataException("the CHD is too large to be made plain");
            Length = _dataStart + _hunks * _hunkBytes;

            _head = new byte[_dataStart];
            var h = new MemoryStream(_head, true);
            foreach (var c in "MComprHD") h.WriteByte((byte)c);
            WriteBE(h, HeaderSize, 4);
            WriteBE(h, 5, 4);
            for (int i = 0; i < 4; i++) WriteBE(h, 0, 4);                 // no compressor: uncompressed
            WriteBE(h, chd.TotalBytes, 8);
            WriteBE(h, (ulong)mapOffset, 8);
            WriteBE(h, (ulong)metaOffset, 8);
            WriteBE(h, (ulong)_hunkBytes, 4);
            WriteBE(h, chd.UnitBytes, 4);
            h.Write(Sha(chd.RawSha1), 0, 20);
            h.Write(Sha(chd.Sha1), 0, 20);
            h.Write(new byte[20], 0, 20);                                    // no parent
            meta.Position = 0;
            h.Position = HeaderSize;
            meta.CopyTo(h);
            h.Position = mapOffset;
            uint first = (uint)(_dataStart / _hunkBytes);
            for (long i = 0; i < _hunks; i++) WriteBE(h, first + (ulong)i, 4);
        }

        private static byte[] Sha(byte[] s) => s != null && s.Length == 20 ? s : new byte[20];

        private static void WriteBE(Stream s, ulong v, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
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
                if (_position < _dataStart)
                {
                    int n = (int)Math.Min(count - done, _dataStart - _position);
                    Buffer.BlockCopy(_head, (int)_position, buffer, offset + done, n);
                    done += n; _position += n;
                    continue;
                }
                long hunk = (_position - _dataStart) / _hunkBytes;
                int inHunk = (int)((_position - _dataStart) % _hunkBytes);
                var data = Hunk(hunk);
                int m = Math.Min(count - done, _hunkBytes - inHunk);
                Buffer.BlockCopy(data, inHunk, buffer, offset + done, m);
                done += m; _position += m;
            }
            return done;
        }

        private byte[] Hunk(long index)
        {
            if (_cache.TryGetValue(index, out var hit)) return hit;
            var b = new byte[_hunkBytes];
            var error = _chd.ReadHunk((uint)index, b, CancellationToken.None);
            if (error.ToString() != "Chderrnone") throw new IOException("CHD hunk " + index + ": " + error);
            _cache[index] = b;
            _order.Enqueue(index);
            while (_order.Count > 64) _cache.Remove(_order.Dequeue());
            return b;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => _position = Math.Max(0, origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : Length + offset);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _chd.Dispose();
            base.Dispose(disposing);
        }
    }
}
