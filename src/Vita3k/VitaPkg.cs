// A PlayStation Vita .pkg - Sony's store package - opened and its OUTER layer taken off.
//
// TWO LAYERS, and only the first is here. The package is AES-128-CTR encrypted with a key derived
// from its own header and one of three PUBLIC keys (pkg2zip's, mmozeiko; Vita3K's packages/pkg.cpp
// reads it the same way): no licence is needed to list it, read its param.sfo or unpack it. What
// comes out is the game as the console installs it - still PFS-encrypted, sce_pfs/ and all - and that
// second layer is the one every NoNpDRM zip has too: Vita3kNative decrypts it, with the game's licence
// (a RIF; a zRIF is one in text form, VitaZrif).
//
// The layout, big-endian throughout (Vita3K's PkgHeader / PkgExtHeader / PkgEntry):
//     0x000  header: magic 7F 'P' 'K' 'G', ..., info_offset, info_count, file_count, total_size,
//            data_offset, data_size, content_id[0x30] at 0x30, data IV[0x10] at 0x70
//     0x0C0  extended header: magic 7F 'e' 'x' 't', ...; data_type2 at +0x24 picks the key (& 7)
//     info   blocks of (type, size, data): 2 = content type, 13 = the items' offset, 14 = the
//            param.sfo's offset and size - the param.sfo is NOT encrypted
//     data   the item table (32 bytes each: name offset/size, data offset/size, type) then names and
//            files, all encrypted, the counter being the IV plus the offset in the data area / 16
//
// Content types: 0x15 an app (a game, or an update when its param.sfo says gp), 0x16 a DLC, 0x1F a
// theme (not handled - a theme is not something this plugin launches).

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using LbIntegrations.Psf;

namespace LbIntegrations.Vita3k
{
    internal sealed class VitaPkg
    {
        public const int App = 0x15, Dlc = 0x16, Theme = 0x1F;

        private static readonly byte[][] Keys =
        {
            null, null,
            new byte[] { 0xe3, 0x1a, 0x70, 0xc9, 0xce, 0x1d, 0xd7, 0x2b, 0xf3, 0xc0, 0x62, 0x29, 0x63, 0xf2, 0xec, 0xcb },
            new byte[] { 0x42, 0x3a, 0xca, 0x3a, 0x2b, 0xd5, 0x64, 0x9f, 0x96, 0x86, 0xab, 0xad, 0x6f, 0xd8, 0x80, 0x1f },
            new byte[] { 0xaf, 0x07, 0xfd, 0x59, 0x65, 0x25, 0x27, 0xba, 0xf1, 0x33, 0x89, 0x66, 0x8b, 0x17, 0xd9, 0xea },
        };

        public string Path;
        public string ContentId;
        public int ContentType;
        public byte[] Sfo;
        public int FileCount;

        private ulong _dataOffset;
        private uint _itemsOffset;
        private byte[] _iv, _key;

