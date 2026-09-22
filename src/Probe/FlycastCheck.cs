// The Flycast assertion, against a FORGED installation.
//
// Declared plainly, the way the Xenia checks were: there is no Flycast and no Dreamcast game on the
// machine this was written on. Everything below is built in the temp folder from what Flycast's own
// source says it writes - file names, folder layout, the IP.BIN header - and then handed to the
// plugin through the PUBLIC contract. So this proves the plugin agrees with our READING of Flycast,
// not that our reading is right. Confronting it with a real install is still owed.
//
// What each part would catch:
//
//   1. a Dreamcast save found by DISC ID              - the id must come from inside the image
//   2. IP.BIN found at a non-trivial offset            - a naive "read offset 0" would miss it
//   3. an arcade set found by FILE NAME                - a different rule from the Dreamcast one
//   4. the arcade companions travelling with the primary
//   5. shared cards and machine files refused          - restoring one would wipe other games
//   6. states listed by slot, .state.net/.tmp excluded
//   7. a restore landing under the emulator's own name
//   8. a delete taking the whole arcade set

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
    internal static class FlycastCheck
    {
        private const string Product = "MK-51035";
        private const string DiscRom = "Crazy Taxi (USA).gdi";
        private const string ArcadeRom = "crzytaxi.zip";

        public static bool Run(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            Console.WriteLine("-- flycast, on a FORGED install  [WRITES, in the temp folder] " + new string('-', 3));

            string root = Path.Combine(Path.GetTempPath(), "lbip-flycast-" + Guid.NewGuid().ToString("N"));
            try
            {
                var (exe, data, romDir) = Forge(root);
                Console.WriteLine("  install : " + Path.GetDirectoryName(exe));
                Console.WriteLine("  data    : " + data);

                bool ok = true;
                var saves = Listing(plugin, exe, romDir, out var vmu, out var arcade, out var states);
                ok &= saves;
                if (saves)
                {
                    ok &= Companions(plugin, arcade);
                    ok &= Secondary(plugin, data);
                    ok &= Slots(plugin);
                    ok &= Restore(plugin, exe, vmu);
                    ok &= Delete(plugin, arcade);
                }
                ok &= NoIdNoSave(plugin, exe, romDir);
                ok &= PlatformsCompleted(plugin, exe);
                ok &= ExistingRowsLeftAlone(plugin, exe);
                ok &= ForeignPlatformsUnchecked(plugin, exe);
                ok &= Versions(plugin);
                ok &= SaveLiveness(plugin, exe, data);
                ok &= StartupSweep(plugin, exe);

                Console.WriteLine();
                Console.WriteLine("  " + (ok ? "OK - the plugin matches our reading of Flycast" : "NOT OK - see above"));
                return ok;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        }

        // ── the fixture ──────────────────────────────────────────────────────

        /// <summary>The plugin's own log file, so an assertion can be made about what it SAID. Some
        /// defects only ever show up as a sentence - "X is one slip away from X" was one.</summary>
        private static string LogPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "lb-integrations-plugins", "flycast.log");

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

        private static (string exe, string data, string romDir) Forge(string root)
        {
            string install = Path.Combine(root, "Flycast");
            string data = Path.Combine(install, "data");
            string romDir = Path.Combine(root, "roms");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(romDir);

            string exe = Path.Combine(install, "flycast.exe");
            File.WriteAllBytes(exe, Array.Empty<byte>());

            // A disc whose IP.BIN sits at a NON-TRIVIAL offset: on a real GD-ROM the data track is the
            // third, so anything that assumes offset 0 has to fail here.
            const int ipbinAt = 0x9A00;
            var image = new byte[ipbinAt + 0x200];
            var header = Encoding.ASCII.GetBytes("SEGA SEGAKATANA ");
            Buffer.BlockCopy(header, 0, image, ipbinAt, header.Length);
            var product = Encoding.ASCII.GetBytes((Product + "          ").Substring(0, 10));
            Buffer.BlockCopy(product, 0, image, ipbinAt + 0x40, product.Length);

            string track = Path.Combine(romDir, "Crazy Taxi (USA).track03.bin");
            File.WriteAllBytes(track, image);
            File.WriteAllBytes(Path.Combine(romDir, "Crazy Taxi (USA).track01.bin"), new byte[4096]);
            // The index names an audio track first, so a reader that simply takes the first file listed
            // would look in the wrong one.
            File.WriteAllText(Path.Combine(romDir, DiscRom),
                "2\r\n1 0 0 2352 \"Crazy Taxi (USA).track01.bin\" 0\r\n"
                + "2 600 4 2048 \"Crazy Taxi (USA).track03.bin\" 0\r\n");

            // A disc with no IP.BIN at all, for the negative case.
            File.WriteAllBytes(Path.Combine(romDir, "Not A Disc.gdi.bin"), new byte[8192]);
            File.WriteAllText(Path.Combine(romDir, "Not A Disc.gdi"),
                "1\r\n1 0 4 2048 \"Not A Disc.gdi.bin\" 0\r\n");

            // An arcade ROM: identified by its file name, nothing read from inside.
            File.WriteAllBytes(Path.Combine(romDir, ArcadeRom), new byte[64]);

            // What Flycast would have written.
            Write(data, Product + "_vmu_save_A1.bin", 128 * 1024);       // the per-game VMU
            Write(data, ArcadeRom + ".nvmem", 8192);                     // the arcade set
            Write(data, ArcadeRom + ".nvmem2", 4096);
            Write(data, ArcadeRom + ".eeprom", 128);
            Write(data, ArcadeRom + "-p1.card", 256);
            Write(data, "Crazy Taxi (USA).state", 2048);                 // slot 0
            Write(data, "Crazy Taxi (USA)_3.state", 2048);               // slot 3
            Write(data, "Crazy Taxi (USA).state.net", 2048);             // NOT a slot
            Write(data, "Crazy Taxi (USA).state.tmp", 2048);             // NOT a slot
            Write(data, "vmu_save_A2.bin", 128 * 1024);                  // shared, must never be listed
            Write(data, "dc_nvmem.bin", 128 * 1024);                     // machine state, ditto
            Write(data, "dc_boot.bin", 2 * 1024 * 1024);                 // BIOS, ditto

            return (exe, data, romDir);
        }

        private static void Write(string dir, string name, int size)
        {
            var bytes = new byte[size];
            for (int i = 0; i < size; i++) bytes[i] = (byte)(i * 31 + name.Length);
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }

        // ── 1-2-3-6. listing ─────────────────────────────────────────────────

        private static bool Listing(EmulatorPlugin plugin, string exe, string romDir,
                                    out GameSaveBase vmu, out GameSaveBase arcade,
                                    out List<GameSaveState> states)
        {
            vmu = null; arcade = null; states = new List<GameSaveState>();

            var emulator = new StubEmulator { Title = "Flycast", ApplicationPath = exe };
            var disc = StubGame.Create(Guid.NewGuid().ToString(), "Crazy Taxi",
                                       Path.Combine(romDir, DiscRom), emulator.Id);
            var cart = StubGame.Create(Guid.NewGuid().ToString(), "Crazy Taxi (arcade)",
                                       Path.Combine(romDir, ArcadeRom), emulator.Id);

            var response = plugin.GetSaves(new GetSavesArgs { Emulator = emulator, Games = new[] { disc, cart } });
            if (response is not { WasSuccess: true })
            { Console.WriteLine("  GetSaves failed: " + (response?.Message ?? "no reason given")); return false; }

            var all = (response.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>()).ToList();
            states = all.OfType<GameSaveState>().ToList();
            var files = all.Where(s => s is not GameSaveState).ToList();

            Console.WriteLine();
            Console.WriteLine("  listed  : " + files.Count + " save(s), " + states.Count + " state(s)");
            foreach (var s in all)
                Console.WriteLine("    " + (s is GameSaveState st ? "slot " + st.Slot + "  " : "          ")
                                  + Path.GetFileName(s.FileLocation) + "   group=" + s.SaveGroupId);

            bool ok = true;

            vmu = files.FirstOrDefault(s => (s.SaveGroupId ?? "").StartsWith("flycast-vmu:", StringComparison.OrdinalIgnoreCase));
            if (vmu == null) { Console.WriteLine("  FAIL - no Dreamcast VMU row"); ok = false; }
            else
            {
                bool named = string.Equals(Path.GetFileName(vmu.FileLocation),
                                           Product + "_vmu_save_A1.bin", StringComparison.Ordinal);
                bool byId = (vmu.SaveGroupId ?? "").EndsWith(Product, StringComparison.Ordinal);
                if (!named || !byId)
                { Console.WriteLine("  FAIL - the VMU was not found by its disc id"); ok = false; }
                else Console.WriteLine("  the VMU was found by the disc id read at offset 0x9A00   OK");
            }

            arcade = files.FirstOrDefault(s => (s.SaveGroupId ?? "").StartsWith("flycast-arcade:", StringComparison.OrdinalIgnoreCase));
            if (arcade == null) { Console.WriteLine("  FAIL - no arcade row"); ok = false; }
            else if (!string.Equals(Path.GetFileName(arcade.FileLocation), ArcadeRom + ".nvmem", StringComparison.Ordinal))
            { Console.WriteLine("  FAIL - the arcade primary is " + Path.GetFileName(arcade.FileLocation)); ok = false; }
            else Console.WriteLine("  the arcade set was found by the rom FILE NAME, .nvmem first   OK");

            var slots = states.Select(s => s.Slot ?? -1).OrderBy(x => x).ToList();
            if (!slots.SequenceEqual(new[] { 0, 3 }))
            { Console.WriteLine("  FAIL - slots listed: " + string.Join(", ", slots) + ", expected 0 and 3"); ok = false; }
            else Console.WriteLine("  slots 0 and 3 listed, .state.net and .state.tmp excluded   OK");

            if (all.Any(s => (Path.GetFileName(s.FileLocation) ?? "").StartsWith("vmu_save_", StringComparison.OrdinalIgnoreCase)
                          || (Path.GetFileName(s.FileLocation) ?? "").StartsWith("dc_", StringComparison.OrdinalIgnoreCase)))
            { Console.WriteLine("  FAIL - a shared card or machine file was listed as a game save"); ok = false; }
            else Console.WriteLine("  no shared card and no machine file was listed   OK");

            return ok;
        }

        // ── 4. companions ────────────────────────────────────────────────────

        private static bool Companions(EmulatorPlugin plugin, GameSaveBase arcade)
        {
            if (arcade == null) return false;
            var companions = (plugin.GetCompanionSaveFiles(arcade.FileLocation) ?? Array.Empty<string>())
                             .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var expected = new[] { ArcadeRom + "-p1.card", ArcadeRom + ".eeprom", ArcadeRom + ".nvmem2" }
                           .OrderBy(n => n, StringComparer.Ordinal).ToList();

            Console.WriteLine();
            Console.WriteLine("  companions: " + string.Join(", ", companions));
            bool ok = companions.SequenceEqual(expected, StringComparer.Ordinal);
            Console.WriteLine("  the whole arcade set travels with its primary   " + (ok ? "OK" : "FAIL"));
            return ok;
        }

        // ── 5. what must never be a save ─────────────────────────────────────

        private static bool Secondary(EmulatorPlugin plugin, string data)
        {
            var cases = new (string Name, bool Expected, string What)[]
            {
                (ArcadeRom + ".nvmem",          false, "the arcade primary"),
                (ArcadeRom + ".nvmem2",         true,  "an arcade companion"),
                (ArcadeRom + "-p1.card",        true,  "a player card"),
                ("vmu_save_A2.bin",             true,  "a shared memory card"),
                ("dc_nvmem.bin",                true,  "the console's own NVRAM"),
                ("dc_boot.bin",                 true,  "the BIOS"),
                ("Crazy Taxi (USA).state.net",  true,  "a netplay state"),
                (Product + "_vmu_save_A1.bin",  false, "the per-game VMU"),
            };

            Console.WriteLine();
            bool ok = true;
            foreach (var c in cases)
            {
                bool got = plugin.IsSecondarySaveFile(Path.Combine(data, c.Name));
                bool good = got == c.Expected;
                ok &= good;
                Console.WriteLine("  IsSecondarySaveFile " + (got ? "true " : "false") + "  " + c.What
                                  + (good ? "   OK" : "   FAIL, expected " + c.Expected));
            }
            return ok;
        }

        private static bool Slots(EmulatorPlugin plugin)
        {
            var slots = plugin.GetPotentialSaveSlots();
            Console.WriteLine();
            bool ok = slots != null && slots.Count == 10 && slots.ContainsKey(0) && slots.ContainsKey(9);
            Console.WriteLine("  ten state slots declared, 0 to 9   " + (ok ? "OK" : "FAIL"));
            return ok;
        }

        // ── 7. restore ───────────────────────────────────────────────────────

        private static bool Restore(EmulatorPlugin plugin, string exe, GameSaveBase vmu)
        {
            if (vmu == null) return false;
            Console.WriteLine();

            string vault = Path.Combine(Path.GetTempPath(), "lbip-flyvault-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(vault);
            try
            {
                string name = Path.GetFileName(vmu.FileLocation);
                string copy = Path.Combine(vault, name);
                File.Copy(vmu.FileLocation, copy);
                File.Delete(vmu.FileLocation);

                PluginHelper.DataManager = new StubDataManager(
                    new StubEmulator { Title = "Flycast", ApplicationPath = exe });

                var response = plugin.AddSaveFile(new AddSaveArgs
                {
                    SaveToAdd = new GameSaveGame
                    {
                        FileLocation = copy,
                        OriginalFileName = name,
                        SaveGroupId = vmu.SaveGroupId,
                        GameId = vmu.GameId,
                    },
                    ShouldOverwriteFunc = () => true,
                });

                if (response is not { WasSuccess: true })
                { Console.WriteLine("  AddSaveFile failed: " + (response?.Message ?? "no reason given")); return false; }

                var landed = response.SaveAdded?.FileLocation;
                Console.WriteLine("  restored to : " + landed);

                bool sameName = string.Equals(Path.GetFileName(landed), name, StringComparison.Ordinal);
                bool sameBytes = File.Exists(landed)
                                 && File.ReadAllBytes(landed).AsSpan().SequenceEqual(File.ReadAllBytes(copy));
                Console.WriteLine("  the file name is Flycast's own, unchanged   " + (sameName ? "OK" : "FAIL"));
                Console.WriteLine("  identical byte for byte                     " + (sameBytes ? "OK" : "FAIL"));
                return sameName && sameBytes;
            }
            finally { try { Directory.Delete(vault, true); } catch { } }
        }

        // ── 8. delete ────────────────────────────────────────────────────────

        private static bool Delete(EmulatorPlugin plugin, GameSaveBase arcade)
        {
            if (arcade == null) return false;
            Console.WriteLine();

            var expected = new List<string> { arcade.FileLocation };
            expected.AddRange(plugin.GetCompanionSaveFiles(arcade.FileLocation) ?? Array.Empty<string>());

            var response = plugin.RemoveSave(arcade);
            var left = expected.Where(File.Exists).Select(Path.GetFileName).ToList();

            if (response is not { WasSuccess: true })
            { Console.WriteLine("  RemoveSave failed: " + (response?.Message ?? "no reason given")); return false; }
            if (left.Count > 0)
            { Console.WriteLine("  FAIL - left behind: " + string.Join(", ", left)); return false; }
            Console.WriteLine("  delete took the whole arcade set, " + expected.Count + " file(s)   OK");
            return true;
        }

        // ── the negative case ────────────────────────────────────────────────

        // ── against a REAL install ─────────────────────────────────

        /// <summary>The confrontation the forged fixture cannot give: a real Flycast, a real game,
        /// and the VMU FLYCAST ITSELF named. Everything else in this file proves the plugin agrees
        /// with our reading of the source; this proves the reading.
        ///
        /// The test is simply: Flycast wrote "&lt;something&gt;_vmu_save_A1.bin", we read a disc id
        /// out of the ROM, and the two must be the same string. Read-only.</summary>
        public static bool AgainstReal(EmulatorPlugin plugin, string exe, string romPath)
        {
            Console.WriteLine();
            Console.WriteLine("-- flycast, against a REAL install  [read only] " + new string('-', 16));
            Console.WriteLine("  emulator : " + exe);
            Console.WriteLine("  rom      : " + romPath);

            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            { Console.WriteLine("  no emulator there"); return false; }
            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
            { Console.WriteLine("  no rom there"); return false; }

            var data = Path.Combine(Path.GetDirectoryName(exe), "data");
            var written = Directory.Exists(data)
                ? Directory.GetFiles(data, "*_vmu_save_A1.bin").Select(Path.GetFileName).ToList()
                : new List<string>();

            if (written.Count == 0)
            {
                Console.WriteLine("  Flycast has written no per-game VMU yet - play the game once first");
                return false;
            }
            Console.WriteLine("  Flycast wrote : " + string.Join(", ", written));

            var emulator = new StubEmulator { Title = "Flycast", ApplicationPath = exe };
            var game = StubGame.Create(Guid.NewGuid().ToString(), "Game", romPath, emulator.Id);
            var response = plugin.GetSaves(new GetSavesArgs { Emulator = emulator, Games = new[] { game } });
            if (response is not { WasSuccess: true })
            { Console.WriteLine("  GetSaves failed: " + (response?.Message ?? "no reason given")); return false; }

            var all = (response.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>()).ToList();
            foreach (var row in all)
                Console.WriteLine("    " + (row is GameSaveState st ? "slot " + st.Slot + "  " : "          ")
                                  + Path.GetFileName(row.FileLocation) + "   group=" + row.SaveGroupId);

            var vmu = all.FirstOrDefault(r => (r.SaveGroupId ?? "")
                        .StartsWith("flycast-vmu:", StringComparison.OrdinalIgnoreCase));
            if (vmu == null)
            {
                Console.WriteLine("  FAIL - no VMU row: the disc id we read does not match what Flycast named");
                return false;
            }

            var ours = Path.GetFileName(vmu.FileLocation);
            bool match = written.Contains(ours, StringComparer.Ordinal);
            Console.WriteLine("  we matched   : " + ours);
            Console.WriteLine("  the disc id we read of the ROM is the one Flycast used   " + (match ? "OK" : "FAIL"));

            bool refusedMachine = plugin.IsSecondarySaveFile(Path.Combine(data, "dc_nvmem.bin"));
            Console.WriteLine("  the console's own NVRAM is refused as a game save   " + (refusedMachine ? "OK" : "FAIL"));

            bool ok = match && refusedMachine;
            Console.WriteLine();
            Console.WriteLine("  " + (ok ? "OK - our reading of Flycast is the emulator's own behaviour"
                                        : "NOT OK - see above"));
            return ok;
        }

        /// <summary>When LaunchBox does not know an emulator it associates it with EVERYTHING, every
        /// row ticked "Default Emulator" - which means Flycast becomes the default emulator for the
        /// SNES, the NES and the rest. We clear that flag on the platforms Flycast cannot run.
        ///
        /// We do NOT remove those rows. A row the user set up by hand, under a name we do not know,
        /// must survive a wrong guess on our side; a cleared tick costs them one click, a deleted row
        /// costs them their configuration.</summary>
        private static bool ForeignPlatformsUnchecked(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            var emu = new StubEmulator { Title = "Flycast (over-associated)", ApplicationPath = exe };
            foreach (var name in new[] { "Super Nintendo Entertainment System", "Nintendo 64",
                                         "Sega Dreamcast", "Sony Playstation" })
            {
                var row = emu.AddNewEmulatorPlatform();
                row.Platform = name;
                row.IsDefault = true;              // as LaunchBox leaves them
            }

            plugin.GetApplicableEmulators(new[] { emu });

            var rows = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            var byName = rows.ToDictionary(r => r.Platform, r => r.IsDefault);

            bool nothingRemoved = rows.Length >= 4
                && byName.ContainsKey("Super Nintendo Entertainment System")
                && byName.ContainsKey("Nintendo 64") && byName.ContainsKey("Sony Playstation");
            bool foreignCleared = byName["Super Nintendo Entertainment System"] == false
                && byName["Nintendo 64"] == false && byName["Sony Playstation"] == false;
            bool oursKept = byName.TryGetValue("Sega Dreamcast", out var dc) && dc;
            bool oursCompleted = byName.ContainsKey("Sega Naomi") && byName.ContainsKey("Sega Naomi 2")
                && byName.ContainsKey("Sammy Atomiswave");

            Console.WriteLine("  rows : " + string.Join(", ", rows.Select(r => r.Platform + (r.IsDefault ? "*" : ""))));
            Console.WriteLine("  nothing was removed                              " + (nothingRemoved ? "OK" : "FAIL"));
            Console.WriteLine("  platforms Flycast cannot run are unchecked       " + (foreignCleared ? "OK" : "FAIL"));
            Console.WriteLine("  a platform it does run keeps its tick            " + (oursKept ? "OK" : "FAIL"));
            Console.WriteLine("  the ones it runs that were missing were added    " + (oursCompleted ? "OK" : "FAIL"));
            return nothingRemoved && foreignCleared && oursKept && oursCompleted;
        }

        /// <summary>IsSaveActive, which decides whether the host shows a live save or only its vault
        /// copy.
        ///
        /// A path under the emulator's folder that NO LONGER EXISTS must not be called active.
        /// Measured on Xenia: reinstalling it moved every save under a new profile folder, the stale
        /// record still passed the prefix test, and the host picked the dead one as the group's
        /// active save - so the game showed a vault copy and no live save at all.</summary>
        private static bool SaveLiveness(EmulatorPlugin plugin, string exe, string data)
        {
            Console.WriteLine();
            bool ok = true;
            void Check(string what, bool good)
            {
                ok &= good;
                Console.WriteLine("  " + what.PadRight(46) + (good ? "OK" : "FAIL"));
            }

            var real = Path.Combine(data, "T44102N_vmu_save_A1.bin");
            Directory.CreateDirectory(data);
            if (!File.Exists(real)) File.WriteAllBytes(real, new byte[128]);

            var present = new GameSaveGame { FileLocation = real, SaveGroupId = "flycast-vmu:T44102N" };
            var gone = new GameSaveGame
            {
                FileLocation = Path.Combine(data, "GONE_vmu_save_A1.bin"),
                SaveGroupId = "flycast-vmu:GONE",
            };

            // IsDirectory must say what the save IS. A host that believes a folder is a file finds
            // nothing at that path and treats the save as gone - measured on LaunchBox 14, which then
            // drew the card from the vault copies alone. Flycast's saves are files; Xenia's unit is a
            // folder; both must say so.
            var listed = plugin.GetSaves(new GetSavesArgs
            {
                Emulator = new StubEmulator { Title = "Flycast", ApplicationPath = exe },
                Games = new[] { StubGame.Create("g", "game", Path.Combine(data, "..", "roms", "disc.chd")) },
            })?.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
            // "Secondary" tells the host to ignore a path. Answering true for a save the plugin
            // itself just returned is an instruction to drop it - see XeniaSaves, where it cost an
            // evening of looking everywhere but here.
            Check("no save is declared secondary by its own plugin",
                  listed.All(sv => !plugin.IsSecondarySaveFile(sv.FileLocation)));

            Check("every save says whether it is a directory",
                  listed.All(sv => sv.IsDirectory == Directory.Exists(sv.FileLocation ?? "")));

            Check("a save that is on disk is active", plugin.IsSaveActive(present, exe));
            Check("a save whose file is gone is NOT active", !plugin.IsSaveActive(gone, exe));

            var elsewhere = new GameSaveGame
            {
                FileLocation = Path.Combine(Path.GetTempPath(), "somewhere-else.bin"),
                SaveGroupId = "flycast-vmu:T44102N",
            };
            Check("a save outside the emulator is NOT active", !plugin.IsSaveActive(elsewhere, exe));
            return ok;
        }

        /// <summary>The version comparison, which decides whether LaunchBox shows the update cloud.
        ///
        /// Offline on purpose: the rule is a string rule, and the interesting case is a MEASURED one -
        /// the official v2.7 build calls itself "v2.7-1-g44e4c7b50", so the plain string can never
        /// equal its own tag and the update badge stayed lit forever.</summary>
        private static bool Versions(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            var type = plugin.GetType().Assembly.GetType("LbIntegrations.Flycast.FlycastPlugin");
            var normalize = type?.GetMethod("NormalizeVersion", BindingFlags.NonPublic | BindingFlags.Static);
            var release = type?.GetMethod("ReleasePart", BindingFlags.NonPublic | BindingFlags.Static);
            if (normalize == null || release == null)
            {
                Console.WriteLine("  FAIL - the version helpers are not where we expect them");
                return false;
            }

            string N(string v) => (string)normalize.Invoke(null, new object[] { v });
            string R(string v) => (string)release.Invoke(null, new object[] { v });

            bool ok = true;
            void Check(string what, bool good)
            {
                ok &= good;
                Console.WriteLine("  " + what.PadRight(46) + (good ? "OK" : "FAIL"));
            }

            // What the installed 2.7 really reports, read off the release asset.
            const string Installed = "v2.7-1-g44e4c7b50";
            Check("the leading v is dropped", N(Installed) == "2.7-1-g44e4c7b50");
            Check("the build tail is kept for display", N(Installed).EndsWith("-g44e4c7b50"));
            Check("the 2.7 release matches the 2.7 tag",
                  R(N(Installed)) == R(N("v2.7")));
            Check("an older release does not match", R(N("v2.6-3-gdeadbeef")) != R(N("v2.7")));
            Check("a tag with no tail survives", R("2.7") == "2.7");
            Check("a placeholder resource is refused", N("1.0.0.0") == null && N("") == null);
            return ok;
        }

        // ── the startup sweep ─────────────────────────────────────

        /// <summary>The host announcing that it is up must complete the entries WITHOUT being asked
        /// which emulators we claim. Without this the completion only lands when something calls
        /// GetApplicableEmulators, which can be after a page has already drawn the wrong list.
        ///
        /// Both hosts are covered: LiteBox raises PluginInitialized, LaunchBox raises
        /// LaunchBoxStartupCompleted. An unrelated event must change nothing.</summary>
        private static bool StartupSweep(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            if (plugin is not ISystemEventsPlugin events)
            {
                Console.WriteLine("  FAIL - the plugin does not implement ISystemEventsPlugin");
                return false;
            }

            var emu = new StubEmulator { Title = "Flycast (swept)", ApplicationPath = exe };
            PluginHelper.DataManager = new StubDataManager(emu);

            // An event we do not care about must do nothing at all.
            events.OnEventRaised(SystemEventTypes.SelectionChanged);
            int afterNoise = (emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>()).Length;
            bool quiet = afterNoise == 0;
            Console.WriteLine("  an unrelated event changes nothing   " + (quiet ? "OK" : "FAIL"));

            events.OnEventRaised(SystemEventTypes.LaunchBoxStartupCompleted);
            var platforms = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            bool swept = platforms.Length == 4;
            Console.WriteLine("  platforms after the startup event : "
                              + string.Join(", ", platforms.Select(p => p.Platform)));
            Console.WriteLine("  the startup event completes the entry unasked   " + (swept ? "OK" : "FAIL"));

            return quiet && swept;
        }

        // ── the emulator entry the user made by hand ─────────────────────────

        /// <summary>An emulator created by hand arrives with NO platforms, because LaunchBox's
        /// metadata knows nothing about Flycast. Claiming it must complete it, in memory.</summary>
        private static bool PlatformsCompleted(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            var emu = new StubEmulator { Title = "Flycast", ApplicationPath = exe };

            plugin.GetApplicableEmulators(new[] { emu });

            var platforms = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            var names = platforms.Select(p => p.Platform).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var expected = new[] { "Sammy Atomiswave", "Sega Dreamcast", "Sega Naomi", "Sega Naomi 2" }
                           .OrderBy(n => n, StringComparer.Ordinal).ToList();

            Console.WriteLine("  platforms : " + string.Join(", ", names));
            bool ok = names.SequenceEqual(expected, StringComparer.Ordinal);
            Console.WriteLine("  a hand-made entry is completed with the four platforms   " + (ok ? "OK" : "FAIL"));

            // IsDefault is per PLATFORM - "this emulator is the default emulator for it" - so every
            // platform we claim carries it. Setting it on one only made LaunchBox prune the others.
            bool defaultOk = platforms.Length > 0 && platforms.All(p => p.IsDefault);
            Console.WriteLine("  every platform marks Flycast as its default emulator   " + (defaultOk ? "OK" : "FAIL"));

            bool cmdOk = !string.IsNullOrWhiteSpace(emu.CommandLine);
            Console.WriteLine("  an empty command line was filled   " + (cmdOk ? "OK" : "FAIL"));
            return ok && defaultOk && cmdOk;
        }

        /// <summary>An entry the user already shaped is COMPLETED, never rewritten - and an entry
        /// with a row being typed is not touched at all.
        ///
        /// The blank-row case is the measured defect, not a hypothetical. While the user adds a row in
        /// the Associated Platforms grid, the row exists before its name is committed to the object; a
        /// check that only skipped the NAMES it recognised saw one empty name, concluded there were no
        /// platforms, and added all four beside the one being typed. The user got two Sega Dreamcast
        /// rows.</summary>
        private static bool ExistingRowsLeftAlone(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            bool ok = true;

            // (a) a row the user has filled in
            var shaped = new StubEmulator
            {
                Title = "Flycast (arcade)",
                ApplicationPath = exe,
                CommandLine = "-config window:fullscreen=no",
            };
            var chosen = shaped.AddNewEmulatorPlatform();
            chosen.Platform = "Sega Naomi";
            chosen.IsDefault = true;

            plugin.GetApplicableEmulators(new[] { shaped });

            var after = shaped.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            var naomi = after.FirstOrDefault(p => p.Platform == "Sega Naomi");
            bool keptTheirs = naomi != null && naomi.IsDefault;
            bool completed = after.Length == 4;
            bool keptCommandLine = shaped.CommandLine == "-config window:fullscreen=no";
            Console.WriteLine("  a filled row : " + string.Join(", ", after.Select(p => p.Platform + (p.IsDefault ? "*" : ""))));
            Console.WriteLine("  the platform they chose keeps its tick       " + (keptTheirs ? "OK" : "FAIL"));
            Console.WriteLine("  the three Flycast also runs were added       " + (completed ? "OK" : "FAIL"));
            Console.WriteLine("  their command line is left untouched         " + (keptCommandLine ? "OK" : "FAIL"));
            ok &= keptTheirs && completed && keptCommandLine;

            // (a1b) DefaultPlatform must be left EMPTY, and a value we wrote before must be cleared.
            // Measured on a real library: the Edit Emulator window turns this field into an extra
            // platform row on top of the association that already exists, every time it opens, and
            // saves the duplicate if the user clicks OK. The three emulators carrying the field were
            // the only three with duplicates; every emulator with it empty was clean.
            var withDefault = new StubEmulator
            {
                Title = "Flycast (with default)",
                ApplicationPath = exe,
                DefaultPlatform = "Sega Dreamcast",
            };
            var ownChoice = new StubEmulator
            {
                Title = "Flycast (user's choice)",
                ApplicationPath = exe,
                DefaultPlatform = "Sega Naomi 2",
            };
            plugin.GetApplicableEmulators(new IEmulator[] { withDefault, ownChoice });

            bool cleared = string.IsNullOrEmpty(withDefault.DefaultPlatform);
            bool keptChoice = ownChoice.DefaultPlatform == "Sega Naomi 2";
            Console.WriteLine("  the DefaultPlatform we used to write is cleared " + (cleared ? "OK" : "FAIL"));
            Console.WriteLine("  another value is somebody's choice, and stays   " + (keptChoice ? "OK" : "FAIL"));
            ok &= cleared && keptChoice;

            // The library's platforms, needed from here on: several checks only mean something when
            // the plugin can tell a name this library HAS from one it does not.
            StubDataManager.Platforms = new IPlatform[]
            {
                StubGame.Platform("Sega Dreamcast"), StubGame.Platform("Sega Naomi"),
                StubGame.Platform("Sega Naomi 2"), StubGame.Platform("Sammy Atomiswave"),
            };

            // (a2) A PLATFORM ASSOCIATED TWICE is reported and LEFT ALONE. Nothing in this plugin
            // deletes a platform row: an earlier version collapsed the surplus, never caught anything
            // on a real library, and would have hidden the symptom that led to the real cause
            // (DefaultPlatform). The assertion is that the rows survive, both of them.
            var doubled = new StubEmulator { Title = "Flycast (doubled)", ApplicationPath = exe };
            var firstRow = doubled.AddNewEmulatorPlatform();
            firstRow.Platform = "Sega Dreamcast";
            var secondRow = doubled.AddNewEmulatorPlatform();
            secondRow.Platform = "Sega Dreamcast";
            secondRow.IsDefault = true;

            plugin.GetApplicableEmulators(new IEmulator[] { doubled });
            plugin.GetApplicableEmulators(new IEmulator[] { doubled });   // and again, on reopen

            var afterDup = doubled.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            int dreamcasts = afterDup.Count(p => p.Platform == "Sega Dreamcast");
            Console.WriteLine("  a platform associated twice : "
                              + string.Join(", ", afterDup.Select(p => p.Platform + (p.IsDefault ? "*" : ""))));
            Console.WriteLine("  both rows survive, we delete nothing        " + (dreamcasts == 2 ? "OK" : "FAIL"));
            ok &= dreamcasts == 2;

            // (a4) A MISTYPED platform name, which is what actually happened: "Sega Dreamcaxst" is one
            // character from the real thing and renders in the grid as an identical-looking duplicate
            // row. It must be left exactly where it is - it may be a platform about to be created -
            // and it must be named in the log, because the eye cannot tell it apart.
            StubDataManager.Platforms = new IPlatform[]
            {
                StubGame.Platform("Sega Dreamcast"), StubGame.Platform("Sega Naomi"),
                StubGame.Platform("Sega Naomi 2"), StubGame.Platform("Sammy Atomiswave"),
            };
            var typo = new StubEmulator { Title = "Flycast (typo)", ApplicationPath = exe };
            var good = typo.AddNewEmulatorPlatform(); good.Platform = "Sega Dreamcast"; good.IsDefault = true;
            var wrong = typo.AddNewEmulatorPlatform(); wrong.Platform = "Sega Dreamcaxst"; wrong.IsDefault = true;

            plugin.GetApplicableEmulators(new[] { typo });

            var typoRows = typo.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            bool keptTypo = typoRows.Any(p => p.Platform == "Sega Dreamcaxst");
            bool untickedTypo = !typoRows.Any(p => p.Platform == "Sega Dreamcaxst" && p.IsDefault);
            Console.WriteLine("  a mistyped platform name : "
                              + string.Join(", ", typoRows.Select(p => p.Platform + (p.IsDefault ? "*" : ""))));
            // A platform Flycast runs that the library simply does not have yet is the ordinary
            // state of most libraries, and must never be called a typo. Asserted because the first
            // version of this said "Sammy Atomiswave is one slip away from Sammy Atomiswave".
            var absent = new StubEmulator { Title = "Flycast (absent platform)", ApplicationPath = exe };
            var absentRow = absent.AddNewEmulatorPlatform(); absentRow.Platform = "Sega Naomi";
            StubDataManager.Platforms = new IPlatform[] { StubGame.Platform("Sega Dreamcast") };

            var logBefore = LogLength();
            plugin.GetApplicableEmulators(new[] { absent });
            var said = LogSince(logBefore);

            StubDataManager.Platforms = new IPlatform[]
            {
                StubGame.Platform("Sega Dreamcast"), StubGame.Platform("Sega Naomi"),
                StubGame.Platform("Sega Naomi 2"), StubGame.Platform("Sammy Atomiswave"),
            };
            bool quiet = said.IndexOf("Sega Naomi\", which this library does not have",
                                      StringComparison.Ordinal) < 0;
            Console.WriteLine("  a platform we run is never called a typo    " + (quiet ? "OK" : "FAIL"));
            ok &= quiet;

            Console.WriteLine("  it is NOT removed, it may be deliberate     " + (keptTypo ? "OK" : "FAIL"));
            Console.WriteLine("  but it loses \"default emulator\"             " + (untickedTypo ? "OK" : "FAIL"));
            ok &= keptTypo && untickedTypo;

            // (b) THE bug: a row that exists but has no name yet, as the grid leaves it mid-edit
            var editing = new StubEmulator { Title = "Flycast (being edited)", ApplicationPath = exe };
            var blank = editing.AddNewEmulatorPlatform();
            blank.Platform = "";

            plugin.GetApplicableEmulators(new[] { editing });

            var rows = editing.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
            bool noDuplicate = rows.Length == 1;
            Console.WriteLine("  a BLANK row, as the grid leaves it mid-edit : " + rows.Length + " row(s)");
            Console.WriteLine("  nothing is added beside a row being typed   " + (noDuplicate ? "OK" : "FAIL"));
            ok &= noDuplicate;

            return ok;
        }


        /// <summary>A disc with no IP.BIN must yield no VMU row. Without this the listing assertion
        /// could pass on a plugin that hands out a save for anything at all.</summary>
        private static bool NoIdNoSave(EmulatorPlugin plugin, string exe, string romDir)
        {
            Console.WriteLine();
            var emulator = new StubEmulator { Title = "Flycast", ApplicationPath = exe };
            var game = StubGame.Create(Guid.NewGuid().ToString(), "Not A Disc",
                                       Path.Combine(romDir, "Not A Disc.gdi"), emulator.Id);

            var response = plugin.GetSaves(new GetSavesArgs { Emulator = emulator, Games = new[] { game } });
            var all = (response?.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>()).ToList();
            bool anyVmu = all.Any(s => (s.SaveGroupId ?? "").StartsWith("flycast-vmu:", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("  a disc with no IP.BIN yields no VMU row   " + (anyVmu ? "FAIL" : "OK"));
            return !anyVmu;
        }
    }
}
