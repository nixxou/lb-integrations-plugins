// The source files a virtual volume reads from, opened when read and kept open - up to a limit, the least recently read one
// closed first: a converted MAME romset reads from tens of thousands of zips, a handle each would not do. Read-only, shared
// (another program may read or replace a source; the description's size and date say when it has changed).

using System;
using System.Collections.Generic;
using System.IO;

namespace LbIntegrations.Volume
{
    internal sealed class SourcePool : IDisposable
    {
        private readonly int _max;
        private readonly Dictionary<string, LinkedListNode<(string Path, FileStream Stream)>> _open = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<(string Path, FileStream Stream)> _recent = new();
        private readonly object _lock = new();

        public SourcePool(int max = 64) { _max = Math.Max(1, max); }

        /// <summary><paramref name="count"/> bytes of <paramref name="path"/> at <paramref name="offset"/>; fewer only at its end.</summary>
        public int Read(string path, long offset, byte[] buffer, int bufferOffset, int count)
        {
            lock (_lock)
            {
                var fs = Get(path);
                fs.Position = offset;
                int done = 0, n;
                while (done < count && (n = fs.Read(buffer, bufferOffset + done, count - done)) > 0) done += n;
                return done;
            }
        }

        private FileStream Get(string path)
        {
            if (_open.TryGetValue(path, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                return node.Value.Stream;
            }
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1, FileOptions.RandomAccess);
            _open[path] = _recent.AddFirst((path, fs));
            while (_open.Count > _max)
            {
                var last = _recent.Last;
                _recent.RemoveLast();
                _open.Remove(last.Value.Path);
                last.Value.Stream.Dispose();
            }
            return fs;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var n in _recent) n.Stream.Dispose();
                _recent.Clear();
                _open.Clear();
            }
        }
    }

    /// <summary>A file of a virtual volume: its length and its bytes.</summary>
    internal interface IVolumeFile
    {
        long Length { get; }
        /// <summary>Its bytes at <paramref name="offset"/> (count bytes, fewer only at its end).</summary>
        int Read(long offset, byte[] buffer, int bufferOffset, int count);
    }

    /// <summary>A source file served as it is: its bytes read through.</summary>
    internal sealed class PassThroughFile : IVolumeFile
    {
        private readonly SourcePool _pool;
        public string Source { get; }
        public long Length { get; }

        public PassThroughFile(SourcePool pool, string source, long length)
        {
            _pool = pool;
            Source = source;
            Length = length;
        }

        public int Read(long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (offset >= Length) return 0;
            return _pool.Read(Source, offset, buffer, bufferOffset, (int)Math.Min(count, Length - offset));
        }
    }
}
