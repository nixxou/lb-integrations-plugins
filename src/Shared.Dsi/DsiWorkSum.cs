// What the working image was last synchronised with, so that somebody else changing the save is
// noticed instead of being overwritten.
//
// THE DANGEROUS MOMENT IS THE CAPTURE. There is no "the emulator has quit" event, so a session is
// written down at the START of the next launch: the image is opened, compared against the reference
// walk, and whatever differs becomes the state folder. That is correct exactly as long as the image
// is the newest thing on disk - and it stops being true the moment anything writes the state folder
// from outside. A RomM sync, a restore from another machine, a file dropped in by hand: the state
// folder now holds the new save, the image still holds the old session, and the next launch captures
// the old session ON TOP of the new save. Nothing errors. The sync is simply undone.
//
// SO THE IMAGE CARRIES A RECEIPT. Beside work.bin, work.sum lists every file of the state folder the
// image was last agreed with, by CRC32, size and name. Before a capture, the folder is summed again:
// same, and the capture is the newest thing and goes ahead; different, and somebody else got there
// first - so the image is dropped rather than written, and the next launch rebuilds around the save
// that arrived. An image is always rebuildable; a save that came from elsewhere is not.
//
// CRC32 AND NOT A CRYPTOGRAPHIC HASH, on purpose. This answers "did this change", not "is this
// what somebody claims it is" - there is no adversary here, only two writers who do not know about
// each other. The state folder is tens of kilobytes, so the cost is invisible either way, and CRC32
// needs no dependency: System.IO.Hashing is a package, and this assembly is merged and internalized,
// so every reference it does not take is a collision it cannot cause in somebody else's plugin.
//
// NO RECEIPT MEANS NO OPINION. An installation from before this existed, or one whose sum was lost,
// answers "not moved" and carries on. Guessing that a save has changed, when the truth is that we
// never wrote down what it looked like, would throw away a perfectly good image on every launch.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LbIntegrations.Dsi;

namespace LbIntegrations.Dsi
{
    internal static class DsiWorkSum
    {
        /// <summary>Beside work.bin, because it describes that file and travels with it. Deleting
        /// the image without this would leave a receipt for something that is gone.</summary>
        public const string FileName = "work.sum";

        public static string PathFor(DsiHost layout)
        {
            var dir = DsiWorkspace.DsiDir(layout);
            return dir == null ? null : Path.Combine(dir, FileName);
        }

        /// <summary>Write down the state folder as it stands, as the thing the image now agrees
        /// with. Called at the two moments the two are in step: just after a capture has written the
        /// folder out of the image, and just after a rebuild has put the folder back into it.</summary>
        public static void Write(DsiHost layout, string titleId)
        {
            try
            {
                var path = PathFor(layout);
                if (path == null || string.IsNullOrWhiteSpace(titleId)) return;
                Atomic.WriteBytes(path, Encoding.UTF8.GetBytes(Of(layout, titleId)));
            }
            catch (Exception ex) { DsiLog.Verbose("could not write " + FileName + " - " + ex.Message); }
        }

        /// <summary>Has the save been written by something other than us since the image was last
        /// agreed with it? <paramref name="what"/> says how, for the log.
        ///
        /// FALSE WHENEVER WE CANNOT TELL. No receipt, no folder to read, an unreadable file: all of
        /// them mean "no opinion", never "assume the worst". Being wrong in this direction costs a
        /// rebuild nobody asked for, on every launch, forever.</summary>
        public static bool Moved(DsiHost layout, string titleId, out string what)
        {
            what = null;
            try
            {
                var path = PathFor(layout);
                if (path == null || !File.Exists(path)) return false;
                if (string.IsNullOrWhiteSpace(titleId)) return false;

                var written = File.ReadAllText(path);
                var now = Of(layout, titleId);
                if (string.Equals(written.Trim(), now.Trim(), StringComparison.Ordinal)) return false;

                what = Difference(written, now);
                return true;
            }
            catch (Exception ex) { DsiLog.Verbose("could not read " + FileName + " - " + ex.Message); return false; }
        }

        /// <summary>Drop the receipt. Always together with the image it describes.</summary>
        public static void Forget(DsiHost layout)
        {
            try
            {
                var path = PathFor(layout);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // ── the receipt itself ───────────────────────────────────────────────

        /// <summary>The title, then one line per member of the save, sorted by name so the text is
        /// the same whatever order they come back in.
        ///
        /// THE CONTENT, NEVER THE CONTAINER. A save is one zip now, and hashing its bytes would have
        /// been shorter - but it would tie this receipt to whatever a zip writer decides to do with
        /// its headers, and this receipt is what decides whether a session is thrown away. So the
        /// entries are read out and summed one by one, exactly as the loose files used to be. The
        /// determinism of the archive is a convenience for the host; it is not load-bearing here.
        ///
        /// A save that cannot be opened yields just the title, which reads as a change - the same
        /// answer the folder form gave for a file something else was holding open.</summary>
        private static string Of(DsiHost layout, string titleId)
        {
            var lines = new List<string> { titleId };
            try
            {
                var save = DsiWorkspace.SavePathFor(layout, titleId);
                foreach (var entry in DsiSaveFile.Entries(save))
                    lines.Add(Crc32(entry.Value).ToString("x8", CultureInfo.InvariantCulture)
                              + "\t" + entry.Value.Length.ToString(CultureInfo.InvariantCulture)
                              + "\t" + entry.Key);
            }
            catch (Exception ex) { DsiLog.Verbose("could not sum a save - " + ex.Message); }
            return string.Join("\n", lines);
        }

        /// <summary>A short account of what changed, for the log line that explains the rebuild.</summary>
        private static string Difference(string written, string now)
        {
            try
            {
                var before = Names(written);
                var after = Names(now);

                int added = 0, gone = 0, changed = 0;
                foreach (var pair in after)
                    if (!before.TryGetValue(pair.Key, out var sum)) added++;
                    else if (sum != pair.Value) changed++;
                foreach (var pair in before)
                    if (!after.ContainsKey(pair.Key)) gone++;

                var parts = new List<string>();
                if (changed > 0) parts.Add(changed + " changed");
                if (added > 0) parts.Add(added + " added");
                if (gone > 0) parts.Add(gone + " removed");
                return parts.Count == 0 ? "it is for another title" : string.Join(", ", parts);
            }
            catch { return "it no longer matches"; }
        }

        private static Dictionary<string, string> Names(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                var parts = line.Split('\t');
                if (parts.Length == 3) map[parts[2]] = parts[0] + "\t" + parts[1];
            }
            return map;
        }

        // ── CRC32, the ordinary one ──────────────────────────────────────────

        private static uint[] _table;

        private static uint Crc32(byte[] data)
        {
            var table = _table ??= BuildTable();
            uint crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }
}
