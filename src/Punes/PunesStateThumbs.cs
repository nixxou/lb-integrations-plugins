// A thumbnail beside each save state (Mehdi, 07/10) - LbipStateThumbs's file, <name>.p00.png - taken from puNES's OWN
// picture of the state, never from a capture of its window.
//
// THE PICTURE (read in src\core\save_slot.c, v0.111: save_slot_operation, save_slot_element; measured 18/10 on the lab's
// Zelda II .p00, 22 284 bytes): a state starts with three little-endian 32-bit words - the version (100, SAVE_VERSION),
// the ROM's CRC32, and the SIZE of the state's data, header included; from that offset to the end of the file puNES
// writes the preview, a complete PNG (gui_save_slot_preview_to_png - 256x240, the NES's frame, IEND at the end of the
// file). It is copied as it is: no decoding, no scaling.
//
// WHEN: made again whenever it is missing or older than its state - when LaunchBox lists the states (so before it backs
// one up), after a restore, and once puNES has quit after a launch (AfterSession). No watch, no capture.

using System;
using System.IO;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Punes
{
    internal static class PunesStateThumbs
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>The state's thumbnail made again when it is missing or older than the state. Never throws.</summary>
        public static void Refresh(string state)
        {
            try
            {
                if (string.IsNullOrEmpty(state) || !File.Exists(state)) return;
                var png = LbipStateThumbs.PathOf(state);
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(state)) return;
                var picture = Preview(File.ReadAllBytes(state));
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

        /// <summary>The PNG a state carries, or null: the header's size word (offset 8) points at it, and it runs to the end.</summary>
        internal static byte[] Preview(byte[] data)
        {
            if (data == null || data.Length < 12 + PngSignature.Length) return null;
            uint version = BitConverter.ToUInt32(data, 0);
            long at = BitConverter.ToUInt32(data, 8);
            if (version < 100 || at < 12 || at + PngSignature.Length > data.Length) return null;
            for (int i = 0; i < PngSignature.Length; i++) if (data[at + i] != PngSignature[i]) return null;
            var png = new byte[data.Length - at];
            Array.Copy(data, at, png, 0, png.Length);
            return png;
        }

        /// <summary>Once the puNES of this launch has come and gone (a minute to appear), the thumbnails of the game's states
        /// made again.</summary>
        public static void AfterSession(string exe, string rom, string platform)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrWhiteSpace(rom)) return;
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 120 && !PunesLaunch.Running(exe); i++) await Task.Delay(500).ConfigureAwait(false);
                    while (PunesLaunch.Running(exe)) await Task.Delay(1000).ConfigureAwait(false);
                    foreach (var state in PunesPlugin.StatesOf(exe, rom, platform)) Refresh(state);
                }
                catch (Exception ex) { Log.Warn("thumbnails after the session", ex); }
            });
        }
    }
}
