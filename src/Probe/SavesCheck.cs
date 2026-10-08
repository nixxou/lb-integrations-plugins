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
            Check("our ids start with the plugin's prefix", ours.StartsWith(NixxSavePlugin.OwnPrefix("ares"), StringComparison.Ordinal));
            var longer = SaveId.Write(new SaveIdData { Emu = "aresx", Kind = "save", By = "name", Id = "Zelda.srm" });
            Check("another emu that starts the same is not taken for ours", !longer.StartsWith(NixxSavePlugin.OwnPrefix("ares"), StringComparison.Ordinal));
            Check("a translated id is not ours either", !SaveId.Write(new SaveIdData { Emu = "lb.retroarch", Kind = "save" }).StartsWith(NixxSavePlugin.OwnPrefix("ares"), StringComparison.Ordinal));
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

            Check("RetroArch and Dolphin are restored by the desk, PCSX2 is not",
                  StockRestore.Handles("lb.retroarch") && StockRestore.Handles("lb.dolphin") && !StockRestore.Handles("lb.pcsx2") && !StockRestore.Handles("ares"));
        }

        private static string Short(string s) => (s.Length > 60 ? s.Substring(0, 57) + "..." : s).Replace("\n", "\\n").Replace("\t", "\\t");

        private static void Check(string what, bool ok, string? error = null)
        {
            Console.WriteLine((ok ? "  OK    " : "  FAIL  ") + what);
            if (!ok) { _bad++; if (error != null) Console.WriteLine("        " + error); }
        }
    }
}
