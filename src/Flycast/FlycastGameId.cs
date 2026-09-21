// The disc id of a Dreamcast game, from whatever container the user has.
//
// Five shapes reach us, and only two of them hold the data themselves:
//
//   .chd            one file, compressed. Tracks packed contiguously from frame 0.
//   .gdi            a TEXT index; the sectors live in sibling track files (track03.bin...).
//   .cue            a TEXT sheet; likewise, FILE "..." BINARY lines point at the data.
//   .cdi .iso .bin  one file holding sectors directly.
//   anything else   not a Dreamcast disc as far as we are concerned.
//
// For the text indexes we do NOT interpret the geometry. Parsing a .gdi's LBAs and sector sizes to
// compute where IP.BIN starts would be work in service of a scan that finds it anyway: we take the
// files the index names, in the order it names them, and let Ipbin look. Data tracks are preferred
// when the index says which they are, because scanning an audio track is wasted reading, not
// because the answer would differ.
//
// Every answer is cached on path|size|mtime. GetSaves runs over a whole library, and a CHD scan is
// megabytes of decompression - doing it once per page render would be felt.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Flycast
{
    internal static class FlycastGameId
    {
        private static readonly Dictionary<string, string> Cache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheGate = new object();

        /// <summary>Extensions we will look inside for a Dreamcast disc id.</summary>
        private static readonly string[] DiscExtensions =
        { ".chd", ".gdi", ".cue", ".cdi", ".iso", ".bin", ".img", ".ccd", ".mds" };

        public static bool LooksLikeDisc(string romPath)
        {
            try
            {
                var ext = Path.GetExtension(romPath ?? "");
                return ext != null
                    && DiscExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>The product number of this disc, or null. Never throws.</summary>
        public static string Of(string romPath)
        {
            if (string.IsNullOrWhiteSpace(romPath)) return null;

            string key;
            try
            {
                var info = new FileInfo(romPath);
                if (!info.Exists) return null;
                key = info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }

            lock (CacheGate)
                if (Cache.TryGetValue(key, out var cached)) return cached;

            string product = null;
            try { product = Read(romPath); }
            catch (Exception ex) { Log.Warn("could not read the disc id of " + Path.GetFileName(romPath), ex); }

            if (product != null)
                Log.Info("disc id " + product + " <- " + Path.GetFileName(romPath));

            lock (CacheGate) Cache[key] = product;
            return product;
        }

        private static string Read(string romPath)
        {
            var ext = (Path.GetExtension(romPath) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".chd": return FromChd(romPath);
                case ".gdi": return FromIndex(romPath, ParseGdi);
                case ".cue": return FromIndex(romPath, ParseCue);
                default: return FromFile(romPath);
            }
        }

        // ── one file holding sectors ─────────────────────────────────────────

        private static string FromFile(string path)
        {
            FileStream fs;
            try { fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
            catch { return null; }
            using (fs)
                return Ipbin.ProductFrom((offset, length) => ReadAt(fs, offset, length), fs.Length);
        }

        private static byte[] ReadAt(Stream stream, long offset, int length)
        {
            if (offset < 0 || length <= 0) return null;
            try
            {
                if (offset >= stream.Length) return null;
                stream.Position = offset;
                int want = (int)Math.Min(length, stream.Length - offset);
                var buffer = new byte[want];
                int got = 0;
                while (got < want)
                {
                    int n = stream.Read(buffer, got, want - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got == 0) return null;
                if (got == want) return buffer;
                var shorter = new byte[got];
                Buffer.BlockCopy(buffer, 0, shorter, 0, got);
                return shorter;
            }
            catch { return null; }
        }

        // ── CHD ──────────────────────────────────────────────────────────────

        /// <summary>The same scan over a CHD's decompressed image. CHDSharp only ever sees primitives
        /// across this boundary, and the whole library is internalized into this assembly, so nothing
        /// of it is visible to the host or to another plugin.</summary>
        private static string FromChd(string path)
        {
            var status = CHDSharp.ChdFile.Open(path, out var chd);
            if (chd == null) { Log.Warn("CHD would not open (" + status + "): " + Path.GetFileName(path)); return null; }
            using (chd)
            {
                return Ipbin.ProductFrom((offset, length) =>
                {
                    var buffer = new byte[length];
                    return chd.Read((ulong)offset, buffer, 0, length) == CHDSharp.Models.ChdError.Chderrnone
                        ? buffer : null;
                }, (long)chd.TotalBytes);
            }
        }

        // ── text indexes ─────────────────────────────────────────────────────

        /// <summary>Scan the track files an index names, data tracks first, and take the first answer.</summary>
        private static string FromIndex(string indexPath, Func<string, IEnumerable<IndexedTrack>> parse)
        {
            List<IndexedTrack> tracks;
            try { tracks = parse(indexPath).ToList(); }
            catch (Exception ex) { Log.Warn("could not read " + Path.GetFileName(indexPath), ex); return null; }

            if (tracks.Count == 0)
            {
                Log.Warn(Path.GetFileName(indexPath) + " names no track file");
                return null;
            }

            var folder = Path.GetDirectoryName(Path.GetFullPath(indexPath)) ?? "";
            foreach (var track in tracks.OrderBy(t => t.IsAudio ? 1 : 0).ThenBy(t => t.Number))
            {
                string full;
                try { full = Path.GetFullPath(Path.Combine(folder, track.FileName)); }
                catch { continue; }
                // A track file must stay inside the index's own folder tree: an index is user data and
                // a "../../.." in it should not turn into a read anywhere on the disk.
                if (!full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(full)) continue;

                var product = FromFile(full);
                if (product != null) return product;
            }
            return null;
        }

        private readonly struct IndexedTrack
        {
            public IndexedTrack(int number, string fileName, bool isAudio)
            { Number = number; FileName = fileName; IsAudio = isAudio; }
            public int Number { get; }
            public string FileName { get; }
            public bool IsAudio { get; }
        }

        /// <summary>A .gdi line is: track, LBA, type, sector size, file name, offset. Type 4 is data and
        /// 0 is audio. The file name may be quoted when it holds spaces.</summary>
        private static IEnumerable<IndexedTrack> ParseGdi(string path)
        {
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                var fields = Tokenize(line);
                if (fields.Count < 5) continue;
                if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    continue;                                   // the first line is just the track count
                bool isAudio = int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                            out var type) && type == 0;
                yield return new IndexedTrack(number, fields[4], isAudio);
            }
        }

        /// <summary>A .cue names its data files with FILE "name" BINARY. The TRACK lines that follow
        /// say AUDIO or MODE1/MODE2; we carry that onto the most recent FILE.</summary>
        private static IEnumerable<IndexedTrack> ParseCue(string path)
        {
            string pending = null;
            int number = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    var fields = Tokenize(line);
                    if (fields.Count >= 2) pending = fields[1];
                    continue;
                }
                if (pending != null && line.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
                {
                    var fields = Tokenize(line);
                    bool isAudio = fields.Count >= 3
                                   && fields[2].IndexOf("AUDIO", StringComparison.OrdinalIgnoreCase) >= 0;
                    yield return new IndexedTrack(++number, pending, isAudio);
                    pending = null;
                }
            }
            if (pending != null) yield return new IndexedTrack(++number, pending, false);
        }

        /// <summary>Split on whitespace, honouring double quotes around a file name.</summary>
        private static List<string> Tokenize(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            foreach (var c in line)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0) { fields.Add(sb.ToString()); sb.Clear(); }
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) fields.Add(sb.ToString());
            return fields;
        }
    }
}
