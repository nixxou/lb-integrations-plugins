// What Shared.Saves has to get right (lot 1, docs\saves-lb14-routing.md §3 and §4b).
//
// The ids are compared AS STRINGS by LaunchBox and LiteBox, so the exact bytes are the contract:
// every example is pinned to the string it must produce, and a .NET update that changed one byte
// would fail here first. Then: a read and a rewrite give the same string back, awkward characters
// survive, LaunchBox's and LiteBox's forms are recognised as not ours, the fingerprint reads what it
// says it reads (and only once for an unchanged file), and a restore finds the right file.
//
// The files are forged under the temp folder and deleted afterwards. No emulator, no LaunchBox.
//
//   Probe --saves-selftest

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using LbIntegrations.Saves;

namespace LbIntegrations.Probe
{
    internal static class SavesCheck
    {
        private static int _bad;

        public static bool Run()
        {
            Console.WriteLine();
            Console.WriteLine("-- save ids, fingerprints, restore targets  [WRITES, in the temp folder] " + new string('-', 4));
            _bad = 0;
            var root = Path.Combine(Path.GetTempPath(), "lbip-saves-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                Pinned();
                RoundTrip();
                Awkward();
                Foreign();
                Prints(root);
                Targets(root);
            }
            catch (Exception ex) { Check("no exception", false, ex.ToString()); }
            finally { try { Directory.Delete(root, true); } catch { } }

            Console.WriteLine();
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILURE(S) - see above");
            return _bad == 0;
        }

        // ── 1. the exact strings ─────────────────────────────────────────────────────────────────
        private static void Pinned()
        {
            Console.WriteLine();
            Console.WriteLine("  pinned strings");
            Pin("ares save",
                new SaveIdData { Emu = "ares", Kind = "save", Sys = "Mega CD", By = "name", Id = "Sonic CD (U).bram" },
                "{\"emu\":\"ares\",\"kind\":\"save\",\"sys\":\"Mega CD\",\"by\":\"name\",\"id\":\"Sonic CD (U).bram\"}");
            Pin("ares state",
                new SaveIdData { Emu = "ares", Kind = "state", Sys = "Mega CD", By = "name", Id = "Sonic CD (U)", Slot = 1 },
                "{\"emu\":\"ares\",\"kind\":\"state\",\"sys\":\"Mega CD\",\"by\":\"name\",\"id\":\"Sonic CD (U)\",\"slot\":1}");
            Pin("melonDS DSiWare",
                new SaveIdData { Emu = "melonds", Kind = "nand", By = "title", Id = "00030004484e4741" },
                "{\"emu\":\"melonds\",\"kind\":\"nand\",\"by\":\"title\",\"id\":\"00030004484e4741\"}");
            Pin("Flycast arcade",
                new SaveIdData { Emu = "flycast", Kind = "nvram", By = "set", Id = "crzytaxi" },
                "{\"emu\":\"flycast\",\"kind\":\"nvram\",\"by\":\"set\",\"id\":\"crzytaxi\"}");
            Pin("xemu state",
                new SaveIdData { Emu = "xemu", Kind = "state", By = "title", Id = "4D530064", Slot = 1 },
                "{\"emu\":\"xemu\",\"kind\":\"state\",\"by\":\"title\",\"id\":\"4D530064\",\"slot\":1}");
            Pin("with origin, small rom",
                new SaveIdData
                {
                    Emu = "ares", Kind = "save", Sys = "Super Famicom", By = "name", Id = "Zelda (France).ram",
                    Game = "2cf24c23-0000-4000-8000-000000000001",
                    Rom = new RomPrintData { Size = 1048576, Crc = "9A1C0F3E" },
                },
                "{\"emu\":\"ares\",\"kind\":\"save\",\"sys\":\"Super Famicom\",\"by\":\"name\",\"id\":\"Zelda (France).ram\","
                + "\"game\":\"2cf24c23-0000-4000-8000-000000000001\",\"rom\":{\"size\":1048576,\"crc\":\"9A1C0F3E\"}}");
            Pin("with origin, big rom, a version",
                new SaveIdData
                {
                    Emu = "ppsspp", Kind = "save", By = "disc", Id = "ULUS10041",
                    Game = "2cf24c23-0000-4000-8000-000000000002", Ver = "5a7e0000-5a7e-4000-8000-00000000c001",
                    Rom = new RomPrintData { Size = 1503238553, Head = "01234567", Tail = "89ABCDEF" },
                },
                "{\"emu\":\"ppsspp\",\"kind\":\"save\",\"by\":\"disc\",\"id\":\"ULUS10041\",\"game\":\"2cf24c23-0000-4000-8000-000000000002\","
                + "\"ver\":\"5a7e0000-5a7e-4000-8000-00000000c001\",\"rom\":{\"size\":1503238553,\"head\":\"01234567\",\"tail\":\"89ABCDEF\"}}");
            Pin("a translated LaunchBox id",
                new SaveIdData { Emu = "lb.pcsx2", Kind = "save", Native = "pcsx2:SLUS-20062" },
                "{\"emu\":\"lb.pcsx2\",\"kind\":\"save\",\"native\":\"pcsx2:SLUS-20062\"}");
            Pin("# and | escaped, accents and CJK kept",
                new SaveIdData { Emu = "ares", Kind = "save", By = "name", Id = "Shin Megami Tensei #FE | é 漢字" },
                "{\"emu\":\"ares\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"Shin Megami Tensei \\u0023FE \\u007C é 漢字\"}");
            // What .NET escapes whatever the settings (measured 08/10): the HTML-sensitive characters and the
            // quote as \uXXXX, characters outside the BMP as a surrogate pair. Pinned so a change is seen.
            Pin(".NET's own escapes: HTML-sensitive characters",
                new SaveIdData { Emu = "ares", Id = "<html> & 'apostrophe' + `tick`" },
                "{\"emu\":\"ares\",\"id\":\"\\u003Chtml\\u003E \\u0026 \\u0027apostrophe\\u0027 \\u002B \\u0060tick\\u0060\"}");
            Pin(".NET's own escapes: emoji as a surrogate pair, other scripts kept",
                new SaveIdData { Emu = "ares", Id = "emoji \U0001F642 and \u0416 and \u00DF" },
                "{\"emu\":\"ares\",\"id\":\"emoji \\uD83D\\uDE42 and \u0416 and \u00DF\"}");
            Pin(".NET's own escapes: quote, backslash, tab",
                new SaveIdData { Emu = "ares", Id = "quote \" backslash \\ tab \t" },
                "{\"emu\":\"ares\",\"id\":\"quote \\u0022 backslash \\\\ tab \\t\"}");
        }
        private static void Pin(string what, SaveIdData data, string expected)
        {
            var got = SaveId.Write(data);
            Check(what, got == expected, "got      " + got + "\n             expected " + expected);
        }

