// A thumbnail beside each save state (Mehdi, 07/10) - LbipStateThumbs's file, <name>.000.png - taken from Snes9x's OWN
// picture of the state, not from a capture of its window.
//
// THE PICTURE: with SnapshotScreenshots on (as it ships, snes9x.conf: "on to save the screenshot in each snapshot"),
// every state carries the frame on screen (snapshot.cpp, S9xFreezeToStream, read 07/10). The file is gzip (Snes9x
// opens it with gzopen; a plain one is read too): "#!s9xsnp:NNNN\n", then blocks "XXX:NNNNNN:" + data - the length in
// six digits, or packed big-endian into bytes 6..9 when the 5th is '-'. The "SHO" block: width and height (big-endian
// uint16), interlaced (1 byte), then the rows, 3 bytes a pixel, red green blue on 5 bits each. A hires or interlaced
// frame (512 wide, 448 high) is halved to the 256x224 of the others.
//
// WHEN: made again whenever it is missing or older than its state - when LaunchBox lists the states (so before it backs
// one up), after a restore, and once RASnes9x has quit after a launch (AfterSession). No watch, no capture.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xStateThumbs
    {
        private const string Magic = "#!s9xsnp";

        /// <summary>The state's thumbnail made again when it is missing or older than the state. Never throws.</summary>
        public static void Refresh(string state)
        {
            try
            {
                if (string.IsNullOrEmpty(state) || !File.Exists(state)) return;
                var png = LbipStateThumbs.PathOf(state);
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(state)) return;
                using (var bmp = Screenshot(state))
                {
                    // No picture in the state (SnapshotScreenshots off): the old one would be of another moment.
                    if (bmp == null) { LbipStateThumbs.Remove(state); return; }
                    var tmp = png + ".part";
                    bmp.Save(tmp, ImageFormat.Png);
                    File.Move(tmp, png, overwrite: true);
                    LbIntegrations.Saves.SaveScan.Touched(png);   // a big listing sees it
                }
                Log.Info("thumbnail: " + Path.GetFileName(png));
            }
            catch (Exception ex) { Log.Warn("thumbnail of " + Path.GetFileName(state) + " not made", ex); }
        }

        /// <summary>Once the RASnes9x of this launch has come and gone (a minute to appear), the thumbnails of the game's
        /// states made again.</summary>
        public static void AfterSession(string exe, string rom)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrWhiteSpace(rom)) return;
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 120 && !RaSnes9xLaunch.Running(exe); i++) await Task.Delay(500).ConfigureAwait(false);
                    while (RaSnes9xLaunch.Running(exe)) await Task.Delay(1000).ConfigureAwait(false);
                    foreach (var state in RaSnes9xPlugin.StatesOf(exe, rom)) Refresh(state);
                }
                catch (Exception ex) { Log.Warn("thumbnails after the session", ex); }
            });
        }

        /// <summary>The frame the state carries, or null.</summary>
        private static Bitmap Screenshot(string state)
        {
            var data = File.ReadAllBytes(state);
            if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b)
                using (var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    data = ms.ToArray();
                }
            if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, Magic.Length) != Magic) return null;

            int at = Magic.Length + 6;   // ":NNNN\n"
            while (at + 11 <= data.Length)
            {
                if (data[at + 3] != ':') return null;
                var name = Encoding.ASCII.GetString(data, at, 3);
                int size;
                if (data[at + 4] == '-') size = data[at + 6] << 24 | data[at + 7] << 16 | data[at + 8] << 8 | data[at + 9];
                else if (!int.TryParse(Encoding.ASCII.GetString(data, at + 4, 6), out size)) return null;
                at += 11;
                if (size < 0 || at + (long)size > data.Length) return null;
                if (name == "SHO") return Picture(data, at, size);
                at += size;
            }
            return null;
        }

        private static Bitmap Picture(byte[] data, int at, int size)
        {
            if (size < 5) return null;
            int w = data[at] << 8 | data[at + 1], h = data[at + 2] << 8 | data[at + 3];
            if (w < 16 || h < 16 || w > 512 || h > 478 || 5 + w * h * 3 > size) return null;
            at += 5;
            var full = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var bits = full.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[w * 3];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++, at += 3)
                    {
                        row[x * 3 + 0] = Five(data[at + 2]);
                        row[x * 3 + 1] = Five(data[at + 1]);
                        row[x * 3 + 2] = Five(data[at]);
                    }
                    Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, row.Length);
                }
            }
            finally { full.UnlockBits(bits); }

            int ow = w > 256 ? w / 2 : w, oh = h > 240 ? h / 2 : h;
            if (ow == w && oh == h) return full;
            using (full)
            {
                var small = new Bitmap(ow, oh, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, 0, 0, ow, oh);
                }
                return small;
            }
        }

        private static byte Five(int v) { v &= 31; return (byte)(v << 3 | v >> 2); }
    }
}
