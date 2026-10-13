// A Dreamcast CHD Demul cannot read, handed to it as one it can (Mehdi, 06/10: "détecter un format invalide et faire un proxy").
//
// WHAT DEMUL READS, measured 06/10 by calling its own libchd.dll (MAME's CHD code of 2017, behind gdrCHD.dll) on a GD-ROM:
// v5 with CD LZMA, CD Deflate and CD FLAC - opened, its nine tracks, "SEGA SEGAKATANA" at LBA 45000; the same disc
// UNCOMPRESSED - the same; the same disc in CD Zstandard (chdman 0.289 "-c cdzs,cdfl") - an access violation inside open.
// The library knows zlib, LZMA, Huffman, FLAC, their CD forms and A/V Huffman (its codec names), v3 to v5 - so a CHD is
// handed over as it is unless its version is past 5 or a codec is not one of those.
//
// THE OTHERS, AS AN UNCOMPRESSED CHD v5 (Shared.Disc\ChdPlain): the same hunks decoded, the same metadata - a GD-ROM's CHGD
// tracks, their PAD frames, its high-density area, nothing reinterpreted - the same SHA-1s (checked: chdman recompressing it
// finds the source's data SHA-1). SERVED where it is when it can be - the RAM disk helper 1.14's view=chd through AIM, a
// file game.chd on a disk of its own, detached when Demul has gone (DemulSession); else COPIED once into
// <demul>\lbip-chd\, kept as a cache (20 GB, the copies used longest ago go first).

using System;
using System.IO;
using System.Linq;

namespace LbIntegrations.Demul
{
    internal static class DemulChd
    {
        /// <summary>The codecs Demul's libchd.dll has (its own names: Deflate, LZMA, Huffman, FLAC, CD Deflate, CD LZMA, CD FLAC,
        /// A/V Huffman), as the four-letter tags a v5 header names them by; 0 is "none".</summary>
        private static readonly uint[] Known = { 0, Tag("zlib"), Tag("lzma"), Tag("huff"), Tag("flac"), Tag("cdzl"), Tag("cdlz"), Tag("cdfl"), Tag("avhu") };

        private const long CacheBytes = 20L << 30;

        private static uint Tag(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
        private static string Name(uint t) => t == 0 ? "none" : new string(new[] { (char)(t >> 24), (char)(t >> 16 & 0xFF), (char)(t >> 8 & 0xFF), (char)(t & 0xFF) });

        /// <summary>Why Demul cannot read this CHD (its version, a codec it lacks), or null when it can - or when it is no CHD.
        /// By its header alone: nothing decoded.</summary>
        public static string Unreadable(string path)
        {
            try
            {
                if (!string.Equals(Path.GetExtension(path), ".chd", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var h = new byte[32];
                if (f.Read(h, 0, h.Length) < 32 || System.Text.Encoding.ASCII.GetString(h, 0, 8) != "MComprHD") return null;
                uint version = BE(h, 12);
                if (version > 5) return "a CHD v" + version + " (Demul reads up to v5)";
                if (version < 5) return null;                      // v3, v4: zlib or A/V Huffman, both known
                var codecs = Enumerable.Range(0, 4).Select(i => BE(h, 16 + 4 * i)).ToArray();
                var unknown = codecs.Where(c => !Known.Contains(c)).Select(Name).ToList();
                return unknown.Count == 0 ? null : "its codec" + (unknown.Count > 1 ? "s " : " ") + string.Join(", ", unknown) + " (Demul's CHD reader is of 2017)";
            }
            catch (Exception ex) { Log.Warn("chd: the header of " + Path.GetFileName(path) + " could not be read", ex); return null; }
        }

        private static uint BE(byte[] b, int at) => (uint)(b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]);

        /// <summary>The path to hand Demul for <paramref name="rom"/>: the file itself when Demul reads it, else the CHD served
        /// uncompressed (<paramref name="root"/> set: to detach), else its uncompressed copy in the cache. Null with
        /// <paramref name="problem"/> when none can be made.</summary>
        public static string Present(string rom, string exe, out string root, out string problem)
        {
            root = null; problem = null;
            var why = Unreadable(rom);
            if (why == null) return rom;
            var name = Path.GetFileName(rom);
            Log.Info("chd: " + name + " is " + why + " - handed over uncompressed");

            if (RamDisk.RamDrive.CanAttachChd(out var cannot))
            {
                LbIntegrations.Lbip.LbipLaunchStatus.Report("Attaching " + name + " uncompressed…");
                var file = RamDisk.RamDrive.AttachChd(rom, out var r, out var error);
                if (file != null && File.Exists(file)) { root = r; Log.Info("chd: served where it is - " + file); return file; }
                if (r != null) Release(r);
                Log.Warn("chd: " + name + " could not be served (" + (error ?? "the file did not appear") + ") - copied instead");
            }
            else Log.Info("chd: not served where it is (" + cannot + ") - copied");

            try
            {
                var cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), "lbip-chd");
                Directory.CreateDirectory(cache);
                var fi = new FileInfo(rom);
                var target = Path.Combine(cache, Safe(Path.GetFileNameWithoutExtension(rom)) + "-" + Stamp(fi) + ".chd");
                if (File.Exists(target)) { try { File.SetLastAccessTimeUtc(target, DateTime.UtcNow); } catch { } Log.Info("chd: in the cache - " + target); return target; }
                var part = target + ".part";
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (var plain = LbIntegrations.Disc.ChdPlain.Open(rom))
                {
                    Purge(cache, target, plain.Length);
                    using var o = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                    LbIntegrations.Lbip.LbipLaunchStatus.CopyStream(plain, o, plain.Length, "Decompressing " + name + " for Demul…");
                }
                File.Move(part, target, true);
                Log.Info("chd: copied uncompressed in " + watch.Elapsed.TotalSeconds.ToString("0") + " s - " + target + " (" + (new FileInfo(target).Length >> 20) + " MB)");
                return target;
            }
            catch (Exception ex)
            {
                problem = name + " is " + why + ", and its uncompressed copy could not be made: " + ex.Message;
                Log.Warn("chd: " + problem);
                return null;
            }
        }

        /// <summary>A CHD served where it is, detached - at the session's end, or when the launch goes no further.</summary>
        public static void Release(string root)
        {
            if (root == null) return;
            try
            {
                if (RamDisk.RamDrive.DetachImage(root, out var error)) Log.Info("chd: " + root + " detached");
                else Log.Warn("chd: " + root + " could not be detached - " + error);
            }
            catch (Exception ex) { Log.Warn("chd: detaching " + root, ex); }
        }

        private static void Purge(string cache, string keep, long needed)
        {
            try
            {
                var files = new DirectoryInfo(cache).GetFiles("*.chd").Where(f => !string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.LastAccessTimeUtc).ToList();
                long total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total + needed <= CacheBytes) break;
                    try { total -= f.Length; f.Delete(); Log.Info("chd: " + f.Name + " taken out of the cache"); } catch { }
                }
            }
            catch (Exception ex) { Log.Warn("chd: the cache could not be purged", ex); }
        }

        private static string Stamp(FileInfo fi)
        {
            var key = fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            using var sha = System.Security.Cryptography.SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key)), 0, 4).Replace("-", "").ToLowerInvariant();
        }

        private static string Safe(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string((name ?? "disc").Select(c => bad.Contains(c) ? '_' : c).ToArray());
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }
    }
}
