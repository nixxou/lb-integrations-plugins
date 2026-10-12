// What Shared.Saves has to get right (lot 1, docs\saves-lb14-routing.md §3 and §4b).
//
// The ids are compared AS STRINGS by LaunchBox and LiteBox, so the exact bytes are the contract:
// every example is pinned to the string it must produce, and a .NET update that changed one byte
// would fail here first. Then: a read and a rewrite give the same string back, awkward characters
// survive, LaunchBox's and LiteBox's forms are recognised as not ours, and a restore finds the right file.
//
// The files are forged under the temp folder and deleted afterwards. No emulator, no LaunchBox.
//
//   Probe --saves-selftest

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Saves;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class SavesCheck
    {
        private static int _bad;

        public static bool Run()
        {
            Console.WriteLine();
            Console.WriteLine("-- save ids and restore targets  [WRITES, in the temp folder] " + new string('-', 16));
            _bad = 0;
            var root = Path.Combine(Path.GetTempPath(), "lbip-saves-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                Pinned();
                RoundTrip();
                Awkward();
                Foreign();
                Targets(root);
                Desk();
                StockOwners();
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
            var full = new SaveIdData { Emu = "cxbx", Kind = "save", Sys = "Xbox", By = "title", Id = "4D530064", Slot = 3, Native = "n" };
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

        // ── 5. where a restore writes ────────────────────────────────────────────────────────────
        private static void Targets(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  restore targets");
            var a = Path.Combine(root, "Game (France).sfc"); File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
            var b = Path.Combine(root, "Game (Alt).sfc"); File.WriteAllBytes(b, new byte[] { 4, 5, 6 });
            var gone = Path.Combine(root, "Game (old name).sfc");
            string? Full(string? x) => x;
            const string V = "c001", V2 = "c002";

            var t = SaveRows.ChooseTarget(null, a, new[] { (V, (string?)b) }, Full);
            Check("a game save goes to the game", t.RomPath == a && t.VersionId == null && t.Warning == null);

            t = SaveRows.ChooseTarget(V, a, new[] { (V, (string?)b) }, Full);
            Check("a version save goes to its version", t.RomPath == b && t.VersionId == V && t.Warning == null);

            // LiteBox's Make Default: the game now launches B, a new version V2 keeps A, and the game's old
            // saves were moved onto V2 - the row says so, nothing else is needed.
            t = SaveRows.ChooseTarget(V2, b, new[] { (V, (string?)b), (V2, (string?)a) }, Full);
            Check("LiteBox's Make Default: the old save follows the old rom", t.RomPath == a && t.VersionId == V2 && t.Warning == null);

            t = SaveRows.ChooseTarget("c999", a, new[] { (V, (string?)b) }, Full);
            Check("a version that no longer exists: refused, nothing written", t.Refused && t.RomPath == null && t.Warning != null);

            t = SaveRows.ChooseTarget(null, gone, new[] { (V, (string?)b) }, Full);
            Check("the game's file is missing: kept, with a warning", t.RomPath == gone && t.Warning != null);
            t = SaveRows.ChooseTarget(V, a, new[] { (V, (string?)gone) }, Full);
            Check("the version's file is missing: kept, with a warning", t.RomPath == gone && t.VersionId == V && t.Warning != null);

            Check("a version on its game's file is not tied", SaveRows.TieTo(V, a, a, Full) == null);
            Check("a version on another file is tied", SaveRows.TieTo(V, a, b, Full) == V);
            Check("a game save is not tied", SaveRows.TieTo(null, a, null, Full) == null);

            Check("IsSaveActive compares strings, exactly", SaveRows.SameId("{\"a\":1}", "{\"a\":1}") && !SaveRows.SameId("{\"a\":1}", "{\"A\":1}"));
        }

        // The front desk's pure parts (lot 3): what tells our rows from the others' without parsing, the core
        // written on the rows it lists for others, the copy that carries them.
        private static void Desk()
        {
            Console.WriteLine();
            Console.WriteLine("  front desk (lot 3)");
            var ours = SaveId.Write(new SaveIdData { Emu = "ares", Kind = "save", By = "name", Id = "Zelda.srm" });
            Check("our ids start with the plugin's prefix", ours.StartsWith(SaveDesk.OwnPrefix("ares"), StringComparison.Ordinal));
            var longer = SaveId.Write(new SaveIdData { Emu = "aresx", Kind = "save", By = "name", Id = "Zelda.srm" });
            Check("another emu that starts the same is not taken for ours", !longer.StartsWith(SaveDesk.OwnPrefix("ares"), StringComparison.Ordinal));
            Check("a translated id is not ours either", !SaveId.Write(new SaveIdData { Emu = "lb.retroarch", Kind = "save" }).StartsWith(SaveDesk.OwnPrefix("ares"), StringComparison.Ordinal));
            Check("the desk is on outside LiteBox", SaveRouter.Active);

            Check("core: LaunchBox's own line", SaveRouter.CoreIn("-L \"cores\\snes9x_libretro.dll\"") == "snes9x_libretro");
            Check("core: a full path, no quotes", SaveRouter.CoreIn("-f -L D:\\RA\\cores\\mesen_libretro.dll") == "mesen_libretro");
            Check("core: no -L, none", SaveRouter.CoreIn("-f --verbose") == null && SaveRouter.CoreIn(null) == null);

            var state = new GameSaveState { GameId = "g1", AdditionalApplicationId = "a1", SaveGroupId = "x", FileLocation = @"C:\s\Zelda.state1", Slot = 1, Title = "Zelda", EmulatorFileName = "old.exe" };
            var copy = SaveRows.With(state, "retroarch.exe", "snes9x_libretro");
            Check("the copy of a state keeps its slot and its fields",
                  copy is GameSaveState c && c.Slot == 1 && c.GameId == "g1" && c.AdditionalApplicationId == "a1" && c.SaveGroupId == "x"
                  && c.FileLocation == state.FileLocation && c.Title == "Zelda" && c.EmulatorFileName == "retroarch.exe" && c.EmulatorCore == "snes9x_libretro");
            var game = new GameSaveGame { GameId = "g1", FileLocation = @"C:\s\Zelda.srm", EmulatorFileName = "retroarch.exe", EmulatorCore = "snes9x_libretro" };
            Check("a row already right is handed as it is", ReferenceEquals(SaveRows.With(game, "retroarch.exe", "snes9x_libretro"), game));
            Check("a row's own core is kept when the desk has none", SaveRows.With(game, "retroarch.exe", null).EmulatorCore == "snes9x_libretro");
            var tied = SaveRows.WithVersion(state, "v9");
            Check("a state tied to a version keeps its slot and its fields",
                  tied is GameSaveState t && t.Slot == 1 && t.AdditionalApplicationId == "v9" && t.SaveGroupId == "x" && t.EmulatorFileName == "old.exe" && t.FileLocation == state.FileLocation);
            Check("a row already on that version is handed as it is", ReferenceEquals(SaveRows.WithVersion(state, "A1"), state));

            Check("RetroArch and Dolphin are restored by the desk, PCSX2 is not",
                  StockRestore.Handles("lb.retroarch") && StockRestore.Handles("lb.dolphin") && !StockRestore.Handles("lb.pcsx2") && !StockRestore.Handles("ares"));

            Slots();
            View();
            Installs();
            Companions();
            CompatSwitches();
            Scan();
            GlobalCacheCheck();
            StampedCacheCheck();
        }

        /// <summary>Shared.Lbip\StampedCache: kept while the file is unchanged, read again after a change, and a read made while
        /// the file was locked returned but never kept (Codex review 10/10).</summary>
        private static void StampedCacheCheck()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-stamped-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                var cfg = Path.Combine(dir, "emu.cfg");
                File.WriteAllText(cfg, "A");
                int reads = 0;
                string Read() { reads++; try { return File.ReadAllText(cfg); } catch { return "FAILED"; } }
                string Get() => LbIntegrations.Lbip.StampedCache.Get(cfg, "test|" + dir, Read);

                Check("stamped: first read", Get() == "A" && reads == 1);
                Check("stamped: kept while the file is unchanged", Get() == "A" && reads == 1);
                File.WriteAllText(cfg, "BB");
                Check("stamped: read again after a change", Get() == "BB" && reads == 2);

                // Locked during the read, released without the file changing: the fallback must not stick.
                var stamp = File.GetLastWriteTimeUtc(cfg);
                string locked;
                using (new FileStream(cfg, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    File.SetLastWriteTimeUtc(cfg, stamp);
                    // A new key, as a first read would be.
                    locked = LbIntegrations.Lbip.StampedCache.Get(cfg, "locked|" + dir, Read);
                }
                Check("stamped: the file locked - its fallback returned", locked == "FAILED");
                Check("stamped: ...and not kept - the file released, the real value read", LbIntegrations.Lbip.StampedCache.Get(cfg, "locked|" + dir, Read) == "BB");

                // Held open for WRITING with read sharing - an emulator rewriting its settings: the readers (sharing Read only)
                // fail, so must the check, and the fallback must not stick (Codex review 10/10).
                string heldValue;
                using (new FileStream(cfg, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                    heldValue = LbIntegrations.Lbip.StampedCache.Get(cfg, "held|" + dir, Read);
                Check("stamped: held open for writing (read shared) - its fallback returned", heldValue == "FAILED");
                Check("stamped: ...and not kept - released unchanged, the real value read", LbIntegrations.Lbip.StampedCache.Get(cfg, "held|" + dir, Read) == "BB");

                // keepNull false: a reader's null ("could not read") is never kept.
                int nulls = 0;
                string NullOnce() => nulls++ == 0 ? null : "read";
                var n1 = LbIntegrations.Lbip.StampedCache.Get(cfg, "nonull|" + dir, NullOnce, keepNull: false);
                var n2 = LbIntegrations.Lbip.StampedCache.Get(cfg, "nonull|" + dir, NullOnce, keepNull: false);
                Check("stamped: keepNull false - a null not kept, read again next time", n1 == null && n2 == "read");

                var missing = Path.Combine(dir, "none.cfg");
                int m = 0;
                LbIntegrations.Lbip.StampedCache.Get(missing, "missing|" + dir, () => ++m);
                LbIntegrations.Lbip.StampedCache.Get(missing, "missing|" + dir, () => ++m);
                Check("stamped: a missing file is a state, kept until it appears", m == 1);
                File.WriteAllText(missing, "now");
                LbIntegrations.Lbip.StampedCache.Get(missing, "missing|" + dir, () => ++m);
                Check("stamped: ...read again once it appears", m == 2);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        /// <summary>One of the writers GlobalCacheCheck starts: 200 files of its own, a value each, into the cache at <paramref name="dir"/>.</summary>
        internal static int GlobalCacheWriter(string dir, string who)
        {
            LbIntegrations.Lbip.GlobalCache.DirForTests = Path.Combine(dir, "cache");
            for (int i = 0; i < 200; i++)
            {
                var f = Path.Combine(dir, "files", who + "-" + i + ".bin");
                LbIntegrations.Lbip.GlobalCache.Set("race", f, who + ":" + i);
            }
            return 0;
        }

        /// <summary>Shared.Lbip\GlobalCache: a value kept while its file is unchanged, others' lines read in, a cut line
        /// skipped, escaping, compaction, a drive not plugged in kept - and three processes writing at once.</summary>
        private static void GlobalCacheCheck()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-gcache-" + Guid.NewGuid().ToString("N"));
            var was = LbIntegrations.Lbip.GlobalCache.DirForTests;
            try
            {
                var gc = typeof(LbIntegrations.Lbip.GlobalCache);
                LbIntegrations.Lbip.GlobalCache.DirForTests = Path.Combine(dir, "cache");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                var files = Path.Combine(dir, "files"); Directory.CreateDirectory(files);
                var rom = Path.Combine(files, "game.iso"); File.WriteAllText(rom, "disc");
                bool Get(string kind, string f, out string v) => LbIntegrations.Lbip.GlobalCache.TryGet(kind, f, out v);

                Check("gcache: nothing kept → a miss", !Get("ids", rom, out _));
                LbIntegrations.Lbip.GlobalCache.Set("ids", rom, "ULUS10080");
                Check("gcache: kept → a hit", Get("ids", rom, out var v1) && v1 == "ULUS10080");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: read back from the file (another session)", Get("ids", rom, out var v2) && v2 == "ULUS10080");
                File.WriteAllText(rom, "another disc");
                Check("gcache: the file changed → a miss", !Get("ids", rom, out _));
                LbIntegrations.Lbip.GlobalCache.Set("ids", rom, "");
                Check("gcache: an empty value is a value (read, nothing found)", Get("ids", rom, out var v3) && v3 == "");

                var odd = Path.Combine(files, "odd.bin"); File.WriteAllText(odd, "x");
                LbIntegrations.Lbip.GlobalCache.Set("ids", odd, "a\tb\nc\\d%09 100%");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: tabs, new lines, backslashes and % come back", Get("ids", odd, out var v4) && v4 == "a\tb\nc\\d%09 100%");

                // Another process's line, appended while this one has the table in memory.
                var other = Path.Combine(files, "other.bin"); File.WriteAllText(other, "o");
                var fo = new FileInfo(other);
                File.AppendAllText(LbIntegrations.Lbip.GlobalCache.PathOf("ids"), fo.FullName + "\t" + fo.Length + "\t" + fo.LastWriteTimeUtc.Ticks + "\tfrom-elsewhere\n");
                Check("gcache: another process's line is read in", Get("ids", other, out var v5) && v5 == "from-elsewhere");

                // A line a crash cut short, then a value set: the cut one is skipped, nothing else lost.
                File.AppendAllText(LbIntegrations.Lbip.GlobalCache.PathOf("ids"), @"C:\half\written.iso" + "\t12");
                var after = Path.Combine(files, "after.bin"); File.WriteAllText(after, "a");
                LbIntegrations.Lbip.GlobalCache.Set("ids", after, "after-crash");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: a cut line skipped, the next one and the earlier ones kept",
                      Get("ids", after, out var v6) && v6 == "after-crash" && Get("ids", other, out _) && Get("ids", odd, out _));

                // A record that looks whole but whose VALUE was cut (Codex review 10/10): never a hit, gone at the next append.
                var cutRom = Path.Combine(files, "cut.iso"); File.WriteAllText(cutRom, "c");
                var fc = new FileInfo(cutRom);
                File.AppendAllText(LbIntegrations.Lbip.GlobalCache.PathOf("ids"), fc.FullName + "\t" + fc.Length + "\t" + fc.LastWriteTimeUtc.Ticks + "\tCUT-VALUE-XYZ");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: a record cut inside its value is not a hit", !Get("ids", cutRom, out _));
                var next = Path.Combine(files, "next.bin"); File.WriteAllText(next, "n");
                LbIntegrations.Lbip.GlobalCache.Set("ids", next, "next");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: ...nor after the next append, and it is cut off the file",
                      !Get("ids", cutRom, out _) && Get("ids", next, out var vn) && vn == "next" && !File.ReadAllText(LbIntegrations.Lbip.GlobalCache.PathOf("ids")).Contains("CUT-VALUE-XYZ"));

                // The zeros a killed write leaves at the end: skipped, then cut off; the records before them kept.
                using (var z = new FileStream(LbIntegrations.Lbip.GlobalCache.PathOf("ids"), FileMode.Append)) z.Write(new byte[4096], 0, 4096);
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: a tail of zeros is skipped", Get("ids", next, out _) && Get("ids", other, out _));
                var afterZeros = Path.Combine(files, "after-zeros.bin"); File.WriteAllText(afterZeros, "z");
                LbIntegrations.Lbip.GlobalCache.Set("ids", afterZeros, "fine");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: ...and cut off at the next append",
                      !File.ReadAllBytes(LbIntegrations.Lbip.GlobalCache.PathOf("ids")).Contains((byte)0) && Get("ids", afterZeros, out var vz) && vz == "fine" && Get("ids", next, out _));
                // Compaction: many values for one file, a file gone, an entry on a drive that is not there.
                var gone = Path.Combine(files, "gone.bin"); File.WriteAllText(gone, "g");
                LbIntegrations.Lbip.GlobalCache.Set("compact", gone, "soon gone");
                char free = "QRSTUVWXYZ".First(ch => !Directory.Exists(ch + @":\"));
                File.AppendAllText(LbIntegrations.Lbip.GlobalCache.PathOf("compact"), free + @":\unplugged\game.iso" + "\t5\t42\tkept\n");
                File.Delete(gone);
                var busy = Path.Combine(files, "busy.bin"); File.WriteAllText(busy, "b");
                for (int i = 0; i < 150; i++) LbIntegrations.Lbip.GlobalCache.Set("compact", busy, "v" + i);
                var lines = File.ReadAllLines(LbIntegrations.Lbip.GlobalCache.PathOf("compact"));
                Check("gcache: compacted (" + lines.Length + " lines for 150 writes)", lines.Length < 80);
                Check("gcache: compaction keeps the last value", lines.Count(l => l.Contains("busy.bin")) <= 70 && Get("compact", busy, out var v7) && v7 == "v149");
                Check("gcache: a file gone from a drive that is there is dropped", !lines.Any(l => l.Contains("gone.bin")));
                Check("gcache: an entry of a drive not plugged in is kept", lines.Any(l => l.StartsWith(free + @":\unplugged")));
                Check("gcache: no temporary file left", Directory.GetFiles(Path.Combine(dir, "cache"), "*.tmp").Length == 0);

                // A value replaced by ANOTHER process for an unchanged file ("unknown" made known): seen within RecheckMs.
                var known = Path.Combine(files, "archive.zip"); File.WriteAllText(known, "zip");
                LbIntegrations.Lbip.GlobalCache.Set("titles", known, "");
                Check("gcache: warmed here with 'unknown'", Get("titles", known, out var w0) && w0 == "");
                var setter = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,
                    "--globalcache-set \"" + Path.Combine(dir, "cache") + "\" titles \"" + known + "\" 4D5307E6") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                setter.WaitForExit(60000);
                System.Threading.Thread.Sleep(LbIntegrations.Lbip.GlobalCache.RecheckMs + 100);
                Check("gcache: another process's replacement is returned (within " + LbIntegrations.Lbip.GlobalCache.RecheckMs + " ms)", Get("titles", known, out var w1) && w1 == "4D5307E6");
                // A:X, then B:Y, then A:X again - A's write is not skipped on its memory's say-so (Codex review 10/10).
                var flip = Path.Combine(files, "flip.bin"); File.WriteAllText(flip, "f");
                LbIntegrations.Lbip.GlobalCache.Set("flip", flip, "X");
                var b = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,
                    "--globalcache-set \"" + Path.Combine(dir, "cache") + "\" flip \"" + flip + "\" Y") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                b.WaitForExit(60000);
                LbIntegrations.Lbip.GlobalCache.Set("flip", flip, "X");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: A:X, B:Y, A:X - X written again", Get("flip", flip, out var fx) && fx == "X");

                // The file held by another process: the value is kept as pending (answered here), then written by a later Set.
                var held = Path.Combine(files, "held.bin"); File.WriteAllText(held, "h");
                var holder = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--globalcache-hold busy 2500")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                holder.StandardOutput.ReadLine();   // "held"
                var wasTimeout = LbIntegrations.Lbip.GlobalCache.LockTimeoutMs;
                LbIntegrations.Lbip.GlobalCache.LockTimeoutMs = 200;
                LbIntegrations.Lbip.GlobalCache.Set("busy", held, "later");
                Check("gcache: the file busy - answered from this process meanwhile", Get("busy", held, out var hv) && hv == "later");
                Check("gcache: ...and not on disk yet", !File.Exists(LbIntegrations.Lbip.GlobalCache.PathOf("busy")) || !File.ReadAllText(LbIntegrations.Lbip.GlobalCache.PathOf("busy")).Contains("later"));
                holder.WaitForExit(60000);
                LbIntegrations.Lbip.GlobalCache.LockTimeoutMs = wasTimeout;
                LbIntegrations.Lbip.GlobalCache.Set("busy", held, "later");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: ...written by the next Set once the file is free", Get("busy", held, out var hv2) && hv2 == "later");
                // A replacement whose write fails is answered ahead of the older value written before (Codex review 10/10).
                var repl = Path.Combine(files, "repl.bin"); File.WriteAllText(repl, "r");
                LbIntegrations.Lbip.GlobalCache.Set("repl", repl, "X");
                var holder2 = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--globalcache-hold repl 2500")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                holder2.StandardOutput.ReadLine();
                var wasT2 = LbIntegrations.Lbip.GlobalCache.LockTimeoutMs;
                LbIntegrations.Lbip.GlobalCache.LockTimeoutMs = 200;
                LbIntegrations.Lbip.GlobalCache.Set("repl", repl, "Y");
                Check("gcache: X written, Y's write blocked - Y answered", Get("repl", repl, out var ry) && ry == "Y");
                holder2.WaitForExit(60000);
                LbIntegrations.Lbip.GlobalCache.LockTimeoutMs = wasT2;
                LbIntegrations.Lbip.GlobalCache.Set("repl", repl, "Y");
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                Check("gcache: ...and Y written at the next Set", Get("repl", repl, out var ry2) && ry2 == "Y");

                // The cache file deleted by hand while this process holds its values: forgotten, and written again by Set.
                var del = Path.Combine(files, "del.bin"); File.WriteAllText(del, "d");
                LbIntegrations.Lbip.GlobalCache.Set("del", del, "V");
                Check("gcache: before the deletion, a hit", Get("del", del, out _));
                File.Delete(LbIntegrations.Lbip.GlobalCache.PathOf("del"));
                System.Threading.Thread.Sleep(LbIntegrations.Lbip.GlobalCache.RecheckMs + 100);
                Check("gcache: the cache file deleted - its values forgotten", !Get("del", del, out _));
                LbIntegrations.Lbip.GlobalCache.Set("del", del, "V");
                Check("gcache: ...and Set writes the file again", File.Exists(LbIntegrations.Lbip.GlobalCache.PathOf("del")) && File.ReadAllText(LbIntegrations.Lbip.GlobalCache.PathOf("del")).Contains("\tV\n"));

                // Three processes at once, 200 values each.
                for (int w = 0; w < 3; w++) for (int i = 0; i < 200; i++) File.WriteAllText(Path.Combine(files, "w" + w + "-" + i + ".bin"), "w");
                var exe = Environment.ProcessPath!;
                var procs = Enumerable.Range(0, 3).Select(w => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--globalcache-writer \"" + dir + "\" w" + w) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!).ToList();
                foreach (var p in procs) p.WaitForExit(60000);
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                int found = 0;
                for (int w = 0; w < 3; w++) for (int i = 0; i < 200; i++) if (Get("race", Path.Combine(files, "w" + w + "-" + i + ".bin"), out var rv) && rv == "w" + w + ":" + i) found++;
                var bad = File.ReadAllLines(LbIntegrations.Lbip.GlobalCache.PathOf("race")).Count(l => l.Length > 0 && l.Split('\t').Length != 4);
                Check("gcache: three processes at once - all 600 values kept, no broken line (" + found + ", " + bad + " broken)", found == 600 && bad == 0);
            }
            finally
            {
                LbIntegrations.Lbip.GlobalCache.DirForTests = was;
                LbIntegrations.Lbip.GlobalCache.ForgetForTests();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        /// <summary>SaveScan answers what File.Exists, Directory.Exists and Directory.GetFiles answer (a big listing).</summary>
        private static void Scan()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-scan-" + Guid.NewGuid().ToString("N"));
            var cwd = Environment.CurrentDirectory;
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "sub"));
                foreach (var n in new[] { "Game (USA).srm", "Game (USA).srm1", "Game (USA).bs1", "Game (USA).bs10", "game (usa).state", "game (usa).state2",
                                          "x.htm", "x.html", "noext", "a.b.c", "Zelda.SRM", "[brackets] (v1.1).srm", "émulé.srm" })
                    File.WriteAllText(Path.Combine(dir, n), n);
                var hidden = Path.Combine(dir, "hidden.srm"); File.WriteAllText(hidden, "h"); File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
                var patterns = new[] { "*", "*.*", ".", "", "*.srm", "*.SRM", "Game (USA).*", "game (usa).state*", "*.htm", "Game (USA).bs?", "?ame*", "*.", "noext.*",
                                       "*.bs1", "[brackets]*", "émulé*", "*c", "a.b.*", "nothing*" };
                var paths = new[] { "Game (USA).srm", "GAME (USA).SRM", "game (usa).srm1", "sub", "missing.srm", "x.htm", "hidden.srm", "noext", "noext.", "sub\\none.srm",
                                    "nodir\\x.srm", "..\\" + Path.GetFileName(dir) + "\\Zelda.srm", "émulé.srm" };
                Environment.CurrentDirectory = dir;   // relative paths: the same base for both

                string Real(Func<object> f) { try { var v = f(); return v is string[] a ? string.Join("|", a) : v?.ToString() ?? "null"; } catch (Exception ex) { return "!" + ex.GetType().Name; } }
                var before = new List<string>();
                foreach (var p in patterns) { before.Add("F " + p + "=" + Real(() => Directory.GetFiles(dir, p))); before.Add("Fm " + p + "=" + Real(() => Directory.GetFiles(Path.Combine(dir, "nodir"), p))); }
                foreach (var p in paths) { before.Add("E " + p + "=" + Real(() => File.Exists(p))); before.Add("D " + p + "=" + Real(() => Directory.Exists(p))); }

                var after = new List<string>();
                using (SaveScan.Begin(SaveScan.Threshold + 1))
                {
                    Check("scan: a big listing is active", SaveScan.Active);
                    foreach (var p in patterns) { after.Add("F " + p + "=" + Real(() => SaveScan.Files(dir, p))); after.Add("Fm " + p + "=" + Real(() => SaveScan.Files(Path.Combine(dir, "nodir"), p))); }
                    foreach (var p in paths) { after.Add("E " + p + "=" + Real(() => SaveScan.Exists(p))); after.Add("D " + p + "=" + Real(() => SaveScan.DirExists(p))); }

                    var late = Path.Combine(dir, "late.srm");
                    File.WriteAllText(late, "l");
                    Check("scan: a file written without Touched is not seen (the index is the listing's)", !SaveScan.Exists(late));
                    SaveScan.Touched(late);
                    Check("scan: Touched → seen", SaveScan.Exists(late) && SaveScan.Files(dir, "late*").Length == 1);

                    var conf = Path.Combine(dir, "a.b.c");
                    var t1 = SaveScan.Text(conf);
                    File.WriteAllText(conf, "changed");
                    Check("scan: a file is read once per listing", SaveScan.Text(conf) == t1);
                    SaveScan.Touched(conf);
                    Check("scan: Touched → read again", SaveScan.Text(conf) == "changed");
                    int made = 0;
                    SaveScan.Memo("k", () => ++made); SaveScan.Memo("k", () => ++made);
                    Check("scan: a memo is worked out once", made == 1);
                }
                Check("scan: the listing over → nothing kept", !SaveScan.Active);
                Check("scan: a small listing changes nothing", SaveScan.Begin(SaveScan.Threshold) is IDisposable d && !SaveScan.Active && Dispose(d));
                LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["saveScan"] = false });
                Check("scan: off from the Compatibility page → the real calls", SaveScan.Begin(SaveScan.Threshold + 1) is IDisposable off && !SaveScan.Active && Dispose(off));
                LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool>());

                var diff = before.Zip(after, (b, a) => (b, a)).Where(x => x.b != x.a).ToList();
                foreach (var (b, a) in diff.Take(8)) Console.WriteLine("    real: " + b + "\n    scan: " + a);
                Check("scan: the same answers as the real calls (" + before.Count + " questions)", before.Count == after.Count && diff.Count == 0);
            }
            finally
            {
                Environment.CurrentDirectory = cwd;
                try { foreach (var x in Directory.GetFiles(dir)) File.SetAttributes(x, FileAttributes.Normal); Directory.Delete(dir, true); } catch { }
            }
        }

        private static bool Dispose(IDisposable d) { d.Dispose(); return true; }
        /// <summary>Shared.Compat: the registry complete, the ini read and written, a patch off, a needed patch off.</summary>
        private static void CompatSwitches()
        {
            var all = LbIntegrations.Compat.CompatRegistry.All;
            Check("compat: every patch has its id, title and four texts",
                  all.All(p => p.Id.Length > 0 && p.Title.Length > 0 && p.Problem.Length > 0 && p.Example.Length > 0 && p.Does.Length > 0 && p.Without.Length > 0));
            Check("compat: ids are unique", all.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count);
            Check("compat: every 'needs' names a patch", all.Where(p => p.Needs != null).All(p => LbIntegrations.Compat.CompatRegistry.Find(p.Needs!) != null));
            var known = new HashSet<string> { "igame.cmdline", "ra.statics", "ra.loadConfig", "dolphin.statics", "lb.saves", "lb.applyPersisted", "sqlite.reader", "lb.openEmulator", "lb.wizard", "lb.version", "ra.saves", "dolphin.saves", "pcsx2.saves", "sdk.saves", "lb.saveIds", "nosave.saves" };
            Check("compat: every check is one CompatChecks knows", all.SelectMany(p => p.Checks).All(known.Contains));
            Check("compat: the reflection check of the SDK passes here", LbIntegrations.Compat.CompatChecks.Check("igame.cmdline").State == LbIntegrations.Compat.CompatCheckState.Ok);

            var dir = Path.Combine(Path.GetTempPath(), "lbip-compat-" + Guid.NewGuid().ToString("N"));
            try
            {
                var ini = Path.Combine(dir, "nixx-compat.ini");
                Check("compat: no file → everything on", LbIntegrations.Compat.Compat.Load(ini).Count == 0);
                LbIntegrations.Compat.Compat.Save(ini, new Dictionary<string, bool> { ["planA"] = false, ["desk"] = true });
                var back = LbIntegrations.Compat.Compat.Load(ini);
                Check("compat: written and read back (planA off, others on)",
                      back.TryGetValue("planA", out var a) && !a && back.TryGetValue("desk", out var d) && d && back.TryGetValue("overwriteWarning", out var w) && w);
                LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["desk"] = false });
                Check("compat: a patch off is off", !LbIntegrations.Compat.Compat.On("desk"));
                Check("compat: a patch whose needed patch is off is off", !LbIntegrations.Compat.Compat.On("retroarchView") && !LbIntegrations.Compat.Compat.On("stockRestore"));
                Check("compat: the others stay on", LbIntegrations.Compat.Compat.On("planA") && LbIntegrations.Compat.Compat.On("overwriteWarning"));
                Check("compat: the desk off → SaveRouter inactive", !SaveRouter.Active);
            }
            finally
            {
                LbIntegrations.Compat.Compat.UseForTests(new Dictionary<string, bool>());
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>SaveCompanions.CopyBack: a vault set's companions beside the restored file, under its stem.</summary>
        private static void Companions()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-companions-" + Guid.NewGuid().ToString("N"));
            var vault = Path.Combine(dir, "vault"); var live = Path.Combine(dir, "live");
            Directory.CreateDirectory(vault); Directory.CreateDirectory(live);
            try
            {
                File.WriteAllText(Path.Combine(vault, "Zelda (F)-10.srm"), "save"); File.WriteAllText(Path.Combine(vault, "Zelda (F)-10.rtc"), "clock");
                File.WriteAllText(Path.Combine(live, "Zelda (F).rtc"), "old clock");
                var target = Path.Combine(live, "Zelda (F).srm");
                var srcSrm = Path.Combine(vault, "Zelda (F)-10.srm"); var srcRtc = Path.Combine(vault, "Zelda (F)-10.rtc");
                var liveRtc = Path.Combine(live, "Zelda (F).rtc");
                File.WriteAllText(target, "old save");
                string State() => File.ReadAllText(target) + "|" + File.ReadAllText(liveRtc) + "|" + Directory.GetFiles(live).Length;
                // Codex review 10/10: all or nothing - a source that cannot be read, a destination that cannot be swapped.
                bool threw = false;
                using (new FileStream(srcRtc, FileMode.Open, FileAccess.Read, FileShare.None))
                    try { SaveCompanions.RestoreSet(srcSrm, target, new[] { srcRtc }, null); } catch (IOException) { threw = true; }
                Check("restore set: the backup's .rtc unreadable → refused, the live .srm and .rtc as they were, nothing left behind",
                      threw && State() == "old save|old clock|2", State());
                threw = false;
                using (new FileStream(liveRtc, FileMode.Open, FileAccess.Read, FileShare.None))
                    try { SaveCompanions.RestoreSet(srcSrm, target, new[] { srcRtc }, null); } catch (IOException) { threw = true; }
                Check("restore set: the live .rtc held (its swap fails after the .srm's) → the .srm put back, nothing left behind",
                      threw && State() == "old save|old clock|2", State());
                var w = SaveCompanions.RestoreSet(srcSrm, target, new[] { srcRtc }, null);
                Check("restore set: the save and the backup's .rtc replace the live ones, under the live stem",
                      w.Count == 2 && State() == "save|clock|2", State());
                File.WriteAllText(liveRtc, "old clock");
                Check("restore set: none in the backup → the live one is left",
                      SaveCompanions.RestoreSet(srcSrm, target, Array.Empty<string>(), null).Count == 1 && State() == "save|old clock|2", State());
                var fresh = Path.Combine(dir, "new", "Zelda (F).srm");
                SaveCompanions.RestoreSet(srcSrm, fresh, new[] { srcRtc }, null);
                Check("restore set: into a folder not there yet → made, both in", File.ReadAllText(fresh) == "save" && File.ReadAllText(Path.ChangeExtension(fresh, ".rtc")) == "clock");
                Check("companions: a tail that is not the stem's → its extension", SaveCompanions.TailOf("Zelda (F)-10", @"C:\v\other.psr") == ".psr");
                Check("writable: all free → nothing to say", SaveCompanions.WhyNotWritable(Path.Combine(vault, "Zelda (F)-10.srm"), target, new[] { Path.Combine(vault, "Zelda (F)-10.rtc") }) == null);
                var rtc = Path.Combine(live, "Zelda (F).rtc"); File.SetAttributes(rtc, FileAttributes.ReadOnly);
                var why = SaveCompanions.WhyNotWritable(Path.Combine(vault, "Zelda (F)-10.srm"), target, new[] { Path.Combine(vault, "Zelda (F)-10.rtc") });
                File.SetAttributes(rtc, FileAttributes.Normal);
                Check("writable: a read-only companion → refused before anything is written", why != null && why.Contains("Zelda (F).rtc") && why.Contains("read-only"), why);
                using (new FileStream(rtc, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Check("writable: a companion held by another program → refused", SaveCompanions.WhyNotWritable(Path.Combine(vault, "Zelda (F)-10.srm"), target, new[] { Path.Combine(vault, "Zelda (F)-10.rtc") }) != null);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        /// <summary>SaveRouter.PickInstall: which of two installs a restore writes into.</summary>
        private static void Installs()
        {
            string? why;
            var one = new List<(string, int)> { (@"C:\RA", 7) };
            Check("install: one candidate → it", SaveRouter.PickInstall(one, Array.Empty<string>(), _ => -1, out why) == 0 && why == null);
            var shared = new List<(string, int)> { (@"C:\RA", 7), (@"C:\RA", 7) };
            Check("install: two entries on one folder (one exe, two cores) → not ambiguous", SaveRouter.PickInstall(shared, Array.Empty<string>(), _ => -1, out why) == 0 && why == null);
            var scored = new List<(string, int)> { (@"C:\RA", 5), (@"D:\RA2", 7) };
            Check("install: the better score wins", SaveRouter.PickInstall(scored, Array.Empty<string>(), _ => -1, out why) == 1);
            var two = new List<(string, int)> { (@"C:\RA", 7), (@"D:\RA2", 7) };
            Check("install: a tie, the card's live file listed from the second → the second",
                  SaveRouter.PickInstall(two, new[] { @"D:\RA2\saves\Snes9x\Zelda.srm" }, p => p.StartsWith(@"D:\RA2") ? 1 : -1, out why) == 1);
            Check("install: a tie, not listed, its folder holds the live file → that one",
                  SaveRouter.PickInstall(two, new[] { @"D:\RA2\saves\Snes9x\Zelda.srm" }, _ => -1, out why) == 1);
            Check("install: a tie, no live file → refused, with a reason",
                  SaveRouter.PickInstall(two, Array.Empty<string>(), _ => -1, out why) == -1 && why != null, why);
            Check("install: a folder prefix is not its sibling (C:\\RA vs C:\\RA2)",
                  SaveRouter.PickInstall(new List<(string, int)> { (@"C:\RA", 7), (@"C:\RA2", 7) }, new[] { @"C:\RA2\saves\x.srm" }, _ => -1, out why) == 1);
        }

        /// <summary>SaveGameView: the game as RetroArch would run it, everything else the real game's.</summary>
        private static void View()
        {
            var game = LbIntegrations.Probe.StubGame.With(LbIntegrations.Probe.StubGame.Create("g9", "Zelda", @"C:\r\Zelda.sfc"), "Platform", "Super Nintendo Entertainment System");
            var ra = new LbIntegrations.Probe.StubEmulator { Title = "RetroArch", ApplicationPath = @"C:\RA\retroarch.exe" };
            var p = ra.AddNewEmulatorPlatform(); p.Platform = "Super Nintendo Entertainment System"; p.CommandLine = "-L \"cores\\snes9x_libretro.dll\" -f";
            var seen = SaveGameView.As(game, ra);
            Check("view: the command line is the emulator's for the platform", seen.GetEffectiveCommandLine() == "-L \"cores\\snes9x_libretro.dll\" -f");
            Check("view: everything else is the real game's", seen.Id == "g9" && seen.Title == "Zelda" && seen.ApplicationPath == @"C:\r\Zelda.sfc" && seen.Platform == "Super Nintendo Entertainment System");
            var none = new LbIntegrations.Probe.StubEmulator { Title = "Bare", ApplicationPath = @"C:\x\x.exe" };
            Check("view: an emulator without a line leaves the game as it is", ReferenceEquals(SaveGameView.As(game, none), game));
            Check("RetroArch slot from a name: .state1 1, .state 0, .state.auto -1, other null",
                  StockRestore.SlotOfName(@"C:\s\Zelda.state1") == 1 && StockRestore.SlotOfName("Zelda.state") == 0
                  && StockRestore.SlotOfName("Zelda.state.auto") == -1 && StockRestore.SlotOfName("Zelda.srm") == null && StockRestore.SlotOfName("Zelda.state12") == 12);
        }

        /// <summary>SlotRestore: the slot a state goes to, its id, and the warning before an unbacked overwrite.</summary>
        private static void Slots()
        {
            var ares = Enumerable.Range(1, 9).ToList();
            Check("slot: the pick, when the emulator has it", SlotRestore.Choose(3, 1, ares, "ares") is (3, null));
            Check("slot: no pick → the backup's own", SlotRestore.Choose(null, 2, ares, "ares") is (2, null));
            Check("slot: Auto (-1) → the backup's own", SlotRestore.Choose(-1, 2, ares, "ares") is (2, null));
            var zero = SlotRestore.Choose(0, 1, ares, "ares");
            Check("slot: one it lacks is refused, saying its numbers", zero.Refused != null && zero.Refused.Contains("ares numbers its states 1 to 9"), zero.Refused);
            Check("slot: no pick and no own → refused", SlotRestore.Choose(null, null, ares, "ares").Refused != null);
            Check("slot: a set that is not a run is listed", SlotRestore.Choose(4, null, new[] { 1, 3 }, "x").Refused?.Contains("1, 3") == true);

            var id = SaveId.Write(new SaveIdData { Emu = "ares", Kind = "state", Sys = "SuperFamicom", By = "name", Id = "Zelda", Slot = 1 });
            Check("id: recomputed for the slot written",
                  SlotRestore.WithSlot(id, 4) == "{\"emu\":\"ares\",\"kind\":\"state\",\"sys\":\"SuperFamicom\",\"by\":\"name\",\"id\":\"Zelda\",\"slot\":4}");
            Check("id: not ours, left as it is", SlotRestore.WithSlot("a2289149085747d39372772546f2fe8c", 4) == "a2289149085747d39372772546f2fe8c");
            var dirId = SaveId.Write(new SaveIdData { Emu = "lb.retroarch", Kind = "state", By = "name", Id = "Zelda.state", Slot = 1, Dir = "emulators\\retroarch\\states" });
            Check("id: its folder kept with the new slot", SaveId.TryRead(SlotRestore.WithSlot(dirId, 4), out var dd) && dd?.Dir == "emulators\\retroarch\\states" && dd?.Slot == 4, SlotRestore.WithSlot(dirId, 4));
            Check("id: its slot read back", SlotRestore.SlotOf(id) == 1 && SlotRestore.SlotOf("guid") == null);

            // A path from an emulator's config, as the emulator resolves it (ExpectedDir.OfConfig, measured 12/10).
            Check("config path: relative → from the emulator's folder", ExpectedDir.OfConfig("Saves/", @"G:\Emu\ares") == @"G:\Emu\ares\Saves\");
            Check("config path: \"..\\x\" → beside the emulator's folder", ExpectedDir.OfConfig(@"..\shared", @"G:\Emu\ares") == @"G:\Emu\shared");
            Check("config path: rooted → as it is", ExpectedDir.OfConfig("D:/Saves/ares/", @"G:\Emu\ares") == @"D:\Saves\ares\");
            Check("config path: empty → none (the emulator's default applies)", ExpectedDir.OfConfig("  ", @"G:\Emu\ares") == null);

            var dir = Path.Combine(Path.GetTempPath(), "lbip-slotrestore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var asked = 0; string? text = null;
            try
            {
                string src = Path.Combine(dir, "backup.bs1"), same = Path.Combine(dir, "same.bs2"), other = Path.Combine(dir, "other.bs3"), none = Path.Combine(dir, "none.bs4");
                File.WriteAllText(src, "the backup"); File.WriteAllText(same, "the backup"); File.WriteAllText(other, "another state");
                SlotRestore.Instead = (t, x) => { asked++; text = x; return false; };
                SlotRestore.HostOverride = "launchbox";
                Check("warn: own card → never asked", SlotRestore.MayOverwrite(other, src, false, "Slot 3", null, out _) && asked == 0);
                Check("warn: target empty → never asked", SlotRestore.MayOverwrite(none, src, true, "Slot 4", null, out _) && asked == 0);
                Check("warn: same content → never asked", SlotRestore.MayOverwrite(same, src, true, "Slot 2", null, out _) && asked == 0);
                bool go = SlotRestore.MayOverwrite(other, src, true, "Slot 3", null, out var no);
                Check("warn: another card's, different → asked; No cancels", !go && asked == 1 && no != null && text != null && text.Contains("Slot 3") && text.Contains("LaunchBox bug"), text);
                SlotRestore.Instead = (t, x) => { asked++; return true; };
                Check("warn: Yes goes on", SlotRestore.MayOverwrite(other, src, true, "Slot 3", null, out _) && asked == 2);
                SlotRestore.HostOverride = "litebox";
                Check("warn: under LiteBox never asked (it backs up itself)", SlotRestore.MayOverwrite(other, src, true, "Slot 3", null, out _) && asked == 2);
                SlotRestore.HostOverride = "bigbox";
                bool big = SlotRestore.MayOverwrite(other, src, true, "Slot 3", null, out var bigNo);
                Check("warn: in BigBox refused with a message, no dialog", !big && bigNo != null && asked == 2, bigNo);
                // RetroArch's ordinary saves through a desk (StockRestore, Codex review 11/10): whose file the target is.
                var liveOfCard = Path.Combine(dir, "Zelda.srm");
                File.WriteAllText(liveOfCard, "card B's 20 hours");
                Check("retroarch save: the target is this card's live file → its own (no warning)", !StockRestore.OtherCard(liveOfCard, new[] { liveOfCard.ToUpperInvariant() }));
                Check("retroarch save: an \"In Vault\" card (no live file) onto an existing file → another card's", StockRestore.OtherCard(liveOfCard, Array.Empty<string>()));
                Check("retroarch save: the card lives elsewhere → the target is another card's", StockRestore.OtherCard(liveOfCard, new[] { Path.Combine(dir, "Zelda (Rev 1).srm") }));
                Check("retroarch save: nothing at the target → nothing to lose", !StockRestore.OtherCard(Path.Combine(dir, "nothing.srm"), Array.Empty<string>()));
                // A rom renamed since the backup (Codex review 12/10): the restore goes to TODAY's name, over the save of
                // the new name - another card's. Card A lived as "Zelda (France).ram", the new card's file is "Zelda (F).ram".
                string oldLive = Path.Combine(dir, "Zelda (France).ram"), newLive = Path.Combine(dir, "Zelda (F).ram");
                File.WriteAllText(newLive, "card B's 10 new hours");
                var cardA = new GameSaveGame { GameId = "g", SaveGroupId = "{\"emu\":\"ares\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"Zelda (France).ram\"}" };
                SlotRestore.LivesForTests = (g, id) => id == cardA.SaveGroupId ? new[] { oldLive } : Array.Empty<string>();
                Check("renamed rom: an old-name backup onto the new name's save → another card's file", SlotRestore.OverAnotherFile(newLive, cardA));
                SlotRestore.LivesForTests = (g, id) => new[] { newLive.ToUpperInvariant() };
                Check("renamed rom: ... onto the card's own live file → not another's", !SlotRestore.OverAnotherFile(newLive, cardA));
                SlotRestore.LivesForTests = (g, id) => Array.Empty<string>();
                Check("renamed rom: no live file known → the caller's rule stands (false)", !SlotRestore.OverAnotherFile(newLive, cardA));
                SlotRestore.LivesForTests = (g, id) => new[] { oldLive };
                Check("renamed rom: nothing at the target → nothing to lose", !SlotRestore.OverAnotherFile(Path.Combine(dir, "nothing.ram"), cardA));
                SlotRestore.HostOverride = "launchbox"; asked = 0;
                SlotRestore.Instead = (t, x) => { asked++; return false; };
                Check("renamed rom: through MayOverwrite → asked, No keeps the new save",
                      !SlotRestore.MayOverwrite(newLive, src, SlotRestore.OverAnotherFile(newLive, cardA), "The current save", null, out _) && asked == 1 && File.ReadAllText(newLive) == "card B's 10 new hours");
            }
            finally
            {
                SlotRestore.Instead = null; SlotRestore.HostOverride = null; SlotRestore.LivesForTests = null;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>Lot 4: the desk of a LaunchBox integration never routes its own rows - its emu in our JSON, an empty id,
        /// its native forms, a GUID (LaunchBox's cards) - whatever instance asks (StockDesk's is never the router's).</summary>
        private static void StockOwners()
        {
            Console.WriteLine();
            Console.WriteLine("  -- a LaunchBox integration's own rows (lot 4)");
            GameSaveBase Row(string id) => new GameSaveGame { GameId = "g1", FileLocation = @"C:\s\x.srm", EmulatorFileName = "retroarch.exe", SaveGroupId = id };
            foreach (var (id, emu) in new[]
            {
                ("{\"emu\":\"lb.retroarch\",\"kind\":\"save\",\"native\":\"saturn-x\"}", "lb.retroarch"),
                ("", "lb.retroarch"),
                ("saturn-atlantis", "lb.retroarch"),
                ("pcsx2:SLUS-20062", "lb.pcsx2"),
                ("c380b29f2702480a982dda74c642a367", "lb.retroarch"),
                ("{\"emu\":\"lb.dolphin\",\"kind\":\"state\",\"native\":\"x\"}", "lb.dolphin"),
            })
                Check("own to " + emu + ": " + Short(id.Length == 0 ? "(empty)" : id), LbIntegrations.Saves.SaveRouter.OwnerForStock(Row(id), emu) == null);
            // A pack plugin's row on a stock desk is not its own: routed when that plugin is registered, else left to the
            // integration (null here - nothing is registered in the probe), never taken for the integration's own by emu.
            Check("a pack row is never taken for lb.retroarch's own (no plugin registered: null, not a twin)",
                  LbIntegrations.Saves.SaveRouter.OwnerForStock(Row("{\"emu\":\"ares\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"zelda\"}"), "lb.retroarch") == null);
            StockIdsCheck();
            IdsBackCheck();
        }

        /// <summary>Lot 7: the setup gives LaunchBox's integrations their own ids back in the platform XML (StockIdsXml,
        /// SaveIdsBack) - every case of id, the rest of the file untouched, all or nothing on a failure.</summary>
        private static void IdsBackCheck()
        {
            Console.WriteLine();
            Console.WriteLine("  -- the ids given back at uninstall (lot 7, StockIdsXml + the setup's SaveIdsBack)");
            const string RA = "lb.retroarch", PS2 = "lb.pcsx2", G1 = "11111111-1111-1111-1111-111111111111", G2 = "22222222-2222-2222-2222-222222222222";
            string X(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            string Row(string game, string? id, string? lineage, string file, int? slot = null)
                => "  <GameSave>\r\n    <GameId>" + game + "</GameId>\r\n"
                   + (id == null ? "    <SaveGroupId />\r\n" : "    <SaveGroupId>" + X(id) + "</SaveGroupId>\r\n")
                   + (lineage == null ? "    <MatchLineageId />\r\n" : "    <MatchLineageId>" + X(lineage) + "</MatchLineageId>\r\n")
                   + "    <FilePath>" + X(file) + "</FilePath>\r\n" + (slot == null ? "" : "    <Slot>" + slot + "</Slot>\r\n") + "  </GameSave>\r\n";
            var saturn = StockIds.Encode(RA, "saturn-atlantis - the lost tales (fr) (disc 1)", false, "a.bcr", null)!;
            var byName = StockIds.Encode(RA, "", false, "Zelda.srm", null, @"emulators\retroarch\saves\snes9x")!;
            var ps2 = StockIds.Encode(PS2, "pcsx2:SLUS-20062", false, "Mcd001.ps2", null)! + "#c2";
            var amp = StockIds.Encode(RA, "a&b <c>", false, "x", null)!;
            var ares = "{\"emu\":\"ares\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"Zelda.ram\"}";
            var guid = "c380b29f2702480a982dda74c642a367";
            var xml = "<?xml version=\"1.0\" standalone=\"yes\"?>\r\n<LaunchBox>\r\n  <Game>\r\n    <ID>" + G1 + "</ID>\r\n    <Title>Ratchet &amp; Clank</Title>\r\n  </Game>\r\n"
                      + Row(G1, saturn, saturn, @"Emulators\RetroArch\saves\a.bcr")
                      + Row(G1, byName, byName, @"Emulators\RetroArch\saves\Snes9x\Zelda.srm")
                      + Row(G1, byName, byName, @"Saves\Super Nintendo\Zelda-01.srm")
                      + Row(G2, byName, byName, @"Emulators\RetroArch\saves\Snes9x\Zelda.srm")
                      + Row(G1, ares, ares, @"Emulators\Nixx-ares\Saves\Zelda.ram")
                      + Row(G1, ps2, ps2, @"Emulators\PCSX2\memcards\Mcd001.ps2")
                      + Row(G1, guid, guid, @"x.state", 1)
                      + Row(G1, amp, null, @"y.srm")
                      + Row(G1, StockIds.Encode(RA, "", false, "Other.srm", null)!, saturn, @"Other.srm")
                      + "</LaunchBox>\r\n";
            int n = 0;
            var back = StockIdsXml.Reverse(xml, () => "fresh" + (++n), out var rows);
            // Per row (an empty <MatchLineageId /> has no value): null for a field written empty.
            string?[] Ids(string text, string field) => System.Text.RegularExpressions.Regex.Matches(text, "(?s)<GameSave>(.*?)</GameSave>")
                .Select(r => System.Text.RegularExpressions.Regex.Match(r.Groups[1].Value, "<" + field + ">([^<]*)</" + field + ">"))
                .Select(m => m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null).ToArray();
            var ids = Ids(back, "SaveGroupId"); var lin = Ids(back, "MatchLineageId");
            Check("back: 7 rows changed, none of ours left", rows == 7 && StockIdsXml.Remaining(back) == 0, "rows " + rows + ", left " + StockIdsXml.Remaining(back));
            Check("back: a native id as it was (Saturn), its lineage too", ids[0] == "saturn-atlantis - the lost tales (fr) (disc 1)" && lin[0] == ids[0], ids[0]);
            Check("back: a by-name card - one new GUID for its rows of one game, its lineage the same", ids[1]!.StartsWith("fresh") && ids[2] == ids[1] && lin[1] == ids[1] && lin[2] == ids[1], ids[1] + " " + ids[2]);
            Check("back: ... the same id on another game - another GUID", ids[3]!.StartsWith("fresh") && ids[3] != ids[1], ids[3]);
            Check("back: the pack's own row (ares) left exactly", ids[4] == ares && lin[4] == ares);
            Check("back: a decoration after the JSON kept (pcsx2:...#c2)", ids[5] == "pcsx2:SLUS-20062#c2" && lin[5] == ids[5], ids[5]);
            Check("back: a GUID left as it is", ids[6] == guid);
            Check("back: & < > in an id escaped as LaunchBox writes them", ids[7] == "a&b <c>" && lin[7] == null && back.Contains("<SaveGroupId>a&amp;b &lt;c&gt;</SaveGroupId>"), ids[7]);
            Check("back: a lineage naming another card follows that card", ids[8]!.StartsWith("fresh") && lin[8] == "saturn-atlantis - the lost tales (fr) (disc 1)", lin[8]);
            Check("back: every other byte of the file where it was (the game, its title, the paths, the slot)",
                  System.Text.RegularExpressions.Regex.Replace(back, "<(SaveGroupId|MatchLineageId)>[^<]*</", "<$1></")
                  == System.Text.RegularExpressions.Regex.Replace(xml, "<(SaveGroupId|MatchLineageId)>[^<]*</", "<$1></"));
            Check("back: twice = once (nothing of ours left the second time)", StockIdsXml.Reverse(back, () => "again", out var again) == back && again == 0);

            // The setup's run on a made-up LaunchBox: the files, the copy, a failure halfway, an unreadable file.
            var root = Path.Combine(Path.GetTempPath(), "lbip-idsback-" + Guid.NewGuid().ToString("N"));
            var platforms = Path.Combine(root, "Data", "Platforms");
            Directory.CreateDirectory(platforms);
            var plain = "<?xml version=\"1.0\" standalone=\"yes\"?>\r\n<LaunchBox>\r\n" + Row(G1, ares, ares, "z.ram") + "</LaunchBox>\r\n";
            var a = Path.Combine(platforms, "A.xml"); var b = Path.Combine(platforms, "B.xml"); var c = Path.Combine(platforms, "C.xml");
            void Reset() { File.WriteAllText(a, xml, new System.Text.UTF8Encoding(false)); File.WriteAllText(b, xml, new System.Text.UTF8Encoding(true)); File.WriteAllText(c, plain, new System.Text.UTF8Encoding(false)); }
            string H(string p) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)));
            try
            {
                Reset();
                var before = new[] { H(a), H(b), H(c) };
                NixxIntegrations.SaveIdsBack.FailWriteForTests = name => name == "B.xml";
                var failed = NixxIntegrations.SaveIdsBack.Run(root);
                NixxIntegrations.SaveIdsBack.FailWriteForTests = null;
                Check("setup: B's write fails after A's - refused, A put back, every file as it was, nothing left beside them",
                      !failed.ok && H(a) == before[0] && H(b) == before[1] && H(c) == before[2] && Directory.GetFiles(platforms).Length == 3, failed.message);
                File.WriteAllBytes(c, new byte[] { 0x3C, 0xC3, 0x28, 0x3E });   // not UTF-8
                var unreadable = NixxIntegrations.SaveIdsBack.Run(root);
                Check("setup: a file that cannot be read - refused, nothing written", !unreadable.ok && H(a) == before[0] && H(b) == before[1] && unreadable.message.Contains("C.xml"), unreadable.message);
                Reset();
                var done = NixxIntegrations.SaveIdsBack.Run(root);
                var store = Path.Combine(root, "Local", "Plugins", ".data", "nixx-saves-backup");
                var copies = Directory.Exists(store) ? Directory.GetDirectories(store, "uninstall-*") : Array.Empty<string>();
                var latest = copies.OrderBy(d => d, StringComparer.Ordinal).LastOrDefault();
                Check("setup: given back - A and B with none of ours left, C (nothing of ours) untouched",
                      done.ok && StockIdsXml.Remaining(File.ReadAllText(a)) == 0 && StockIdsXml.Remaining(File.ReadAllText(b)) == 0 && H(c) == before[2], done.message);
                Check("setup: ... the files as they were copied aside first", latest != null && H(Path.Combine(latest, "A.xml")) == before[0] && H(Path.Combine(latest, "B.xml")) == before[1], latest ?? "no copy");
                var bytes = File.ReadAllBytes(b);
                Check("setup: ... each file's own encoding kept (B with its BOM, A without)", bytes.Length > 3 && bytes[0] == 0xEF && File.ReadAllBytes(a)[0] == (byte)'<');
                Check("setup: ... and a second run finds nothing to do", NixxIntegrations.SaveIdsBack.Run(root) is { ok: true } again2 && again2.message.Length == 0);
                // A host started during the uninstall: before the first write, nothing written; after it, put back.
                Reset();
                var hostUp = NixxIntegrations.SaveIdsBack.Run(root, () => "LaunchBox");
                Check("setup: LaunchBox started before the first write - refused, nothing written",
                      !hostUp.ok && hostUp.message.Contains("LaunchBox") && H(a) == before[0] && H(b) == before[1], hostUp.message);
                var ran = NixxIntegrations.SaveIdsBack.Run(root);
                Check("setup: LaunchBox started after the write, before the folders - the files put back as they were",
                      ran.ok && ran.Written.Count == 2 && NixxIntegrations.SaveIdsBack.Undo(ran) && H(a) == before[0] && H(b) == before[1] && H(c) == before[2]);
            }
            finally { NixxIntegrations.SaveIdsBack.FailWriteForTests = null; try { Directory.Delete(root, true); } catch { } }
        }

        /// <summary>Lot 4: LaunchBox's integrations' ids in our form and back (StockIds) - pinned strings, exact round trips.</summary>
        private static void StockIdsCheck()
        {
            Console.WriteLine();
            Console.WriteLine("  -- LaunchBox's integrations' ids in our form (lot 4, StockIds)");
            const string RA = "lb.retroarch", PS2 = "lb.pcsx2";
            void Pin(string what, string? got, string? want) => Check(what + ": " + Short(want ?? "null"), got == want, "got " + (got ?? "null"));
            Pin("PCSX2's own", StockIds.Encode(PS2, "pcsx2:SLUS-20062", false, "Mcd001.ps2", null), "{\"emu\":\"lb.pcsx2\",\"kind\":\"save\",\"native\":\"pcsx2:SLUS-20062\"}");
            Pin("a Saturn set", StockIds.Encode(RA, "saturn-atlantis - the lost tales (fr) (disc 1)", false, "x.bcr", null),
                "{\"emu\":\"lb.retroarch\",\"kind\":\"save\",\"native\":\"saturn-atlantis - the lost tales (fr) (disc 1)\"}");
            Pin("a GUID card, a state", StockIds.Encode(RA, "c380b29f2702480a982dda74c642a367", true, "x.state1", 1),
                "{\"emu\":\"lb.retroarch\",\"kind\":\"state\",\"native\":\"c380b29f2702480a982dda74c642a367\"}");
            Pin("no id, a save: by name", StockIds.Encode(RA, "", false, "Legend of Zelda, The - A Link to the Past (France).srm", null),
                "{\"emu\":\"lb.retroarch\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"Legend of Zelda, The - A Link to the Past (France).srm\"}");
            Pin("no id, a state: by name and slot", StockIds.Encode(RA, null, true, "Zelda.state2", 2), "{\"emu\":\"lb.retroarch\",\"kind\":\"state\",\"by\":\"name\",\"id\":\"Zelda.state2\",\"slot\":2}");
            // Review 10/10: one name in two cores' folders is two saves - the folder in the id.
            var inSnes9x = StockIds.Encode(RA, "", true, "Zelda.state", 0, @"emulators\retroarch\states\snes9x");
            Pin("no id, a state: by name, slot and folder", inSnes9x,
                "{\"emu\":\"lb.retroarch\",\"kind\":\"state\",\"by\":\"name\",\"id\":\"Zelda.state\",\"slot\":0,\"dir\":\"emulators\\\\retroarch\\\\states\\\\snes9x\"}");
            Check("by name: the same file name in another core's folder is another id", inSnes9x != StockIds.Encode(RA, "", true, "Zelda.state", 0, @"emulators\retroarch\states\bsnes"));
            Check("by name: the folder changes nothing for a native id", StockIds.Encode(RA, "saturn-x", false, "x.bcr", null, "d") == StockIds.Encode(RA, "saturn-x", false, "x.bcr", null));
            Check("by name: decoded, still no id", StockIds.TryDecode(inSnes9x, RA, out var noneDir) && noneDir == "");
            Check("dir: under the root → relative, lower case", StockIds.DirOf(@"G:\LB\Emulators\RetroArch\states\Snes9x\Zelda.state", @"G:\LB") == @"emulators\retroarch\states\snes9x");
            Check("dir: a path LaunchBox keeps relative → the same", StockIds.DirOf(@"Emulators\RetroArch\states\Snes9x\Zelda.state", @"G:\LB") == @"emulators\retroarch\states\snes9x");
            Check("dir: outside the root → whole, lower case", StockIds.DirOf(@"H:\Saves\Zelda.srm", @"G:\LB") == @"h:\saves");
            Pin("result: by name, restored into slot 3 of that folder - its card",
                StockIds.Result(RA, "", inSnes9x, "", true, "Zelda.state3", 3, @"emulators\retroarch\states\snes9x"),
                StockIds.Encode(RA, "", true, "Zelda.state3", 3, @"emulators\retroarch\states\snes9x"));
            // Second review 10/10: a card made before "dir", restored into its own file, keeps its id.
            var oldByName = "{\"emu\":\"lb.retroarch\",\"kind\":\"state\",\"by\":\"name\",\"id\":\"Zelda.state1\",\"slot\":1}";
            Pin("result: a by-name card made before dir, back into its own file - its id kept",
                StockIds.Result(RA, "", oldByName, "", true, "Zelda.state1", 1, @"emulators\retroarch\states\snes9x"), oldByName);
            Pin("result: ... into another slot - that slot's card, with its folder",
                StockIds.Result(RA, "", oldByName, "", true, "Zelda.state2", 2, @"emulators\retroarch\states\snes9x"),
                StockIds.Encode(RA, "", true, "Zelda.state2", 2, @"emulators\retroarch\states\snes9x"));
            Pin("result: a by-name card of one folder restored into another's file of that name - the other folder's",
                StockIds.Result(RA, "", inSnes9x, "", true, "Zelda.state", 0, @"emulators\retroarch\states\bsnes"),
                StockIds.Encode(RA, "", true, "Zelda.state", 0, @"emulators\retroarch\states\bsnes"));
            Pin("already ours: left alone", StockIds.Encode(RA, "{\"emu\":\"ares\",\"kind\":\"save\"}", false, "x", null), "{\"emu\":\"ares\",\"kind\":\"save\"}");
            Pin("LiteBox's: left alone", StockIds.Encode(RA, "entry:42", false, "x", null), "entry:42");
            Pin("no id, no file: left alone", StockIds.Encode(RA, "", false, "", null), "");

            // Exact round trips, awkward characters in the native id.
            foreach (var native in new[] { "pcsx2:SLUS-20062", "saturn-a#b|c \"d\" é 🎮", "c380b29f2702480a982dda74c642a367", "x\\y/z" })
            {
                var ours = StockIds.Encode(PS2, native, false, "f", null)!;
                Check("round trip: " + Short(native), StockIds.TryDecode(ours, PS2, out var back) && back == native && !ours.Contains('#') && !ours.Contains('|'), ours + " -> " + back);
                Check("... and back for lot 7: " + Short(native), StockIds.Reverse(ours, () => "NEW") == native);
            }
            Check("decode: a by-name id gives no id (what the integration listed)", StockIds.TryDecode(StockIds.Encode(RA, "", false, "z.srm", null), RA, out var none) && none == "");
            Check("decode: another integration's id is not this one's", !StockIds.TryDecode(StockIds.Encode(PS2, "pcsx2:X", false, "f", null), RA, out _));
            Check("decode: a pack plugin's id is not this one's", !StockIds.TryDecode("{\"emu\":\"ares\",\"kind\":\"save\"}", RA, out _));
            Check("decode: a native id is not ours", !StockIds.TryDecode("saturn-x", RA, out _) && !StockIds.TryDecode("", RA, out _) && !StockIds.TryDecode(null, RA, out _));
            Check("decode: a decoration after the JSON stays on the native id", StockIds.TryDecode(StockIds.Encode(RA, "saturn-x", false, "f", null) + "#c2", RA, out var dec) && dec == "saturn-x#c2", dec);
            Check("reverse: a by-name id gets a fresh GUID", StockIds.Reverse(StockIds.Encode(RA, "", false, "z.srm", null), () => "NEW") == "NEW");
            Check("reverse: a pack plugin's id is left alone", StockIds.Reverse("{\"emu\":\"ares\",\"kind\":\"save\"}", () => "NEW") == "{\"emu\":\"ares\",\"kind\":\"save\"}");

            // A restore's result.
            var saturn = StockIds.Encode(RA, "saturn-x", false, "x.bcr", null);
            Pin("result: the id sent given back - the one that came in", StockIds.Result(RA, "saturn-x", saturn, "saturn-x", false, "x.bcr", null), saturn);
            Pin("result: came in native - ours", StockIds.Result(RA, "saturn-x", null, "saturn-x", false, "x.bcr", null), saturn);
            Pin("result: another id of its own - ours", StockIds.Result(RA, "saturn-x", saturn, "saturn-y", false, "y.bcr", null), StockIds.Encode(RA, "saturn-y", false, "y.bcr", null));
            var guid = StockIds.Encode(RA, "c380b29f2702480a982dda74c642a367", true, "z.state1", 1);
            Pin("result: no id for a GUID card - its card kept", StockIds.Result(RA, "c380b29f2702480a982dda74c642a367", guid, "", true, "z.state3", 3), guid);
            Pin("result: by name, restored into slot 3 - slot 3's card", StockIds.Result(RA, "", StockIds.Encode(RA, "", true, "z.state1", 1), "", true, "z.state3", 3), StockIds.Encode(RA, "", true, "z.state3", 3));

            Pin("lineage: equal to the old id - follows", StockIds.Lineage("saturn-x", "saturn-x", saturn), saturn);
            Pin("lineage: empty - follows", StockIds.Lineage("", "saturn-x", saturn), saturn);
            Pin("lineage: its own - kept", StockIds.Lineage("other", "saturn-x", saturn), "other");
            MigrationCheck();
        }

        /// <summary>Lot 4: the persisted cards of LaunchBox's integrations given our ids and back (StockIdMigration.Plan) - on rows
        /// shaped like G:\LB1326's (Atlantis's Saturn set, Pokemon Cristal's two GUID cards on one file).</summary>
        private static void MigrationCheck()
        {
            Console.WriteLine();
            Console.WriteLine("  -- the persisted cards given our ids (lot 4, StockIdMigration)");
            PersistedRow R(string game, string? id, string? lineage, string exe, string file, bool state = false)
                => new PersistedRow { GameId = game, SaveGroupId = id, MatchLineageId = lineage, EmulatorFileName = exe, FilePath = file, IsState = state };
            const string Sat = "saturn-atlantis - the lost tales (fr) (disc 1)";
            const string G1 = "e47db733aaaaaaaaaaaaaaaaaaaaaaaa", G2 = "5eeffc4ebbbbbbbbbbbbbbbbbbbbbbbb", G3 = "c380b29fcccccccccccccccccccccccc";
            var ares = "{\"emu\":\"ares\",\"kind\":\"save\",\"by\":\"name\",\"id\":\"zelda\"}";
            var rows = new List<PersistedRow>
            {
                R("atlantis", Sat, Sat, "retroarch.exe", @"Emulators\RetroArch\saves\Beetle Saturn\Atlantis.bcr"),
                R("atlantis", Sat, Sat, "retroarch.exe", @"Saves\Sega Saturn\Atlantis.bcr"),
                R("atlantis", Sat, "", "retroarch.exe", @"Saves\Sega Saturn\Atlantis-01.bcr"),
                R("pokemon", G1, G1, "retroarch.exe", @"Emulators\RetroArch\saves\Gambatte\Pokemon.srm"),
                R("pokemon", G2, G2, "retroarch.exe", @"Emulators\RetroArch\saves\Gambatte\Pokemon.srm"),
                R("pokemon", G2, G1, "retroarch.exe", @"Saves\Nintendo Game Boy\Pokemon-01.srm"),   // a backup whose lineage names the other card
                R("rolan", G3, G3, "retroarch.exe", @"Emulators\RetroArch\states\Gambatte\Rolan.state1", state: true),
                R("doubt", "d0d0d0d0dddddddddddddddddddddddd", "", "shared.exe", @"x\doubt.sav"),
                R("zelda", ares, ares, "ares.exe", @"Emulators\ares\Saves\zelda.srm"),
                R("ps2", "pcsx2:SLUS-20062", "pcsx2:SLUS-20062", "pcsx2-qt.exe", @"memcards\Mcd001.ps2"),
            };
            string? StockOf(string? exe) => exe switch { "retroarch.exe" => "lb.retroarch", "pcsx2-qt.exe" => "lb.pcsx2", _ => null };   // shared.exe: a pack plugin drives it too
            var said = new List<string>();
            var forward = StockIdMigration.Plan(rows, StockOf, true, () => "NEW", said.Add);
            string? After(int i) { var c = forward.FirstOrDefault(x => ReferenceEquals(x.Row, rows[i])); return c == null ? rows[i].SaveGroupId : c.GroupId; }
            string? LineageAfter(int i) { var c = forward.FirstOrDefault(x => ReferenceEquals(x.Row, rows[i])); return c == null ? rows[i].MatchLineageId : c.Lineage; }
            var satJson = StockIds.Encode("lb.retroarch", Sat, false, null, null);
            Check("Atlantis: its three rows one card, ours", After(0) == satJson && After(1) == satJson && After(2) == satJson, After(0));
            Check("... its lineage follows; an empty one stays empty", LineageAfter(0) == satJson && LineageAfter(2) == "");
            var p1 = StockIds.Encode("lb.retroarch", G1, false, null, null); var p2 = StockIds.Encode("lb.retroarch", G2, false, null, null);
            Check("Pokemon: two GUID cards on one file stay two", After(3) == p1 && After(4) == p2 && p1 != p2);
            Check("... a backup naming the other card in its lineage follows that card", After(5) == p2 && LineageAfter(5) == p1, LineageAfter(5));
            Check("Rolan's Curse: a state card - kind state", After(6) == StockIds.Encode("lb.retroarch", G3, true, null, null));
            Check("an exe a pack plugin drives too: left as it is, and said", After(7) == "d0d0d0d0dddddddddddddddddddddddd" && said.Any(s => s.Contains("not certain")));
            Check("a pack plugin's card: never touched", After(8) == ares && LineageAfter(8) == ares);
            Check("PCSX2's own: ours", After(9) == StockIds.Encode("lb.pcsx2", "pcsx2:SLUS-20062", false, null, null));

            // Applied, then planned again: nothing (idempotent); and back: every id as it was, byte for byte.
            var applied = rows.Select((r, i) => R(r.GameId, After(i), LineageAfter(i), r.EmulatorFileName!, r.FilePath!, r.IsState)).ToList();
            Check("planned again: nothing to change", StockIdMigration.Plan(applied, StockOf, true, () => "NEW").Count == 0);
            var back = StockIdMigration.Plan(applied, StockOf, false, () => "NEW");
            bool Same(int i)
            {
                var c = back.FirstOrDefault(x => ReferenceEquals(x.Row, applied[i]));
                var id = c == null ? applied[i].SaveGroupId : c.GroupId;
                var lin = c == null ? applied[i].MatchLineageId : c.Lineage;
                return id == rows[i].SaveGroupId && lin == rows[i].MatchLineageId;
            }
            Check("back: every id and lineage as it was, exactly", Enumerable.Range(0, rows.Count).All(Same),
                  string.Join(" | ", Enumerable.Range(0, rows.Count).Where(i => !Same(i)).Select(i => rows[i].GameId)));
            // An integration not fully hooked (Codex review 10/10): its cards stay - or go back - in its own form, the others not.
            var notPs2 = StockIdMigration.Plan(rows, StockOf, true, () => "NEW", null, e => e != "lb.pcsx2");
            Check("one integration left out: its card stays native", !notPs2.Any(c => ReferenceEquals(c.Row, rows[9])));
            Check("... the others' cards still ours", notPs2.Any(c => ReferenceEquals(c.Row, rows[0])) && notPs2.Any(c => ReferenceEquals(c.Row, rows[3])));
            var onlyPs2Back = StockIdMigration.Plan(applied, StockOf, false, () => "NEW", null, e => e == "lb.pcsx2");
            Check("... and back for that one only", onlyPs2Back.Count == 1 && onlyPs2Back[0].GroupId == "pcsx2:SLUS-20062" && ReferenceEquals(onlyPs2Back[0].Row, applied[9]));
            // The card a listed row takes (StockDesk.PickCard, Codex review 10/10): one file can hold several games' saves.
            Check("a listed row with its own id keeps it (a PCSX2 card per game on one .ps2)", StockDesk.PickCard("pcsx2:SLES-1", new[] { "{\"emu\":\"lb.pcsx2\",\"kind\":\"save\",\"native\":\"pcsx2:SLUS-2\"}" }) == null);
            Check("a row listed with no id takes the one card at its place", StockDesk.PickCard("", new[] { "A", "A" }) == "A");
            Check("... none when two cards are there", StockDesk.PickCard("", new[] { "A", "B" }) == null && StockDesk.PickCard(null, null) == null);            var byName = new List<PersistedRow> { R("z", StockIds.Encode("lb.retroarch", "", false, "z.srm", null), null, "retroarch.exe", "z.srm") };
            Check("back: a by-name card gets a fresh id", StockIdMigration.Plan(byName, StockOf, false, () => "NEW").Single().GroupId == "NEW");
        }

        private static string Short(string s) => (s.Length > 60 ? s.Substring(0, 57) + "..." : s).Replace("\n", "\\n").Replace("\t", "\\t");

        private static void Check(string what, bool ok, string? error = null)
        {
            Console.WriteLine((ok ? "  OK    " : "  FAIL  ") + what);
            if (!ok) { _bad++; if (error != null) Console.WriteLine("        " + error); }
        }
    }
}
