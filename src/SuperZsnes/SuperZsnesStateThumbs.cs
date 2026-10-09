// A thumbnail beside each save state (Mehdi, 07/10) - LbipStateThumbs's file, <name>.szst0.png - taken from the
// emulator's OWN picture of the state, not from a capture of its window.
//
// THE PICTURE: a state is a .NET BinaryFormatter stream of MasterExecutor+SaveStateMaster, and its "screenshot" field is
// a PNG, whole, as a byte array (measured 07/10 on a .szst-last of Super Mario Kart: 256x224, at byte 271, its 67 471
// bytes ending with IEND). It is found by its signature and its chunks walked to IEND: the bytes are the thumbnail as
// they are - nothing decoded, nothing scaled.
//
// WHEN: made again whenever it is missing or older than its state - when LaunchBox lists the states (so before it backs
// one up), after a restore, and once SUPER ZSNES has quit after a launch (AfterSession). No watch, no capture. The
// resume state (-last) gets none: it is not listed (SuperZsnesSaves).

using System;
using System.IO;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesStateThumbs
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>The state's thumbnail made again when it is missing or older than the state. Never throws.</summary>
        public static void Refresh(string state)
        {
            try
            {
                if (string.IsNullOrEmpty(state) || !File.Exists(state)) return;
                var png = LbipStateThumbs.PathOf(state);
                if (File.Exists(png) && File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(state)) return;
                var picture = Screenshot(File.ReadAllBytes(state));
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

        /// <summary>Once the SUPER ZSNES of this launch has come and gone (two minutes to appear - Unity starts slowly),
        /// the thumbnails of the game's states made again.</summary>
        public static void AfterSession(string exe, string rom)
        {
            if (string.IsNullOrEmpty(exe) || string.IsNullOrWhiteSpace(rom)) return;
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 240 && !SuperZsnesScreenSession.Running(); i++) await Task.Delay(500).ConfigureAwait(false);
                    while (SuperZsnesScreenSession.Running()) await Task.Delay(1000).ConfigureAwait(false);
                    foreach (var state in SuperZsnesPlugin.StatesOf(exe, rom)) Refresh(state);
                }
                catch (Exception ex) { Log.Warn("thumbnails after the session", ex); }
            });
        }

        /// <summary>The first PNG in the state, signature to IEND, or null.</summary>
        private static byte[] Screenshot(byte[] data)
        {
            int start = IndexOf(data, Signature);
            if (start < 0) return null;
            long at = start + Signature.Length;
            while (at + 12 <= data.Length)
            {
                long length = (long)data[at] << 24 | (long)data[at + 1] << 16 | (long)data[at + 2] << 8 | data[at + 3];
                bool end = data[at + 4] == 'I' && data[at + 5] == 'E' && data[at + 6] == 'N' && data[at + 7] == 'D';
                at += 12 + length;   // length, type, data, CRC
                if (at > data.Length) return null;
                if (end)
                {
                    var png = new byte[at - start];
                    Array.Copy(data, start, png, 0, png.Length);
                    return png;
                }
            }
            return null;
        }

        private static int IndexOf(byte[] data, byte[] what)
        {
            for (int i = 0; i + what.Length <= data.Length; i++)
            {
                int j = 0;
                while (j < what.Length && data[i + j] == what[j]) j++;
                if (j == what.Length) return i;
            }
            return -1;
        }
    }
}
