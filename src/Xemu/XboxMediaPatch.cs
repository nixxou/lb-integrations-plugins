// The "media enable" patch, extract-xiso's rule (Mehdi, 04/10): in every .xbe of the disc, the 8 bytes E8 CA FD FF FF 85 C0 7D
// - a call, the test of what it returned, "jump if it succeeded" - get their last byte made EB, "always jump". extract-xiso
// does it by default when it makes an XISO ("-m: disable automatic .xbe media enable patching (not recommended)"); xdvdfs
// does not, and xemu's docs say so: "Some games on some BIOSes will not load as a result" (xemu.app/docs/disc-images).
// Measured 04/10 on Batman: Rise of Sin Tzu - the redump stays on a black screen under the Complex 4627 BIOS, an XISO of the
// same game from another source runs, and the two differ by that one byte of default.xbe and nothing else.
//
// THE USER'S FILE IS NEVER WRITTEN: the byte is served (ExfatOneFileView, the RAM disk helper's view=xiso) or written into
// the plugin's own copy (XemuDisc). An .xbe already patched no longer holds the pattern and is left as it is.
// This is the repository's own code - no line of extract-xiso's; the pattern and its byte are a fact about the XDK's code.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Cxbx;

namespace LbIntegrations.Xemu
{
    internal static class XboxMediaPatch
    {
        private static readonly byte[] Pattern = { 0xE8, 0xCA, 0xFD, 0xFF, 0xFF, 0x85, 0xC0, 0x7D };
        private const int BytePos = 7;
        public const byte Patched = 0xEB;
        private const long LargestXbe = 64L << 20;

        /// <summary>Where the patch goes: offsets in the disc's XISO (from its game partition's start), sorted - the byte to make
        /// <see cref="Patched"/>. <paramref name="disc"/>: the disc's bytes as <paramref name="listing"/> addresses them.</summary>
        public static long[] Find(Stream disc, XdvdfsResult listing)
        {
            var found = new List<long>();
            if (disc == null || listing == null || !listing.Found) return found.ToArray();
            foreach (var f in listing.Files)
            {
                if (!f.Path.EndsWith(".xbe", StringComparison.OrdinalIgnoreCase) || f.Length < Pattern.Length || f.Length > LargestXbe) continue;
                var b = new byte[f.Length];
                disc.Seek(f.Offset, SeekOrigin.Begin);
                int got = 0, n;
                while (got < b.Length && (n = disc.Read(b, got, b.Length - got)) > 0) got += n;
                foreach (var at in Matches(b, got)) found.Add(f.Offset - listing.PartitionBase + at + BytePos);
            }
            found.Sort();
            return found.ToArray();
        }

        /// <summary>Every place the pattern starts in the first <paramref name="length"/> bytes of <paramref name="b"/>.</summary>
        internal static IEnumerable<int> Matches(byte[] b, int length)
        {
            for (int i = 0; i + Pattern.Length <= length; i++)
            {
                if (b[i] != Pattern[0]) continue;
                int k = 1;
                while (k < Pattern.Length && b[i + k] == Pattern[k]) k++;
                if (k == Pattern.Length) yield return i;
            }
        }

        /// <summary>The patch laid over bytes just read: <paramref name="buffer"/>[<paramref name="at"/>..+<paramref name="count"/>]
        /// holds the XISO's bytes from <paramref name="xisoOffset"/>.</summary>
        public static void Apply(long[] patches, long xisoOffset, byte[] buffer, int at, int count)
        {
            if (patches == null || patches.Length == 0 || count <= 0) return;
            int i = Array.BinarySearch(patches, xisoOffset);
            if (i < 0) i = ~i;
            for (; i < patches.Length && patches[i] < xisoOffset + count; i++) buffer[at + (patches[i] - xisoOffset)] = Patched;
        }

        /// <summary>The patch written into a copy of the XISO (the plugin's own file).</summary>
        public static void Write(string xiso, long[] patches)
        {
            if (patches == null || patches.Length == 0) return;
            using var f = new FileStream(xiso, FileMode.Open, FileAccess.Write, FileShare.None);
            foreach (var p in patches) { f.Seek(p, SeekOrigin.Begin); f.WriteByte(Patched); }
        }
    }
}
