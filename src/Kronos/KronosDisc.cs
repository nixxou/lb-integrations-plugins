// A Saturn disc as the plugin reads it: its header (and Ymir's hash of it), read from the image without Kronos - YmirDisc.
//
// THE HEADER gives what Kronos names a game's states with (its product code: <product>_<slot>.yss, docs\kronos.md §4) and the
// disc's area, which decides the BIOS Kronos is given (KronosBios.PickFor).
//
// THE HASH is Ymir's (XXH3-128 of the system area and the volume descriptors): shown in the game's window, not used by Kronos.
//
// THE HEADER (LBA 0, "SEGA SEGASATURN "): product number at 0x20 (10), version at 0x2A (6), area symbols at 0x40 (10 -
// J T U B K A E L), title at 0x60 (112). The product number alone is not unique (two releases of Atlantis share one).
//
// THE IMAGES Kronos opens (§4), read here the same way - one track 1 of data, FAD 150 at its first sector:
//   .chd   CHDSharp: frames of 2448 bytes (2352 + 96 of subchannel); track 1 from frame 0 - after its pregap when the
//          CHD holds it (CHT2 "PGTYPE:V..." with a PREGAP); user data at +16 (MODE1_RAW), +24 (MODE2_RAW), +0 (MODE1);
//   .cue   its first FILE and TRACK 01 (MODE1/2048, MODE1/2352, MODE2/2352), from TRACK 01's INDEX 01;
//   .ccd   the .img beside it, raw 2352 from LBA 0;
//   .mds   the .mdf beside it, raw 2352 or 2448 (told by where the next sector's sync pattern is);
//   .iso   2048 cooked, or 2352 raw when it starts with the sync pattern.
// A disc that is none of those, or whose sector 0 is not a Saturn header, has no hash here (null) - the launch goes on,
// only what needs the hash (states, the list) has nothing to say.
//
// KEPT while the image keeps its date and length (StampedCache): the game's window, the save listing and a launch ask often.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LbIntegrations.Lbip;

namespace LbIntegrations.Kronos
{
    internal sealed class SaturnDisc
    {
        public string Path;
        public string Hash;           // Kronos's, upper-case hex; null when the image could not be read
        public string Product, Version, Area, Title;
        public string Problem;        // why there is no hash
        public override string ToString() => Title + " [" + Product + "] " + Hash;
    }

    internal static class KronosDisc
    {
        /// <summary>What Kronos opens itself (docs\kronos.md §4).</summary>
        public static readonly string[] Native = { ".chd", ".cue", ".ccd", ".mds", ".iso" };

        private static readonly byte[] Sync = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        /// <summary>A disc's hash and header, kept while the file is unchanged. Never throws.</summary>
        public static SaturnDisc Read(string path)
            => StampedCache.Get(path, "kronos.disc", () => ReadCore(path));

        private static SaturnDisc ReadCore(string path)
        {
            var disc = new SaturnDisc { Path = path };
            try
            {
                using var reader = Open(path);
                if (reader == null) { disc.Problem = "not a disc image Kronos opens"; return disc; }
                var head = reader.User(150);
                if (Encoding.ASCII.GetString(head, 0, 15) != "SEGA SEGASATURN")
                {
                    disc.Problem = "its first sector is not a Sega Saturn header";
                    return disc;
                }
                string Field(int at, int len) => Encoding.ASCII.GetString(head, at, len).Trim('\0', ' ');
                disc.Product = Field(0x20, 10);
                disc.Version = Field(0x2A, 6);
                disc.Area = Field(0x40, 10);
                disc.Title = Regex.Replace(Field(0x60, 112), @"\s{2,}", " ");

                var x = new XxHash128();
                x.Append(head);
                for (long fad = 151; fad < 166; fad++) x.Append(reader.User(fad));
                // The volume descriptors, up to the terminator - a bounded walk: a disc has a handful.
                for (long fad = 166; fad < 166 + 64; fad++)
                {
                    var u = reader.User(fad);
                    x.Append(u);
                    if (u[0] == 255 || Encoding.ASCII.GetString(u, 1, 5) != "CD001") break;
                }
                disc.Hash = Convert.ToHexString(x.GetCurrentHash());
            }
            catch (Exception ex) { disc.Problem = ex.Message; Log.Verbose("disc: " + path + " - " + ex.Message); }
            return disc;
        }

