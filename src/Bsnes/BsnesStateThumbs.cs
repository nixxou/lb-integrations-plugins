// A thumbnail beside the save states (Mehdi, 07/10) - LbipStateThumbs's file, <name>.bsz.png - taken from bsnes's OWN
// picture of the state, not from a capture of its window.
//
// THE PICTURE: every state bsnes writes carries a preview (target-bsnes/program/states.cpp, saveState, read 07/10):
// three little-endian uint32 - the signature 0x5a220000, the size of the serialized state, the size of the preview -
// then the state, then the preview: 256x240, 15 bits a pixel (red 0x7c00, green 0x03e0, blue 0x001f, little-endian),
// packed by nall's RLE<2>. Empty when the state was taken before the first frame: no thumbnail then.
//
// ONE PICTURE FOR THE .BSZ: the zip holds every slot, and LaunchBox sees one file. Its picture is the state written last:
// bsnes writes the zip again with the new entry at the end and the others in their order, so the last entry - leaving
// out Quick/Undo and Quick/Redo, which LOADING a state writes - is the newest save.
//
// WHEN: made again whenever it is missing or older than the .bsz - when LaunchBox lists the states (so before it backs
// one up), after a restore, and once bsnes has quit after a launch (AfterSession). No watch, no capture.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Bsnes
{
    internal static class BsnesStateThumbs
    {
        private const uint Signature = 0x5a220000;
        private const int Width = 256, Height = 240;

        /// <summary>One of our thumbnails: "&lt;name&gt;.bsz.png".</summary>
        public static bool IsThumbOfStates(string path)
            => LbipStateThumbs.IsThumb(path) && path.Substring(0, path.Length - LbipStateThumbs.Suffix.Length).EndsWith(".bsz", StringComparison.OrdinalIgnoreCase);

        /// <summary>The .bsz's thumbnail made again when it is missing or older than the .bsz. Never throws.</summary>
        public static void Refresh(string bsz)
        {
            try
            {
                if (string.IsNullOrEmpty(bsz) || !File.Exists(bsz)) return;
                var png = LbipStateThumbs.PathOf(bsz);
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(bsz)) return;
                using (var bmp = Preview(bsz))
                {
                    // No picture in the newest state: the old one would be of another moment.
                    if (bmp == null) { LbipStateThumbs.Remove(bsz); return; }
                    var tmp = png + ".part";
                    bmp.Save(tmp, ImageFormat.Png);
                    File.Move(tmp, png, overwrite: true);
                    LbIntegrations.Saves.SaveScan.Touched(png);   // a big listing sees it
                }
                Log.Info("thumbnail: " + Path.GetFileName(png));
            }
            catch (Exception ex) { Log.Warn("thumbnail of " + Path.GetFileName(bsz) + " not made", ex); }
        }

        /// <summary>Once the bsnes of this launch has come and gone (a minute to appear), the game's thumbnail made again.</summary>
        public static void AfterSession(string exe, string rom)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrWhiteSpace(rom)) return;
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 120 && !BsnesLaunch.Running(exe); i++) await Task.Delay(500).ConfigureAwait(false);
                    while (BsnesLaunch.Running(exe)) await Task.Delay(1000).ConfigureAwait(false);
                    var dir = BsnesPlugin.FoldersFor(exe, rom).StateDir;
                    if (dir != null) Refresh(Path.Combine(dir, BsnesPlugin.NameOf(rom) + ".bsz"));
                }
                catch (Exception ex) { Log.Warn("thumbnail after the session", ex); }
            });
        }

        /// <summary>The preview of the newest state in the .bsz, or null.</summary>
        private static Bitmap Preview(string bsz)
        {
            byte[] data;
            using (var zip = ZipFile.OpenRead(bsz))
            {
                var entry = zip.Entries.LastOrDefault(e => e.FullName.EndsWith(".bst", StringComparison.OrdinalIgnoreCase)
                                                           && !e.FullName.Equals("Quick/Undo.bst", StringComparison.OrdinalIgnoreCase)
                                                           && !e.FullName.Equals("Quick/Redo.bst", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;
                using (var s = entry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }
            }
            if (data.Length < 12 || BitConverter.ToUInt32(data, 0) != Signature) return null;
            long serialized = BitConverter.ToUInt32(data, 4), preview = BitConverter.ToUInt32(data, 8);
            if (preview == 0 || 12 + serialized + preview > data.Length) return null;
            var pixels = Rle2(data, (int)(12 + serialized), (int)preview);
            if (pixels == null || pixels.Length < Width * Height * 2) return null;

            var bmp = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            var bits = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[Width * 3];
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int at = (y * Width + x) * 2, c = pixels[at] | pixels[at + 1] << 8;
                        row[x * 3 + 0] = Five(c);
                        row[x * 3 + 1] = Five(c >> 5);
                        row[x * 3 + 2] = Five(c >> 10);
                    }
                    Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, row.Length);
                }
            }
            finally { bmp.UnlockBits(bits); }
            return bmp;
        }

        private static byte Five(int v) { v &= 31; return (byte)(v << 3 | v >> 2); }

        /// <summary>nall's Decode::RLE&lt;2&gt; (nall/decode/rle.hpp): the size as a little-endian uint64, then runs - a byte
        /// under 128 is that many words plus one as they are, one of 128 or more a word repeated (its low 7 bits) + 2
        /// times. Null when it cannot be one.</summary>
        private static byte[] Rle2(byte[] src, int at, int length)
        {
            if (length < 8) return null;
            int i = at, end = at + length;
            ulong size = BitConverter.ToUInt64(src, i);
            i += 8;
            if (size == 0 || size > 1 << 22) return null;
            var output = new byte[size];
            int o = 0;
            byte Load() => i < end ? src[i++] : (byte)0;
            void Put(byte a, byte b) { if (o < output.Length) output[o++] = a; if (o < output.Length) output[o++] = b; }
            while (o < output.Length && i < end)
            {
                int control = Load();
                if (control < 128)
                    for (int n = control + 1; n > 0; n--) { var a = Load(); var b = Load(); Put(a, b); }
                else
                {
                    var a = Load(); var b = Load();
                    for (int n = (control & 127) + 2; n > 0; n--) Put(a, b);
                }
            }
            return output;
        }
    }
}
