// The Dreamcast disc id, read from IP.BIN.
//
// A Dreamcast disc boots from an IP.BIN header at the start of the data track:
//
//     0x00   "SEGA SEGAKATANA "     hardware id
//     0x40   10 bytes ASCII         the product number, space padded  ("T-8118N", "MK-51035")
//
// That product number IS the save identity. Flycast names the per-game VMU after it
// (core/oslib/oslib.cpp, getVmuPath), and sigil's dreamcast.c says the same in as many words. So
// every side of this already agrees; the only work is finding IP.BIN inside whatever the user has.
//
// WE ANCHOR ON THE MAGIC, NOT ON SECTOR ARITHMETIC. A Dreamcast dump comes as .gdi + track files,
// .cue + .bin, .cdi, .chd or a bare track image, and those disagree about sector size (2048 cooked
// vs 2352 raw), about where the data track starts, and about whether tracks are separate files. A
// scan for the 15-byte hardware id sidesteps all of it: wherever IP.BIN really is, the product is
// 0x40 past the magic. Flycast itself scans (DC_SCAN_MAX_SECTORS in sigil, the same idea), because
// a GD-ROM's data track is the third and a CHD packs tracks contiguously from frame 0.
//
// The scan is BOUNDED. Tracks 1-2 live in the single-density area, the first four minutes of the
// disc (4 * 60 * 75 = 18000 frames), so no conformant dump pushes IP.BIN beyond that. sigil caps at
// 20000 sectors; we cap at the same distance expressed in bytes at the larger sector size, which is
// the conservative direction, and a miss then costs a bounded read rather than a whole disc.

using System;
using System.Text;

namespace LbIntegrations.Flycast
{
    internal static class Ipbin
    {
        /// <summary>"SEGA SEGAKATANA" — 15 bytes, the trailing space of the 16-byte field excluded so a
        /// dump that pads differently still matches.</summary>
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SEGA SEGAKATANA");

        /// <summary>Offset of the product number inside IP.BIN.</summary>
        private const int ProductOffset = 0x40;
        /// <summary>Length of the product number field.</summary>
        private const int ProductLength = 10;

        /// <summary>20000 sectors of 2352 bytes — sigil's bound, taken at the LARGER sector size so a
        /// raw dump is covered too. About 45 MB.</summary>
        public const long ScanLimit = 20000L * 2352L;

        private const int ChunkSize = 1 << 20;   // 1 MiB

        /// <summary>The product number, or null when this is not a Dreamcast image we can read.
        /// <paramref name="read"/> answers in image offsets and may return short or null at the end.</summary>
        public static string ProductFrom(Func<long, int, byte[]> read, long imageLength)
        {
            if (read == null) return null;

            long limit = imageLength > 0 ? Math.Min(imageLength, ScanLimit) : ScanLimit;
            // The overlap has to hold a magic that straddles a chunk boundary AND the product that
            // follows it, or a disc whose IP.BIN lands on the seam would read as unknown.
            int overlap = Magic.Length + ProductOffset + ProductLength;

            long position = 0;
            while (position < limit)
            {
                int want = (int)Math.Min(ChunkSize, limit - position + overlap);
                byte[] chunk;
                try { chunk = read(position, want); } catch { return null; }
                if (chunk == null || chunk.Length < Magic.Length) return null;

                int at = IndexOf(chunk, Magic);
                if (at >= 0)
                {
                    int productAt = at + ProductOffset;
                    if (productAt + ProductLength <= chunk.Length)
                        return Clean(chunk, productAt);

                    // The magic landed near the end of what we read: fetch the header outright.
                    byte[] header;
                    try { header = read(position + at, ProductOffset + ProductLength); } catch { return null; }
                    return header != null && header.Length >= ProductOffset + ProductLength
                        ? Clean(header, ProductOffset)
                        : null;
                }

                if (chunk.Length < want) break;           // short read: end of image
                position += Math.Max(1, chunk.Length - overlap);
            }
            return null;
        }

        /// <summary>The product as Flycast keeps it: trimmed of the padding spaces, printable only.
        /// Returns null for a field that is blank or holds anything a disc id cannot hold, which is
        /// what a false positive on the magic looks like.</summary>
        private static string Clean(byte[] buffer, int offset)
        {
            var sb = new StringBuilder(ProductLength);
            for (int i = 0; i < ProductLength; i++)
            {
                byte c = buffer[offset + i];
                if (c < 0x20 || c > 0x7E) return null;     // not ASCII printable: not a product number
                sb.Append((char)c);
            }
            var product = sb.ToString().Trim();
            return product.Length == 0 ? null : product;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            int last = haystack.Length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                if (haystack[i] != needle[0]) continue;
                int j = 1;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        /// <summary>Flycast replaces these before using a product number as a file name
        /// (core/oslib/oslib.cpp: INVALID_CHARS). Reproduced exactly, double quote included — Argosy's
        /// own copy of this list omits it, which is a divergence worth not inheriting.</summary>
        private const string InvalidChars = " /\\:*?|<>\"";

        /// <summary>The product number turned into the file-name stem Flycast uses.</summary>
        public static string SanitizeForFileName(string product)
        {
            if (string.IsNullOrEmpty(product)) return product;
            var chars = product.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (InvalidChars.IndexOf(chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }
}
