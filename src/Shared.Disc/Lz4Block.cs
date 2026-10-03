// One LZ4 block decompressed (the raw block format, lz4's lz4_Block_format.md) - what a CSO's and a CCI's compressed
// sectors hold. Ours, from the format: a sequence of (token, literals, offset, match) until the input ends; the token's
// high nibble is the literal count, its low nibble the match length - 4, each extended by 255-bytes while 255.

using System;

namespace LbIntegrations.Disc
{
    internal static class Lz4Block
    {
        /// <summary>Decompress <paramref name="srcLength"/> bytes into <paramref name="dst"/> - the bytes written, or -1 when
        /// the block is not well formed (or would not fit).</summary>
        public static int Decode(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
        {
            int s = srcOffset, sEnd = srcOffset + srcLength, d = dstOffset, dEnd = dstOffset + dstLength;
            while (s < sEnd)
            {
                int token = src[s++];
                int literals = token >> 4;
                if (literals == 15)
                {
                    int b;
                    do { if (s >= sEnd) return -1; b = src[s++]; literals += b; } while (b == 255);
                }
                if (s + literals > sEnd || d + literals > dEnd) return -1;
                Buffer.BlockCopy(src, s, dst, d, literals);
                s += literals;
                d += literals;
                if (s >= sEnd) break;                       // the last sequence has literals only
                if (s + 2 > sEnd) return -1;
                int offset = src[s] | src[s + 1] << 8;
                s += 2;
                if (offset == 0 || d - offset < dstOffset) return -1;
                int match = token & 15;
                if (match == 15)
                {
                    int b;
                    do { if (s >= sEnd) return -1; b = src[s++]; match += b; } while (b == 255);
                }
                match += 4;
                if (d + match > dEnd) return -1;
                int from = d - offset;
                for (int i = 0; i < match; i++) dst[d++] = dst[from++];    // may overlap: byte by byte
            }
            return d - dstOffset;
        }
    }
}
