// A ZArchive's files laid out as one image (Mehdi, 03/10: a .zar mounted for Cxbx-Reloaded as an ISO is): each file
// at its own offset, every one on a 2048-byte boundary, read through the archive - so what serves a disc image by its
// files' sectors (XisoFatView, the RAM disk helper's view=xbox) serves an archive the same way, without a byte of it
// unpacked. Between two files: zeros. Read-only, seekable.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Zar
{
    internal sealed class ZArchiveImage : Stream
    {
        private readonly ZArchive _zar;
        private readonly long[] _starts;
        private readonly ZArchiveEntry[] _entries;
        private long _position;

        /// <summary>Each file and its offset in the image, in the image's order.</summary>
        public IReadOnlyList<(ZArchiveEntry Entry, long Offset)> Files { get; }
        /// <summary>The directories, "a" and "a\b" - the root left out.</summary>
        public IReadOnlyList<string> Dirs { get; }

        public ZArchiveImage(ZArchive zar, int align = 2048)
        {
            _zar = zar;
            var files = new List<(ZArchiveEntry, long)>();
            long cursor = 0;
            foreach (var e in zar.Entries.Where(x => !x.IsDirectory).OrderBy(x => x.Offset))
            {
                files.Add((e, cursor));
                cursor += (e.Length + align - 1) / align * align;
            }
            Files = files;
            Dirs = zar.Entries.Where(x => x.IsDirectory && x.Path.Length > 0).Select(x => x.Path).ToList();
            _starts = files.Select(f => f.Item2).ToArray();
            _entries = files.Select(f => f.Item1).ToArray();
            Length = Math.Max(cursor, align);
        }

        public override long Length { get; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Position { get => _position; set => _position = Math.Max(0, value); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int done = 0;
            while (done < count && _position < Length)
            {
                int i = Array.BinarySearch(_starts, _position);
                if (i < 0) i = ~i - 1;
                if (i >= 0 && _position < _starts[i] + _entries[i].Length)
                {
                    long inFile = _position - _starts[i];
                    int n = (int)Math.Min(count - done, _entries[i].Length - inFile);
                    int got = _zar.Read(_entries[i], inFile, buffer, offset + done, n);
                    if (got <= 0) break;
                    done += got;
                    _position += got;
                }
                else
                {
                    // A gap - a file's padding to the boundary - up to the next file or the end.
                    long next = i + 1 < _starts.Length ? _starts[i + 1] : Length;
                    int n = (int)Math.Min(count - done, next - _position);
                    if (n <= 0) break;
                    Array.Clear(buffer, offset + done, n);
                    done += n;
                    _position += n;
                }
            }
            return done;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => _position = Math.Max(0, origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : Length + offset);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
