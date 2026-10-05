// A CHD read on several cores (Mehdi, 06/10: "décompresser plusieurs blocs à la fois, à l'avance, sur plusieurs cœurs").
// CHDSharp's own stream decodes one hunk at a time on the caller's thread - measured on Batman's CHD (891 200 hunks of 4 KB):
// 85 MB/s, one core full, while the file itself reads at 4 GB/s. MEASURED BEFORE WRITING THIS (06/10):
//   - ChdFile.ReadHunkConcurrent is slower than one thread (38 MB/s alone, 62 MB/s on 8 threads): not used;
//   - one ChdFile per thread, each its plain ReadHunk in a loop: 85 MB/s alone, 203 on 4, 419 on 16 - this is what is used;
//   - tasks blocked on a semaphore starve the thread pool: dedicated threads here.
//
//   in sequence  a read that starts where the last one ended: the next Window bytes queued, by batches of 64 hunks (256 KB),
//                to Threads worker threads, each with its own ChdFile; the reads wait for their batch if it is under way
//   at random    the hunk decoded on the caller's thread with the caller's ChdFile - as it was (a small read costs one hunk)
//   kept         the batches decoded, up to Keep bytes; the batches behind the reader go first
//
// Read-only, one reader at a time (as every Stream). Same bytes as CHDSharp's stream - checked by the probe (--disc-bench).
// DVDs and hard disks only: a CD's hunks are frames with their subchannels, and DiscImages gives a CD CHDSharp's own stream.
// Measured 06/10 on the same disc made zstd (chdman 0.289 copy -c zstd): one core 377 MB/s, ours 475 / 590 / 486 on 2 / 4 / 8 -
// with a codec that fast the reader's own copying, not the decoding, is what is left.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LbIntegrations.Disc
{
    internal sealed class ChdParallel : Stream
    {
        private const int BatchHunks = 64;

        private sealed class Batch
        {
            public byte[] Data;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public Exception Error;
        }

        private readonly CHDSharp.ChdFile _chd;      // the caller's
        private readonly string _path;
        private readonly int _hunk;
        private readonly long _hunks, _length, _batches;
        private readonly int _window, _keep;
        private readonly ConcurrentDictionary<long, Batch> _done = new ConcurrentDictionary<long, Batch>();
        private readonly LinkedList<long> _order = new LinkedList<long>();
        private readonly BlockingCollection<(long Index, Batch Slot)> _queue = new BlockingCollection<(long, Batch)>();
        private readonly List<Thread> _threads = new List<Thread>();
        private readonly int _threadCount;
        private readonly byte[] _one;
        private long _oneIndex = -1;
        private long _position, _lastEnd = -1;
        private volatile bool _stopping;

        /// <summary><paramref name="threads"/>: the workers decoding ahead (they start at the first sequential read).</summary>
        public ChdParallel(CHDSharp.ChdFile chd, string path, int threads, int windowBytes = 32 << 20, int keepBytes = 96 << 20)
        {
            _chd = chd;
            _path = path;
            _hunk = (int)chd.HunkBytes;
            _hunks = chd.HunkCount;
            _length = (long)chd.TotalBytes;
            _batches = (_hunks + BatchHunks - 1) / BatchHunks;
            _window = Math.Max(1, windowBytes / (_hunk * BatchHunks));
            _keep = Math.Max(_window * 2, keepBytes / (_hunk * BatchHunks));
            _threadCount = Math.Max(1, threads);
            _one = new byte[_hunk];
        }

        public override long Length => _length;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Position { get => _position; set => _position = Math.Max(0, value); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length || count <= 0) return 0;
            count = (int)Math.Min(count, _length - _position);
            bool sequential = _position == _lastEnd;
            long firstBatch = _position / _hunk / BatchHunks, lastBatch = (_position + count - 1) / _hunk / BatchHunks;
            if (sequential) Ahead(firstBatch, lastBatch);

            int done = 0;
            while (done < count)
            {
                long h = _position / _hunk;
                long b = h / BatchHunks;
                int inHunk = (int)(_position % _hunk);
                if (_done.TryGetValue(b, out var slot))
                {
                    slot.Done.Wait();
                    if (slot.Error != null) throw new IOException("CHD batch " + b + ": " + slot.Error.Message, slot.Error);
                    int at = (int)(h % BatchHunks) * _hunk + inHunk;
                    int n = (int)Math.Min(count - done, slot.Data.Length - at);
                    Buffer.BlockCopy(slot.Data, at, buffer, offset + done, n);
                    done += n; _position += n;
                }
                else
                {
                    // At random: this hunk alone, here.
                    if (_oneIndex != h) { Check(_chd.ReadHunk((uint)h, _one, CancellationToken.None), h); _oneIndex = h; }
                    int n = Math.Min(count - done, _hunk - inHunk);
                    Buffer.BlockCopy(_one, inHunk, buffer, offset + done, n);
                    done += n; _position += n;
                }
            }
            _lastEnd = _position;
            return done;
        }

        /// <summary>The batches of this read and the Window after it queued - those not decoded nor under way; the oldest dropped.</summary>
        private void Ahead(long first, long last)
        {
            if (_threads.Count == 0) Start();
            long to = Math.Min(_batches, last + 1 + _window);
            for (long b = first; b < to; b++)
            {
                if (_done.ContainsKey(b)) continue;
                var slot = new Batch();
                if (!_done.TryAdd(b, slot)) continue;
                lock (_order) _order.AddLast(b);
                _queue.Add((b, slot));
            }
            // Kept under Keep: the batches behind the reader first, then the oldest; never one under way.
            lock (_order)
                while (_order.Count > _keep)
                {
                    var node = _order.First;
                    while (node != null && node.Value >= first) node = node.Next;
                    node ??= _order.First;
                    if (_done.TryGetValue(node.Value, out var s) && !s.Done.IsSet) break;
                    _done.TryRemove(node.Value, out _);
                    _order.Remove(node);
                }
        }

        private void Start()
        {
            for (int i = 0; i < _threadCount; i++)
            {
                var t = new Thread(Work) { IsBackground = true, Name = "chd-decode-" + i };
                _threads.Add(t);
                t.Start();
            }
        }

        private void Work()
        {
            CHDSharp.ChdFile chd = null;
            try
            {
                var e = CHDSharp.ChdFile.Open(_path, out chd, CancellationToken.None);
                if (chd == null || e.ToString() != "Chderrnone") throw new IOException("the CHD does not open again (" + e + ")");
                var hunk = new byte[_hunk];
                foreach (var (index, slot) in _queue.GetConsumingEnumerable())
                {
                    if (_stopping) { slot.Done.Set(); continue; }
                    try
                    {
                        long h0 = index * BatchHunks, h1 = Math.Min(_hunks, h0 + BatchHunks);
                        var data = new byte[(h1 - h0) * _hunk];
                        for (long h = h0; h < h1; h++)
                        {
                            Check(chd.ReadHunk((uint)h, hunk, CancellationToken.None), h);
                            Buffer.BlockCopy(hunk, 0, data, (int)(h - h0) * _hunk, _hunk);
                        }
                        slot.Data = data;
                    }
                    catch (Exception ex) { slot.Error = ex; }
                    finally { slot.Done.Set(); }
                }
            }
            catch (Exception ex)
            {
                // This worker could not start: what it would have taken fails, the reader says why.
                foreach (var (_, slot) in _queue.GetConsumingEnumerable()) { slot.Error = ex; slot.Done.Set(); }
            }
            finally { chd?.Dispose(); }
        }

        private static void Check(object error, long h)
        {
            if (error.ToString() != "Chderrnone") throw new IOException("CHD hunk " + h + ": " + error);
        }

        public override long Seek(long offset, SeekOrigin origin)
            => _position = Math.Max(0, origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? _position + offset : _length + offset);

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stopping = true;
                _queue.CompleteAdding();
                foreach (var t in _threads) t.Join(2000);
                _chd.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
