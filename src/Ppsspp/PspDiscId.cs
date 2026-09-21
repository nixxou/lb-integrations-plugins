// The DISC_ID of a PSP game, read from the ROM itself.
//
// This is what matches a game in the library to its folders under PSP\SAVEDATA, and reading it from
// the CONTENT rather than the file name is what makes the matching immune to renames, to regional
// tags, and to the whole archive-extraction problem class that dominates RetroArch's save handling.
//
// It lives in PARAM.SFO under the key DISC_ID, and where that file sits depends on the container:
//
//   .pbp          PARAM.SFO is subfile 0 of the PBP itself. Trivial.
//   .iso          ISO9660: /PSP_GAME/PARAM.SFO, 2048-byte sectors.
//   .cso / .zso   the same ISO behind a block index; only the blocks we touch are decompressed.
//                 CISO blocks are raw deflate, ZISO blocks are LZ4 - the containers are otherwise
//                 identical, and an LZ4 block decoder is fifty lines (see Lz4.Decompress).
//   .chd          read through CHDSharp, which is merged into this assembly - see THIRD-PARTY.md.
//                 A CHD carries a compressed hunk map and up to four codecs per file; chdman writes
//                 all four into the header by default, so the FLAC decoder is needed to open one
//                 even when no hunk uses it (measured: removing it makes the read throw).
//
// Extraction is cached per (path, size, mtime) including failures, the way PCSX2's integration
// caches its own serial lookups - GetSaves runs over the whole library and must not re-read discs.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    internal static class PspDiscId
    {
        private const int SectorSize = 2048;
        private const long PvdOffset = 16 * SectorSize;       // ISO9660 puts the primary volume descriptor at sector 16

        // An empty string memoises "looked, found nothing" so a failure costs one read, not one per scan.
        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The 9-character disc id (ULUS10064), or null. Never throws.</summary>
        public static string Of(string romPath)
        {
            if (string.IsNullOrWhiteSpace(romPath)) return null;
            string key;
            try
            {
                var fi = new FileInfo(romPath);
                if (!fi.Exists) return null;
                key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }

            if (Cache.TryGetValue(key, out var cached)) return cached.Length == 0 ? null : cached;

            string found = null;
            try { found = Extract(romPath); }
            catch (Exception ex) { Log.Warn("could not read a disc id from " + romPath, ex); }

            Cache[key] = found ?? "";
            if (found != null) Log.Info("disc id " + found + " <- " + Path.GetFileName(romPath));
            return found;
        }

        private static string Extract(string romPath)
        {
            string ext;
            try { ext = (Path.GetExtension(romPath) ?? "").ToLowerInvariant(); } catch { return null; }

            byte[] sfo = ext switch
            {
                ".pbp" => SfoFromPbp(romPath),
                ".iso" => SfoFromIso(romPath),
                ".cso" or ".zso" => SfoFromCso(romPath),
                ".chd" => SfoFromChd(romPath),
                _ => null,
            };
            if (sfo == null) return null;

            var id = ParamSfo.Parse(sfo)?.GetString("DISC_ID");
            if (string.IsNullOrWhiteSpace(id)) return null;
            id = id.Trim().Replace("-", "").ToUpperInvariant();     // SAVEDATA folders drop the dash
            return id.Length >= PspSaveUnits.DiscIdLength ? id.Substring(0, PspSaveUnits.DiscIdLength) : null;
        }

        // ── PBP ──────────────────────────────────────────────────────────────

        /// <summary>A PBP is a header of 8 absolute offsets followed by the subfiles. PARAM.SFO is the
        /// first, so it runs from offsets[0] to offsets[1].</summary>
        private static byte[] SfoFromPbp(string path)
        {
            using var fs = File.OpenRead(path);
            var header = Read(fs, 0, 40);
            if (header == null || BitConverter.ToUInt32(header, 0) != 0x50425000u) return null;   // "\0PBP"

            uint start = BitConverter.ToUInt32(header, 8);
            uint end = BitConverter.ToUInt32(header, 12);
            if (end <= start || end > fs.Length) return null;
            return Read(fs, start, (int)(end - start));
        }

        // ── ISO / CSO ────────────────────────────────────────────────────────

        private static byte[] SfoFromIso(string path)
        {
            using var fs = File.OpenRead(path);
            return SfoFromIso9660((off, len) => Read(fs, off, len), fs.Length);
        }

        private static byte[] SfoFromCso(string path)
        {
            using var fs = File.OpenRead(path);
            var header = Read(fs, 0, 24);
            if (header == null) return null;
            var magic = Encoding.ASCII.GetString(header, 0, 4);
            // Same container, different block codec.
            bool lz4 = magic == "ZISO";
            if (magic != "CISO" && !lz4) return null;

            ulong totalBytes = BitConverter.ToUInt64(header, 8);
            uint blockSize = BitConverter.ToUInt32(header, 16);
            byte align = header[21];
            if (blockSize == 0 || totalBytes == 0) return null;

            uint blocks = (uint)(totalBytes / blockSize) + 1;
            var index = Read(fs, 24, checked((int)(blocks * 4)));
            if (index == null) return null;

            byte[] BlockAt(uint i)
            {
                if (i + 1 >= blocks) return null;
                uint raw = BitConverter.ToUInt32(index, (int)(i * 4));
                uint next = BitConverter.ToUInt32(index, (int)((i + 1) * 4));
                bool stored = (raw & 0x80000000u) != 0;
                long pos = (long)(raw & 0x7FFFFFFFu) << align;
                long posNext = (long)(next & 0x7FFFFFFFu) << align;
                int packed = (int)(posNext - pos);
                if (packed <= 0) return null;

                var bytes = Read(fs, pos, packed);
                if (bytes == null) return null;
                if (stored) return bytes;

                try
                {
                    if (lz4) return Lz4.Decompress(bytes, (int)blockSize);
                    // Raw deflate, no zlib wrapper - what maxcso writes.
                    using var ms = new MemoryStream(bytes);
                    using var inflate = new DeflateStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream((int)blockSize);
                    inflate.CopyTo(outMs);
                    return outMs.ToArray();
                }
                catch { return null; }
            }

            // Map logical offsets onto blocks, inflating only what is actually touched.
            byte[] ReadLogical(long offset, int length)
            {
                var result = new byte[length];
                int done = 0;
                while (done < length)
                {
                    long abs = offset + done;
                    uint blockIndex = (uint)(abs / blockSize);
                    int inBlock = (int)(abs % blockSize);
                    var block = BlockAt(blockIndex);
                    if (block == null || inBlock >= block.Length) return null;
                    int take = Math.Min(length - done, block.Length - inBlock);
                    Buffer.BlockCopy(block, inBlock, result, done, take);
                    done += take;
                }
                return result;
            }

            return SfoFromIso9660(ReadLogical, (long)totalBytes);
        }

        // ── CHD ──────────────────────────────────────────────────────────────

        /// <summary>The same ISO9660 walk, over a CHD's decompressed image. CHDSharp only ever sees
        /// primitives across this boundary, and the whole library is internalized into this assembly,
        /// so nothing of it is visible to the host or to another plugin.</summary>
        private static byte[] SfoFromChd(string path)
        {
            var status = CHDSharp.ChdFile.Open(path, out var chd);
            if (chd == null) { Log.Warn("CHD would not open (" + status + "): " + Path.GetFileName(path)); return null; }
            using (chd)
            {
                return SfoFromIso9660((offset, length) =>
                {
                    var buffer = new byte[length];
                    return chd.Read((ulong)offset, buffer, 0, length) == CHDSharp.Models.ChdError.Chderrnone
                        ? buffer : null;
                }, (long)chd.TotalBytes);
            }
        }

        /// <summary>Walks an ISO9660 image for /PSP_GAME/PARAM.SFO. <paramref name="read"/> answers in
        /// LOGICAL image offsets, so the same walk serves a plain .iso and an inflated .cso.</summary>
        private static byte[] SfoFromIso9660(Func<long, int, byte[]> read, long imageLength)
        {
            var pvd = read(PvdOffset, SectorSize);
            if (pvd == null || pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001") return null;

            // The root directory record is embedded in the PVD at offset 156.
            uint rootLba = BitConverter.ToUInt32(pvd, 156 + 2);
            uint rootLen = BitConverter.ToUInt32(pvd, 156 + 10);
            if (rootLen == 0) return null;

            if (!TryFind(read, rootLba, rootLen, "PSP_GAME", wantDirectory: true, out var gameLba, out var gameLen))
                return null;
            if (!TryFind(read, gameLba, gameLen, "PARAM.SFO", wantDirectory: false, out var sfoLba, out var sfoLen))
                return null;
            if (sfoLen == 0 || sfoLen > 1 << 20) return null;

            return read((long)sfoLba * SectorSize, (int)sfoLen);
        }

        /// <summary>Scans one directory extent for a named child.</summary>
        private static bool TryFind(Func<long, int, byte[]> read, uint lba, uint length,
                                    string name, bool wantDirectory, out uint foundLba, out uint foundLen)
        {
            foundLba = 0; foundLen = 0;
            var data = read((long)lba * SectorSize, (int)length);
            if (data == null) return false;

            int pos = 0;
            while (pos < data.Length)
            {
                int recordLength = data[pos];
                if (recordLength == 0)
                {
                    // Records never straddle a sector: a zero length means "skip to the next one".
                    pos = (pos / SectorSize + 1) * SectorSize;
                    if (pos >= data.Length) break;
                    continue;
                }
                if (pos + recordLength > data.Length) break;

                int nameLength = data[pos + 32];
                if (nameLength > 0 && pos + 33 + nameLength <= data.Length)
                {
                    var raw = Encoding.ASCII.GetString(data, pos + 33, nameLength);
                    int semi = raw.IndexOf(';');                       // "PARAM.SFO;1"
                    if (semi >= 0) raw = raw.Substring(0, semi);
                    bool isDirectory = (data[pos + 25] & 0x02) != 0;

                    if (isDirectory == wantDirectory &&
                        string.Equals(raw, name, StringComparison.OrdinalIgnoreCase))
                    {
                        foundLba = BitConverter.ToUInt32(data, pos + 2);
                        foundLen = BitConverter.ToUInt32(data, pos + 10);
                        return true;
                    }
                }
                pos += recordLength;
            }
            return false;
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static byte[] Read(FileStream fs, long offset, int length)
        {
            if (length <= 0 || offset < 0 || offset >= fs.Length) return null;
            if (offset + length > fs.Length) length = (int)(fs.Length - offset);
            var buffer = new byte[length];
            fs.Position = offset;
            int done = 0;
            while (done < length)
            {
                int read = fs.Read(buffer, done, length - done);
                if (read <= 0) break;
                done += read;
            }
            return done == length ? buffer : null;
        }
    }
}
