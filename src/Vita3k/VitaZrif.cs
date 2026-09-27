// A zRIF - the text form a Vita licence travels in - turned back into the licence itself (a RIF, the
// 512-byte file Vita3K and the console keep as ux0/license/<TITLE_ID>/<CONTENT_ID>.rif, and a
// NoNpDRM dump carries as sce_sys/package/work.bin).
//
// A zRIF is base64 of a zlib stream compressed WITH A PRESET DICTIONARY: 1024 bytes, mostly zeros, then
// the strings every licence repeats. The dictionary is psvpfstools' (libzrif/src/keyflate.c, after
// weaknespase's PkgDecrypt) - GPL-2.0, as this plugin is. Its source lists 1025 values; zlib is given
// the first 1024 (g_dict_size), and so is this.
//
// .NET's DeflateStream takes no dictionary. It does not need one: a deflate stream may refer back to
// anything already decompressed, so the dictionary is put in FRONT, as a stored block, and the zRIF's
// own blocks follow it. Its back-references then land in the dictionary, exactly as zlib's
// inflateSetDictionary makes them; what comes after the dictionary is the licence.

using System;
using System.IO;
using System.IO.Compression;

namespace LbIntegrations.Vita3k
{
    internal static class VitaZrif
    {
        public const int RifSize = 512;

        /// <summary>psvpfstools' g_dict: 880 zeros, then these.</summary>
        private static readonly byte[] Tail =
        {
            48, 48, 48, 48, 57, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 48, 48, 48,
            54, 48, 48, 48, 48, 55, 48, 48, 48, 48, 56, 0, 48, 48, 48, 48, 51, 48, 48, 48,
            48, 52, 48, 48, 48, 48, 53, 48, 95, 48, 48, 45, 65, 68, 68, 67, 79, 78, 84, 48,
            48, 48, 48, 50, 45, 80, 67, 83, 71, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 49,
            45, 80, 67, 83, 69, 48, 48, 48, 45, 80, 67, 83, 70, 48, 48, 48, 45, 80, 67, 83,
            67, 48, 48, 48, 45, 80, 67, 83, 68, 48, 48, 48, 45, 80, 67, 83, 65, 48, 48, 48,
            45, 80, 67, 83, 66, 48, 48, 48, 0, 1, 0, 1, 0, 1, 0, 2, 239, 205, 171, 137,
            103, 69, 35, 1
        };

        private static readonly byte[] Dictionary = Build();

        private static byte[] Build()
        {
            var d = new byte[880 + Tail.Length];
            Tail.CopyTo(d, 880);
            return d;
        }

        /// <summary>The licence a zRIF holds, or null with <paramref name="error"/>.</summary>
        public static byte[] ToRif(string zrif, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(zrif)) { error = "no zRIF"; return null; }
                var packed = Convert.FromBase64String(zrif.Trim());
                if (packed.Length < 2 || (packed[0] & 0x0F) != 8) { error = "not a zRIF (not a zlib stream)"; return null; }

                // The zlib header, and the dictionary's id after it when FDICT is set.
                int start = 2 + ((packed[1] & 0x20) != 0 ? 4 : 0);
                using var stream = new MemoryStream();
                stream.WriteByte(0x00);                                   // a stored block, not the last one
                stream.WriteByte((byte)(Dictionary.Length & 0xFF));
                stream.WriteByte((byte)(Dictionary.Length >> 8));
                stream.WriteByte((byte)(~Dictionary.Length & 0xFF));
                stream.WriteByte((byte)((~Dictionary.Length >> 8) & 0xFF));
                stream.Write(Dictionary, 0, Dictionary.Length);
                stream.Write(packed, start, packed.Length - start);        // the zRIF's blocks; the Adler-32 after them is not read
                stream.Position = 0;

                using var inflate = new DeflateStream(stream, CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflate.CopyTo(output);
                var all = output.ToArray();
                if (all.Length < Dictionary.Length + RifSize)
                { error = "the zRIF holds " + (all.Length - Dictionary.Length) + " bytes, a licence is " + RifSize; return null; }
                var rif = new byte[RifSize];
                Array.Copy(all, Dictionary.Length, rif, 0, RifSize);
                return rif;
            }
            catch (FormatException) { error = "not a zRIF (not base64)"; return null; }
            catch (Exception ex) { error = "not a readable zRIF: " + ex.Message; return null; }
        }
    }
}