        // ── 2. read, rewrite, same string ────────────────────────────────────────────────────────
        private static void RoundTrip()
        {
            Console.WriteLine();
            Console.WriteLine("  round trip and stability");
            var full = new SaveIdData
            {
                Emu = "cxbx", Kind = "save", Sys = "Xbox", By = "title", Id = "4D530064", Slot = 3,
                Game = "g", Ver = "v", Rom = new RomPrintData { Size = 12, Crc = "AABBCCDD" }, Native = "n",
            };
            var s = SaveId.Write(full);
            Check("every field round-trips", SaveId.Canonical(s) == s, s + " -> " + SaveId.Canonical(s));

            // The same state built field by field in another order is the same object, so the same string.
            var a = new SaveIdData { Slot = 2, Id = "x", Kind = "state", Emu = "bsnes" };
            var b = new SaveIdData { Emu = "bsnes", Kind = "state", Id = "x", Slot = 2 };
            Check("same state = same string", SaveId.Write(a) == SaveId.Write(b));

            // An id written with spaces by someone else still reads, and comes back canonical.
            Check("a spaced id reads and comes back canonical",
                  SaveId.Canonical("{ \"emu\" : \"ares\", \"kind\":\"save\" }") == "{\"emu\":\"ares\",\"kind\":\"save\"}");

            // LiteBox decorates ids with #cN / #web / #b:<name>: our JSON never holds a '#', so it reads through.
            Check("a LiteBox decoration is read through", SaveId.Canonical(s + "#c2") == s);
            Check("no slot = no key", !SaveId.Write(new SaveIdData { Emu = "a" }).Contains("slot"));
        }

        // ── 3. characters that broke the old grammar, and size ───────────────────────────────────
        private static void Awkward()
        {
            Console.WriteLine();
            Console.WriteLine("  awkward characters");
            foreach (var name in new[]
            {
                "quote \" backslash \\ tab \t newline \n",
                "colon : semicolon ; slash / hash # pipe |",
                "<html> & 'apostrophe' + `tick`",
                "emoji 🙂 and Ж and ß and ü",
                "entry:not-litebox",
                new string('é', 1950),
            })
            {
                var s = SaveId.Write(new SaveIdData { Emu = "ares", Kind = "save", By = "name", Id = name });
                bool back = SaveId.TryRead(s, out var d) && d!.Id == name;
                bool clean = s.IndexOf('#') < 0 && s.IndexOf('|') < 0;
                Check("\"" + Short(name) + "\"", back && clean && SaveId.Canonical(s) == s,
                      "back=" + back + " no #/|=" + clean + "  " + Short(s));
            }
        }

