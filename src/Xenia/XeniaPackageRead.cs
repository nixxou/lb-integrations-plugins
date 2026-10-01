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
}
