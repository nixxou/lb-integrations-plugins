// A zip's central directory, read without System.IO.Compression: what a virtual zip (VirtualZip) needs to serve an entry's
// bytes straight from its archive - the entry's compressed data, where it starts and how long it is, its method and CRC.
// Nothing is decompressed: the bytes served are the archive's own, so their CRC is too.
//
// Read: the end of central directory (PK 05 06, searched backwards past a comment of up to 64 KB), its zip64 locator and
// record when a field is saturated (PK 06 07 / PK 06 06), then every central header (PK 01 02) with its zip64 extra field
// (id 1: uncompressed size, compressed size, local header offset, in that order, present only for the saturated ones). The
// data's own offset is known once the local header (PK 03 04) is read - its name and extra lengths may differ from the
// central header's - so it is read once, on demand (DataOffset).
//
// Refused: an encrypted entry (flag bit 0), a multi-disk archive. Names: UTF-8 when flag bit 11 says so, else code page 437
// (MAME's names are ASCII either way).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbIntegrations.Volume
{
    internal sealed class ZipEntryRef
    {
        public string Name;
        public uint Crc;
        public long CompressedSize, Size;
        public ushort Method, Flags, Time, Date;
        /// <summary>The local header's offset in its archive.</summary>
        public long LocalOffset;
        /// <summary>The archive it is in.</summary>
        public string Archive;
        /// <summary>Where its compressed data starts in its archive; -1 until known (read from its local header, or given by a
        /// volume description that has it).</summary>
        public long KnownDataOffset = -1;

        /// <summary>Where its compressed data starts in its archive (the local header read once, through the pool).</summary>
        public long DataOffset(SourcePool pool)
        {
            if (KnownDataOffset >= 0) return KnownDataOffset;
            var head = new byte[30];
            if (pool.Read(Archive, LocalOffset, head, 0, 30) != 30 || BitConverter.ToUInt32(head, 0) != 0x04034b50)
                throw new InvalidDataException(Archive + ": no local header for " + Name + " at " + LocalOffset);
            return KnownDataOffset = LocalOffset + 30 + BitConverter.ToUInt16(head, 26) + BitConverter.ToUInt16(head, 28);
        }

        public override string ToString() => Name + " " + Crc.ToString("x8") + " " + Size;
    }

    internal static class ZipCentral
    {
        private static readonly Encoding Cp437 = CodePage437();

        private static Encoding CodePage437()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(437); }
            catch { return Encoding.Latin1; }
        }

        internal static void ReadExactly(Stream s, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count)
            {
                int n = s.Read(buffer, done, count - done);
                if (n <= 0) throw new EndOfStreamException();
                done += n;
            }
        }

        public static List<ZipEntryRef> Read(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.RandomAccess);
            return Read(fs, path);
        }

        public static List<ZipEntryRef> Read(Stream s, string archive)
        {
            long length = s.Length;
            if (length < 22) throw new InvalidDataException(archive + " is too short to be a zip");
            int tail = (int)Math.Min(length, 22 + 65535 + 20);
            var buf = new byte[tail];
            s.Position = length - tail;
            ReadExactly(s, buf, tail);
            int eocd = -1;
            for (int i = tail - 22; i >= 0; i--)
                if (buf[i] == 0x50 && buf[i + 1] == 0x4b && buf[i + 2] == 5 && buf[i + 3] == 6 && i + 22 + BitConverter.ToUInt16(buf, i + 20) <= tail) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException(archive + ": no end of central directory");
            if (BitConverter.ToUInt16(buf, eocd + 4) != 0 || BitConverter.ToUInt16(buf, eocd + 6) != 0) throw new InvalidDataException(archive + ": a multi-disk zip");
            long count = BitConverter.ToUInt16(buf, eocd + 10);
            long cdSize = BitConverter.ToUInt32(buf, eocd + 12);
            long cdOffset = BitConverter.ToUInt32(buf, eocd + 16);
            if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
            {
                // zip64: the locator right before the end record, then the zip64 end record it points to.
                int loc = eocd - 20;
                if (loc < 0 || BitConverter.ToUInt32(buf, loc) != 0x07064b50) throw new InvalidDataException(archive + ": a saturated end record without a zip64 locator");
                long rec = (long)BitConverter.ToUInt64(buf, loc + 8);
                var z = new byte[56];
                s.Position = rec;
                ReadExactly(s, z, 56);
                if (BitConverter.ToUInt32(z, 0) != 0x06064b50) throw new InvalidDataException(archive + ": no zip64 end record");
                count = (long)BitConverter.ToUInt64(z, 32);
                cdSize = (long)BitConverter.ToUInt64(z, 40);
                cdOffset = (long)BitConverter.ToUInt64(z, 48);
            }
            if (cdOffset + cdSize > length || cdSize > int.MaxValue) throw new InvalidDataException(archive + ": the central directory is past the file's end");
            var cd = new byte[cdSize];
            s.Position = cdOffset;
            ReadExactly(s, cd, (int)cdSize);
            var list = new List<ZipEntryRef>((int)Math.Min(count, 100000));
            int p = 0;
            for (long n = 0; n < count; n++)
            {
                if (p + 46 > cd.Length || BitConverter.ToUInt32(cd, p) != 0x02014b50) throw new InvalidDataException(archive + ": a bad central header at " + (cdOffset + p));
                var e = new ZipEntryRef
                {
                    Archive = archive,
                    Flags = BitConverter.ToUInt16(cd, p + 8),
                    Method = BitConverter.ToUInt16(cd, p + 10),
                    Time = BitConverter.ToUInt16(cd, p + 12),
                    Date = BitConverter.ToUInt16(cd, p + 14),
                    Crc = BitConverter.ToUInt32(cd, p + 16),
                    CompressedSize = BitConverter.ToUInt32(cd, p + 20),
                    Size = BitConverter.ToUInt32(cd, p + 24),
                    LocalOffset = BitConverter.ToUInt32(cd, p + 42),
                };
                int nameLen = BitConverter.ToUInt16(cd, p + 28), extraLen = BitConverter.ToUInt16(cd, p + 30), commentLen = BitConverter.ToUInt16(cd, p + 32);
                if (p + 46 + nameLen + extraLen + commentLen > cd.Length) throw new InvalidDataException(archive + ": a central header past the directory's end");
                if ((e.Flags & 1) != 0) throw new InvalidDataException(archive + ": " + "an encrypted entry");
                e.Name = ((e.Flags & 0x800) != 0 ? Encoding.UTF8 : Cp437).GetString(cd, p + 46, nameLen);
                // zip64 extra: only the saturated fields are there, in this order.
                int x = p + 46 + nameLen, xEnd = x + extraLen;
                while (x + 4 <= xEnd)
                {
                    int id = BitConverter.ToUInt16(cd, x), size = BitConverter.ToUInt16(cd, x + 2);
                    if (id == 1)
                    {
                        int q = x + 4;
                        if (e.Size == 0xFFFFFFFF && q + 8 <= x + 4 + size) { e.Size = (long)BitConverter.ToUInt64(cd, q); q += 8; }
                        if (e.CompressedSize == 0xFFFFFFFF && q + 8 <= x + 4 + size) { e.CompressedSize = (long)BitConverter.ToUInt64(cd, q); q += 8; }
                        if (e.LocalOffset == 0xFFFFFFFF && q + 8 <= x + 4 + size) { e.LocalOffset = (long)BitConverter.ToUInt64(cd, q); q += 8; }
                    }
                    x += 4 + size;
                }
                list.Add(e);
                p += 46 + nameLen + extraLen + commentLen;
            }
            return list;
        }
    }
}