        /// <summary>The package's header, its info and its param.sfo - no decryption yet. Null, with
        /// <paramref name="error"/>, for anything that is not a Vita package we can open.</summary>
        public static VitaPkg Open(string path, out string error)
        {
            error = null;
            try
            {
                using var file = File.OpenRead(path);
                var head = new byte[0xC0 + 0x40];
                if (file.Read(head, 0, head.Length) != head.Length) { error = "too small to be a .pkg"; return null; }
                if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0)) != 0x7F504B47) { error = "not a .pkg (no PKG magic)"; return null; }
                if (BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0xC0)) != 0x7F657874) { error = "not a Vita .pkg (no extended header)"; return null; }

                var pkg = new VitaPkg { Path = path };
                uint infoOffset = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x08));
                uint infoCount = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x0C));
                pkg.FileCount = (int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0x14));
                ulong totalSize = BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(0x18));
                pkg._dataOffset = BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(0x20));
                pkg.ContentId = System.Text.Encoding.ASCII.GetString(head, 0x30, 0x30).TrimEnd('\0');
                pkg._iv = head.AsSpan(0x70, 0x10).ToArray();
                if ((ulong)file.Length < totalSize) { error = "the .pkg is cut short (" + file.Length + " of " + totalSize + " bytes)"; return null; }

                int keyType = (int)(BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(0xC0 + 0x24)) & 7);
                if (keyType >= Keys.Length || Keys[keyType] == null) { error = "an unknown .pkg key (" + keyType + ")"; return null; }
                using (var aes = Aes.Create()) { aes.Key = Keys[keyType]; pkg._key = aes.EncryptEcb(pkg._iv, PaddingMode.None); }

                // The info blocks: type, size, then the block's data - its first two words are all we read.
                long at = infoOffset;
                uint sfoOffset = 0, sfoSize = 0;
                var block = new byte[16];
                for (uint i = 0; i < infoCount; i++)
                {
                    file.Position = at;
                    if (file.Read(block, 0, 16) != 16) break;
                    uint type = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(0)), size = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(4));
                    uint a = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(8)), b = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(12));
                    if (type == 2) pkg.ContentType = (int)a;
                    else if (type == 13) pkg._itemsOffset = a;
                    else if (type == 14) { sfoOffset = a; sfoSize = b; }
                    at += 8 + size;
                }
                if (sfoSize > 0 && sfoSize < 1024 * 1024)
                {
                    pkg.Sfo = new byte[sfoSize];
                    file.Position = sfoOffset;
                    if (file.Read(pkg.Sfo, 0, (int)sfoSize) != sfoSize) pkg.Sfo = null;
                }
                return pkg;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        /// <summary>What the package says it is, as the rest of the plugin reads a .vpk: its param.sfo.</summary>
        public VitaContent Describe(out string error)
        {
            error = null;
            if (ContentType == Theme) { error = "a theme - not handled"; return null; }
            if (ContentType != App && ContentType != Dlc) { error = "an unknown content type (0x" + ContentType.ToString("X") + ")"; return null; }
            var sfo = Sfo == null ? null : ParamSfo.Parse(Sfo);
            if (sfo == null) { error = "the .pkg carries no readable param.sfo"; return null; }
            var content = new VitaContent
            {
                TitleId = sfo.FirstString("TITLE_ID"),
                Category = sfo.FirstString("CATEGORY"),
                ContentId = sfo.FirstString("CONTENT_ID") ?? ContentId,
                Title = Vita3kContent.ShortTitleOf(sfo),
                FullTitle = Vita3kContent.TitleOf(sfo),
                AppVer = sfo.FirstString("APP_VER"),
            };
            if (string.IsNullOrWhiteSpace(content.TitleId) && ContentId?.Length >= 16) content.TitleId = ContentId.Substring(7, 9);
            if (string.IsNullOrWhiteSpace(content.TitleId)) { error = "the .pkg names no title id"; return null; }
            // A DLC's param.sfo may say anything; the package type says DLC.
            if (ContentType == Dlc) content.Category = "ac";
            return content;
        }

        /// <summary>Every item of the package, its outer layer taken off, written under
        /// <paramref name="destination"/> - still PFS-encrypted. Names are checked: none may leave it.</summary>
        public bool Extract(string destination, Action<double> progress, out string error)
        {
            error = null;
            try
            {
                var root = System.IO.Path.GetFullPath(destination).TrimEnd('\\') + "\\";
                Directory.CreateDirectory(root);
                using var file = File.OpenRead(Path);
                using var aes = Aes.Create();
                aes.Key = _key;
                var entry = new byte[32];
                var buffer = new byte[1 << 16];
                for (int i = 0; i < FileCount; i++)
                {
                    ulong entryAt = _itemsOffset + (ulong)i * 32;
                    Read(file, (long)(_dataOffset + entryAt), entry, 32);
                    Ctr(aes, entryAt / 16, entry, 32);
                    uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(0)), nameSize = BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(4));
                    ulong dataOffset = BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(8)), dataSize = BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(16));
                    uint type = BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(24)) & 0xFF;
                    if (nameSize == 0 || nameSize > 4096) { error = "item " + i + " has a name of " + nameSize + " bytes"; return false; }

                    var name = new byte[nameSize];
                    Read(file, (long)(_dataOffset + nameOffset), name, (int)nameSize);
                    Ctr(aes, nameOffset / 16, name, (int)nameSize);
                    var relative = System.Text.Encoding.UTF8.GetString(name).TrimEnd('\0').Replace('/', '\\');
                    var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { error = "an item names a path outside the package: " + relative; return false; }

                    if (type == 4 || type == 18) { Directory.CreateDirectory(target); }
                    else
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        using var output = File.Create(target);
                        file.Position = (long)(_dataOffset + dataOffset);
                        ulong left = dataSize, block = dataOffset / 16;
                        while (left > 0)
                        {
                            int n = (int)Math.Min((ulong)buffer.Length, left);
                            if (file.Read(buffer, 0, n) != n) { error = "the .pkg ends inside " + relative; return false; }
                            Ctr(aes, block, buffer, n);
                            output.Write(buffer, 0, n);
                            block += (ulong)n / 16;
                            left -= (ulong)n;
                        }
                    }
                    progress?.Invoke((i + 1) / (double)Math.Max(1, FileCount));
                }
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        /// <summary>The size of the package's largest item - the item table read, nothing else.</summary>
        public long LargestItem()
        {
            long largest = 0;
            try
            {
                using var file = File.OpenRead(Path);
                using var aes = Aes.Create();
                aes.Key = _key;
                var entry = new byte[32];
                for (int i = 0; i < FileCount; i++)
                {
                    ulong entryAt = _itemsOffset + (ulong)i * 32;
                    Read(file, (long)(_dataOffset + entryAt), entry, 32);
                    Ctr(aes, entryAt / 16, entry, 32);
                    largest = Math.Max(largest, (long)BinaryPrimitives.ReadUInt64BigEndian(entry.AsSpan(16)));
                }
            }
            catch { }
            return largest;
        }

        private static void Read(FileStream file, long at, byte[] into, int count)
        {
            file.Position = at;
            int got = 0;
            while (got < count) { int n = file.Read(into, got, count - got); if (n <= 0) throw new EndOfStreamException(); got += n; }
        }

        /// <summary>AES-128-CTR in place: the keystream is the IV plus <paramref name="block"/> (and on,
        /// one per 16 bytes) encrypted with the package's key. <paramref name="count"/> is a multiple of
        /// 16 but for the last piece of a file, whose last block is used in part.</summary>
        private void Ctr(Aes aes, ulong block, byte[] data, int count)
        {
            int blocks = (count + 15) / 16;
            var counters = new byte[blocks * 16];
            for (int b = 0; b < blocks; b++) Counter(block + (ulong)b, counters.AsSpan(b * 16, 16));
            var stream = aes.EncryptEcb(counters, PaddingMode.None);
            for (int k = 0; k < count; k++) data[k] ^= stream[k];
        }

        /// <summary>Vita3K's ctr_init: the IV read as a 128-bit big-endian number, plus n.</summary>
        private void Counter(ulong n, Span<byte> into)
        {
            ulong carry = n;
            for (int i = 15; i >= 0; i--)
            {
                carry += _iv[i];
                into[i] = (byte)carry;
                carry >>= 8;
            }
        }
    }
}
