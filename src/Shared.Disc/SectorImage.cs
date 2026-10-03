// The compressed Xbox disc images read as the image they hold (Mehdi, 03/10: "CSO et CHD pour Cxbx"), at any offset -
// what XDVDFS listing, unpacking and the RAM disk helper's FAT32 view (view=xbox) read a plain ISO through.
//
//   CSO  - "CISO" v2, Project Stellar's (read from antangelo/ciso, MIT/BSD): header {u32 magic, u32 header size (24),
//          u64 uncompressed size, u32 block size (2048), u8 version 2, u8 index shift (2), u16}, then (sectors + 1) u32
//          LE index entries: bits 0-30 the position >> shift, bit 31 "compressed"; a sector's stored size is the next
//          entry's position minus its own. A compressed sector is an LZ4 frame stripped of its 7-byte header and end
//          mark: one LZ4 frame block, {u32 size | 0x80000000 when stored as is} and its bytes. A plain one is 2048 bytes.
//          SPLIT as ONE stream cut every 0xFFBF6000 bytes - name.1.cso, name.2.cso...: positions count from the first.
//   CCI  - "CCIM" v1, Team Resurgent's / Cerbios' (its layout read in XboxToolkit's reader - the format, not its code):
//          header {u32 magic, u32 header size (32), u64 uncompressed size, u64 index offset, u32 block size (2048),
//          u8 version 1, u8 index shift (2), u16}, (sectors + 1) u32 LE index entries at the index offset, bit 31 "LZ4".
//          A sector stored smaller than 2048 or marked: a padding count byte, then raw LZ4 of (size - padding - 1).
//          SPLIT as self-contained parts - name.1.cci, name.2.cci...: each its header, its index, its run of sectors.
// A part other than the first (name.2.cso) is no image of its own: IsLaterPart.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LbIntegrations.Disc
{
    /// <summary>A disc image of 2048-byte sectors, read sector by sector - the last ones read kept.</summary>
    internal abstract class SectorImage : Stream
    {
        protected const int Sector = 2048;
        private readonly Dictionary<long, byte[]> _cache = new Dictionary<long, byte[]>();
        private readonly Queue<long> _order = new Queue<long>();
        private readonly object _gate = new object();
        private long _position;

        protected abstract long Sectors { get; }
        /// <summary>One sector into <paramref name="buffer"/> (2048 bytes); false when it does not read.</summary>
        protected abstract bool ReadSector(long sector, byte[] buffer);

        public override long Length => Sectors * Sector;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Position { get => _position; set => _position = Math.Max(0, value); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                int done = 0;
                while (done < count && _position < Length)
                {
                    long sector = _position / Sector;
                    int inSector = (int)(_position % Sector), n = Math.Min(count - done, Sector - inSector);
                    if (!_cache.TryGetValue(sector, out var data))
                    {
                        data = new byte[Sector];
                        if (!ReadSector(sector, data)) throw new IOException("sector " + sector + " of the image does not read");
                        _cache[sector] = data;
                        _order.Enqueue(sector);
                        while (_order.Count > 512) _cache.Remove(_order.Dequeue());
                    }
                    Buffer.BlockCopy(data, inSector, buffer, offset + done, n);
                    done += n;
                    _position += n;
                }
                return done;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
            => _position = Math.Max(0, origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : Length + offset);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // ── parts ───────────────────────────────────────────────────────────

        private static readonly Regex Part = new Regex("^(?<stem>.+)\\.(?<n>\\d+)\\.(?<ext>cso|cci)$", RegexOptions.IgnoreCase);

        /// <summary>The parts of a split image, first first - just the file when it is not split.</summary>
        public static List<string> Parts(string path)
        {
            var m = Part.Match(Path.GetFileName(path));
            if (!m.Success) return new List<string> { path };
            var dir = Path.GetDirectoryName(path) ?? ".";
            string stem = m.Groups["stem"].Value, ext = m.Groups["ext"].Value;
            var parts = Directory.EnumerateFiles(dir, stem + ".*." + ext)
                .Select(f => (File: f, M: Part.Match(Path.GetFileName(f))))
                .Where(x => x.M.Success && string.Equals(x.M.Groups["stem"].Value, stem, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => int.Parse(x.M.Groups["n"].Value))
                .Select(x => x.File).ToList();
            return parts.Count > 0 ? parts : new List<string> { path };
        }

        /// <summary>name.2.cso, name.3.cci...: a part the first one stands for.</summary>
        public static bool IsLaterPart(string path)
        {
            var m = Part.Match(Path.GetFileName(path ?? ""));
            return m.Success && int.Parse(m.Groups["n"].Value) > 1;
        }

        protected static uint U32(byte[] b, int at) => (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24);
        protected static ulong U64(byte[] b, int at) => U32(b, at) | (ulong)U32(b, at + 4) << 32;

        protected static byte[] ReadAt(Stream s, long offset, int length)
        {
            var b = new byte[length];
            s.Seek(offset, SeekOrigin.Begin);
            int got = 0, r;
            while (got < length && (r = s.Read(b, got, length - got)) > 0) got += r;
            return got == length ? b : null;
        }
    }

    /// <summary>A CSO - see the header.</summary>
    internal sealed class CsoImage : SectorImage
    {
        private const long SplitPoint = 0xFFBF6000;
        private readonly FileStream[] _parts;
        private readonly uint[] _index;
        private readonly int _shift;
        private readonly long _sectors;

        private CsoImage(FileStream[] parts, uint[] index, int shift, long sectors)
        {
            _parts = parts; _index = index; _shift = shift; _sectors = sectors;
        }

        public static CsoImage Open(string path)
        {
            var files = Parts(path).Select(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess)).ToArray();
            try
            {
                var h = ReadAt(files[0], 0, 24);
                if (h == null || U32(h, 0) != 0x4F534943 || U32(h, 16) != Sector || h[20] != 2) throw new InvalidDataException("not a CSO v2 of 2048-byte sectors");
                long size = (long)U64(h, 8);
                long sectors = (size + Sector - 1) / Sector;
                int headerSize = (int)U32(h, 4);
                var raw = ReadAt(files[0], headerSize, checked((int)((sectors + 1) * 4)));
                if (raw == null) throw new InvalidDataException("the CSO's index does not read");
                var index = new uint[sectors + 1];
                Buffer.BlockCopy(raw, 0, index, 0, raw.Length);
                return new CsoImage(files, index, h[21], sectors);
            }
            catch { foreach (var f in files) f.Dispose(); throw; }
        }

        protected override long Sectors => _sectors;

        private byte[] ReadSplit(long position, int length)
        {
            var b = new byte[length];
            int done = 0;
            while (done < length)
            {
                long at = position + done;
                int part = (int)(at / SplitPoint);
                if (part >= _parts.Length) return null;
                long inPart = at % SplitPoint;
                int n = (int)Math.Min(length - done, SplitPoint - inPart);
                var piece = ReadAt(_parts[part], inPart, n);
                if (piece == null) return null;
                Buffer.BlockCopy(piece, 0, b, done, n);
                done += n;
            }
            return b;
        }

        protected override bool ReadSector(long sector, byte[] buffer)
        {
            uint entry = _index[sector];
            long position = (long)(entry & 0x7FFFFFFF) << _shift;
            if ((entry & 0x80000000) == 0)
            {
                var plain = ReadSplit(position, Sector);
                if (plain == null) return false;
                Buffer.BlockCopy(plain, 0, buffer, 0, Sector);
                return true;
            }
            // The size is the block's own, NOT the next entry's position minus this one: the last entry of Batman's CSO
            // (measured 03/10) points 3 bytes short of the end of the file, inside the last block.
            var size = ReadSplit(position, 4);
            if (size == null) return false;
            uint blockSize = U32(size, 0);
            int length = (int)(blockSize & 0x7FFFFFFF);
            if (length <= 0 || length > Sector) return false;
            var data = ReadSplit(position + 4, length);
            if (data == null) return false;
            if ((blockSize & 0x80000000) != 0)              // an LZ4 frame block stored as is
            {
                if (length < Sector) return false;
                Buffer.BlockCopy(data, 0, buffer, 0, Sector);
                return true;
            }
            return Lz4Block.Decode(data, 0, length, buffer, 0, Sector) == Sector;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var f in _parts) f.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>A CCI - see the header.</summary>
    internal sealed class CciImage : SectorImage
    {
        private sealed class Slice
        {
            public FileStream File;
            public long First, Count;
            public uint[] Index;
            public int Shift;
        }

        private readonly List<Slice> _slices;
        private readonly long _sectors;

        private CciImage(List<Slice> slices) { _slices = slices; _sectors = slices.Sum(s => s.Count); }

        public static CciImage Open(string path)
        {
            var slices = new List<Slice>();
            try
            {
                long first = 0;
                foreach (var p in Parts(path))
                {
                    var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
                    slices.Add(new Slice { File = f });
                    var h = ReadAt(f, 0, 32);
                    if (h == null || U32(h, 0) != 0x4D494343 || U32(h, 4) != 32 || U32(h, 24) != Sector || h[28] != 1)
                        throw new InvalidDataException(Path.GetFileName(p) + " is not a CCI v1 of 2048-byte sectors");
                    long count = (long)U64(h, 8) / Sector;
                    var raw = ReadAt(f, (long)U64(h, 16), checked((int)((count + 1) * 4)));
                    if (raw == null) throw new InvalidDataException("the index of " + Path.GetFileName(p) + " does not read");
                    var index = new uint[count + 1];
                    Buffer.BlockCopy(raw, 0, index, 0, raw.Length);
                    var s = slices[slices.Count - 1];
                    s.First = first; s.Count = count; s.Index = index; s.Shift = h[29];
                    first += count;
                }
                return new CciImage(slices);
            }
            catch { foreach (var s in slices) s.File?.Dispose(); throw; }
        }

        protected override long Sectors => _sectors;

        protected override bool ReadSector(long sector, byte[] buffer)
        {
            var s = _slices.FirstOrDefault(x => sector >= x.First && sector < x.First + x.Count);
            if (s == null) return false;
            long i = sector - s.First;
            long position = (long)(s.Index[i] & 0x7FFFFFFF) << s.Shift;
            int stored = (int)(((long)(s.Index[i + 1] & 0x7FFFFFFF) << s.Shift) - position);
            bool lz4 = (s.Index[i] & 0x80000000) != 0;
            var data = ReadAt(s.File, position, lz4 || stored != Sector ? stored : Sector);
            if (data == null) return false;
            if (!lz4 && stored == Sector) { Buffer.BlockCopy(data, 0, buffer, 0, Sector); return true; }
            int padding = data[0];
            return Lz4Block.Decode(data, 1, stored - padding - 1, buffer, 0, Sector) == Sector;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var s in _slices) s.File.Dispose();
            base.Dispose(disposing);
        }
    }
}
