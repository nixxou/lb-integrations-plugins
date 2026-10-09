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
        }

        /// <summary>Shared.Compat: the registry complete, the ini read and written, a patch off, a needed patch off.</summary>
        private static void CompatSwitches()
        {
            var all = LbIntegrations.Compat.CompatRegistry.All;
            Check("compat: every patch has its id, title and four texts",
                  all.All(p => p.Id.Length > 0 && p.Title.Length > 0 && p.Problem.Length > 0 && p.Example.Length > 0 && p.Does.Length > 0 && p.Without.Length > 0));
            Check("compat: ids are unique", all.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count);
            Check("compat: every 'needs' names a patch", all.Where(p => p.Needs != null).All(p => LbIntegrations.Compat.CompatRegistry.Find(p.Needs!) != null));
            var known = new HashSet<string> { "igame.cmdline", "ra.statics", "ra.loadConfig", "dolphin.statics", "lb.saves", "lb.applyPersisted", "sqlite.reader", "lb.openEmulator", "lb.wizard", "lb.version" };
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
                var w = SaveCompanions.CopyBack(Path.Combine(vault, "Zelda (F)-10.srm"), target, new[] { Path.Combine(vault, "Zelda (F)-10.rtc") }, null);
                Check("companions: the backup's .rtc replaces the live one, under the live stem",
                      w.Count == 1 && File.ReadAllText(Path.Combine(live, "Zelda (F).rtc")) == "clock");
                Check("companions: none in the backup → the live one is left", SaveCompanions.CopyBack(Path.Combine(vault, "Zelda (F)-10.srm"), target, Array.Empty<string>(), null).Count == 0);
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
            Check("id: its slot read back", SlotRestore.SlotOf(id) == 1 && SlotRestore.SlotOf("guid") == null);

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
            }
            finally
            {
                SlotRestore.Instead = null; SlotRestore.HostOverride = null;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static string Short(string s) => (s.Length > 60 ? s.Substring(0, 57) + "..." : s).Replace("\n", "\\n").Replace("\t", "\\t");

        private static void Check(string what, bool ok, string? error = null)
        {
            Console.WriteLine((ok ? "  OK    " : "  FAIL  ") + what);
            if (!ok) { _bad++; if (error != null) Console.WriteLine("        " + error); }
        }
    }
}
