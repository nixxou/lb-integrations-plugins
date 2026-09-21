// LZ4 block decompression - just enough to read a ZISO's blocks.
//
// The BCL has no LZ4 and a NuGet package would break this repo's rule that a plugin ships as one
// DLL with nothing beside it. The BLOCK format (not the frame format, which adds headers, checksums
// and linked blocks) is small enough to implement honestly:
//
//   token byte : high nibble = literal count, low nibble = match length minus the 4-byte minimum
//   if a nibble reads 15, keep adding following bytes until one is not 255
//   copy <literal count> bytes straight through
//   stop if the input is exhausted - the last sequence is literals only
//   read a 2-byte little-endian offset, copy <match length> bytes from that far back in the OUTPUT
//
// The match copy must be byte by byte: offsets smaller than the length are legal and are how LZ4
// encodes runs, so a block copy would read bytes it has not written yet.
//
// Reference: the LZ4 block format specification, https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md

using System;

namespace LbIntegrations.Ppsspp
{
    internal static class Lz4
    {
        /// <summary>Decompress one LZ4 block. Returns null when the input is malformed or would run
        /// past <paramref name="maxOutput"/> - a corrupt disc image must yield nothing, not an
        /// exception and not a half-filled buffer.</summary>
        public static byte[] Decompress(byte[] input, int maxOutput)
        {
            if (input == null || input.Length == 0 || maxOutput <= 0) return null;

            var output = new byte[maxOutput];
            int src = 0, dst = 0;

            while (src < input.Length)
            {
                int token = input[src++];

                int literals = token >> 4;
                if (literals == 15)
                {
                    int more;
                    do
                    {
                        if (src >= input.Length) return null;
                        more = input[src++];
                        literals += more;
                    } while (more == 255);
                }

                if (src + literals > input.Length || dst + literals > maxOutput) return null;
                Buffer.BlockCopy(input, src, output, dst, literals);
                src += literals;
                dst += literals;

                // A block ends on a literal run: no offset follows.
                if (src >= input.Length) break;
                if (src + 2 > input.Length) return null;

                int offset = input[src] | (input[src + 1] << 8);
                src += 2;
                if (offset == 0 || offset > dst) return null;

                int matchLength = token & 0x0F;
                if (matchLength == 15)
                {
                    int more;
                    do
                    {
                        if (src >= input.Length) return null;
                        more = input[src++];
                        matchLength += more;
                    } while (more == 255);
                }
                matchLength += 4;                       // the format's minimum match

                if (dst + matchLength > maxOutput) return null;
                int from = dst - offset;
                // Byte by byte, deliberately: overlapping matches are how LZ4 encodes runs.
                for (int i = 0; i < matchLength; i++) output[dst++] = output[from++];
            }

            if (dst == maxOutput) return output;
            var trimmed = new byte[dst];
            Buffer.BlockCopy(output, 0, trimmed, 0, dst);
            return trimmed;
        }
    }
}