        // ── 4. what is not ours ──────────────────────────────────────────────────────────────────
        private static void Foreign()
        {
            Console.WriteLine();
            Console.WriteLine("  foreign ids and owners");
            foreach (var id in new[] { "pcsx2:SLUS-20062", "saturn-Panzer Dragoon", "2f6c3a48-1b2e-4c1e-9a43-8a7d1e0f9b11",
                                       "entry:abc:G:\\x.zip", "unit:abc", "ares:save:MegaCD:old.bram", "", "{not json" })
                Check("not our JSON: \"" + id + "\"", !SaveId.TryRead(id, out _));

            Owner("pcsx2:SLUS-20062", SaveOwnerKind.LaunchBox, "lb.pcsx2");
            Owner("saturn-Panzer Dragoon", SaveOwnerKind.LaunchBox, "lb.retroarch");
            Owner("entry:abc:G:\\x.zip", SaveOwnerKind.LiteBox, "litebox");
            Owner("2f6c3a48-1b2e-4c1e-9a43-8a7d1e0f9b11", SaveOwnerKind.Unknown, null);
            Owner(SaveId.Write(new SaveIdData { Emu = "ares", Kind = "save" }), SaveOwnerKind.Nixx, "ares");
            Owner(SaveId.Write(new SaveIdData { Emu = "lb.retroarch", Kind = "save", Native = "x" }), SaveOwnerKind.LaunchBox, "lb.retroarch");

            var ares = SaveId.Write(new SaveIdData { Emu = "ares", Kind = "save" });
            Check("IsOurs: ares's id is ares's", SaveRows.IsOurs(ares, "ares"));
            Check("IsOurs: ares's id is not bsnes's", !SaveRows.IsOurs(ares, "bsnes"));
        }

        private static void Owner(string id, SaveOwnerKind kind, string? name)
        {
            var o = SaveOwner.Of(id);
            Check("owner of \"" + Short(id) + "\" = " + kind + (name != null ? " " + name : ""),
                  o.Kind == kind && o.Name == name, "got " + o.Kind + " " + o.Name);
        }

