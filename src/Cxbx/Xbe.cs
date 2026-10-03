// What an original Xbox executable says about itself: its certificate.
//
//   image header, at 0:
//     +0x000  char[4]  "XBEH"
//     +0x104  u32      base address (where the image is loaded)
//     +0x118  u32      certificate address - a virtual address, so at file offset (address - base)
//   certificate:
//     +0x008  u32          title id - the folder its saves go under: E:\UDATA\<title id, 8 hex digits>
//     +0x00C  wchar[40]    title name, UTF-16, NUL padded
//     +0x0A0  u32          game region (1 North America, 2 Japan, 4 the rest of the world, 0x80000000 debug)
//     +0x0AC  u32          version: disc version (low byte) and patch version - Cxbx-Reloaded shows "v<disc>.<patch>"
//
// The header and the certificate sit in the first pages of the file, so 64 KB is always enough.

using System;
using System.IO;
using System.Text;

namespace LbIntegrations.Cxbx
{
    internal sealed class XbeInfo
    {
        public uint TitleId;
        public string TitleName = "";
        public uint Region;
        /// <summary>Certificate +0xAC: disc version in the low byte, patch version above - "v1.0" for 1.</summary>
        public uint Version;
        /// <summary>8 hex digits, lower case - how the Xbox names a save folder, E:\UDATA\4d530004.</summary>
        public string TitleIdText => TitleId.ToString("x8");
    }

    internal static class Xbe
    {
        public const int HeadBytes = 64 * 1024;

        public static XbeInfo Read(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var head = new byte[Math.Min(HeadBytes, fs.Length)];
                int got = 0, n;
                while (got < head.Length && (n = fs.Read(head, got, head.Length - got)) > 0) got += n;
                return Parse(head);
            }
            catch (Exception ex) { Log.Warn("could not read the XBE " + path, ex); return null; }
        }

        /// <summary>The certificate out of an XBE's first bytes, or null when it is not one.</summary>
        public static XbeInfo Parse(byte[] head)
        {
            if (head == null || head.Length < 0x178) return null;
            if (head[0] != 'X' || head[1] != 'B' || head[2] != 'E' || head[3] != 'H') return null;
            uint baseAddress = U32(head, 0x104), certAddress = U32(head, 0x118);
            if (certAddress < baseAddress) return null;
            long cert = certAddress - baseAddress;
            if (cert < 0 || cert + 0xB0 > head.Length) return null;

            var name = Encoding.Unicode.GetString(head, (int)cert + 0x0C, 80);
            int nul = name.IndexOf('\0');
            if (nul >= 0) name = name.Substring(0, nul);
            return new XbeInfo { TitleId = U32(head, cert + 0x08), TitleName = name.Trim(), Region = U32(head, cert + 0xA0), Version = U32(head, cert + 0xAC) };
        }

        private static uint U32(byte[] b, long o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }
}
