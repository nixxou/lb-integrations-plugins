// The melonDS assertion, against a FORGED installation.
//
// Declared plainly, as the Flycast and Xenia checks were: there is no melonDS and no DS game on the
// machine this was written on. Everything below is built in the temp folder from what melonDS's own
// source says it does - the TOML keys, the .sav and .ml<n> naming, the header offsets - and then
// handed to the plugin through the PUBLIC contract. So this proves the plugin agrees with our
// READING of melonDS, not that our reading is right. Confronting it with a real install is owed.
//
// What each part would catch:
//
//   1. a save found BESIDE THE ROM when nothing is configured    - the stock disposition
//   2. a save found in the CONFIGURED folder when there is one    - and not listed twice
//   3. states read as .ml1 .. .ml8, and .mln ignored              - the extension that looks right
//   4. the asset name taken from INSIDE an archive                - not from the archive's own name
//   5. the TOML written without disturbing what it did not own    - foreign tables and keys survive
//   6. a path holding an apostrophe surviving the round trip      - literal strings cannot hold one
//   7. two passes leaving the file byte for byte identical        - no drift on every launch
//   8. the console mode chosen from the ROM header                - DS, DSi, and DSiWare refused
//   9. AutoHotkey scripts naming the keys melonDS really binds

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class MelonDsCheck
    {
        private const string PlainRom = "Plain DS Game (USA).nds";
        private const string DsiRom = "DSi Enhanced (USA).nds";
        private const string WareRom = "DSiWare Title (USA).nds";
        private const string ArchiveRom = "Zipped Game.zip";
        private const string ArchiveInner = "Inner Name (Europe).nds";

        public static bool Run(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            Console.WriteLine("-- melonds, on a FORGED install  [WRITES, in the temp folder] " + new string('-', 3));

            string root = Path.Combine(Path.GetTempPath(), "lbip-melonds-" + Guid.NewGuid().ToString("N"));
            try
            {
                var (exe, romDir) = Forge(root);
                Console.WriteLine("  install : " + Path.GetDirectoryName(exe));
                Console.WriteLine("  roms    : " + romDir);

                bool ok = true;
                ok &= BesideTheRom(plugin, exe, romDir);
                ok &= Redirected(plugin, exe, romDir);
                ok &= TomlLeavesForeignContentAlone(exe);
                ok &= TomlSurvivesAnApostrophe(root);
                ok &= ConsoleMode(plugin, exe, romDir);
                ok &= PerTitleNand(exe, romDir);
                ok &= Scripts(plugin, exe);
                ok &= Slots(plugin);

                Console.WriteLine();
                Console.WriteLine("  " + (ok ? "OK - the plugin matches our reading of melonDS" : "NOT OK - see above"));
                return ok;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        }

        // ── the fixture ──────────────────────────────────────────────────────

        private static (string exe, string romDir) Forge(string root)
        {
            string install = Path.Combine(root, "melonDS");
            string romDir = Path.Combine(root, "roms");
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(romDir);

            string exe = Path.Combine(install, "melonDS.exe");
            File.WriteAllBytes(exe, Array.Empty<byte>());

            File.WriteAllBytes(Path.Combine(romDir, PlainRom), Header(unitCode: 0x00, titleIdHigh: 0));
            File.WriteAllBytes(Path.Combine(romDir, DsiRom), Header(unitCode: 0x02, titleIdHigh: 0x00030000));
            File.WriteAllBytes(Path.Combine(romDir, WareRom), Header(unitCode: 0x03, titleIdHigh: 0x00030004));

            // A ROM inside an archive, whose INNER name is what melonDS uses for the save.
            var zip = Path.Combine(romDir, ArchiveRom);
            using (var stream = new FileStream(zip, FileMode.Create))
            using (var archive = new System.IO.Compression.ZipArchive(
                       stream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(ArchiveInner);
                using var writer = entry.Open();
                var bytes = Header(unitCode: 0x00, titleIdHigh: 0);
                writer.Write(bytes, 0, bytes.Length);
            }

            // What melonDS would have written beside the ROMs, with nothing configured.
            Write(romDir, "Plain DS Game (USA).sav", 65536);
            Write(romDir, "Plain DS Game (USA).ml1", 4096);
            Write(romDir, "Plain DS Game (USA).ml8", 4096);
            Write(romDir, "Plain DS Game (USA).mln", 4096);   // the dialog's suffix - NOT a slot
            Write(romDir, "Inner Name (Europe).sav", 32768);  // the archive's save, by its inner name

            return (exe, romDir);
        }

        /// <summary>A DS cart header as far as the two fields we read: UnitCode at 0x12 and
        /// DSiTitleIDHigh at 0x234 (NDS_Header.h:44-52 and the DSi block).</summary>
        private static byte[] Header(byte unitCode, uint titleIdHigh)
        {
            var bytes = new byte[0x1000];
            var title = Encoding.ASCII.GetBytes("FORGEDTITLE");
            Buffer.BlockCopy(title, 0, bytes, 0, title.Length);
            bytes[0x12] = unitCode;
            bytes[0x230] = 0x21; bytes[0x231] = 0x43; bytes[0x232] = 0x65; bytes[0x233] = 0x87;
            bytes[0x234] = (byte)(titleIdHigh & 0xFF);
            bytes[0x235] = (byte)((titleIdHigh >> 8) & 0xFF);
            bytes[0x236] = (byte)((titleIdHigh >> 16) & 0xFF);
            bytes[0x237] = (byte)((titleIdHigh >> 24) & 0xFF);
            return bytes;
        }

        private static void Write(string dir, string name, int size)
        {
            var bytes = new byte[size];
            for (int i = 0; i < size; i++) bytes[i] = (byte)(i * 31 + name.Length);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }

        private static string ConfigPath(string exe)
            => Path.Combine(Path.GetDirectoryName(exe) ?? "", "melonDS.toml");

        // ── 1, 3, 4. the stock disposition ───────────────────────────────────

        private static bool BesideTheRom(EmulatorPlugin plugin, string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- no save path configured: everything sits beside the ROM");

            var all = List(plugin, exe, romDir, PlainRom, ArchiveRom);
            if (all == null) return false;

            bool ok = true;
            var saves = all.Where(s => s is not GameSaveState).ToList();
            var states = all.OfType<GameSaveState>().ToList();

            ok &= Check("the plain ROM's .sav is listed",
                        saves.Any(s => Name(s) == "Plain DS Game (USA).sav"));
            ok &= Check("both occupied slots are listed, 1 and 8",
                        states.Select(s => s.Slot ?? -1).OrderBy(n => n).SequenceEqual(new[] { 1, 8 }));
            ok &= Check(".mln is NOT taken for a slot",
                        !all.Any(s => Name(s).EndsWith(".mln", StringComparison.OrdinalIgnoreCase)));
            ok &= Check("the archived ROM's save is found by its INNER name",
                        saves.Any(s => Name(s) == "Inner Name (Europe).sav"));
            ok &= Check("nothing is named after the archive itself",
                        !all.Any(s => Name(s).StartsWith("Zipped Game", StringComparison.OrdinalIgnoreCase)));
            return ok;
        }

        // ── 2. the redirected disposition ────────────────────────────────────

        private static bool Redirected(EmulatorPlugin plugin, string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- save paths configured: the folder wins, and nothing is listed twice");

            string install = Path.GetDirectoryName(exe);
            string saveDir = Path.Combine(install, "saves");
            string stateDir = Path.Combine(install, "savestates");
            Directory.CreateDirectory(saveDir);
            Directory.CreateDirectory(stateDir);
            Write(saveDir, "Plain DS Game (USA).sav", 65536);
            Write(stateDir, "Plain DS Game (USA).ml3", 4096);

            File.WriteAllText(ConfigPath(exe),
                "[Instance0]\r\nSaveFilePath = '" + saveDir + "'\r\nSavestatePath = '" + stateDir + "'\r\n");

            var all = List(plugin, exe, romDir, PlainRom);
            if (all == null) return false;

            bool ok = true;
            var saves = all.Where(s => s is not GameSaveState).ToList();
            var states = all.OfType<GameSaveState>().ToList();

            ok &= Check("exactly one save, and it is the configured one",
                        saves.Count == 1 && Dir(saves[0]) == saveDir);
            ok &= Check("exactly one state, slot 3, from the configured folder",
                        states.Count == 1 && states[0].Slot == 3 && Dir(states[0]) == stateDir);
            ok &= Check("the copy beside the ROM is not active any more",
                        !plugin.IsSaveActive(BesideRom(saves[0], romDir), exe));
            ok &= Check("the configured copy IS active", plugin.IsSaveActive(saves[0], exe));

            try { File.Delete(ConfigPath(exe)); } catch { }
            try { Directory.Delete(saveDir, true); Directory.Delete(stateDir, true); } catch { }
            return ok;
        }

        /// <summary>The same save row, but pointing at the copy beside the ROM.</summary>
        private static GameSaveBase BesideRom(GameSaveBase save, string romDir)
            => new GameSaveGame
            {
                GameId = save.GameId,
                FileLocation = Path.Combine(romDir, Name(save)),
                OriginalFileName = save.OriginalFileName,
                SaveGroupId = save.SaveGroupId,
                SaveGroupName = save.SaveGroupName,
            };

        // ── 5, 7. the TOML writer ────────────────────────────────────────────

        private static bool TomlLeavesForeignContentAlone(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- writing the TOML leaves everything it does not own alone");

            var path = ConfigPath(exe);
            const string original =
                "# a comment melonDS itself would drop, but we must not\r\n"
                + "[Emu]\r\nConsoleType = 0\r\n\r\n"
                + "[Instance0]\r\nWindowWidth = 640\r\n\r\n"
                + "[Instance0.Keyboard]\r\nHK_Pause = 16777216\r\n";
            File.WriteAllText(path, original);

            var error = Write(exe, "Instance0", "SaveFilePath", "'C:\\saves'");
            if (error != null) { Console.WriteLine("    write refused: " + error); return false; }

            var after = File.ReadAllText(path);
            bool ok = true;
            ok &= Check("the foreign table survives", after.Contains("[Instance0.Keyboard]"));
            ok &= Check("the foreign key survives", after.Contains("HK_Pause = 16777216"));
            ok &= Check("the sibling key survives", after.Contains("WindowWidth = 640"));
            ok &= Check("the comment survives", after.Contains("# a comment"));
            ok &= Check("our key landed in OUR table, not another",
                        after.IndexOf("SaveFilePath", StringComparison.Ordinal)
                        > after.IndexOf("[Instance0]", StringComparison.Ordinal)
                        && after.IndexOf("SaveFilePath", StringComparison.Ordinal)
                        < after.IndexOf("[Instance0.Keyboard]", StringComparison.Ordinal));

            // 7. A second identical write must change nothing at all.
            var before = File.ReadAllBytes(path);
            Write(exe, "Instance0", "SaveFilePath", "'C:\\saves'");
            ok &= Check("a second pass leaves the file byte for byte identical",
                        File.ReadAllBytes(path).SequenceEqual(before));

            try { File.Delete(path); } catch { }
            return ok;
        }

        // ── 6. the apostrophe ────────────────────────────────────────────────

        private static bool TomlSurvivesAnApostrophe(string root)
        {
            Console.WriteLine();
            Console.WriteLine("  -- a path holding an apostrophe round-trips");

            var toml = TypeIn("MelonDsToml");
            var text = toml.GetMethod("Text", BindingFlags.Public | BindingFlags.Static);
            var read = toml.GetMethod("Read", BindingFlags.Public | BindingFlags.Static);
            if (text == null || read == null) { Console.WriteLine("    no MelonDsToml.Text/Read"); return false; }

            string awkward = Path.Combine(root, "John's Saves");
            var encoded = (string)text.Invoke(null, new object[] { awkward });

            var path = Path.Combine(root, "apostrophe.toml");
            File.WriteAllText(path, "[Instance0]\r\nSaveFilePath = " + encoded + "\r\n");

            var map = (IDictionary<string, string>)read.Invoke(
                null, new object[] { path, "Instance0", new[] { "SaveFilePath" } });
            var back = map.TryGetValue("SaveFilePath", out var v) ? v : null;

            bool ok = Check("it is written as a BASIC string, not a literal one", encoded.StartsWith("\""));
            ok &= Check("and it reads back exactly", back == awkward);
            if (back != awkward) Console.WriteLine("    wrote " + encoded + ", read back " + (back ?? "(null)"));
            return ok;
        }

        // ── 8. the console mode ──────────────────────────────────────────────

        private static bool ConsoleMode(EmulatorPlugin plugin, string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the console mode is chosen from the ROM header");

            var choose = TypeIn("MelonDsPlugin")
                .GetMethod("ChooseConsoleMode", BindingFlags.NonPublic | BindingFlags.Static)
                ?? TypeIn("MelonDsPlugin").GetMethod("ChooseConsoleMode", BindingFlags.Public | BindingFlags.Static);
            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            if (choose == null || resolve == null)
            { Console.WriteLine("    no ChooseConsoleMode / Resolve to call"); return false; }

            var path = ConfigPath(exe);
            bool ok = true;

            // A plain DS ROM: mode 0, written because the file starts out claiming 1.
            File.WriteAllText(path, "[Emu]\r\nConsoleType = 1\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), Path.Combine(romDir, PlainRom) });
            ok &= Check("a plain DS ROM asks for DS mode", ConsoleTypeIn(path) == 0);

            // A DSi ROM with no DSi files configured: refused, and left as it was.
            File.WriteAllText(path, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), Path.Combine(romDir, DsiRom) });
            ok &= Check("a DSi ROM without the DSi files stays in DS mode", ConsoleTypeIn(path) == 0);

            // The same ROM with the three files present: DSi mode.
            var bios = Path.Combine(Path.GetDirectoryName(exe), "bios");
            Directory.CreateDirectory(bios);
            foreach (var name in new[] { "dsi_bios7.bin", "dsi_bios9.bin", "dsi_nand.bin" })
                Write(bios, name, 1024);
            File.WriteAllText(path,
                "[Emu]\r\nConsoleType = 0\r\n\r\n[DSi]\r\n"
                + "BIOS7Path = '" + Path.Combine(bios, "dsi_bios7.bin") + "'\r\n"
                + "BIOS9Path = '" + Path.Combine(bios, "dsi_bios9.bin") + "'\r\n"
                + "NANDPath = '" + Path.Combine(bios, "dsi_nand.bin") + "'\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), Path.Combine(romDir, DsiRom) });
            ok &= Check("a DSi ROM WITH the DSi files asks for DSi mode", ConsoleTypeIn(path) == 1);

            // DSiWare: nothing is written, whatever the mode happens to be.
            File.WriteAllText(path, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), Path.Combine(romDir, WareRom) });
            ok &= Check("a DSiWare ROM changes nothing - melonDS boots those from the NAND",
                        ConsoleTypeIn(path) == 0);

            try { File.Delete(path); Directory.Delete(bios, true); } catch { }
            return ok;
        }

        private static int ConsoleTypeIn(string tomlPath)
        {
            foreach (var raw in File.ReadAllLines(tomlPath))
            {
                var line = raw.Trim();
                if (!line.StartsWith("ConsoleType", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq > 0 && int.TryParse(line.Substring(eq + 1).Trim(), out var n)) return n;
            }
            return -1;
        }

        // ── 10. one NAND per DSiWare title ───────────────────────────────────

        private static bool PerTitleNand(string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- a DSiWare title gets a NAND of its own under dsi\\");

            var choose = TypeIn("MelonDsPlugin").GetMethod("ChooseConsoleMode",
                             BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve",
                             BindingFlags.Public | BindingFlags.Static);

            string install = Path.GetDirectoryName(exe);
            string dsi = Path.Combine(install, "dsi");
            string bios = Path.Combine(install, "bios");
            string toml = ConfigPath(exe);
            string ware = Path.Combine(romDir, WareRom);

            // The title id the forged DSiWare header carries: high 0x00030004, low 0x87654321.
            const string titleId = "0003000487654321";
            string expected = Path.Combine(dsi, titleId, "nand.bin");

            Directory.CreateDirectory(bios);
            foreach (var name in new[] { "dsi_bios7.bin", "dsi_bios9.bin" }) Write(bios, name, 1024);

            bool ok = true;

            // 1. No NAND anywhere. Nothing is INVENTED - a NAND cannot be - but the folder and its
            //    note are made, because the message that follows names a path and a path nobody can
            //    see is a spelling to guess at. That was a real complaint, not a hypothesis.
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            ok &= Check("no NAND is invented out of nothing",
                        !File.Exists(Path.Combine(dsi, titleId, "nand.bin"))
                        && !File.Exists(Path.Combine(dsi, "base.bin")));
            ok &= Check("but the folder the message names does exist, with its note",
                        Directory.Exists(dsi) && File.Exists(Path.Combine(dsi, "PUT-YOUR-NAND-HERE.txt")));
            ok &= Check("and the console mode is untouched", ConsoleTypeIn(toml) == 0);

            // 2. The user's own dump, configured in melonDS: captured as the base, copied per title,
            //    and selected - without the original ever being written to.
            string mine = Path.Combine(install, "my_nand.bin");
            Write(install, "my_nand.bin", 512 * 1024);
            var fingerprint = File.ReadAllBytes(mine);

            File.WriteAllText(toml,
                "[Emu]\r\nConsoleType = 0\r\n\r\n[DSi]\r\n"
                + "BIOS7Path = '" + Path.Combine(bios, "dsi_bios7.bin") + "'\r\n"
                + "BIOS9Path = '" + Path.Combine(bios, "dsi_bios9.bin") + "'\r\n"
                + "NANDPath = '" + mine + "'\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });

            ok &= Check("the user's dump is kept as dsi\\base.bin",
                        File.Exists(Path.Combine(dsi, "base.bin")));
            ok &= Check("a NAND is created for the title, named by its id", File.Exists(expected));
            ok &= Check("it is a copy of the dump, byte for byte",
                        File.Exists(expected) && File.ReadAllBytes(expected).SequenceEqual(fingerprint));
            ok &= Check("the user's own dump is left untouched",
                        File.ReadAllBytes(mine).SequenceEqual(fingerprint));
            ok &= Check("melonDS is pointed at the per-title NAND", NandPathIn(toml) == expected);
            ok &= Check("and DSi mode is asked for", ConsoleTypeIn(toml) == 1);
            ok &= Check("no half-written copy is left behind",
                        !Directory.EnumerateFiles(dsi, "*.part", SearchOption.AllDirectories).Any());

            // 3. A second launch reuses it rather than copying 240 MB again.
            var stamp = File.GetLastWriteTimeUtc(expected);
            File.WriteAllText(toml,
                "[Emu]\r\nConsoleType = 1\r\n\r\n[DSi]\r\n"
                + "BIOS7Path = '" + Path.Combine(bios, "dsi_bios7.bin") + "'\r\n"
                + "BIOS9Path = '" + Path.Combine(bios, "dsi_bios9.bin") + "'\r\n"
                + "NANDPath = '" + expected + "'\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            ok &= Check("a second launch reuses the NAND instead of copying again",
                        File.GetLastWriteTimeUtc(expected) == stamp);

            // 4. THE TRAP THIS EXISTS TO CATCH: once DSi.NANDPath points at a per-title NAND, that
            //    file must never become the base for the NEXT title - one game's state would seed
            //    every other. base.bin already exists here, so the guard is exercised by deleting it.
            File.Delete(Path.Combine(dsi, "base.bin"));
            string other = Path.Combine(dsi, "0003000411112222", "nand.bin");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            ok &= Check("a per-title NAND is refused as a base for another title",
                        !File.Exists(Path.Combine(dsi, "base.bin")) && !File.Exists(other));

            // 5. THE NATIVE LIBRARY, when this checkout has built it. A forged NAND is not a NAND,
            //    so opening it must FAIL - and the point of the assertion is that the failure is
            //    melonDS's, reported through the library, rather than swallowed into the "do it by
            //    hand" message. That is what proves the P/Invoke resolved and the answer was read.
            if (!NativeLibraryUsable())
            {
                Console.WriteLine("    --   melonds-nand.dll not built; its wiring is not exercised");
            }
            else
            {
                File.WriteAllText(toml,
                    "[Emu]\r\nConsoleType = 0\r\n\r\n[DSi]\r\n"
                    + "BIOS7Path = '" + Path.Combine(bios, "dsi_bios7.bin") + "'\r\n"
                    + "BIOS9Path = '" + Path.Combine(bios, "dsi_bios9.bin") + "'\r\n"
                    + "NANDPath = '" + expected + "'\r\n");

                long mark = LogLength();
                choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
                var said = LogSince(mark);

                ok &= Check("the library is used rather than a click being asked for",
                            !said.Contains("ONE MANUAL STEP"));
                ok &= Check("and melonDS's refusal of a forged NAND is reported, not swallowed",
                            said.Contains("could not open the NAND") || said.Contains("cannot decrypt")
                            || said.Contains("is not a NAND"));
                if (said.Trim().Length > 0 && !said.Contains("could not open the NAND"))
                    Console.WriteLine("    log said: " + said.Trim());
            }

            // 6. A CARTRIDGE IS NOT DSiWARE. Once DSi.NANDPath points at a per-title NAND, a DSi
            //    cartridge launched next must go back to the base dump - otherwise its system
            //    settings would be written into whichever DSiWare ran last.
            File.Copy(Path.Combine(dsi, titleId, "nand.bin"), Path.Combine(dsi, "base.bin"), true);
            File.WriteAllText(toml,
                "[Emu]\r\nConsoleType = 1\r\n\r\n[DSi]\r\n"
                + "BIOS7Path = '" + Path.Combine(bios, "dsi_bios7.bin") + "'\r\n"
                + "BIOS9Path = '" + Path.Combine(bios, "dsi_bios9.bin") + "'\r\n"
                + "NANDPath = '" + expected + "'\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, DsiRom) });
            ok &= Check("a DSi cartridge is sent back to the base NAND",
                        NandPathIn(toml) == Path.Combine(dsi, "base.bin"));

            //    And a NAND the USER chose is never moved, whatever it is.
            string his = Path.Combine(install, "his_nand.bin");
            Write(install, "his_nand.bin", 4096);
            File.WriteAllText(toml,
                "[Emu]\r\nConsoleType = 1\r\n\r\n[DSi]\r\n"
                + "BIOS7Path = '" + Path.Combine(bios, "dsi_bios7.bin") + "'\r\n"
                + "BIOS9Path = '" + Path.Combine(bios, "dsi_bios9.bin") + "'\r\n"
                + "NANDPath = '" + his + "'\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, DsiRom) });
            ok &= Check("a NAND the user chose himself is left where it is", NandPathIn(toml) == his);
            try { File.Delete(his); } catch { }

            // 7. Without the DSi BIOS the mode is not switched, even though the NAND is ready.
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            ok &= Check("no DSi BIOS means no DSi mode, NAND or not", ConsoleTypeIn(toml) == 0);

            try { Directory.Delete(dsi, true); Directory.Delete(bios, true); File.Delete(mine); File.Delete(toml); }
            catch { }
            return ok;
        }

        /// <summary>Does the plugin consider its native library usable? Asked of the plugin rather
        /// than of the filesystem, because what matters is whether ITS P/Invoke resolves - the
        /// library sits beside the plugin assembly, not beside the probe. Not building it is a
        /// legitimate state - everything else works without it - so this skips rather than fails.</summary>
        private static bool NativeLibraryUsable()
        {
            try
            {
                var type = TypeIn("MelonDsNand");
                var method = type.GetMethod("IsUsable", BindingFlags.Public | BindingFlags.Static);
                var args = new object[] { null };
                var usable = (bool)method.Invoke(null, args);
                if (!usable) Console.WriteLine("    (" + (args[0] as string ?? "no reason given") + ")");
                return usable;
            }
            catch (Exception ex) { Console.WriteLine("    (" + ex.GetType().Name + ")"); return false; }
        }

        /// <summary>The plugin's own log, so an assertion can be made about what it SAID. Some
        /// answers only ever show up as a sentence.</summary>
        private static string LogPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "lb-integrations-plugins", "melonds.log");

        private static long LogLength()
        {
            try { return new FileInfo(LogPath()).Length; } catch { return 0; }
        }

        private static string LogSince(long offset)
        {
            try
            {
                using var stream = new FileStream(LogPath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (offset > stream.Length) return "";
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string NandPathIn(string tomlPath)
        {
            foreach (var raw in File.ReadAllLines(tomlPath))
            {
                var line = raw.Trim();
                if (!line.StartsWith("NANDPath", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                return line.Substring(eq + 1).Trim().Trim('\'', '"');
            }
            return null;
        }

        // ── 9. the scripts ───────────────────────────────────────────────────

        private static bool Scripts(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the AutoHotkey scripts name the keys melonDS really binds");

            var emulator = new StubEmulator { Title = "melonDS", ApplicationPath = exe };
            plugin.GetApplicableEmulators(new[] { (IEmulator)emulator }).ToList();

            bool ok = true;
            ok &= Check("save state sends Shift+F1",
                        (emulator.SaveStateAutoHotkeyScript ?? "").Contains("Shift down")
                        && (emulator.SaveStateAutoHotkeyScript ?? "").Contains("F1 down"));
            ok &= Check("load state sends F1 alone",
                        (emulator.LoadStateAutoHotkeyScript ?? "").Contains("F1 down")
                        && !(emulator.LoadStateAutoHotkeyScript ?? "").Contains("Shift"));
            ok &= Check("exit sends Ctrl+Q", (emulator.ExitAutoHotkeyScript ?? "").Contains("Ctrl down"));
            ok &= Check("the running script maps Escape across",
                        (emulator.AutoHotkeyScript ?? "").Contains("$Esc::"));

            // A second object must be filled too - the idempotence is per object, never per executable.
            var second = new StubEmulator { Title = "melonDS", ApplicationPath = exe };
            plugin.GetApplicableEmulators(new[] { (IEmulator)second }).ToList();
            ok &= Check("a second emulator object is filled as well",
                        !string.IsNullOrWhiteSpace(second.SaveStateAutoHotkeyScript));

            // And a script the user wrote is never replaced.
            var mine = new StubEmulator { Title = "melonDS", ApplicationPath = exe, SaveStateAutoHotkeyScript = "; mine" };
            plugin.GetApplicableEmulators(new[] { (IEmulator)mine }).ToList();
            ok &= Check("a script the user wrote is left alone", mine.SaveStateAutoHotkeyScript == "; mine");
            return ok;
        }

        private static bool Slots(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the slots are 1 to 8, on disk and on screen alike");
            var slots = plugin.GetPotentialSaveSlots();
            return Check("eight slots, keyed and labelled 1 to 8",
                         slots != null && slots.Count == 8
                         && slots.Keys.OrderBy(k => k).SequenceEqual(Enumerable.Range(1, 8))
                         && slots.All(kv => kv.Value == kv.Key.ToString()));
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static List<GameSaveBase> List(EmulatorPlugin plugin, string exe, string romDir,
                                               params string[] roms)
        {
            var emulator = new StubEmulator { Title = "melonDS", ApplicationPath = exe };
            var games = roms.Select(r => StubGame.Create(Guid.NewGuid().ToString(), r,
                                                         Path.Combine(romDir, r), emulator.Id)).ToArray();

            var response = plugin.GetSaves(new GetSavesArgs { Emulator = emulator, Games = games });
            if (response is not { WasSuccess: true })
            { Console.WriteLine("    GetSaves failed: " + (response?.Message ?? "no reason given")); return null; }

            var all = (response.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>()).ToList();
            foreach (var s in all)
                Console.WriteLine("    " + (s is GameSaveState st ? "slot " + st.Slot + "  " : "        ")
                                  + Name(s));
            return all;
        }

        private static string Write(string exe, string table, string key, string value)
        {
            var toml = TypeIn("MelonDsToml");
            var write = toml.GetMethod("Write", BindingFlags.Public | BindingFlags.Static);
            var values = new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value };
            return (string)write.Invoke(null, new object[] { ConfigPath(exe), table, values });
        }

        private static Type _assemblyAnchor;

        /// <summary>A type from the plugin's own assembly, by simple name. The plugin is loaded
        /// reflectively, so the probe cannot reference these types directly.</summary>
        internal static void Anchor(EmulatorPlugin plugin) => _assemblyAnchor = plugin.GetType();

        private static Type TypeIn(string simpleName)
            => _assemblyAnchor.Assembly.GetTypes().First(t => t.Name == simpleName);

        private static string Name(GameSaveBase save)
        {
            try { return Path.GetFileName(save.FileLocation) ?? ""; } catch { return ""; }
        }

        private static string Dir(GameSaveBase save)
        {
            try { return Path.GetDirectoryName(save.FileLocation) ?? ""; } catch { return ""; }
        }

        private static bool Check(string what, bool ok)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what);
            return ok;
        }
    }
}