        // ── 5. the fingerprint ───────────────────────────────────────────────────────────────────
        private static void Prints(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  fingerprints");
            RomPrint.ClearCache();
            var rnd = new Random(1234);

            var small = Path.Combine(root, "small.sfc");
            var smallBytes = new byte[3 * 1024 * 1024 + 17]; rnd.NextBytes(smallBytes); File.WriteAllBytes(small, smallBytes);
            var p = RomPrint.Of(small);
            Check("up to 10 MB: CRC32 of the whole file",
                  p != null && p.Size == smallBytes.Length && p.Crc == Hex(Crc32.HashToUInt32(smallBytes)) && p.Head == null && p.Tail == null,
                  Describe(p));

            var limit = Path.Combine(root, "limit.bin");
            var limitBytes = new byte[RomPrint.WholeLimit]; rnd.NextBytes(limitBytes); File.WriteAllBytes(limit, limitBytes);
            Check("exactly 10 MB: still the whole file", RomPrint.Of(limit)?.Crc == Hex(Crc32.HashToUInt32(limitBytes)));

            var big = Path.Combine(root, "big.iso");
            var bigBytes = new byte[RomPrint.WholeLimit + 5 * 1024 * 1024 + 3]; rnd.NextBytes(bigBytes); File.WriteAllBytes(big, bigBytes);
            var q = RomPrint.Of(big);
            string head = Hex(Crc32.HashToUInt32(bigBytes.AsSpan(0, RomPrint.Chunk)));
            string tail = Hex(Crc32.HashToUInt32(bigBytes.AsSpan(bigBytes.Length - RomPrint.Chunk)));
            Check("above 10 MB: CRC32 of the first and the last MB",
                  q != null && q.Size == bigBytes.Length && q.Crc == null && q.Head == head && q.Tail == tail, Describe(q));

            var zip = Path.Combine(root, "game.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) using (var w = z.CreateEntry("game.sfc").Open()) w.Write(smallBytes, 0, 4096);
            Check("an archive is fingerprinted as the archive", RomPrint.Of(zip)?.Crc == Hex(Crc32.HashToUInt32(File.ReadAllBytes(zip))));

            int reads = RomPrint.Reads;
            RomPrint.Of(small); RomPrint.Of(big);
            Check("an unchanged file is not read again", RomPrint.Reads == reads, "reads " + reads + " -> " + RomPrint.Reads);

            smallBytes[100] ^= 0xFF; File.WriteAllBytes(small, smallBytes);
            File.SetLastWriteTimeUtc(small, DateTime.UtcNow.AddMinutes(1));
            var p2 = RomPrint.Of(small);
            Check("a changed file is read again", RomPrint.Reads == reads + 1 && p2?.Crc == Hex(Crc32.HashToUInt32(smallBytes)) && !p2.SameAs(p));

            Check("a missing file has no fingerprint", RomPrint.Of(Path.Combine(root, "nope.bin")) == null);
        }

        // ── 6. where a restore writes ────────────────────────────────────────────────────────────
        private static void Targets(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  restore targets");
            var a = Path.Combine(root, "Game (France).sfc"); File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
            var b = Path.Combine(root, "Game (Alt).sfc"); File.WriteAllBytes(b, new byte[] { 4, 5, 6 });
            string? Full(string? x) => x;
            const string V = "c001", V2 = "c002";

            var madeOnA = new SaveIdData { Emu = "ares", Kind = "save", Game = "g", Rom = RomPrint.Of(a) };
            var madeOnB = new SaveIdData { Emu = "ares", Kind = "save", Game = "g", Ver = V, Rom = RomPrint.Of(b) };

            var t = SaveRows.ChooseTarget(null, madeOnA, a, new[] { (V, (string?)b) }, Full);
            Check("a game save goes to the game", t.RomPath == a && t.VersionId == null && t.Matches && !t.FoundByPrint);

            t = SaveRows.ChooseTarget(null, madeOnB, a, new[] { (V, (string?)b) }, Full);
            Check("a version save goes to its version (ver)", t.RomPath == b && t.VersionId == V && t.Matches);

            t = SaveRows.ChooseTarget(V, madeOnB.WithoutVer(), a, new[] { (V, (string?)b) }, Full);
            Check("the row's version wins over the id", t.RomPath == b && t.VersionId == V);

            // LiteBox's Make Default: the game now launches B, and a new version V2 keeps A, with the game's
            // old saves moved onto it. Their ids have no ver; the row says V2.
            t = SaveRows.ChooseTarget(V2, madeOnA, b, new[] { (V, (string?)b), (V2, (string?)a) }, Full);
            Check("LiteBox's Make Default: the old save follows the old rom", t.RomPath == a && t.VersionId == V2 && t.Matches);

            // Files swapped by hand: the id says the game, the game's file is now B, a version holds A.
            t = SaveRows.ChooseTarget(null, madeOnA, b, new[] { (V, (string?)a) }, Full);
            Check("swapped files: found by fingerprint", t.RomPath == a && t.VersionId == V && t.FoundByPrint && t.Matches);

            // LaunchBox's Make Default: the game and its version both launch B, A is referenced nowhere.
            t = SaveRows.ChooseTarget(V, madeOnA, b, new[] { (V, (string?)b) }, Full);
            Check("LaunchBox's Make Default: nothing matches, it warns", !t.Matches && t.Warning != null && t.RomPath == b);

            var o = SaveRows.Origin("g", a, V, a, Full);
            Check("a version on its game's file is not tied", o.Ver == null && o.Game == "g" && o.Rom!.SameAs(RomPrint.Of(a)));
            o = SaveRows.Origin("g", a, V, b, Full);
            Check("a version on another file is tied, and printed on its own file", o.Ver == V && o.Rom!.SameAs(RomPrint.Of(b)));
            o = SaveRows.Origin("g", a, null, null, Full);
            Check("a game save: no ver, the game's file", o.Ver == null && o.Rom!.SameAs(RomPrint.Of(a)));

            Check("IsSaveActive compares strings, exactly", SaveRows.SameId("{\"a\":1}", "{\"a\":1}") && !SaveRows.SameId("{\"a\":1}", "{\"A\":1}"));
        }

        // The id of a version save as LiteBox's move would leave it: tied by the row, not by the id.
        private static SaveIdData WithoutVer(this SaveIdData d) => new()
        {
            Emu = d.Emu, Kind = d.Kind, Sys = d.Sys, By = d.By, Id = d.Id, Slot = d.Slot, Game = d.Game, Rom = d.Rom, Native = d.Native,
        };

        private static string Hex(uint crc) => crc.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        private static string Describe(RomPrintData? p) => p == null ? "null" : SaveId.Write(new SaveIdData { Rom = p });
        private static string Short(string s) => (s.Length > 60 ? s.Substring(0, 57) + "..." : s).Replace("\n", "\\n").Replace("\t", "\\t");

        private static void Check(string what, bool ok, string? error = null)
        {
            Console.WriteLine((ok ? "  OK    " : "  FAIL  ") + what);
            if (!ok) { _bad++; if (error != null) Console.WriteLine("        " + error); }
        }
    }
}
