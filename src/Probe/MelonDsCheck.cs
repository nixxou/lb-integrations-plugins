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
//  10. what a DSiWare launch decides when it has nothing to run on
//  11. which containers a ROM may be handed in - measured, not copied from melonDS
//  12. which DSi a title needs - its mask, then its letter, then its name
//  13. the carried metadata index answering for titles it holds  - and only those

using System;
using System.Collections;
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

            // EVERY WRITE THIS CHECK MAKES IS REFUSED WHILE melonDS IS RUNNING, and rightly: the
            // plugin will not touch a configuration or a NAND the emulator is holding. Reporting
            // fifteen failures for that would be a harness telling a lie - the repository's own rule.
            // So it says what is in the way and stops.
            if (MelonDsRunning())
            {
                Console.WriteLine("  skipped - melonDS is running, and the plugin refuses to write");
                Console.WriteLine("            under a live emulator. Close it and run this again.");
                return true;
            }

            // NO WINDOW, EVER, from a harness. The plugin opens one when a DSiWare title has no
            // NAND to run on, which is exactly the case most of this check builds on purpose.
            Silence();

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
                ok &= DsiWareWithoutANand(exe, romDir);
                ok &= NandFirstUse(exe);
                ok &= TwoOfTheSameRegion();
                ok &= DsiWareDelete(exe);
                ok &= DsiWareRestore(exe);
                ok &= DsiWareBackup(plugin, exe);
                ok &= DsiWareRoundTrip(plugin, exe);
                ok &= RegionCascade(romDir);
                ok &= ArchiveFormats(exe, romDir);
                ok &= CarriedIndex(exe, romDir);
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

        /// <summary>A DS cart header as far as the fields we read. The title id defaults to the one
        /// the rest of this check expects; the carried-index part passes real ones.</summary>
        private static byte[] Header(byte unitCode, uint titleIdHigh, uint titleIdLow = 0x87654321,
                                     uint regionMask = 0)
        {
            var bytes = new byte[0x1000];
            var title = Encoding.ASCII.GetBytes("FORGEDTITLE");
            Buffer.BlockCopy(title, 0, bytes, 0, title.Length);
            bytes[0x12] = unitCode;
            bytes[0x230] = (byte)(titleIdLow & 0xFF);
            bytes[0x231] = (byte)((titleIdLow >> 8) & 0xFF);
            bytes[0x232] = (byte)((titleIdLow >> 16) & 0xFF);
            bytes[0x233] = (byte)((titleIdLow >> 24) & 0xFF);
            bytes[0x234] = (byte)(titleIdHigh & 0xFF);
            bytes[0x235] = (byte)((titleIdHigh >> 8) & 0xFF);
            bytes[0x236] = (byte)((titleIdHigh >> 16) & 0xFF);
            bytes[0x237] = (byte)((titleIdHigh >> 24) & 0xFF);

            // DSiRegionMask, at the offset MelonDsRegion reads it from.
            bytes[0x1B0] = (byte)(regionMask & 0xFF);
            bytes[0x1B1] = (byte)((regionMask >> 8) & 0xFF);
            bytes[0x1B2] = (byte)((regionMask >> 16) & 0xFF);
            bytes[0x1B3] = (byte)((regionMask >> 24) & 0xFF);
            return bytes;
        }

        private static void Write(string dir, string name, int size)
        {
            var bytes = new byte[size];
            for (int i = 0; i < size; i++) bytes[i] = (byte)(i * 31 + name.Length);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }

        private static bool MelonDsRunning()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                    using (p)
                    {
                        string n;
                        try { n = p.ProcessName; } catch { continue; }
                        if (n != null && n.StartsWith("melonDS", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
            }
            catch { }
            return false;
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

        /// <summary>What the plugin makes of one file handed to it: is it a ROM, is it a container,
        /// what does it think the game is called, and which DSi would it want.
        ///
        /// A DIAGNOSTIC, not an assertion. It answers the question a user actually asks - "why is my
        /// save named that?" - and it is how a container nobody could build a sample of gets
        /// measured: point it at a real one.</summary>
        public static bool Describe(EmulatorPlugin plugin, string romPath)
        {
            Anchor(plugin);
            Console.WriteLine();
            Console.WriteLine("-- what melonDS's plugin makes of one file ----------------------");

            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
            { Console.WriteLine("  --rom is missing or does not exist"); return false; }

            try
            {
                var header = TypeIn("NdsHeader");
                var isArchive = (bool)header.GetMethod("IsArchive", BindingFlags.Public | BindingFlags.Static)
                                            .Invoke(null, new object[] { romPath });
                var rom = header.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static)
                                .Invoke(null, new object[] { romPath });

                string Field(string name)
                {
                    var f = rom.GetType().GetField(name);
                    return f == null ? "?" : Convert.ToString(f.GetValue(rom));
                }

                Console.WriteLine("  file       : " + Path.GetFileName(romPath));
                // "Is it a container" and "could we open it" are DIFFERENT QUESTIONS, and conflating
                // them is how a container we cannot read looks like a container holding nothing.
                // KindOf actually opens it, so it separates the two.
                string kind = isArchive
                    ? (string)TypeIn("Archives").GetMethod("KindOf", BindingFlags.Public | BindingFlags.Static)
                                                .Invoke(null, new object[] { romPath })
                    : null;
                Console.WriteLine("  container  : " + (!isArchive ? "no, a plain ROM"
                    : kind == null ? "yes by its name, but IT COULD NOT BE OPENED"
                                   : "yes, opened as " + kind));
                Console.WriteLine("  read       : " + Field("Known"));
                Console.WriteLine("  asset name : " + Field("AssetName") + "   <- what the save is called");
                Console.WriteLine("  DSi        : " + Field("IsDSi") + ", DSiWare: " + Field("IsDSiWare"));

                var titleId = rom.GetType().GetProperty("TitleId")?.GetValue(rom) as string;
                if (!string.IsNullOrEmpty(titleId)) Console.WriteLine("  title id   : " + titleId);

                var region = TypeIn("MelonDsRegion");
                var args = new object[] { rom, romPath, null };
                var regions = region.GetMethod("RegionsFor", BindingFlags.Public | BindingFlags.Static)
                                    .Invoke(null, args);
                Console.WriteLine("  needs a    : "
                    + region.GetMethod("Names", BindingFlags.Public | BindingFlags.Static)
                            .Invoke(null, new[] { regions })
                    + " NAND, from " + (args[2] as string ?? "nothing it could find"));

                bool ok = (bool)rom.GetType().GetField("Known").GetValue(rom);
                Console.WriteLine();
                Console.WriteLine("  " + (ok ? "OK - the plugin can read this file"
                                             : "NOT OK - nothing could be read out of it"));
                return ok;
            }
            catch (Exception ex) { Console.WriteLine("  " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        /// <summary>The folder the plugin declares its BIOS files in, ASKED OF IT rather than
        /// written here. It moved once already - from a private one beside the executable to
        /// RetroArch's system folder - and a harness that hardcoded it went on passing while
        /// testing a directory nothing used.</summary>
        private static string BiosDir(object layout)
        {
            try
            {
                return (string)TypeIn("MelonDsBios")
                    .GetMethod("Dir", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new[] { layout });
            }
            catch { return null; }
        }

        /// <summary>A BIOS file name, ASKED OF THE PLUGIN rather than written here. These names are
        /// a contract with the user, so they will be argued about and changed; a harness that
        /// hardcoded them would go on passing while testing the wrong thing.</summary>
        private static string BiosName(string constant)
        {
            try
            {
                return (string)TypeIn("MelonDsBios")
                    .GetField(constant, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
            }
            catch { return null; }
        }

        /// <summary>Stop the plugin opening its missing-files window. Through the plugin's own
        /// flag rather than by dropping its kill-switch marker in the user's log folder, which is
        /// theirs and not somewhere a test should leave things.</summary>
        private static void Silence()
        {
            try
            {
                var field = TypeIn("MelonDsDialog")
                    .GetField("Suppressed", BindingFlags.Public | BindingFlags.Static);
                if (field == null)
                {
                    // Said out loud rather than shrugged off: a null here means this build predates
                    // the flag, and the run is about to open windows.
                    Console.WriteLine("    WARNING: this build has no Suppressed flag; it may open windows");
                    return;
                }
                field.SetValue(null, true);
            }
            catch (Exception ex) { Console.WriteLine("    (could not silence the window: " + ex.GetType().Name + ")"); }
        }

        /// <summary>Whether one of the plugin's markers is set, asked of the plugin itself so the
        /// probe never has to know where they live.</summary>
        private static bool Marked(string name)
        {
            try
            {
                var method = TypeIn("Log").GetMethod("Marker", BindingFlags.Public | BindingFlags.Static);
                return method != null && (bool)method.Invoke(null, new object[] { name });
            }
            catch { return false; }
        }

        /// <summary>Emu.DirectBoot as the file has it, or null when the key is not written. melonDS
        /// defaults it to true, so an absent key is not the same as false.</summary>
        private static bool? DirectBootIn(string tomlPath)
        {
            foreach (var raw in File.ReadAllLines(tomlPath))
            {
                var line = raw.Trim();
                if (!line.StartsWith("DirectBoot", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) return line.Substring(eq + 1).Trim()
                                       .Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            return null;
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

        // -- 10. what a DSiWare launch decides, with nothing to run on --------

        /// <summary>A forged install cannot hold a real NAND - a dump is 240 MB and is encrypted
        /// against a console this machine does not have. So this asserts the DECISIONS a DSiWare
        /// launch makes, and the machinery that acts on them is proved separately, against genuine
        /// dumps, by AgainstReal.
        ///
        /// The split is deliberate rather than a concession. Everything here is about what happens
        /// when a title CANNOT run: what is refused rather than invented, what is created so a
        /// message can name a real path, what the user is told, and what is left alone. Those are
        /// the paths a working install never exercises, and the ones most likely to rot.</summary>
        private static bool DsiWareWithoutANand(string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- a DSiWare title with nothing to run on");

            var choose = TypeIn("MelonDsPlugin").GetMethod("ChooseConsoleMode",
                             BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve",
                             BindingFlags.Public | BindingFlags.Static);

            string install = Path.GetDirectoryName(exe);
            string dsi = Path.Combine(install, "dsi");
            string bios = BiosDir(resolve.Invoke(null, new object[] { exe }));
            string toml = ConfigPath(exe);
            string ware = Path.Combine(romDir, WareRom);

            bool ok = true;

            // 1. Nothing anywhere. A NAND is NOT invented - it cannot be, it is a dump of somebody's
            //    own console - but the folders are made, because the message that follows names
            //    paths and a path nobody can see is a spelling to guess at.
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            long mark = LogLength();
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            var said = LogSince(mark);

            ok &= Check("no NAND is invented out of nothing", !File.Exists(Path.Combine(dsi, "work.bin")));
            ok &= Check("the bios folder the message names exists, with its note",
                        Directory.Exists(bios) && File.Exists(Path.Combine(bios, "WHICH-FILES-GO-HERE.txt")));
            ok &= Check("the console mode is left alone", ConsoleTypeIn(toml) == 0);
            ok &= Check("and the log says which region of NAND is wanted",
                        said.Contains("NAND dump for") && said.Contains("USA"));
            if (!said.Contains("NAND dump for")) Console.WriteLine("    log said: " + said.Trim());

            // 2. The DSi BIOS and firmware, dropped in the folder, are FOUND AND CONFIGURED. This is
            //    what lets somebody put files in and launch, rather than put files in and then go
            //    and point melonDS at each one by hand.
            Directory.CreateDirectory(bios);
            string dsi7 = BiosName("DsiBios7"), dsi9 = BiosName("DsiBios9"), dsiFw = BiosName("DsiFirmware");
            foreach (var name in new[] { dsi7, dsi9, dsiFw }) Write(bios, name, 0x10000);

            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });

            var keys = TomlValues(toml, "DSi");
            ok &= Check("a " + dsi7 + " dropped in the folder is pointed at",
                        (keys.TryGetValue("BIOS7Path", out var b7) ? b7 : "").Contains(dsi7));
            ok &= Check("so is the ARM9 BIOS",
                        (keys.TryGetValue("BIOS9Path", out var b9) ? b9 : "").Contains(dsi9));
            ok &= Check("and the firmware, which melonDS needs whatever its verify step suggests",
                        (keys.TryGetValue("FirmwarePath", out var fw) ? fw : "").Contains(dsiFw));
            ok &= Check("but with no NAND the console mode is still left alone", ConsoleTypeIn(toml) == 0);

            // 3. A file that is not a NAND is never opened. 240 MB is the size of the thing, and
            //    decrypting every stray .bin in the folder to find out would cost a second a launch.
            Write(bios, "not-a-nand.bin", 4096);
            mark = LogLength();
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            ok &= Check("a file that is not NAND-sized is never opened",
                        !LogSince(mark).Contains("not-a-nand.bin"));

            // 3b. THE DS FILES ARE THE PLUGIN'S JOB, and so is the switch that demands them.
            //     Nothing in the folder: external BIOS goes OFF, and melonDS boots on its built-in
            //     one rather than refusing to start.
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\nExternalBIOSEnable = true\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            ok &= Check("with no DS BIOS, external BIOS is turned off so the game still runs",
                        TomlValues(toml, "Emu").TryGetValue("ExternalBIOSEnable", out var off)
                        && off.Equals("false", StringComparison.OrdinalIgnoreCase));

            //     Drop the three in, and they are CONFIGURED - nobody should have to type a path
            //     into Config > Emu settings to make a game start - and the switch goes back on.
            Directory.CreateDirectory(bios);
            string ds7 = BiosName("DsBios7"), ds9 = BiosName("DsBios9"), dsFw = BiosName("DsFirmware");
            Write(bios, ds7, 0x4000);
            Write(bios, ds9, 0x1000);
            Write(bios, dsFw, 0x40000);

            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            var ds = TomlValues(toml, "DS");
            ok &= Check("the DS ARM7 BIOS is found by name and configured",
                        (ds.TryGetValue("BIOS7Path", out var d7) ? d7 : "").Contains(ds7));
            ok &= Check("so is the ARM9 BIOS",
                        (ds.TryGetValue("BIOS9Path", out var d9) ? d9 : "").Contains(ds9));
            ok &= Check("and the firmware",
                        (ds.TryGetValue("FirmwarePath", out var df) ? df : "").Contains(dsFw));
            ok &= Check("with all three there, external BIOS is turned on",
                        TomlValues(toml, "Emu").TryGetValue("ExternalBIOSEnable", out var on)
                        && on.Equals("true", StringComparison.OrdinalIgnoreCase));

            //     A PATH THE USER SET HIMSELF IS NOT OVERWRITTEN, wherever he put it.
            string his7 = Path.Combine(install, "my_own_bios7.bin");
            Write(install, "my_own_bios7.bin", 0x4000);
            File.WriteAllText(toml,
                "[Emu]\r\nConsoleType = 0\r\n\r\n[DS]\r\n"
                + "BIOS7Path = '" + his7 + "'\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            ok &= Check("a DS path the user set himself is left alone",
                        TomlValues(toml, "DS").TryGetValue("BIOS7Path", out var kept) && kept == his7);
            try { File.Delete(his7); } catch { }

            //     A FILE OF THE WRONG SIZE IS STILL HANDED OVER, with a word in the log. melonDS
            //     makes the final call - it is better at it - and silently refusing a file somebody
            //     deliberately put there would be second-guessing them with less information.
            File.Delete(Path.Combine(bios, ds7));
            Write(bios, ds7, 1234);
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            mark = LogLength();
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            said = LogSince(mark);
            ok &= Check("a BIOS of the wrong size is used anyway",
                        (TomlValues(toml, "DS").TryGetValue("BIOS7Path", out var odd) ? odd : "")
                            .Contains(ds7));
            ok &= Check("but the log says its size is not one melonDS wants",
                        said.Contains("1234 bytes") && said.Contains("melonDS wants"));

            try { Directory.Delete(bios, true); } catch { }

            // 3b-bis. RETROARCH'S FOLDER AND RETROARCH'S NAMES. Two conventions exist for the same
            //     seven files - RetroArch imposes its own through its cores' .info files - and
            //     somebody who set that up already has them. Making them copy seven files under
            //     seven other names would be inventing work.
            try { Directory.Delete(bios, true); } catch { }
            string shared = bios;                          // the declared folder IS RetroArch's now
            Directory.CreateDirectory(shared);
            Write(shared, "bios7.bin", 0x4000);           // RetroArch's name for biosnds7.bin
            Write(shared, "bios9.bin", 0x1000);
            Write(shared, "firmware.bin", 0x40000);

            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            ok &= Check("a BIOS in RetroArch's system folder, under RetroArch's name, is found",
                        (TomlValues(toml, "DS").TryGetValue("BIOS7Path", out var ra) ? ra : "")
                            .Replace('/', Path.DirectorySeparatorChar).Contains(shared));
            try { Directory.Delete(shared, true); } catch { }

            // 3c. A KEYBOARD, because melonDS ships without one. Every key is -1 in its default
            //     table and there is no table of defaults anywhere else, so an install nobody has
            //     touched answers to nothing.
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            var keyboard = TomlValues(toml, "Instance0.Keyboard");
            ok &= Check("an unmapped melonDS gets a playable keyboard",
                        keyboard.TryGetValue("A", out var keyA) && keyA != "-1"
                        && keyboard.TryGetValue("Up", out var keyUp) && keyUp == "16777235");
            ok &= Check("including the lid, which some games need to be finished at all",
                        keyboard.TryGetValue("HK_Lid", out var lid) && lid != "-1");

            //     AND A MAPPING SOMEBODY MADE IS THEIRS. One key bound is enough to say the
            //     configuration has an owner - including buttons they left unbound on purpose.
            File.WriteAllText(toml,
                "[Emu]\r\nConsoleType = 0\r\n\r\n[Instance0.Keyboard]\r\n"
                + "A = 12345\r\nB = -1\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }),
                                        Path.Combine(romDir, PlainRom) });
            keyboard = TomlValues(toml, "Instance0.Keyboard");
            ok &= Check("a mapping that has an owner is not touched",
                        keyboard.TryGetValue("A", out var mine2) && mine2 == "12345"
                        && keyboard.TryGetValue("B", out var theirs) && theirs == "-1");

            // 4. A CARTRIDGE IS NOT DSiWARE: it runs off whatever NAND is selected, and a NAND the
            //    user chose himself is his answer.
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

            // 5. Without the DSi BIOS there is no DSi mode, whatever else is in place.
            try { Directory.Delete(bios, true); } catch { }
            File.WriteAllText(toml, "[Emu]\r\nConsoleType = 0\r\n");
            choose.Invoke(null, new[] { resolve.Invoke(null, new object[] { exe }), ware });
            ok &= Check("no DSi BIOS means no DSi mode", ConsoleTypeIn(toml) == 0);

            try { Directory.Delete(dsi, true); File.Delete(his); File.Delete(toml); } catch { }
            return ok;
        }

        /// <summary>Every key of one TOML table, quotes stripped.</summary>
        private static Dictionary<string, string> TomlValues(string tomlPath, string table)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            bool inside = false;
            foreach (var raw in File.ReadAllLines(tomlPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    inside = line.Equals("[" + table + "]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inside) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('\'', '"');
            }
            return map;
        }

        // -- 11. what may be handed in ----------------------------------------

        /// <summary>Which containers this plugin can actually read a ROM out of.
        ///
        /// TWO DIFFERENT QUESTIONS HIDE BEHIND ONE LIST. melonDS opens archives itself, so a DS game
        /// in any container libarchive understands is simply handed over and nothing here has to
        /// unpack it. A DSiWare title is not handed over: it has to be INSTALLED into a NAND first,
        /// and that means reading a .nds out of the container ourselves.
        ///
        /// So declaring melonDS's list wholesale would be wrong in both directions. An archive we
        /// cannot open is one whose inner name we cannot read - and that name is what a save is
        /// called, so the save would be silently misfiled. What this asserts is the honest set: the
        /// containers the plugin opens, measured by handing it one of each.</summary>
        private static bool ArchiveFormats(string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- what a ROM may be handed in");

            var describe = TypeIn("NdsHeader").GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            if (describe == null) { Console.WriteLine("    no Describe to call"); return false; }

            const string inner = "Packed Game (Europe).nds";
            var rom = Header(0x00, 0);

            bool Reads(string fileName, Action<string, byte[], string> build)
            {
                var path = Path.Combine(romDir, fileName);
                try
                {
                    build(path, rom, inner);
                    var got = describe.Invoke(null, new object[] { path });
                    var name = (string)got.GetType().GetField("AssetName").GetValue(got);
                    return name == "Packed Game (Europe)";
                }
                catch (Exception ex) { Console.WriteLine("    (" + fileName + ": " + ex.GetType().Name + ")"); return false; }
                finally { try { File.Delete(path); } catch { } }
            }

            bool ok = true;
            ok &= Check(".zip - the inner name is read, so the save is named after the GAME",
                        Reads("Packed.zip", WriteZip));
            ok &= Check(".tar", Reads("Packed.tar", WriteTar));
            ok &= Check(".tar.gz", Reads("Packed.tar.gz", WriteTarGz));
            ok &= Check(".tgz", Reads("Packed.tgz", WriteTarGz));

            // A plain ROM must not be mistaken for a container, whatever it is called.
            var plain = Path.Combine(romDir, "Not An Archive.nds");
            File.WriteAllBytes(plain, rom);
            var described = describe.Invoke(null, new object[] { plain });
            ok &= Check("a plain .nds is read as itself",
                        (string)described.GetType().GetField("AssetName").GetValue(described) == "Not An Archive");
            try { File.Delete(plain); } catch { }

            return ok;
        }

        private static void WriteZip(string path, byte[] rom, string inner)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var archive = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Create);
            using var entry = archive.CreateEntry(inner).Open();
            entry.Write(rom, 0, rom.Length);
        }

        private static void WriteTar(string path, byte[] rom, string inner)
        {
            using var stream = new FileStream(path, FileMode.Create);
            WriteTarInto(stream, rom, inner);
        }

        private static void WriteTarGz(string path, byte[] rom, string inner)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var gzip = new System.IO.Compression.GZipStream(
                stream, System.IO.Compression.CompressionLevel.Fastest);
            WriteTarInto(gzip, rom, inner);
        }

        private static void WriteTarInto(Stream stream, byte[] rom, string inner)
        {
            using var writer = new System.Formats.Tar.TarWriter(stream, leaveOpen: true);
            var entry = new System.Formats.Tar.PaxTarEntry(
                System.Formats.Tar.TarEntryType.RegularFile, inner)
            {
                DataStream = new MemoryStream(rom),
            };
            writer.WriteEntry(entry);
        }

        // -- 12. which DSi a DSiWare title needs ------------------------------

        /// <summary>Three sources say which console a title runs on, and they are tried in order of
        /// authority: the header's region MASK, then the region LETTER of the game code, then the
        /// parentheses in the FILE NAME.
        ///
        /// The ORDER is what this checks. A mask naming two regions may be narrowed by a file name,
        /// but a file name must never overrule a mask that already answered - a rename is the least
        /// trustworthy link in the chain, and the mask is what the console itself is handed.</summary>
        /// <summary>A NAND dump's first use: the three states it can be in, the size window the scan
        /// looks through, and the one thing this flow must never do.
        ///
        /// THE LAST PART IS THE POINT. With the windows silenced - which is how a harness runs, and
        /// how a headless host would run - the flow must copy nothing, start nothing and write
        /// nothing. Configuring somebody's console without asking would be worse than leaving it
        /// unconfigured, and nothing else here can catch that going wrong.</summary>
        private static bool NandFirstUse(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- a NAND's first use");

            var setup = TypeIn("MelonDsNandSetup");
            var of = setup?.GetMethod("Of", BindingFlags.Public | BindingFlags.Static);
            var run = setup?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (of == null || run == null)
            { Console.WriteLine("    no MelonDsNandSetup to call"); return false; }

            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            var layout = resolve.Invoke(null, new object[] { exe });
            string bios = BiosDir(layout);
            Directory.CreateDirectory(bios);

            bool ok = true;

            // 1. THE STATE IS READ OFF THE FOLDER, and off nothing else. No index, no registry, no
            //    remembered list: move a dump somewhere else and it is new again, which is right,
            //    because the differences kept against it were keyed to where it was.
            string probe = Path.Combine(bios, "state-probe.bin");
            Sized(probe, 4096);
            ok &= Check("a dump with nothing beside it has never been used",
                        StateOf(of, probe) == "NeverUsed");

            File.WriteAllText(probe + ".lock", "");
            ok &= Check("a .lock beside it means set up, and never ask again",
                        StateOf(of, probe) == "Locked");

            File.WriteAllText(probe + ".bak", "");
            ok &= Check("a .bak left behind means a setup that never got its answer",
                        StateOf(of, probe) == "Interrupted");

            foreach (var leftover in new[] { probe, probe + ".lock", probe + ".bak" })
                try { File.Delete(leftover); } catch { }

            // 2. THE SIZE WINDOW. A NAND is 240 MB; the scan looks between 220 and 260 and opens
            //    nothing outside it. Opening one costs a decrypt and a FAT mount, so a folder full
            //    of disk images must not turn a launch into a minute.
            //
            //    Four files of a quarter of a gigabyte apiece, so this stands aside on a full disk
            //    rather than failing for a reason that is not the plugin's.
            long free = FreeSpaceOn(bios);
            if (free >= 0 && free < 3L * 1024 * 1024 * 1024)
            {
                Console.WriteLine("    skipped the size window - less than 3 GB free here");
                return ok;
            }

            string inRange = Path.Combine(bios, "nand-inrange.bin");
            string tooSmall = Path.Combine(bios, "nand-small.bin");
            string tooBig = Path.Combine(bios, "nand-big.bin");
            string locked = inRange + ".lock";
            const long MB = 1024L * 1024;

            try
            {
                Sized(inRange, 230 * MB);
                Sized(tooSmall, 219 * MB);
                Sized(tooBig, 261 * MB);
                Sized(locked, 230 * MB);

                // A DSi ARM7 BIOS has to be nameable or the scan declines to look at anything at
                // all - it cannot decrypt a dump without one, and guessing is not on offer.
                string bios7 = Path.Combine(bios, BiosName("DsiBios7"));
                if (!File.Exists(bios7)) Sized(bios7, 0x10000);

                // The line that says a file was looked at is a Verbose one - it is per-candidate
                // chatter and belongs behind the trace marker. So the marker is turned on for the
                // length of this one call rather than the line being promoted to the real log.
                var nands = TypeIn("MelonDsBios").GetMethod("Nands", BindingFlags.Public | BindingFlags.Static);
                bool wasTracing = Tracing(true);
                long mark = LogLength();
                nands.Invoke(null, new object[] { layout, bios7 });
                var said = LogSince(mark);
                Tracing(wasTracing);

                ok &= Check("a 230 MB file is a candidate and gets looked at",
                            said.Contains("nand-inrange.bin"));
                ok &= Check("219 MB is not a NAND and is never opened",
                            !said.Contains("nand-small.bin"));
                ok &= Check("261 MB is not a NAND either",
                            !said.Contains("nand-big.bin"));
                ok &= Check("a .lock of exactly the right size is still not a NAND",
                            !said.Contains("nand-inrange.bin.lock"));
                if (!said.Contains("nand-inrange.bin")) Console.WriteLine("    log said: " + said.Trim());

                // 3. NO WINDOW, NO ACTION. The dump below has never been set up, so the flow is
                //    live - and with the windows off it must decline, not proceed quietly.
                try { File.Delete(locked); } catch { }
                var dump = Activator.CreateInstance(TypeIn("NandDump"));
                TypeIn("NandDump").GetField("Path").SetValue(dump, inRange);

                mark = LogLength();
                var cancel = run.Invoke(null, new[] { layout, dump });
                said = LogSince(mark);

                ok &= Check("with the windows off, the launch is not cancelled", !(bool)cancel);
                ok &= Check("nothing was copied - no .bak appeared", !File.Exists(inRange + ".bak"));
                ok &= Check("and nothing was locked behind the user's back",
                            !File.Exists(inRange + ".lock"));
                ok &= Check("the log says why it stood aside",
                            said.Contains("windows are turned off"));
                if (!said.Contains("windows are turned off")) Console.WriteLine("    log said: " + said.Trim());
            }
            finally
            {
                foreach (var big in new[] { inRange, tooSmall, tooBig, locked })
                    try { if (File.Exists(big)) File.Delete(big); } catch { }
            }

            return ok;
        }

        /// <summary>Deleting a DSiWare save, which is the one kind that is a folder of ours.
        ///
        /// THREE THINGS HOLD THAT SAVE and deleting one of them is not a deletion. The state folder
        /// is the truth between sessions; the working image may still be holding the same title,
        /// and the next question about this game's saves would capture it straight back out; and a
        /// legacy per-title image IS the save where there is no native library. A delete that leaves
        /// any of the three behind looks, from the outside, exactly like a button that does nothing -
        /// which is what it did until this was written.
        ///
        /// And nothing of this title survives afterwards. A folder named after the game, still there
        /// once its save is deleted, reads the same way.</summary>
        private static bool DsiWareDelete(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- deleting a DSiWare save");

            var drop = TypeIn("MelonDsDsi")?.GetMethod("DropState",
                           BindingFlags.Public | BindingFlags.Static);
            if (drop == null) { Console.WriteLine("    no DropState to call"); return false; }

            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            var layout = resolve.Invoke(null, new object[] { exe });

            const string titleId = "000300044b393945";
            string dsi = Path.Combine(Path.GetDirectoryName(exe), "dsi");
            string state = Path.Combine(dsi, titleId, "state");
            string marker = Path.Combine(dsi, "work.title");
            string work = Path.Combine(dsi, "work.bin");
            string legacy = Path.Combine(dsi, titleId, "nand.bin");

            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "files.txt"), "F\t0\t0:/sys/HWINFO_S.dat\r\n");
            File.WriteAllText(Path.Combine(state, "0"), "a captured file");
            File.WriteAllText(Path.Combine(dsi, titleId, "reference.txt"), "the fresh install");
            File.WriteAllText(marker, titleId + "\tsomewhere\t1\t2\tnand.bin");
            File.WriteAllText(work, "not really an image");
            File.WriteAllText(legacy, "not really an image either");

            var args = new object[] { layout, titleId, null };
            bool done = (bool)drop.Invoke(null, args);

            bool ok = true;
            ok &= Check("the delete is reported done", done);
            if (!done) Console.WriteLine("    it said: " + args[2]);
            ok &= Check("the state folder is gone", !Directory.Exists(state));
            ok &= Check("the working image is forgotten, so nothing captures it back",
                        !File.Exists(marker));
            ok &= Check("the per-title image goes too - there, it IS the save",
                        !File.Exists(legacy));
            ok &= Check("and so do the 240 MB it held, which nothing would ever read again",
                        !File.Exists(work));
            ok &= Check("nothing of this title is left behind at all",
                        !Directory.Exists(Path.Combine(dsi, titleId)));

            // Again, with nothing left. A second delete is not an error - the row may be stale, and
            // answering "no" to a save that is already gone would be a failure about nothing.
            ok &= Check("deleting what is already gone is not a failure",
                        (bool)drop.Invoke(null, new object[] { layout, titleId, null }));

            try { Directory.Delete(Path.Combine(dsi, titleId), recursive: true); } catch { }
            try { File.Delete(work); } catch { }
            return ok;
        }

        /// <summary>Restoring a DSiWare save out of the vault.
        ///
        /// THE STATE IS REPLACED, NOT MERGED, and the working image is DROPPED rather than written.
        /// Applying a state onto an image that has been played looks like the same thing and is not:
        /// MelonDsDelta.Apply puts back the files the state names and takes away nothing the current
        /// session added, so a file the restored save never had would survive into play - and the
        /// next capture would write it back into the state folder, growing the restored save back
        /// into what it was restored to be rid of. A state describes a fresh install and nothing
        /// else, so that is the only surface it may land on.</summary>
        private static bool DsiWareRestore(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- restoring a DSiWare save");

            var restore = TypeIn("MelonDsDsi")?.GetMethod("RestoreSave",
                              BindingFlags.Public | BindingFlags.Static);
            if (restore == null) { Console.WriteLine("    no RestoreSave to call"); return false; }

            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            var layout = resolve.Invoke(null, new object[] { exe });

            const string titleId = "000300044b393945";
            string install = Path.GetDirectoryName(exe);
            string dsi = Path.Combine(install, "dsi");
            string state = Path.Combine(dsi, titleId, "state");
            string marker = Path.Combine(dsi, "work.title");
            string work = Path.Combine(dsi, "work.bin");
            string vault = Path.Combine(install, "vault-copy");

            // What is on disk now: a session with three files in it, and the image that played it.
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "files.txt"),
                              "F\t0\t0:/a\r\nF\t1\t0:/b\r\nF\t2\t0:/c\r\n");
            foreach (var n in new[] { "0", "1", "2" }) File.WriteAllText(Path.Combine(state, n), "today " + n);
            File.WriteAllText(marker, titleId + "\tsomewhere\t1\t2\tnand.bin");
            File.WriteAllText(work, "not really an image");

            // What comes back out of the vault: an older save, which never had the third file.
            if (Directory.Exists(vault)) Directory.Delete(vault, recursive: true);
            Directory.CreateDirectory(vault);
            File.WriteAllText(Path.Combine(vault, "files.txt"), "F\t0\t0:/a\r\nF\t1\t0:/b\r\n");
            foreach (var n in new[] { "0", "1" }) File.WriteAllText(Path.Combine(vault, n), "backup " + n);

            var args = new object[] { layout, titleId, null, vault, null };
            bool done = (bool)restore.Invoke(null, args);

            bool ok = true;
            ok &= Check("the restore is reported done", done);
            if (!done) Console.WriteLine("    it said: " + args[4]);
            ok &= Check("the backup's files are back",
                        File.Exists(Path.Combine(state, "0"))
                        && File.ReadAllText(Path.Combine(state, "0")) == "backup 0");
            ok &= Check("the file the backup never had is gone, not merged in",
                        !File.Exists(Path.Combine(state, "2")));
            ok &= Check("the played image is dropped, not written onto", !File.Exists(work));
            ok &= Check("and its marker with it, so nothing reuses it", !File.Exists(marker));

            // A folder that is not a state is refused rather than half-applied.
            string junk = Path.Combine(install, "not-a-state");
            Directory.CreateDirectory(junk);
            File.WriteAllText(Path.Combine(junk, "something.bin"), "nope");
            ok &= Check("a folder with no files.txt is refused",
                        !(bool)restore.Invoke(null, new object[] { layout, titleId, null, junk, null }));

            foreach (var leftover in new[] { vault, junk, Path.Combine(dsi, titleId) })
                try { Directory.Delete(leftover, recursive: true); } catch { }
            try { File.Delete(work); } catch { }
            try { File.Delete(marker); } catch { }
            return ok;
        }

        /// <summary>Backing a DSiWare save up, which is the other half of calling it a container.
        ///
        /// Saying IsSaveContainer is a promise that TryBackupSave can extract the thing. The host
        /// takes it literally: it makes a destination folder, asks, and records a backup from what
        /// turns up. A refusal therefore does not mean "no backup" - it means an EMPTY FOLDER and no
        /// backup, once per session, with nothing on screen to say why. That is what it did.</summary>
        private static bool DsiWareBackup(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- backing a DSiWare save up");

            const string titleId = "000300044b513945";
            string install = Path.GetDirectoryName(exe);
            string state = Path.Combine(install, "dsi", titleId, "state");
            string vault = Path.Combine(install, "vault-here");

            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "files.txt"), "F\t0\t0:/a\r\nX\t-\t0:/b\r\n");
            File.WriteAllText(Path.Combine(state, "0"), "the save");

            var row = new GameSaveGame
            {
                GameId = "probe",
                FileLocation = state,
                IsDirectory = true,
                OriginalFileName = "state",
                SaveGroupId = "melonds:dsiware:" + titleId,
                SaveGroupName = "DSiWare save",
            };

            bool ok = true;
            ok &= Check("a DSiWare save says it is a container", plugin.IsSaveContainer(row));

            if (Directory.Exists(vault)) Directory.Delete(vault, recursive: true);
            bool done = plugin.TryBackupSave(row, exe, vault, out var error);
            ok &= Check("and can therefore be extracted, as it promised", done);
            if (!done) Console.WriteLine("    it said: " + (error ?? "no reason given"));

            ok &= Check("the index is in the backup", File.Exists(Path.Combine(vault, "files.txt")));
            ok &= Check("and so is the file it names", File.Exists(Path.Combine(vault, "0")));
            ok &= Check("the backup is not an empty folder",
                        Directory.Exists(vault) && Directory.GetFiles(vault).Length == 2);

            // Without an emulator path: the host does not always supply one, and the install is
            // reachable from the save's own location.
            string second = Path.Combine(install, "vault-again");
            ok &= Check("it works with no emulator path, from the save's location alone",
                        plugin.TryBackupSave(row, null, second, out _)
                        && File.Exists(Path.Combine(second, "files.txt")));

            // A cartridge save is one file: the host copies those itself and must never be told
            // otherwise, or it would ask for a container that does not exist.
            var plain = new GameSaveGame
            {
                GameId = "probe",
                FileLocation = Path.Combine(install, "whatever.sav"),
                SaveGroupId = "melonds:save:whatever",
            };
            ok &= Check("a cartridge save is NOT a container", !plugin.IsSaveContainer(plain));

            foreach (var leftover in new[] { vault, second, Path.Combine(install, "dsi", titleId) })
                try { Directory.Delete(leftover, recursive: true); } catch { }
            return ok;
        }

        /// <summary>Back up, delete everything, restore from the backup - through the host's own
        /// doors, in that order, which is the sequence somebody actually performs.
        ///
        /// THE DELETE IN THE MIDDLE IS THE POINT. It takes the whole title folder, reference walk and
        /// cached metadata included, so the restore that follows lands on nothing at all. If it
        /// depended on any of what the delete removed, this is where that would show - and it is the
        /// one arrangement no single-method test reaches.</summary>
        private static bool DsiWareRoundTrip(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- back up, delete, restore from the vault");

            const string titleId = "000300044b393945";
            string install = Path.GetDirectoryName(exe);
            string dsi = Path.Combine(install, "dsi");
            string titleDir = Path.Combine(dsi, titleId);
            string state = Path.Combine(titleDir, "state");
            string vault = Path.Combine(install, "vault-roundtrip");

            var was = PluginHelper.DataManager;
            try
            {
                // AddSaveFile finds the installation through the library, as it does in the host.
                PluginHelper.DataManager = new StubDataManager(
                    new StubEmulator { Title = "melonDS", ApplicationPath = exe });

                // A good evening, and the metadata that sits beside it.
                Directory.CreateDirectory(state);
                File.WriteAllText(Path.Combine(state, "files.txt"), "F\t0\t0:/a\r\nF\t1\t0:/b\r\n");
                File.WriteAllText(Path.Combine(state, "0"), "the good save");
                File.WriteAllText(Path.Combine(state, "1"), "and its neighbour");
                File.WriteAllText(Path.Combine(titleDir, "reference.txt"), "the fresh install");
                File.WriteAllText(Path.Combine(titleDir, "title.tmd"), "metadata");

                var row = new GameSaveGame
                {
                    GameId = "probe",
                    FileLocation = state,
                    IsDirectory = true,
                    OriginalFileName = "state",
                    SaveGroupId = "melonds:dsiware:" + titleId,
                    SaveGroupName = "DSiWare save",
                };

                bool ok = true;

                if (Directory.Exists(vault)) Directory.Delete(vault, recursive: true);
                ok &= Check("the evening goes into the vault",
                            plugin.TryBackupSave(row, exe, vault, out var why) && File.Exists(Path.Combine(vault, "0")));
                if (!File.Exists(Path.Combine(vault, "0"))) Console.WriteLine("    it said: " + why);

                // Then it is all thrown away - the folder, the reference walk, the metadata.
                var response = plugin.RemoveSave(row);
                ok &= Check("the delete takes the whole title folder",
                            response is { WasSuccess: true } && !Directory.Exists(titleDir));

                // And back out of the vault, onto nothing.
                var restored = plugin.AddSaveFile(new AddSaveArgs
                {
                    SaveToAdd = new GameSaveGame
                    {
                        GameId = "probe",
                        FileLocation = vault,
                        IsDirectory = true,
                        SaveGroupId = "melonds:dsiware:" + titleId,
                        SaveGroupName = "DSiWare save",
                    },
                    ShouldOverwriteFunc = () => true,
                });

                ok &= Check("a folder out of the vault is restored, not rejected for being a folder",
                            restored is { WasSuccess: true });
                if (restored is not { WasSuccess: true }) Console.WriteLine("    " + Why(restored));

                ok &= Check("the evening is back, byte for byte",
                            File.Exists(Path.Combine(state, "0"))
                            && File.ReadAllText(Path.Combine(state, "0")) == "the good save");
                ok &= Check("and its index with it, so a launch knows what to put where",
                            File.Exists(Path.Combine(state, "files.txt")));

                return ok;
            }
            finally
            {
                PluginHelper.DataManager = was;
                foreach (var leftover in new[] { vault, titleDir })
                    try { if (Directory.Exists(leftover)) Directory.Delete(leftover, recursive: true); } catch { }
            }
        }

        /// <summary>Two dumps of one region, which is the case that silently breaks saves.
        ///
        /// Directory order is not an ordering - it is whatever the filesystem hands back, and it
        /// moves when files are added or renamed. Every DSiWare save is the difference against ONE
        /// dump, so a choice that drifts replays saves onto a console they never came from. The rule
        /// is checked here rather than through a launch because two readable 240 MB dumps are not
        /// something a forged install can produce.</summary>
        private static bool TwoOfTheSameRegion()
        {
            Console.WriteLine();
            Console.WriteLine("  -- two NAND dumps of the same region");

            var steadiest = TypeIn("MelonDsBios")?.GetMethod("Steadiest",
                                BindingFlags.Public | BindingFlags.Static);
            if (steadiest == null)
            { Console.WriteLine("    no Steadiest to call"); return false; }

            var type = TypeIn("NandDump");
            var list = (IList)Activator.CreateInstance(
                           typeof(List<>).MakeGenericType(type));

            object Dump(string path, string setup)
            {
                var dump = Activator.CreateInstance(type);
                type.GetField("Path").SetValue(dump, path);
                type.GetField("Setup").SetValue(dump,
                    Enum.Parse(TypeIn("NandSetup"), setup));
                return dump;
            }

            string Chosen()
            {
                var picked = steadiest.Invoke(null, new object[] { list });
                return (string)type.GetField("Path").GetValue(picked);
            }

            bool ok = true;

            // Names alone: the same answer every time, whatever order they arrive in.
            list.Add(Dump("z-dump.bin", "NeverUsed"));
            list.Add(Dump("a-dump.bin", "NeverUsed"));
            ok &= Check("with neither set up, the name decides", Chosen() == "a-dump.bin");

            // And a dump that has been through its setup beats a name that sorts before it - that
            // is the one whose saves exist.
            list.Add(Dump("z-other.bin", "Locked"));
            ok &= Check("a dump that has been set up wins over one that has not",
                        Chosen() == "z-other.bin");

            list.Add(Dump("b-other.bin", "Locked"));
            ok &= Check("and between two set up, the name decides again",
                        Chosen() == "b-other.bin");

            return ok;
        }

        /// <summary>Turn the plugin's trace marker on or off for a moment, through its own field
        /// rather than by creating a file in the user's log folder. Answers what it was.</summary>
        private static bool Tracing(bool on)
        {
            try
            {
                var field = TypeIn("Log").GetField("_tracing",
                                BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null) return false;
                var before = (bool?)field.GetValue(null);
                field.SetValue(null, (bool?)on);
                return before ?? false;
            }
            catch { return false; }
        }

        /// <summary>Whatever a response carries as its reason, without this harness having to know
        /// which of the SDK's several names for it that type uses.</summary>
        private static string Why(object response)
        {
            try
            {
                if (response == null) return "no response at all";
                foreach (var name in new[] { "ErrorMessage", "Error", "Message", "Reason" })
                {
                    var value = response.GetType().GetProperty(name)?.GetValue(response) as string;
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                return "no reason given";
            }
            catch { return "no reason given"; }
        }

        private static string StateOf(MethodInfo of, string path)
        {
            try { return of.Invoke(null, new object[] { path }).ToString(); }
            catch { return "(threw)"; }
        }

        /// <summary>A file of a given length without writing a given length. SetLength extends it in
        /// the file table; nothing is read back, and a quarter of a gigabyte of zeroes never has to
        /// travel through a buffer to prove that the scan skipped it.</summary>
        private static void Sized(string path, long bytes)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            stream.SetLength(bytes);
        }

        private static long FreeSpaceOn(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch { return -1; }
        }

        private static bool RegionCascade(string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- which DSi a DSiWare title needs");

            var region = TypeIn("MelonDsRegion");
            var regionsFor = region.GetMethod("RegionsFor", BindingFlags.Public | BindingFlags.Static);
            var names = region.GetMethod("Names", BindingFlags.Public | BindingFlags.Static);
            var describe = TypeIn("NdsHeader").GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            if (regionsFor == null || names == null || describe == null)
            { Console.WriteLine("    no RegionsFor / Names / Describe to call"); return false; }

            string Ask(string fileName, uint mask, string gameCode)
            {
                var path = Path.Combine(romDir, fileName);
                File.WriteAllBytes(path, Header(0x03, 0x00030004, GameCode(gameCode), mask));
                var args = new object[] { describe.Invoke(null, new object[] { path }), path, null };
                var got = regionsFor.Invoke(null, args);
                try { File.Delete(path); } catch { }
                return (string)names.Invoke(null, new[] { got }) + " | " + (args[2] as string ?? "nothing");
            }

            bool ok = true;

            // The mask alone. 0x02 is USA - measured on a real dump, and the bit melonDS's own
            // RegionMask enum gives that region.
            ok &= Check("a USA mask asks for a USA NAND",
                        Ask("Masked (USA).nds", 0x02, "K99E").StartsWith("USA | the ROM's region mask"));

            // The ordering, in one line: the mask is what the console is handed, the letter is a
            // note about which market sold it.
            ok &= Check("a mask beats a letter that says otherwise",
                        Ask("Disagreeing (Japan).nds", 0x02, "K99J").StartsWith("USA |"));

            // A zero mask is a field nobody filled in, not a title with no region. Reading it as the
            // latter would strand the game with no NAND at all.
            ok &= Check("with no mask, the game code's letter answers",
                        Ask("Lettered.nds", 0x00, "K99J").StartsWith("Japan | the region letter"));
            ok &= Check("a single-country European letter still means a European console",
                        Ask("Lettered.nds", 0x00, "K99F").StartsWith("Europe |"));

            // A wide answer narrowed by the file name - what the brackets in a dump's name are for.
            ok &= Check("a two-region mask is narrowed by the file name",
                        Ask("Narrowed (Europe).nds", 0x04 | 0x08, "K99V")
                            .StartsWith("Europe | the ROM's region mask, narrowed"));
            ok &= Check("and a name agreeing with nothing leaves it alone",
                        Ask("Ignored (Brazil).nds", 0x04 | 0x08, "K99V").StartsWith("Europe or Australia |"));

            ok &= Check("a region-free title accepts any NAND",
                        Ask("Free.nds", 0xFFFFFFFF, "K99A")
                            .StartsWith("Japan or USA or Europe or Australia or China or Korea |"));

            // Nothing anywhere. An empty answer lets the caller say so, instead of picking a NAND at
            // random and leaving the DSi menu to refuse it without explanation.
            ok &= Check("a title that says nothing anywhere gets no answer",
                        Ask("Silent.nds", 0x00, "K99 ").StartsWith("(none) | nothing"));

            return ok;
        }

        /// <summary>Four ASCII letters as the u32 the title id's low word holds. The bytes sit in the
        /// ROM in reverse - "K99E" is stored 45 39 39 4b and reads back 0x4b393945 - so the region
        /// letter lands in the LOW byte, which is where MelonDsRegion looks.</summary>
        private static uint GameCode(string code)
        {
            uint value = 0;
            for (int i = 0; i < 4 && i < code.Length; i++)
                value |= (uint)code[3 - i] << (i * 8);      // reversed, so 'E' of "K99E" is the low byte
            return value;
        }

        // ── the same path, against a REAL NAND ───────────────────────────────

        /// <summary>The DSiWare launch path run for real: a genuine dump, a genuine DSiWare ROM, and
        /// the plugin's own code driving it. Everything above runs on a forged install, which proves
        /// the plugin agrees with our READING of melonDS; this proves the reading.
        ///
        /// NOTHING GIVEN IS WRITTEN TO. The NANDs are copied into the temp folder first and the
        /// copies are what the plugin sees. That is not politeness: the images passed in are
        /// somebody's console and somebody's save.
        ///
        /// With --melonds-played, a second image is put where the previous layout kept its per-title
        /// NAND, so the migration runs too - and the assertion that matters is that what comes out of
        /// the rebuilt image is what was in that one, file for file.</summary>
        public static bool AgainstReal(EmulatorPlugin plugin, string basePath, string romPath,
                                       string bios7, string bios9, string playedPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- melonds, against a REAL NAND  [WRITES: copies, in the temp folder] --");

            foreach (var (what, path) in new[] { ("--melonds-base", basePath), ("--rom", romPath),
                                                 ("--melonds-bios7", bios7), ("--melonds-bios9", bios9) })
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                { Console.WriteLine("  " + what + " is missing or does not exist"); return false; }

            if (MelonDsRunning())
            {
                Console.WriteLine("  skipped - melonDS is running, and the plugin refuses to write");
                return true;
            }

            Silence();

            string root = Path.Combine(Path.GetTempPath(), "lbip-melonds-real-" + Guid.NewGuid().ToString("N"));
            try
            {
                string install = Path.Combine(root, "melonDS");
                string dsi = Path.Combine(install, "dsi");
                Directory.CreateDirectory(dsi);
                string exe = Path.Combine(install, "melonDS.exe");
                File.WriteAllBytes(exe, Array.Empty<byte>());

                Console.WriteLine("  copying the NANDs and the ROM, so nothing given is touched...");

                // Into the bios folder, UNDER A NAME THAT SAYS NOTHING. The plugin reads a dump's
                // region out of its contents, and a name it could have leaned on instead would hide
                // whether it really does.
                // Resolved here rather than reusing `layout`, which is built further down.
                string bios = BiosDir(TypeIn("MelonDsPaths")
                    .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { exe }));
                Directory.CreateDirectory(bios);
                File.Copy(basePath, Path.Combine(bios, "a-dump-with-an-unhelpful-name.bin"));
                File.Copy(bios7, Path.Combine(bios, BiosName("DsiBios7")));
                File.Copy(bios9, Path.Combine(bios, BiosName("DsiBios9")));

                // melonDS needs a DSi firmware and this check never launches melonDS, so a
                // placeholder is enough to exercise the plugin's own requirement.
                File.WriteAllBytes(Path.Combine(bios, BiosName("DsiFirmware")), new byte[128 * 1024]);

                // The ROM is copied too, because one assertion below ages its timestamp to prove the
                // image is NOT reused for a ROM that is not the one installed. Doing that to the file
                // somebody passed in would be helping myself to their library.
                var romCopy = Path.Combine(root, Path.GetFileName(romPath));
                File.Copy(romPath, romCopy);
                romPath = romCopy;

                var rom = TypeIn("NdsHeader").GetMethod("Describe", BindingFlags.Public | BindingFlags.Static)
                              .Invoke(null, new object[] { romPath });
                string titleId = (string)rom.GetType().GetProperty("TitleId").GetValue(rom);
                bool isWare = (bool)rom.GetType().GetField("IsDSiWare").GetValue(rom);
                if (!isWare) { Console.WriteLine("  " + romPath + " is not DSiWare"); return false; }
                Console.WriteLine("  title   : " + titleId);

                string played = null;
                if (!string.IsNullOrWhiteSpace(playedPath) && File.Exists(playedPath))
                {
                    Directory.CreateDirectory(Path.Combine(dsi, titleId));
                    played = Path.Combine(dsi, titleId, "nand.bin");
                    File.Copy(playedPath, played);
                    Console.WriteLine("  and a played NAND in the old per-title place, to migrate");
                }

                // Nothing configured at all: the plugin has to find everything for itself.
                File.WriteAllText(ConfigPath(exe), "[Emu]\r\nConsoleType = 0\r\n");

                var choose = TypeIn("MelonDsPlugin").GetMethod("ChooseConsoleMode",
                                 BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve",
                                 BindingFlags.Public | BindingFlags.Static);
                var layout = resolve.Invoke(null, new object[] { exe });

                bool ok = true;
                string work = Path.Combine(dsi, "work.bin");
                string state = Path.Combine(dsi, titleId, "state");

                // ── the launch ──────────────────────────────────────────────
                choose.Invoke(null, new object[] { layout, romPath });

                ok &= Check("a working NAND is built", File.Exists(work));
                ok &= Check("melonDS is pointed at it", NandPathIn(ConfigPath(exe)) == work);
                ok &= Check("the DSi BIOS and firmware were found in the bios folder and configured",
                            TomlValues(ConfigPath(exe), "DSi").TryGetValue("FirmwarePath", out var fw)
                            && fw.Contains(BiosName("DsiFirmware")));
                ok &= Check("the dump's region was read out of it, not out of its name",
                            LogSince(0).Contains("a-dump-with-an-unhelpful-name.bin is a"));
                ok &= Check("DSi mode, through the menu",
                            ConsoleTypeIn(ConfigPath(exe)) == 1 && (DirectBootIn(ConfigPath(exe)) ?? true) == false);
                ok &= Check("the walk of the fresh install is written down",
                            File.Exists(Path.Combine(dsi, titleId, "reference.txt")));
                ok &= Check("and the working NAND is remembered as holding this title",
                            File.Exists(Path.Combine(dsi, "work.title")));

                if (played != null)
                {
                    ok &= Check("the old per-title NAND is gone, its 240 MB reclaimed", !File.Exists(played));
                    ok &= Check("its state was written down first",
                                File.Exists(Path.Combine(state, "files.txt")));

                    var index = File.Exists(Path.Combine(state, "files.txt"))
                        ? File.ReadAllLines(Path.Combine(state, "files.txt")) : Array.Empty<string>();
                    long bytes = Directory.Exists(state)
                        ? Directory.EnumerateFiles(state).Sum(f => new FileInfo(f).Length) : 0;
                    Console.WriteLine("    a whole session is " + index.Length + " file(s), "
                                      + bytes.ToString("N0") + " bytes");
                    foreach (var line in index)
                    {
                        var parts = line.Split(new[] { '\t' }, 3);
                        if (parts.Length == 3) Console.WriteLine("      " + parts[0] + "  " + parts[2]);
                    }

                    // THE ASSERTION THIS WHOLE THING EXISTS FOR. Walk the rebuilt image and walk the
                    // one that was actually played, and require them to hold the same files with the
                    // same contents. Anything less is an opinion about whether the save survived.
                    ok &= Check("the rebuilt NAND holds what the played one held, file for file",
                                SameFiles(work, playedPath, bios7, out var difference));
                    if (difference != null) Console.WriteLine("    " + difference);
                }

                // ── the same game again: the image is reused, not rebuilt ───
                var before = Fingerprint(state);
                var built = File.GetLastWriteTimeUtc(work);
                choose.Invoke(null, new object[] { layout, romPath });
                ok &= Check("relaunching the same game reuses the image instead of rebuilding it",
                            File.GetLastWriteTimeUtc(work) == built);
                ok &= Check("and its state is left alone", Fingerprint(state) == before);
                ok &= Check("it still holds what the played NAND held",
                            playedPath == null || SameFiles(work, playedPath, bios7, out _));

                // ── a ROM that is not the one installed must NOT be reused ──
                File.SetLastWriteTimeUtc(romPath, File.GetLastWriteTimeUtc(romPath).AddMinutes(-7));
                choose.Invoke(null, new object[] { layout, romPath });
                ok &= Check("a ROM that is not the one installed makes it rebuild",
                            File.GetLastWriteTimeUtc(work) != built);

                ok &= Check("and the rebuild still holds what the played NAND held",
                            playedPath == null || SameFiles(work, playedPath, bios7, out _));



                // ── the save, as the host sees it and hands it back ────────
                //
                // THIS IS WHERE THE UNIT IS PROVED. The host lists something, copies it to a vault,
                // and one day hands it back. If what it listed was one file out of a state, the
                // round trip loses the rest - and that is exactly what the earlier design did:
                // measured, a game's own public.sav was 16 KB of a 4.2 MB state across eleven files.
                var stateDir = MelonDsDsi_SavePathFor(layout, titleId);
                ok &= Check("what the host is offered is the whole state folder",
                            stateDir != null && Directory.Exists(stateDir)
                            && File.Exists(Path.Combine(stateDir, "files.txt")));

                if (stateDir != null && Directory.Exists(stateDir))
                {
                    int files = Directory.GetFiles(stateDir).Length;
                    long bytes = Directory.EnumerateFiles(stateDir).Sum(f => new FileInfo(f).Length);
                    Console.WriteLine("    it is " + files + " file(s), " + bytes.ToString("N0") + " bytes");

                    // Copy it away the way a vault would, scribble on the live one, hand the copy
                    // back, and require the live one to come back to exactly what was taken.
                    var vault = Path.Combine(root, "vault");
                    Directory.CreateDirectory(vault);
                    foreach (var f in Directory.GetFiles(stateDir))
                        File.Copy(f, Path.Combine(vault, Path.GetFileName(f)));
                    var taken = Fingerprint(stateDir);

                    File.WriteAllText(Path.Combine(stateDir, "files.txt"), "wrecked");
                    File.Delete(Directory.GetFiles(stateDir)[0]);
                    ok &= Check("a wrecked state really is different", Fingerprint(stateDir) != taken);

                    var restore = TypeIn("MelonDsDsi").GetMethod("RestoreSave",
                                      BindingFlags.Public | BindingFlags.Static);
                    var args = new object[] { layout, titleId, bios7, vault, null };
                    bool done = (bool)restore.Invoke(null, args);
                    ok &= Check("the host can hand the folder back", done);
                    if (!done) Console.WriteLine("    " + (args[4] as string ?? "no reason given"));
                    ok &= Check("and the state is exactly what was taken", Fingerprint(stateDir) == taken);

                    // And a folder that is not one of ours is refused rather than half-applied.
                    var junk = Path.Combine(root, "junk");
                    Directory.CreateDirectory(junk);
                    File.WriteAllText(Path.Combine(junk, "hello.txt"), "not a state");
                    var bad = new object[] { layout, titleId, bios7, junk, null };
                    ok &= Check("a folder that is not a melonDS state is refused",
                                !(bool)restore.Invoke(null, bad));
                }

                Console.WriteLine();
                Console.WriteLine("  " + (ok ? "OK - the DSiWare path works on a real NAND" : "NOT OK - see above"));
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        }

        /// <summary>Do two NANDs hold the same files with the same contents? Asked through the
        /// plugin's own walk, one image at a time - melonDS's mount keeps a global filesystem
        /// pointer, so two cannot be open at once.</summary>
        private static bool SameFiles(string a, string b, string bios7, out string difference)
        {
            difference = null;
            string wa = Path.GetTempFileName(), wb = Path.GetTempFileName();
            try
            {
                if (!WalkInto(a, bios7, wa, out difference)) return false;
                if (!WalkInto(b, bios7, wb, out difference)) return false;

                var delta = TypeIn("MelonDsDelta");
                var read = delta.GetMethod("Read", BindingFlags.Public | BindingFlags.Static);
                var compare = delta.GetMethod("Compare", BindingFlags.Public | BindingFlags.Static);

                var args = new object[] { read.Invoke(null, new object[] { wa }),
                                          read.Invoke(null, new object[] { wb }), null, null };
                compare.Invoke(null, args);
                var differing = (System.Collections.IList)args[2];
                var removed = (System.Collections.IList)args[3];
                if (differing.Count == 0 && removed.Count == 0) return true;

                var names = new List<string>();
                foreach (var d in differing) names.Add("~ " + d);
                foreach (var r in removed) names.Add("- " + r);
                difference = string.Join("; ", names);
                return false;
            }
            finally { try { File.Delete(wa); File.Delete(wb); } catch { } }
        }

        private static bool WalkInto(string nand, string bios7, string into, out string error)
        {
            error = null;
            var open = TypeIn("MelonDsNand").GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
            var args = new object[] { nand, bios7, null };
            var session = open.Invoke(null, args);
            if (session == null) { error = "could not open " + nand + " - " + args[2]; return false; }
            try
            {
                var walk = session.GetType().GetMethod("Walk");
                var walkArgs = new object[] { into, null };
                if ((int)walk.Invoke(session, walkArgs) >= 0) return true;
                error = "could not walk " + nand + " - " + walkArgs[1];
                return false;
            }
            finally { ((IDisposable)session).Dispose(); }
        }

        private static string MelonDsDsi_SavePathFor(object layout, string titleId)
        {
            try
            {
                return (string)TypeIn("MelonDsDsi")
                    .GetMethod("SavePathFor", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { layout, titleId });
            }
            catch { return null; }
        }

        /// <summary>Everything in a state folder, as one string. Enough to notice a change.</summary>
        private static string Fingerprint(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return "";
                var parts = new List<string>();
                foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
                {
                    using var sha = System.Security.Cryptography.SHA1.Create();
                    using var stream = File.OpenRead(file);
                    parts.Add(Path.GetFileName(file) + ":" + Convert.ToBase64String(sha.ComputeHash(stream)));
                }
                return string.Join("|", parts);
            }
            catch { return "?"; }
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

        // -- 11. the carried metadata index ----------------------------------

        /// <summary>The embedded index, read TWICE: once here, straight from the resource and
        /// following the layout the packer documents, and once by the plugin through its own lookup.
        /// The two readings have to agree. A check that only called the plugin would confirm the
        /// plugin agrees with itself - which is exactly what let a packer write one byte per block
        /// and still print a reassuring summary.</summary>
        private static bool CarriedIndex(string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the carried metadata index");

            var resource = _assemblyAnchor.Assembly.GetManifestResourceStream("tmd-library.bin");
            if (resource == null)
            {
                Console.WriteLine("    skipped - this build carries no index. It is built by");
                Console.WriteLine("              tools\\pack-tmd-library.ps1 and is not in the repository.");
                return true;
            }

            byte[] all;
            using (resource)
            using (var buffer = new MemoryStream()) { resource.CopyTo(buffer); all = buffer.ToArray(); }

            if (!Check("it is the format the packer writes",
                       all.Length > 20 && all[0] == 'M' && all[1] == 'D' && all[2] == 'S'
                       && all[3] == 'T' && all[4] == 'M' && all[5] == 'D' && all[7] == 2))
                return false;

            bool ok = true;
            int count = BitConverter.ToInt32(all, 8);
            int blocks = BitConverter.ToInt32(all, 12);
            int perBlock = BitConverter.ToUInt16(all, 16);
            int table = 20 + count * 36;
            int payload = table + blocks * 8;
            Console.WriteLine("    " + count.ToString("N0") + " entries in " + blocks + " blocks of "
                              + perBlock + ", " + (all.Length / 1024).ToString("N0") + " KB carried");

            // The block table has to account for the file exactly. A payload that stopped early is
            // the failure that already happened once, and it is invisible from the counts alone.
            long declared = payload;
            for (int i = 0; i < blocks; i++) declared += BitConverter.ToUInt32(all, table + i * 8 + 4);
            ok &= Check("its block table accounts for the whole file", declared == all.Length);

            // Sorted, or the binary search is answering by luck.
            bool sorted = true;
            for (int i = 0; i + 1 < count && sorted; i++)
                for (int k = 0; k < 8; k++)
                {
                    int a = all[20 + i * 36 + k], b = all[20 + (i + 1) * 36 + k];
                    if (a != b) { sorted = a < b; break; }
                }
            ok &= Check("its records are sorted by title id", sorted);

            // A spread of real entries, asked for the way a launch asks for one: forge a DSiWare
            // header carrying that title id, and let the plugin go and find its metadata.
            var index = TypeIn("MelonDsTmd").GetMethod("FromIndex",
                            BindingFlags.NonPublic | BindingFlags.Static);
            var describe = TypeIn("NdsHeader").GetMethod("Describe", BindingFlags.Public | BindingFlags.Static);
            var resolve = TypeIn("MelonDsPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            if (index == null || describe == null || resolve == null)
            { Console.WriteLine("    no FromIndex / Describe / Resolve to call"); return false; }

            var layout = resolve.Invoke(null, new object[] { exe });
            string rom = Path.Combine(romDir, "Carried Index Probe.nds");
            int asked = 0, agreed = 0;

            for (int i = 0; i < count; i += Math.Max(1, count / 12))
            {
                int at = 20 + i * 36;
                uint high = (uint)((all[at] << 24) | (all[at + 1] << 16) | (all[at + 2] << 8) | all[at + 3]);
                uint low = (uint)((all[at + 4] << 24) | (all[at + 5] << 16) | (all[at + 6] << 8) | all[at + 7]);

                File.WriteAllBytes(rom, Header(0x03, high, low));
                var args = new object[] { layout, describe.Invoke(null, new object[] { rom }), rom, null };
                var found = (string)index.Invoke(null, args);
                asked++;

                if (found == null || !File.Exists(found)) continue;
                var bytes = File.ReadAllBytes(found);
                bool right = bytes.Length == 520;
                for (int k = 0; k < 8 && right; k++) right = bytes[0x18C + k] == all[at + k];
                if (right) agreed++;
                try { Directory.Delete(Path.GetDirectoryName(found), recursive: true); } catch { }
            }

            ok &= Check("a sampled title comes back, 520 bytes, describing itself ("
                        + agreed + " of " + asked + ")", asked > 0 && agreed == asked);

            // And a title it does not hold gets no answer rather than a neighbour's. An id of all
            // ones sorts past every record, so it exercises the end of the search.
            File.WriteAllBytes(rom, Header(0x03, 0x00030004, 0xFFFFFFFF));
            var none = new object[] { layout, describe.Invoke(null, new object[] { rom }), rom, null };
            ok &= Check("a title it does not hold gets no answer", index.Invoke(null, none) == null);

            try { File.Delete(rom); } catch { }
            return ok;
        }

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
            // The fourth argument is the launch-path override; reflection does not fill optional
            // parameters, so it is named here rather than left out.
            return (string)write.Invoke(null, new object[] { ConfigPath(exe), table, values, false });
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
