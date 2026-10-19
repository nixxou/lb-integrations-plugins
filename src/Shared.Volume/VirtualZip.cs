// A zip that is nowhere on disk: its headers and central directory are made here, each entry's data is read from the
// archive it is in (ZipEntryRef) - the same compressed bytes, so the same method and CRC, never decompressed. A file of a
// virtual volume (IVolumeFile): a MAME set converted from another layout (Shared.Mame) is one.
//
// LAYOUT, as any zip: per entry a local header (PK 03 04) then its data; the central directory (PK 01 02 per entry); a zip64
// end record and locator (PK 06 06, PK 06 07) when a size, an offset or the count does not fit 32 / 16 bits; the end record
// (PK 05 06). The entries are written in the order given. Local headers carry the sizes and CRC (flag bit 3, "sizes after
// the data", is cleared: the source's data descriptor is not copied); bit 11 is set for a name that is not ASCII.
//
// Its bytes are a list of segments - made bytes, or a range of a source archive - found by a binary search at each read.
// Nothing is opened to make it when each entry's data offset is known (a volume description gives them): its length follows
// from the names and sizes alone. Not the same FILE as one a ROM manager writes (TorrentZip recompresses): the same contents.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Volume
{
    internal sealed class VirtualZip : IVolumeFile
    {
        public sealed class Item
        {
            /// <summary>Its name in this zip ("clone/rom.bin" for a merged set's clone file).</summary>
            public string Name;
            public ZipEntryRef Source;
        }

        private struct Segment
        {
            public long Offset, Length;
            public byte[] Bytes;       // made here, or
            public string Archive;     // a range of an archive
            public long SourceOffset;
        }

        private readonly SourcePool _pool;
        private readonly Segment[] _segments;
        public long Length { get; }
        public int Count { get; }

        public VirtualZip(IReadOnlyList<Item> items, SourcePool pool)
        {
            _pool = pool;
            Count = items.Count;
            var segments = new List<Segment>(items.Count * 2 + 2);
            var central = new MemoryStream();
            var c2 = new BinaryWriter(central);
            long pos = 0;
            void Add(byte[] bytes) { segments.Add(new Segment { Offset = pos, Length = bytes.Length, Bytes = bytes }); pos += bytes.Length; }

            foreach (var it in items)
            {
                var e = it.Source;
                var name = it.Name.Replace('\\', '/');
                bool utf8 = false;
                foreach (var c in name) if (c > 127) { utf8 = true; break; }
                var nameBytes = (utf8 ? Encoding.UTF8 : Encoding.ASCII).GetBytes(name);
                ushort flags = (ushort)((e.Flags & 0x0006) | (utf8 ? 0x800 : 0));   // compression options kept; no data descriptor
                bool big = e.Size >= 0xFFFFFFFF || e.CompressedSize >= 0xFFFFFFFF;
                bool farOffset = pos >= 0xFFFFFFFF;
                long dataOffset = e.DataOffset(pool);

                // local header
                var local = new byte[30 + nameBytes.Length + (big ? 20 : 0)];
                var w = new BinaryWriter(new MemoryStream(local));
                w.Write(0x04034b50u);
                w.Write((ushort)(big ? 45 : 20));
                w.Write(flags);
                w.Write(e.Method);
                w.Write(e.Time);
                w.Write(e.Date);
                w.Write(e.Crc);
                w.Write(big ? 0xFFFFFFFFu : (uint)e.CompressedSize);
                w.Write(big ? 0xFFFFFFFFu : (uint)e.Size);
                w.Write((ushort)nameBytes.Length);
                w.Write((ushort)(big ? 20 : 0));
                w.Write(nameBytes);
                if (big) { w.Write((ushort)1); w.Write((ushort)16); w.Write((ulong)e.Size); w.Write((ulong)e.CompressedSize); }
                long localOffset = pos;
                Add(local);
                if (e.CompressedSize > 0)
                {
                    segments.Add(new Segment { Offset = pos, Length = e.CompressedSize, Archive = e.Archive, SourceOffset = dataOffset });
                    pos += e.CompressedSize;
                }

                // its central header
                int extra = (big ? 16 : 0) + (farOffset ? 8 : 0);
                c2.Write(0x02014b50u);
                c2.Write((ushort)45);                  // made by: MS-DOS, spec 4.5
                c2.Write((ushort)(big || farOffset ? 45 : 20));
                c2.Write(flags);
                c2.Write(e.Method);
                c2.Write(e.Time);
                c2.Write(e.Date);
                c2.Write(e.Crc);
                c2.Write(big ? 0xFFFFFFFFu : (uint)e.CompressedSize);
                c2.Write(big ? 0xFFFFFFFFu : (uint)e.Size);
                c2.Write((ushort)nameBytes.Length);
                c2.Write((ushort)(extra > 0 ? extra + 4 : 0));
                c2.Write((ushort)0);                   // comment
                c2.Write((ushort)0);                   // disk
                c2.Write((ushort)0);                   // internal attributes
                c2.Write(0u);                          // external attributes
                c2.Write(farOffset ? 0xFFFFFFFFu : (uint)localOffset);
                c2.Write(nameBytes);
                if (extra > 0)
                {
                    c2.Write((ushort)1); c2.Write((ushort)extra);
                    if (big) { c2.Write((ulong)e.Size); c2.Write((ulong)e.CompressedSize); }
                    if (farOffset) c2.Write((ulong)localOffset);
                }
            }
            c2.Flush();

            long cdOffset = pos, cdSize = central.Length;
            Add(central.ToArray());
            var end = new MemoryStream();
            var ew = new BinaryWriter(end);
            bool zip64 = Count >= 0xFFFF || cdOffset >= 0xFFFFFFFF || cdSize >= 0xFFFFFFFF;
            if (zip64)
            {
                long rec = pos;
                ew.Write(0x06064b50u); ew.Write(44UL); ew.Write((ushort)45); ew.Write((ushort)45);
                ew.Write(0u); ew.Write(0u); ew.Write((ulong)Count); ew.Write((ulong)Count); ew.Write((ulong)cdSize); ew.Write((ulong)cdOffset);
                ew.Write(0x07064b50u); ew.Write(0u); ew.Write((ulong)rec); ew.Write(1u);
            }
            ew.Write(0x06054b50u);
            ew.Write((ushort)0); ew.Write((ushort)0);
            ew.Write((ushort)Math.Min(Count, 0xFFFF)); ew.Write((ushort)Math.Min(Count, 0xFFFF));
            ew.Write((uint)Math.Min(cdSize, 0xFFFFFFFF)); ew.Write((uint)Math.Min(cdOffset, 0xFFFFFFFF));
            ew.Write((ushort)0);
            ew.Flush();
            Add(end.ToArray());
            Length = pos;
            _segments = segments.ToArray();
        }

        /// <summary>Its bytes at <paramref name="offset"/>.</summary>
        public int Read(long offset, byte[] buffer, int bufferOffset, int count)
        {
            if (offset >= Length || count <= 0) return 0;
            count = (int)Math.Min(count, Length - offset);
            int done = 0;
            for (int i = Find(offset); done < count && i < _segments.Length; i++)
            {
                var s = _segments[i];
                long at = offset + done - s.Offset;
                int n = (int)Math.Min(count - done, s.Length - at);
                if (s.Bytes != null) Buffer.BlockCopy(s.Bytes, (int)at, buffer, bufferOffset + done, n);
                else
                {
                    int got = _pool.Read(s.Archive, s.SourceOffset + at, buffer, bufferOffset + done, n);
                    if (got < n) Array.Clear(buffer, bufferOffset + done + got, n - got);   // a source cut short: zeros, never junk
                }
                done += n;
            }
            return done;
        }

        private int Find(long offset)
        {
            int lo = 0, hi = _segments.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_segments[mid].Offset <= offset) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Written to a file - what the tests check with MAME; a volume never does it.</summary>
        public void WriteTo(string path)
        {
            using var o = new FileStream(path, FileMode.Create, FileAccess.Write);
            var buf = new byte[1 << 20];
            for (long p = 0; p < Length;)
            {
                int n = Read(p, buf, 0, buf.Length);
                o.Write(buf, 0, n);
                p += n;
            }
        }
    }
}
