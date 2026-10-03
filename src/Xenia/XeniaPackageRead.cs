// What a package says of itself beyond its header - read off the files in it (StfsFiles), the same way for a package on
// disk and one in an archive.
//
// A TITLE UPDATE'S TRUTH IS ITS PATCH, default.xexp (Mehdi, 01/10: never guess from an archive's name or its folders).
// Its execution info carries the media id of the disc it belongs to - the header of a disc game's update leaves it 0 -
// and its delta patch descriptor the two versions Xenia prints when it applies it ("base version: 0.0.0.5, new
// version: 0.0.3.5" for Real Steel's v3, measured 01/10).
//
// IN AN ARCHIVE, a deflated entry cannot be read at an offset: SequentialRead decompresses forward as far as asked and
// keeps what it has decompressed, so going back costs nothing. The files of a package come after its first hash and
// table blocks, so a title update's patch - a few MB at most - is reached early; past MaxBuffered, it gives up rather
// than fill the memory.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Xenia
{
    internal static class XeniaPackageRead
    {
        /// <summary>How far an archive's entry is decompressed, at most, to reach a file in it.</summary>
        public const int MaxBuffered = 256 * 1024 * 1024;

        /// <summary>The patch of a title update, read: its execution info and versions. Null when the package holds none
        /// that reads.</summary>
        public static XexInfo UpdatePatch(Func<long, int, byte[]> read)
        {
            var files = StfsFiles.Open(read);
            if (files == null) return null;
            var patch = files.Find("default.xexp")
                        ?? files.Entries.FirstOrDefault(e => !e.IsDirectory && e.Name.EndsWith(".xexp", StringComparison.OrdinalIgnoreCase));
            if (patch == null) return null;
            // The header of a patch is small - its optional headers and their data sit in the first few KB.
            var bytes = files.ReadFile(patch, 64 * 1024);
            if (bytes == null) return null;
            return Xex.Info((offset, length) => offset >= 0 && offset + length <= bytes.Length ? Slice(bytes, offset, length) : null);
        }

        /// <summary>The executable of a game package (Arcade, Indie): its default.xex read - its signature's digest is what a
        /// title update names. Null when the package holds none that reads.</summary>
        public static XexInfo GameExecutable(Func<long, int, byte[]> read)
        {
            var files = StfsFiles.Open(read);
            var xex = files?.Find("default.xex");
            if (xex == null) return null;
            // Its headers - up to the security info and the signature - are at its front: pe_data_offset (+0x08) long.
            var head = files.ReadFile(xex, 0x18);
            if (head == null || !Xex.IsXex(head)) return null;
            var bytes = files.ReadFile(xex, (int)Math.Min(xex.Length, Math.Max(0x18, Math.Min(Xex.BeUInt32(head, 0x08), 16u * 1024 * 1024))));
            return bytes == null ? null : Xex.Info((offset, length) => offset >= 0 && offset + length <= bytes.Length ? Slice(bytes, offset, length) : null);
        }

        /// <summary>A disc image's default.xex read, through its XDVDFS volume.</summary>
        public static XexInfo DiscExecutable(Func<long, int, byte[]> read)
        {
            var xex = Xdvdfs.FindRootFile(read, "default.xex");
            return xex == null ? null : Xex.Info((offset, length) => read(xex.Offset + offset, length));
        }
        private static byte[] Slice(byte[] b, long offset, int length)
        {
            var s = new byte[length];
            Array.Copy(b, offset, s, 0, length);
            return s;
        }

        /// <summary>A file on disk as read(offset, length) - the caller disposes the stream.</summary>
        public static Func<long, int, byte[]> Reader(FileStream fs) => (offset, length) => Xex.ReadAt(fs, offset, length);
    }

    /// <summary>A forward-only stream (an archive's entry) read at any offset: decompressed as far as asked, the bytes kept.</summary>
    internal sealed class SequentialRead
    {
        private readonly Stream _source;
        private readonly int _max;
        private byte[] _buffer = new byte[1024 * 1024];
        private int _length;
        private bool _ended;

        public SequentialRead(Stream source, int max = XeniaPackageRead.MaxBuffered)
        {
            _source = source;
            _max = max;
        }

        public byte[] Read(long offset, int length)
        {
            if (offset < 0 || length <= 0) return null;
            long end = offset + length;
            if (end > _max) return null;
            while (_length < end && !_ended)
            {
                if (_buffer.Length < end) Array.Resize(ref _buffer, (int)Math.Min(_max, Math.Max(end, (long)_buffer.Length * 2)));
                int n = _source.Read(_buffer, _length, _buffer.Length - _length);
                if (n <= 0) _ended = true; else _length += n;
            }
            if (offset >= _length) return null;
            int got = (int)Math.Min(length, _length - offset);
            var s = new byte[got];
            Array.Copy(_buffer, offset, s, 0, got);
            return s;
        }

        /// <summary>How far it had to decompress.</summary>
        public int Decompressed => _length;
    }

    /// <summary>A disc image inside an archive read at any offset (Mehdi, 03/10: "accept ISO in archive"): the entry
    /// decompressed forward as far as asked, by 64 KB blocks, the blocks asked for kept (the last 256 - 16 MB) so that a
    /// look back within an executable costs nothing; a look further back opens the entry again from its start. Disc
    /// images are not made to be read this way - the executable sits hundreds of MB in - so it is slow, once: the scan
    /// keeps what it found.</summary>
    internal sealed class ReopeningRead : IDisposable
    {
        private const int Block = 64 * 1024, Kept = 256;
        private readonly Func<Stream> _open;
        private readonly long _length;
        private readonly Dictionary<long, byte[]> _blocks = new Dictionary<long, byte[]>();
        private readonly Queue<long> _order = new Queue<long>();
        private Stream _stream;
        private long _position;
        private readonly byte[] _skip = new byte[1024 * 1024];

        public long Decompressed { get; private set; }
        public int Opened { get; private set; }

        public ReopeningRead(Func<Stream> open, long length)
        {
            _open = open;
            _length = length;
        }

        public byte[] Read(long offset, int length)
        {
            if (offset < 0 || length <= 0 || offset >= _length) return null;
            length = (int)Math.Min(length, _length - offset);
            var result = new byte[length];
            int done = 0;
            while (done < length)
            {
                long at = offset + done, start = at / Block * Block;
                var block = BlockAt(start);
                if (block == null) break;
                int inBlock = (int)(at - start), n = Math.Min(length - done, block.Length - inBlock);
                if (n <= 0) break;
                Array.Copy(block, inBlock, result, done, n);
                done += n;
            }
            if (done == 0) return null;
            if (done < length) Array.Resize(ref result, done);
            return result;
        }

        private byte[] BlockAt(long start)
        {
            if (_blocks.TryGetValue(start, out var kept)) return kept;
            if (_stream == null || start < _position)
            {
                _stream?.Dispose();
                _stream = _open();
                _position = 0;
                Opened++;
            }
            while (_position < start)
            {
                int n = _stream.Read(_skip, 0, (int)Math.Min(_skip.Length, start - _position));
                if (n <= 0) return null;
                _position += n;
                Decompressed += n;
            }
            var block = new byte[(int)Math.Min(Block, _length - start)];
            int got = 0, r;
            while (got < block.Length && (r = _stream.Read(block, got, block.Length - got)) > 0) got += r;
            _position += got;
            Decompressed += got;
            if (got == 0) return null;
            if (got < block.Length) Array.Resize(ref block, got);
            _blocks[start] = block;
            _order.Enqueue(start);
            while (_order.Count > Kept) _blocks.Remove(_order.Dequeue());
            return block;
        }

        public void Dispose() => _stream?.Dispose();
    }
}
