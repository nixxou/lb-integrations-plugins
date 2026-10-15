// ZiNc 1.1's 71 games, and which one a romset is.
//
// THE TABLE (zinc-games.tsv, embedded) is ZiNc's own --list-games and --list-sets, taken from the ZiNc.exe of the ZiNc-EX
// v1.0.0 release (tools\zinc-games\make-table.py): the number ZiNc starts a game by, its set, parent and BIOS set as
// ZiNc names them, its title, and every ROM with its CRC32. ZiNc 1.1 is of 2005 and will not change.
//
// WHY NOT THE FILE'S NAME. ZiNc's sets are MAME 0.87's (2004). Since then MAME renamed many of them, and reused some
// names for other revisions - measured 15/10 on a MAME 0.288 non-merged folder (43974 zips, 6.6 s with Probe
// --zincex-identify), against every ZiNc game's CRCs:
//   - 64 of the 71 are found, about half of them under another name than ZiNc's: ZiNc's "sfex" (US 961219) is MAME's
//     "sfexu" - MAME's "sfex" is now the Euro revision, whose program ROM ZiNc does not know, so the name alone would
//     start a game ZiNc then cannot load; "tekken3" is "tekken3je1", "pbball96" is "powyak96", and MAME's "tekkenb" is
//     ZiNc's "tekkena" while ZiNc's "tekkenb" is MAME's "tekkenjb";
//   - 7 are in no zip at all (Tekken 2's three sets, Dunk Mania's two, Psychic Force EX, Bloody Roar 2): MAME's dumps changed.
// And every BIOS set has a new name: cpzn1 is coh1000c, cpzn2 coh3002c, taitofx1 coh1000t, psarc95 coh1002e, tps
// coh1002m, atluspsx coh1001l (konamigv kept its name) - each found by its BIOS ROM's CRC in that folder.
// ZiNc itself finds a ROM by its CRC32 ("Load by CRC32. Now if ROMs are renamed in MAME ZiNc will still work", its
// readme), so the game a romset IS is told by what it holds: every CRC of the game must be in the romset, its parent's
// zip or its BIOS zip. The file's name only breaks a tie.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace LbIntegrations.ZincEx
{
    internal sealed class ZincGame
    {
        public int Id;
        public string Set, Parent, Bios, Title;
        public (string File, uint Crc)[] Roms;
    }

    internal static class ZincGames
    {
        /// <summary>The BIOS sets under the names MAME gives them today - each checked by its BIOS ROM's CRC (header).</summary>
        public static readonly Dictionary<string, string[]> BiosAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["cpzn1"] = new[] { "coh1000c" },
            ["cpzn2"] = new[] { "coh3002c" },
            ["taitofx1"] = new[] { "coh1000t" },
            ["psarc95"] = new[] { "coh1002e" },
            ["tps"] = new[] { "coh1002m" },
            ["atluspsx"] = new[] { "coh1001l" },
            ["konamigv"] = new string[0],
        };

        private static List<ZincGame> _all;

        public static IReadOnlyList<ZincGame> All => _all ??= Load();

        public static ZincGame BySet(string set)
            => All.FirstOrDefault(g => string.Equals(g.Set, (set ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        private static List<ZincGame> Load()
        {
            var list = new List<ZincGame>();
            try
            {
                using var s = typeof(ZincGames).Assembly.GetManifestResourceStream("zinc-games.tsv");
                using var r = new StreamReader(s);
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var f = line.Split('\t');
                    if (f.Length < 6 || !int.TryParse(f[0], out var id)) continue;
                    var roms = f[5].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(t => t.Split(':'))
                                   .Where(p => p.Length == 2 && uint.TryParse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                                   .Select(p => (p[0], uint.Parse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
                                   .ToArray();
                    list.Add(new ZincGame { Id = id, Set = f[1], Parent = f[2], Bios = f[3], Title = f[4], Roms = roms });
                }
            }
            catch (Exception ex) { Log.Warn("could not read the table of ZiNc's games", ex); }
            return list;
        }

        // ── which game a romset is ───────────────────────────────────────────

        /// <summary>What a romset was found to be: the game, and the zip that serves each name ZiNc EX asks for
        /// (the game's set, its parent, its BIOS) - the romset itself when it holds all of that name's files.</summary>
        internal sealed class Match
        {
            public ZincGame Game;
            public bool ByName;
            public readonly Dictionary<string, string> Zips = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Why nothing matched, and the game it comes closest to (most of its CRCs found), for the message.</summary>
        internal sealed class Miss
        {
            public string Reason;
            public ZincGame Closest;
            public string[] Lacking = new string[0];
            /// <summary>The Closest's parent and BIOS zips as found, or null - not found.</summary>
            public string ParentZip, BiosZip;
        }

        /// <summary>The game <paramref name="romZip"/> is, its parent and BIOS zips looked for in <paramref name="folders"/> in
        /// that order (under ZiNc's names, then MAME's for a BIOS). Null with <paramref name="miss"/> said when it is none.
        /// <paramref name="only"/>: that game or none (chosen in the game's window). Never throws.</summary>
        public static Match Identify(string romZip, IEnumerable<string> folders, out Miss miss, ZincGame only = null)
        {
            miss = null;
            var crcs = new CrcCache();
            var own = crcs.Of(romZip);
            if (own == null) { miss = new Miss { Reason = "it could not be read as a zip" }; return null; }
            var dirs = (folders ?? Enumerable.Empty<string>()).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var name = Path.GetFileNameWithoutExtension(romZip);

            var found = new List<(Match M, int Own)>();
            ZincGame closest = null; int closestScore = 0;
            foreach (var g in only != null ? new[] { only } : (IEnumerable<ZincGame>)All)
            {
                // For the message when none matches: the game with most of its ROMs - its BIOS's aside - in this zip.
                var bios = BiosCrcs(g.Bios);
                int inZip = g.Roms.Count(r => !bios.Contains(r.Crc) && own.Contains(r.Crc));
                if (inZip > closestScore || only != null) { closestScore = inZip; closest = g; }

                // Quick refusal: none of the game's own ROMs (those its family and BIOS do not carry) in this zip.
                var shared = SharedCrcs(g);
                var mine = g.Roms.Where(r => !shared.Contains(r.Crc)).ToList();
                int ownHits = mine.Count(r => own.Contains(r.Crc));
                if (mine.Count > 0 && ownHits == 0 && only == null) continue;

                var lacking = Lacking(g, own, dirs, romZip, crcs, out var parentZip, out var biosZip);
                if (lacking.Length > 0) continue;
                var m = new Match { Game = g, ByName = string.Equals(g.Set, name, StringComparison.OrdinalIgnoreCase) };
                m.Zips[g.Set] = romZip;
                if (!string.IsNullOrEmpty(g.Parent)) m.Zips[g.Parent] = parentZip ?? romZip;
                if (!string.IsNullOrEmpty(g.Bios)) m.Zips[g.Bios] = biosZip ?? romZip;
                found.Add((m, ownHits));
            }
            if (found.Count == 0)
            {
                var biosOf = BiosRoms.FirstOrDefault(b => own.Contains(b.Value)).Key;
                if (closest != null && closestScore > 0)
                {
                    var lacking = Lacking(closest, own, dirs, romZip, crcs, out var parentZip, out var biosZip);
                    miss = new Miss { Reason = "it lacks ROMs ZiNc needs", Closest = closest, Lacking = lacking, ParentZip = parentZip, BiosZip = biosZip };
                }
                else miss = new Miss { Reason = biosOf != null ? "it is the BIOS set " + biosOf + ", not a game" : "it holds none of the ROMs of ZiNc's 71 games" };
                return null;
            }
            // Several (a merged set holds its clones too): the one named so, then the one with most of its own ROMs in the
            // zip itself, then ZiNc's order.
            return found.OrderByDescending(f => f.M.ByName).ThenByDescending(f => f.Own).ThenBy(f => f.M.Game.Id).First().M;
        }

        /// <summary>The ROMs of <paramref name="g"/> in none of: the romset, its parent's zip, its BIOS's zip (each looked for in
        /// <paramref name="dirs"/>, said back) - empty when the game can start.</summary>
        private static string[] Lacking(ZincGame g, HashSet<uint> own, List<string> dirs, string romZip, CrcCache crcs, out string parentZip, out string biosZip)
        {
            parentZip = Find(g.Parent, dirs, romZip, crcs, null);
            biosZip = Find(g.Bios, dirs, romZip, crcs, BiosAliases.TryGetValue(g.Bios ?? "", out var a) ? a : null);
            var have = new HashSet<uint>(own);
            if (parentZip != null) have.UnionWith(crcs.Of(parentZip) ?? new HashSet<uint>());
            if (biosZip != null) have.UnionWith(crcs.Of(biosZip) ?? new HashSet<uint>());
            return g.Roms.Where(r => !have.Contains(r.Crc)).Select(r => r.File).ToArray();
        }

        /// <summary>The CRCs a game shares with its family - its parent and the parent's other clones, or its own clones - and
        /// with its BIOS (the ROMs common to every game of that BIOS): what does not tell it from its relatives.</summary>
        private static HashSet<uint> SharedCrcs(ZincGame g)
        {
            var shared = new HashSet<uint>();
            var mine = new HashSet<uint>(g.Roms.Select(r => r.Crc));
            bool clone = !string.IsNullOrEmpty(g.Parent);
            foreach (var o in All)
            {
                if (o == g) continue;
                bool family = clone ? string.Equals(o.Set, g.Parent, StringComparison.OrdinalIgnoreCase) || string.Equals(o.Parent, g.Parent, StringComparison.OrdinalIgnoreCase)
                                    : string.Equals(o.Parent, g.Set, StringComparison.OrdinalIgnoreCase);
                if (family) shared.UnionWith(o.Roms.Select(r => r.Crc).Where(mine.Contains));
            }
            if (!string.IsNullOrEmpty(g.Bios)) shared.UnionWith(BiosCrcs(g.Bios));
            return shared;
        }

        /// <summary>Each BIOS set's own ROM, by its CRC32 - the ROM every game of that BIOS carries in ZiNc's listing (Atlus's
        /// one game: its coh-1001l.353), and the one found in MAME's coh*.zip of the same board (15/10, see the header).</summary>
        public static readonly Dictionary<string, uint> BiosRoms = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["cpzn1"] = 0x50033af6,     // coh-1000c.353
            ["cpzn2"] = 0xe860ea8b,     // coh-3002c.353
            ["taitofx1"] = 0xe3f23b6e,  // coh-1000t.353
            ["psarc95"] = 0x910f3a8b,   // coh-1002e.353
            ["tps"] = 0x69ffbcb4,       // coh-1002m.353
            ["atluspsx"] = 0x6721146b,  // coh-1001l.353
            ["konamigv"] = 0xad498d2d,  // 999a01.7e
        };

        public static HashSet<uint> BiosCrcs(string bios)
            => !string.IsNullOrEmpty(bios) && BiosRoms.TryGetValue(bios, out var crc) ? new HashSet<uint> { crc } : new HashSet<uint>();

        /// <summary>The zip of <paramref name="set"/> in the first folder that has it, under its own name or one of
        /// <paramref name="aliases"/> - never <paramref name="romZip"/> itself. Null when none.</summary>
        private static string Find(string set, List<string> dirs, string romZip, CrcCache crcs, string[] aliases)
        {
            if (string.IsNullOrEmpty(set)) return null;
            var names = new[] { set }.Concat(aliases ?? new string[0]).ToList();
            foreach (var dir in dirs)
                foreach (var n in names)
                {
                    var p = Path.Combine(dir, n + ".zip");
                    if (!File.Exists(p) || SamePath(p, romZip) || crcs.Of(p) == null) continue;
                    return p;
                }
            return null;
        }

        internal static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>The CRC32s of a zip's entries, read once per identification - from its central directory only.</summary>
        private sealed class CrcCache
        {
            private readonly Dictionary<string, HashSet<uint>> _seen = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);

            public HashSet<uint> Of(string zip)
            {
                if (_seen.TryGetValue(zip, out var c)) return c;
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    c = new HashSet<uint>(archive.Entries.Where(e => e.Length > 0).Select(e => e.Crc32));
                }
                catch (Exception ex) { Log.Info("could not read " + zip + " as a zip - " + ex.Message); c = null; }
                return _seen[zip] = c;
            }
        }
    }
}
