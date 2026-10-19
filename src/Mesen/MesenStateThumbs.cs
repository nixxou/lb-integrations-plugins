// A thumbnail beside each save state (Mehdi, 07/10) - LbipStateThumbs's file, <rom>_1.mss.png - made from MESEN'S OWN
// picture of the state, never from a capture of its window.
//
// THE PICTURE (read in Core\Shared\SaveStateManager.cpp at CE 2.2.1 - GetSaveStateHeader, SaveVideoData; measured 18/10 with
// the lab's tools\mss2png.py on NES, Game Boy Color and SNES states, docs\mesen.md §10): "MSS", then little-endian 32-bit
// words - Mesen's version, the format (4; 3 still loaded), the console (ConsoleType: Snes 0, Gameboy 1, Nes 2, PcEngine 3,
// Sms 4, Gba 5, Ws 6), the frame's buffer size, its width, its height, its scale x100, the compressed size - then the zlib
// frame, one 16-bit pixel each. The pixel is the console's own (its DefaultVideoFilter):
//
//     NES          a palette index - the colour Nes.UserPalette of settings.json (64 entries: index & 0x3F; 512: & 0x1FF)
//     PC Engine    a 9-bit colour through PcEngine.Palette (512 entries; bit 0x200 = the grey of that colour)
//     WonderSwan   RGB444 (red in bits 8-11)
//     the others   RGB555 (red in bits 0-4): SNES and the Super Game Boy, Game Boy / Color, GBA, Master System family
//
// The palettes are the user's, read in the install's settings.json; without them the NES uses Mesen's default and a PC Engine
// state gets no picture. Hue / brightness options and NTSC filters are not applied: a fair rendering of the moment.
//
// WHEN: made again whenever it is missing or older than its state - when LaunchBox lists the states (so before it backs
// one up), after a restore, and once Mesen has quit after a launch (MesenSession). No watch, no capture.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using LbIntegrations.Lbip;

namespace LbIntegrations.Mesen
{
    internal static class MesenStateThumbs
    {
        private const int Snes = 0, Gameboy = 1, Nes = 2, PcEngine = 3, Sms = 4, Gba = 5, Ws = 6;

        /// <summary>Mesen's default NES palette (UI\Config\NesConfig.cs UserPalette, CE 2.2.1), 0xAARRGGBB.</summary>
        private static readonly uint[] NesDefault =
        {
            0xFF666666, 0xFF002A88, 0xFF1412A7, 0xFF3B00A4, 0xFF5C007E, 0xFF6E0040, 0xFF6C0600, 0xFF561D00, 0xFF333500, 0xFF0B4800, 0xFF005200, 0xFF004F08, 0xFF00404D, 0xFF000000, 0xFF000000, 0xFF000000,
            0xFFADADAD, 0xFF155FD9, 0xFF4240FF, 0xFF7527FE, 0xFFA01ACC, 0xFFB71E7B, 0xFFB53120, 0xFF994E00, 0xFF6B6D00, 0xFF388700, 0xFF0C9300, 0xFF008F32, 0xFF007C8D, 0xFF000000, 0xFF000000, 0xFF000000,
            0xFFFFFEFF, 0xFF64B0FF, 0xFF9290FF, 0xFFC676FF, 0xFFF36AFF, 0xFFFE6ECC, 0xFFFE8170, 0xFFEA9E22, 0xFFBCBE00, 0xFF88D800, 0xFF5CE430, 0xFF45E082, 0xFF48CDDE, 0xFF4F4F4F, 0xFF000000, 0xFF000000,
            0xFFFFFEFF, 0xFFC0DFFF, 0xFFD3D2FF, 0xFFE8C8FF, 0xFFFBC2FF, 0xFFFEC4EA, 0xFFFECCC5, 0xFFF7D8A5, 0xFFE4E594, 0xFFCFEF96, 0xFFBDF4AB, 0xFFB3F3CC, 0xFFB5EBF2, 0xFFB8B8B8, 0xFF000000, 0xFF000000,
        };

        internal sealed class Palettes { public uint[] Nes, Pce; }

        /// <summary>The state's thumbnail made again when it is missing or older than the state. Never throws.</summary>
        public static void Refresh(string state, string exe, Palettes palettes = null)
        {
            try
            {
                if (string.IsNullOrEmpty(state) || !File.Exists(state)) return;
                var png = LbipStateThumbs.PathOf(state);
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(state)) return;
                using var picture = Picture(File.ReadAllBytes(state), palettes ?? PalettesOf(exe));
                // No picture in the state: the old one would be of another moment.
                if (picture == null) { LbipStateThumbs.Remove(state); return; }
                if (!LbipStateThumbs.FromBitmap(picture, state)) return;
                LbIntegrations.Saves.SaveScan.Touched(png);   // a big listing sees it
                Log.Info("thumbnail: " + Path.GetFileName(png));
            }
            catch (Exception ex) { Log.Warn("thumbnail of " + Path.GetFileName(state) + " not made", ex); }
        }