        // ── the images ───────────────────────────────────────────────────────

        private abstract class Reader : IDisposable
        {
            /// <summary>The 2048 bytes of user data of a sector of track 1.</summary>
            public abstract byte[] User(long fad);
            public virtual void Dispose() { }
        }

        private static Reader Open(string path)
        {
            switch (System.IO.Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".chd": return ChdReader.Open(path);
                case ".cue": return CueReader(path);
                case ".ccd": return Sibling(path, ".img") is string img ? RawReader.Detect(img, 0) : null;
                case ".mds": return Sibling(path, ".mdf") is string mdf ? RawReader.Detect(mdf, 0) : null;
                case ".iso": case ".bin": case ".img": return RawReader.Detect(path, 0);
                default: return null;
            }
        }

        private static string Sibling(string path, string ext)
        {
            var p = System.IO.Path.ChangeExtension(path, ext);
            return File.Exists(p) ? p : null;
        }

        /// <summary>A plain file of sectors: 2048 (cooked) or 2352 / 2448 (raw, with or without subchannel).</summary>
        private sealed class RawReader : Reader
        {
            private readonly FileStream _file;
            private readonly int _sector, _skip;
            private readonly long _start;

            private RawReader(FileStream file, long start, int sector, int skip) { _file = file; _start = start; _sector = sector; _skip = skip; }

            /// <summary>The layout told from the bytes at <paramref name="start"/> (track 1's first sector, FAD 150).</summary>
            public static RawReader Detect(string path, long start, int? sectorHint = null, int? skipHint = null)
            {
                var fs = File.OpenRead(path);
                try
                {
                    bool SyncAt(long at)
                    {
                        if (at + 16 > fs.Length) return false;
                        var b = new byte[16];
                        fs.Position = at;
                        if (fs.Read(b, 0, 16) != 16) return false;
                        return b.Take(12).SequenceEqual(Sync);
                    }
                    int sector, skip;
                    if (SyncAt(start))
                    {
                        sector = sectorHint ?? (SyncAt(start + 2352) ? 2352 : SyncAt(start + 2448) ? 2448 : 2352);
                        fs.Position = start + 15;
                        int mode = fs.ReadByte();
                        skip = skipHint ?? (mode == 2 ? 24 : 16);
                    }
                    else { sector = sectorHint ?? 2048; skip = skipHint ?? 0; }
                    return new RawReader(fs, start, sector, skip);
                }
                catch { fs.Dispose(); throw; }
            }

            public override byte[] User(long fad)
            {
                var u = new byte[2048];
                _file.Position = _start + (fad - 150) * _sector + _skip;
                int got = 0, r;
                while (got < 2048 && (r = _file.Read(u, got, 2048 - got)) > 0) got += r;
                if (got < 2048) throw new EndOfStreamException("the image ends before FAD " + fad);
                return u;
            }

            public override void Dispose() => _file.Dispose();
        }

        /// <summary>Track 1 of a .cue: its FILE, its mode, where its INDEX 01 is.</summary>
        private static Reader CueReader(string cue)
        {
            string file = null, mode = null;
            long index01 = 0;
            bool inTrack1 = false;
            foreach (var raw in File.ReadLines(cue))
            {
                var line = raw.Trim();
                var f = Regex.Match(line, "^FILE\\s+(?:\"(?<n>[^\"]+)\"|(?<n>\\S+))", RegexOptions.IgnoreCase);
                if (f.Success) { if (mode != null) break; file = f.Groups["n"].Value; continue; }
                var t = Regex.Match(line, @"^TRACK\s+(?<n>\d+)\s+(?<m>\S+)", RegexOptions.IgnoreCase);
                if (t.Success)
                {
                    if (inTrack1) break;
                    inTrack1 = int.Parse(t.Groups["n"].Value, CultureInfo.InvariantCulture) == 1;
                    if (inTrack1) mode = t.Groups["m"].Value.ToUpperInvariant();
                    continue;
                }
                var i = Regex.Match(line, @"^INDEX\s+0*1\s+(?<m>\d+):(?<s>\d+):(?<f>\d+)", RegexOptions.IgnoreCase);
                if (i.Success && inTrack1)
                    index01 = (int.Parse(i.Groups["m"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(i.Groups["s"].Value, CultureInfo.InvariantCulture)) * 75
                              + int.Parse(i.Groups["f"].Value, CultureInfo.InvariantCulture);
            }
            if (file == null || mode == null) return null;
            var bin = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cue), file);
            if (!File.Exists(bin)) throw new FileNotFoundException("the .cue names " + file + ", which is not beside it");
            int sector = mode.EndsWith("/2048") ? 2048 : mode.EndsWith("/2336") ? 2336 : mode.EndsWith("/2448") ? 2448 : 2352;
            int skip = sector == 2048 ? 0 : sector == 2336 ? 8 : mode.StartsWith("MODE2") ? 24 : 16;
            return RawReader.Detect(bin, index01 * sector, sector, skip);
        }

