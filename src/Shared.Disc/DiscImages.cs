// A disc image opened as the bytes of the disc, whatever holds them (Mehdi, 03/10): a plain image (.iso, .xiso), a CSO, a
// CCI (SectorImage.cs) or a CHD - MAME's container, read by CHDSharp, the library the PPSSPP and Flycast plugins merge (its
// LGPL FLAC: THIRD-PARTY.md). Told apart by their first bytes, not their names.
// Meant for more than Xbox discs: a CD emulator's CHD is the same container (its sectors then 2352 bytes and subchannels).

using System;
using System.IO;
using System.Threading;

namespace LbIntegrations.Disc
{
    internal enum DiscContainer { None, Plain, Cso, Cci, Chd }

    internal static class DiscImages
    {
        /// <summary>The extensions read here besides the plain ones.</summary>
        public static readonly string[] Compressed = { ".cso", ".cci", ".chd" };

        /// <summary>What holds the disc, from its first bytes - Plain for anything else that opens.</summary>
        public static DiscContainer Kind(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var b = new byte[8];
                int got = 0, r;
                while (got < 8 && (r = fs.Read(b, got, 8 - got)) > 0) got += r;
                if (got >= 4 && b[0] == 'C' && b[1] == 'I' && b[2] == 'S' && b[3] == 'O') return DiscContainer.Cso;
                if (got >= 4 && b[0] == 'C' && b[1] == 'C' && b[2] == 'I' && b[3] == 'M') return DiscContainer.Cci;
                if (got == 8 && System.Text.Encoding.ASCII.GetString(b) == "MComprHD") return DiscContainer.Chd;
                return DiscContainer.Plain;
            }
            catch { return DiscContainer.None; }
        }

        /// <summary>Is it a CSO, a CCI or a CHD - an image read through its container.</summary>
        public static bool IsCompressed(string path)
        {
            var k = Kind(path);
            return k == DiscContainer.Cso || k == DiscContainer.Cci || k == DiscContainer.Chd;
        }

        /// <summary>The disc's bytes, seekable, from its start. Throws when the image does not open.</summary>
        public static Stream Open(string path) => Open(path, ChdThreads);

        /// <summary>The same, a CHD decoded on <paramref name="chdThreads"/> cores when it is read in sequence (ChdParallel; 0 or 1:
        /// CHDSharp's own stream) - for what reads a disc from end to end or serves it, not for a listing.</summary>
        public static Stream Open(string path, int chdThreads)
        {
            switch (Kind(path))
            {
                case DiscContainer.Cso: return CsoImage.Open(path);
                case DiscContainer.Cci: return CciImage.Open(path);
                case DiscContainer.Chd: return OpenChd(path, chdThreads);
                default: return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
            }
        }

        // Its own method: CHDSharp (and VendoredFlac) are loaded only when a CHD is opened.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static Stream OpenChd(string path, int threads)
        {
            if (threads > 1) return OpenChdParallel(path, threads);
            var error = CHDSharp.ChdFile.OpenAsStream(path, out var stream, CancellationToken.None);
            if (stream == null || error.ToString() != "Chderrnone")
            {
                stream?.Dispose();
                throw new InvalidDataException("the CHD does not open (" + error + ")");
            }
            return stream;
        }

        /// <summary>What Open(path) gives a CHD: 0, CHDSharp's own stream. The RAM disk helper's server sets it from its cfg
        /// (chd_threads=) - the one stream it opens is the disc it serves.</summary>
        public static int ChdThreads;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static Stream OpenChdParallel(string path, int threads)
        {
            var error = CHDSharp.ChdFile.Open(path, out var chd, CancellationToken.None);
            if (chd == null || error.ToString() != "Chderrnone") { chd?.Dispose(); throw new InvalidDataException("the CHD does not open (" + error + ")"); }
            // A CD's hunks are frames of 2352 + 96 bytes (subchannels), not the sectors one after another: CHDSharp's own stream.
            if (chd.IsCd || chd.IsGdRom)
            {
                var e2 = CHDSharp.ChdFile.OpenAsStream(chd, true, out var plain);
                if (plain == null || e2.ToString() != "Chderrnone") { chd.Dispose(); throw new InvalidDataException("the CHD does not open (" + e2 + ")"); }
                return plain;
            }
            return new ChdParallel(chd, path, threads);
        }

        /// <summary><paramref name="inner"/> handed out more than once: disposing the wrapper leaves it open.</summary>
        public static Stream Shared(Stream inner) => new Kept(inner);

        private sealed class Kept : Stream
        {
            private readonly Stream _s;
            public Kept(Stream s) { _s = s; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _s.Length;
            public override long Position { get => _s.Position; set => _s.Position = value; }
            public override int Read(byte[] buffer, int offset, int count) => _s.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _s.Seek(offset, origin);
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
