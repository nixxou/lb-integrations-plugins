// A thumbnail beside each save state (Mehdi, 07/10) - LbipStateThumbs's file, <name>.fc0.png - made from FCEUX's OWN
// picture of the state, never from a capture of its window.
//
// THE PICTURE (read in src\state.cpp:379-470 at the fceux-2.7.0 tag; measured 18/10, the lab's tools\fcx_state.py on a
// real Zelda II state): a 16-byte header - "FCSX", then three little-endian 32-bit words, the uncompressed size, FCEUX's
// version (20700) and the compressed size (0xFFFFFFFF: not compressed) - then the zlib data. Inside, chunks of one type
// byte and a little-endian 32-bit size. Chunk 8 is XBackBuf, the PPU's output: 256 x 256 bytes, the first 240 lines the
// picture, each byte a palette index (the NES colour is value & 0x3F). Decoded with FCEUX's default palette it is EXACTLY
// FCEUX's own screenshot of the same moment (compared side by side, docs\fceux.md section 8).
//
// THE PALETTE: palettes\FCEUX.pal of the install when it is there (192 bytes, byte-identical to the table palette[] of
// src\palettes\palettes.h), else that same table, carried here. A user palette changes the colours FCEUX shows, not the
// indices: the default is a fair rendering.
//
// WHEN: made again whenever it is missing or older than its state - when LaunchBox lists the states (so before it backs
// one up), after a restore, and once FCEUX has quit after a launch (AfterSession). No watch, no capture.

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Fceux
{
    internal static class FceuxStateThumbs
    {
        private const int Width = 256, Height = 240;

        /// <summary>palettes\FCEUX.pal of 2.7.0 (192 bytes, the default palette), base64.</summary>
        private const string DefaultPalette =
            "dHR0JBiMAACoRACcjAB0qAAQpAAAfAgAQCwAAEQAAFAAADwUGDxcAAAAAAAAAAAAvLy8AHDsIDjsgADwvAC85ABY2CgAyEwMiHAAAJQAAKgAAJA4AICIAAAAAAAAAAAA"
            + "/Pz8PLz8XJT8zIj89Hj8/HS0/HRg/Jg48Lw8gNAQTNxIWPiYAOjYeHh4AAAAAAAA/Pz8qOT8xNT81Mj8/MT8/MTY/Lyw/Nio/OSg4PygqPC8sPzMnPzwxMTEAAAAAAAA";

        /// <summary>The state's thumbnail made again when it is missing or older than the state. Never throws.</summary>
        public static void Refresh(string state, string exe = null)
        {
            try
            {
                if (string.IsNullOrEmpty(state) || !File.Exists(state)) return;
                var png = LbipStateThumbs.PathOf(state);
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(state)) return;
                var picture = Png(File.ReadAllBytes(state), Palette(exe));
                // No picture in the state: the old one would be of another moment.
                if (picture == null) { LbipStateThumbs.Remove(state); return; }
                var tmp = png + ".part";
                File.WriteAllBytes(tmp, picture);
                File.Move(tmp, png, overwrite: true);
                LbIntegrations.Saves.SaveScan.Touched(png);   // a big listing sees it
                Log.Info("thumbnail: " + Path.GetFileName(png));
            }
            catch (Exception ex) { Log.Warn("thumbnail of " + Path.GetFileName(state) + " not made", ex); }
        }

        /// <summary>The install's palette, else FCEUX's default.</summary>
        internal static byte[] Palette(string exe)
        {
            try
            {
                if (!string.IsNullOrEmpty(exe))
                {
                    var pal = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "palettes", "FCEUX.pal");
                    if (File.Exists(pal)) { var b = File.ReadAllBytes(pal); if (b.Length >= 192) return b; }
                }
            }
            catch { }
            return Convert.FromBase64String(DefaultPalette);
        }

        /// <summary>The picture of a state (its chunk 8), as a PNG; null when the file is not an FCEUX state or holds none.</summary>
        internal static byte[] Png(byte[] state, byte[] palette)
        {
            var screen = Screen(state);
            if (screen == null || palette == null || palette.Length < 192) return null;
            var raw = new byte[Height * (1 + Width * 3)];
            int o = 0;
            for (int y = 0; y < Height; y++)
            {
                raw[o++] = 0;   // filter: none
                for (int x = 0; x < Width; x++)
                {
                    int i = (screen[y * Width + x] & 0x3F) * 3;
                    raw[o++] = palette[i]; raw[o++] = palette[i + 1]; raw[o++] = palette[i + 2];
                }
            }
            return EncodePng(raw);
        }

        /// <summary>Chunk 8 of a state, its first 240 lines of 256 bytes; null when there is none.</summary>
        internal static byte[] Screen(byte[] state)
        {
            if (state == null || state.Length < 16 || state[0] != 'F' || state[1] != 'C' || state[2] != 'S' || state[3] != 'X') return null;
            uint total = BitConverter.ToUInt32(state, 4);
            uint compressed = BitConverter.ToUInt32(state, 12);
            if (total == 0 || total > 64 * 1024 * 1024) return null;
            byte[] data;
            if (compressed == 0xFFFFFFFF)
            {
                if (16 + (long)total > state.Length) return null;
                data = new byte[total];
                Array.Copy(state, 16, data, 0, total);
            }
            else
            {
                using var input = new MemoryStream(state, 16, state.Length - 16);
                using var z = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream((int)total);
                z.CopyTo(output);
                data = output.ToArray();
            }
            long p = 0;
            while (p + 5 <= data.Length)
            {
                byte type = data[p];
                uint size = BitConverter.ToUInt32(data, (int)p + 1);
                if (p + 5 + size > data.Length) return null;
                if (type == 8)
                {
                    if (size < Width * Height) return null;
                    var screen = new byte[Width * Height];
                    Array.Copy(data, p + 5, screen, 0, screen.Length);
                    return screen;
                }
                p += 5 + size;
            }
            return null;
        }

        /// <summary>A 24-bit RGB PNG of 256 x 240 from its filtered scanlines.</summary>
        private static byte[] EncodePng(byte[] raw)
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var ihdr = new byte[13];
            BigEndian(ihdr, 0, Width); BigEndian(ihdr, 4, Height);
            ihdr[8] = 8; ihdr[9] = 2;   // 8 bits, RGB; compression, filter, interlace 0
            Chunk(ms, "IHDR", ihdr);
            using (var deflated = new MemoryStream())
            {
                using (var z = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
                Chunk(ms, "IDAT", deflated.ToArray());
            }
            Chunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; BigEndian(len, 0, data.Length); s.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t); s.Write(data);
            uint crc = Crc32(t, 0xFFFFFFFF);
            crc = Crc32(data, crc) ^ 0xFFFFFFFF;
            var c = new byte[4]; BigEndian(c, 0, (int)crc); s.Write(c);
        }

        private static void BigEndian(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        private static uint[] _crc;

        private static uint Crc32(byte[] data, uint crc)
        {
            if (_crc == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[n] = c; }
                _crc = t;
            }
            foreach (var b in data) crc = _crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        /// <summary>Once the FCEUX of this launch has come and gone (a minute to appear), the thumbnails of the game's states
        /// made again.</summary>
        public static void AfterSession(string exe, string rom, string platform)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrWhiteSpace(rom)) return;
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 120 && !FceuxLaunch.Running(exe); i++) await Task.Delay(500).ConfigureAwait(false);
                    while (FceuxLaunch.Running(exe)) await Task.Delay(1000).ConfigureAwait(false);
                    foreach (var state in FceuxPlugin.StatesOf(exe, rom, platform)) Refresh(state, exe);
                }
                catch (Exception ex) { Log.Warn("thumbnails after the session", ex); }
            });
        }
    }
}