        /// <summary>The palettes of the install's settings.json (the NES default when it has none).</summary>
        internal static Palettes PalettesOf(string exe)
        {
            var p = new Palettes { Nes = NesDefault };
            try
            {
                if (string.IsNullOrEmpty(exe)) return p;
                var json = MesenJson.Load(MesenLaunch.SettingsOf(exe));
                if (!json.Readable || !json.Exists) return p;
                p.Nes = Colors(json.Get("Nes/UserPalette"), 64, 512) ?? NesDefault;
                p.Pce = Colors(json.Get("PcEngine/Palette"), 512);
            }
            catch { }
            return p;
        }

        private static uint[] Colors(JsonNode node, params int[] sizes)
        {
            if (node is not JsonArray a || !sizes.Contains(a.Count)) return null;
            try { return a.Select(v => (uint)v.GetValue<long>()).ToArray(); } catch { return null; }
        }

        /// <summary>The frame of a state as a 32-bit picture; null when the file is not a Mesen state or its frame is not one
        /// the plugin reads.</summary>
        internal static Bitmap Picture(byte[] state, Palettes palettes)
        {
            var f = Frame(state);
            if (f == null) return null;
            var (console, w, h, px) = f.Value;
            var nes = palettes?.Nes ?? NesDefault;
            var pce = palettes?.Pce;
            if (console == PcEngine && pce == null) return null;
            var argb = new int[w * h];
            for (int i = 0; i < argb.Length; i++)
            {
                int p = px[i];
                uint c;
                switch (console)
                {
                    case Nes: c = nes[p & (nes.Length == 512 ? 0x1FF : 0x3F)]; break;
                    case PcEngine:
                        c = pce[p & 0x1FF];
                        if ((p & 0x200) != 0)
                        {
                            int y = (int)Math.Clamp(0.299 * ((c >> 16) & 0xFF) + 0.587 * ((c >> 8) & 0xFF) + 0.114 * (c & 0xFF), 0, 255);
                            c = (uint)((y << 16) | (y << 8) | y);
                        }
                        break;
                    case Ws: c = (uint)((Four(p >> 8) << 16) | (Four(p >> 4) << 8) | Four(p)); break;
                    case Snes: case Gameboy: case Sms: case Gba: c = (uint)((Five(p) << 16) | (Five(p >> 5) << 8) | Five(p >> 10)); break;
                    default: return null;
                }
                argb[i] = unchecked((int)(0xFF000000 | c));
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) Marshal.Copy(argb, y * w, data.Scan0 + y * data.Stride, w);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        private static int Five(int v) { v &= 0x1F; return (v << 3) | (v >> 2); }
        private static int Four(int v) { v &= 0xF; return (v << 4) | v; }

        /// <summary>The console and the raw frame of a state; null when it is not one.</summary>
        internal static (int Console, int Width, int Height, ushort[] Pixels)? Frame(byte[] s)
        {
            if (s == null || s.Length < 35 || s[0] != 'M' || s[1] != 'S' || s[2] != 'S') return null;
            uint U(int at) => BitConverter.ToUInt32(s, at);
            uint format = U(7), console = U(11), size = U(15), w = U(19), h = U(23), compressed = U(31);
            if (format < 3 || console > 6 || w == 0 || h == 0 || w > 2048 || h > 2048 || size != w * h * 2) return null;
            if (compressed == 0 || compressed > 2 * 1024 * 1024 || 35 + (long)compressed > s.Length) return null;
            byte[] raw;
            using (var input = new MemoryStream(s, 35, (int)compressed))
            using (var z = new ZLibStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream((int)size))
            {
                z.CopyTo(output);
                raw = output.ToArray();
            }
            if (raw.Length < size) return null;
            var px = new ushort[w * h];
            Buffer.BlockCopy(raw, 0, px, 0, (int)size);
            return ((int)console, (int)w, (int)h, px);
        }

        /// <summary>The thumbnails of the game's states made again - once the Mesen of a launch has come and gone.</summary>
        public static void RefreshAll(string exe, IEnumerable<string> states)
        {
            try
            {
                var palettes = PalettesOf(exe);
                foreach (var state in states ?? Enumerable.Empty<string>()) Refresh(state, exe, palettes);
            }
            catch (Exception ex) { Log.Warn("thumbnails after the session", ex); }
        }
    }
}
