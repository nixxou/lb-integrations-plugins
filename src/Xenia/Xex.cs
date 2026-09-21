// The XEX2 header, far enough in to read a title id.
//
// An Xbox 360 executable begins with a plaintext directory of "optional headers", and the title id
// lives in the one keyed XEX_HEADER_EXECUTION_INFO. Nothing here needs the decryption or the
// decompression that reading the actual code would: the directory is in the clear at the front of
// the file, which is what makes this thirty lines instead of a library.
//
//   xex2_header, big-endian throughout:
//     0x00  u32  magic         XEX2 / XEX1 / XEX% / XEX- / XEX? / XEX0
//     0x14  u32  header_count
//     0x18  optional headers, 8 bytes each: u32 key, u32 value
//
// The LOW BYTE OF THE KEY says how to read the value - this is the part that is easy to get wrong:
//
//     key & 0xFF == 0x00   the value IS the datum
//     key & 0xFF == 0x01   the datum is stored in place, in the value field
//     otherwise            the value is a FILE OFFSET, and the low byte is the size in 32-bit words
//
// XEX_HEADER_EXECUTION_INFO = 0x00040006, so low byte 6 -> a 24-byte struct at that offset, with the
// title id at +0x0C.
//
// Read from Xenia's src/xenia/kernel/util/xex2_info.h and XexModule::GetOptHeader.

using System;
using System.IO;

namespace LbIntegrations.Xenia
{
    internal static class Xex
    {
        private const uint ExecutionInfoKey = 0x00040006u;
        private const int TitleIdInExecutionInfo = 0x0C;
        private const int MinHeaderSize = 0x18;
        /// <summary>A real XEX has a handful. A wild count means a file that is not one.</summary>
        private const int MaxOptionalHeaders = 256;

        /// <summary>Does this look like a XEX? All six magics Xenia accepts.</summary>
        public static bool IsXex(byte[] head)
        {
            if (head == null || head.Length < 4) return false;
            if (head[0] != 'X' || head[1] != 'E' || head[2] != 'X') return false;
            switch ((char)head[3])
            {
                case '2': case '1': case '%': case '-': case '?': case '0': return true;
                default: return false;
            }
        }

        /// <summary>The title id of a XEX reachable through <paramref name="read"/>, or null.
        /// <paramref name="read"/> answers in offsets from the START OF THE XEX, so the same code serves
        /// a bare .xex file and a XEX embedded in a disc image.</summary>
        public static uint? TitleId(Func<long, int, byte[]> read)
        {
            try
            {
                var header = read(0, MinHeaderSize);
                if (!IsXex(header)) return null;

                uint count = BeUInt32(header, 0x14);
                if (count == 0 || count > MaxOptionalHeaders) return null;

                for (uint i = 0; i < count; i++)
                {
                    var entry = read(MinHeaderSize + (long)i * 8, 8);
                    if (entry == null) return null;
                    uint key = BeUInt32(entry, 0);
                    uint value = BeUInt32(entry, 4);
                    if (key != ExecutionInfoKey) continue;

                    // Low byte 6 -> six 32-bit words at `value`, an offset from the start of the XEX.
                    var info = read(value + TitleIdInExecutionInfo, 4);
                    if (info == null) return null;
                    return BeUInt32(info, 0);
                }
                Log.Info("no execution-info header among " + count + " optional headers");
                return null;
            }
            catch (Exception ex) { Log.Warn("could not read the XEX header", ex); return null; }
        }

        /// <summary>Xbox 360 headers are big-endian; the BCL's BitConverter is not.</summary>
        public static uint BeUInt32(byte[] b, int offset)
            => (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);

        /// <summary>The 8 uppercase hex digits everything - the console, Xenia, Argosy - uses.</summary>
        public static string Format(uint titleId) => titleId.ToString("X8");

        /// <summary>Convenience for a bare .xex on disk.</summary>
        public static uint? TitleIdOfFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                return TitleId((offset, length) => ReadAt(fs, offset, length));
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); return null; }
        }

        internal static byte[] ReadAt(FileStream fs, long offset, int length)
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