        /// <summary>A CHD's track 1, frame by frame (the throwaway checker's reading, measured equal to the list).</summary>
        private sealed class ChdReader : Reader
        {
            private readonly CHDSharp.ChdFile _chd;
            private readonly int _unit, _perHunk, _skip;
            private readonly long _first;
            private readonly byte[] _hunk;
            private long _current = -1;

            private ChdReader(CHDSharp.ChdFile chd, long first, int skip)
            {
                _chd = chd;
                _unit = (int)chd.UnitBytes;
                _perHunk = (int)chd.HunkBytes / _unit;
                _hunk = new byte[chd.HunkBytes];
                _first = first;
                _skip = skip;
            }

            public static ChdReader Open(string path)
            {
                var err = CHDSharp.ChdFile.Open(path, out var chd, CancellationToken.None);
                if (chd == null) throw new InvalidDataException("the CHD does not open (" + err + ")");
                try
                {
                    // Track 1's metadata (CHT2 / CHTR): its type, and a pregap the CHD holds.
                    string type = null; long pregap = 0;
                    foreach (var m in chd.Metadata ?? Array.Empty<CHDSharp.Models.ChdMetadataEntry>())
                    {
                        var text = Encoding.ASCII.GetString(m.Data ?? Array.Empty<byte>()).TrimEnd('\0');
                        if (!Regex.IsMatch(text, @"\bTRACK:1\b")) continue;
                        type = Regex.Match(text, @"\bTYPE:(\S+)").Groups[1].Value;
                        var pg = Regex.Match(text, @"\bPREGAP:(\d+)");
                        if (pg.Success && Regex.IsMatch(text, @"\bPGTYPE:V")) pregap = long.Parse(pg.Groups[1].Value, CultureInfo.InvariantCulture);
                        break;
                    }
                    int skip = type == "MODE1" || type == "MODE1/2048" ? 0 : type != null && type.StartsWith("MODE2") ? 24 : 16;
                    var reader = new ChdReader(chd, pregap, skip);
                    // No metadata saying it: the sync pattern says whether the frames are raw.
                    if (type == null)
                    {
                        var f = reader.Frame(0);
                        if (!f.Take(12).SequenceEqual(Sync)) return new ChdReader(chd, pregap, 0);
                    }
                    return reader;
                }
                catch { chd.Dispose(); throw; }
            }

            private byte[] Frame(long frame)
            {
                long h = frame / _perHunk;
                if (h != _current)
                {
                    var e = _chd.ReadHunk((uint)h, _hunk, CancellationToken.None);
                    if (e.ToString() != "Chderrnone") throw new InvalidDataException("CHD hunk " + h + ": " + e);
                    _current = h;
                }
                var f = new byte[_unit];
                Array.Copy(_hunk, (frame % _perHunk) * _unit, f, 0, _unit);
                return f;
            }

            public override byte[] User(long fad)
            {
                var f = Frame(_first + fad - 150);
                var u = new byte[2048];
                Array.Copy(f, _skip, u, 0, 2048);
                return u;
            }

            public override void Dispose() => _chd.Dispose();
        }

        // ── what the header says ─────────────────────────────────────────────

        /// <summary>The BIOS region a disc runs with: "us-eu" when its area symbols name America or Europe (U B A E L - Kronos's
        /// [System] PreferredRegionOrder, by default North America, Japan, Europe, Asia NTSC), else "jp" (J, T, K). For a word
        /// which BIOS of Kronos's bios folder it is given (KronosBios.PickFor).</summary>
        public static string BiosRegion(SaturnDisc disc)
        {
            var area = (disc?.Area ?? "").ToUpperInvariant();
            if (area.Length == 0) return null;
            return area.Any(c => "UBAEL".IndexOf(c) >= 0) ? "us-eu" : "jp";
        }
    }
}
