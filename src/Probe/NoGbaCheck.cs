// What the no$gba plugin has to get right, checked against a forged installation.
//
// The plugin exists for one reason, and the first assertion here is it: no$gba writes its cartridge
// saves COMPRESSED by default, in a container only it can read, and one line of NO$GBA.INI makes
// them plain battery images. If that line stops being written, nothing visible breaks - the games
// still run, the saves still appear in the listing - and every save the user makes from then on is
// quietly unusable anywhere else. That is exactly the kind of defect a harness is for.
//
// The rest is about not breaking somebody's configuration: no$gba's INI holds fifty-odd settings
// this plugin knows nothing about, and writing one key must leave the other fifty character for
// character where they were.
//
// EVERYTHING HERE IS FORGED, under %TEMP%, and deleted afterwards. No emulator is downloaded and
// none is run.

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
    internal static class NoGbaCheck
    {
        private const string SaveFormatLine = "SAV/SNA File Format";
        private const string Game = "Mario Kart DS (Europe) (En,Fr,De,Es,It)";

        private static int _failures;

        public static bool Run(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            Console.WriteLine("-- no$gba, on a FORGED install  [WRITES, in the temp folder] " + new string('-', 3));

            _failures = 0;
            string root = Path.Combine(Path.GetTempPath(), "lbip-nogba-" + Guid.NewGuid().ToString("N"));
            var previous = PluginHelper.DataManager;
            try
            {
                var (exe, romDir) = Forge(root);
                Console.WriteLine("  install : " + Path.GetDirectoryName(exe));
                Console.WriteLine("  roms    : " + romDir);

                PluginHelper.DataManager = new StubDataManager(
                    new StubEmulator { Title = "no$gba", ApplicationPath = exe });

                bool ok = true;
                ok &= Platforms(plugin);
                ok &= Claiming(plugin, exe);
                ok &= TheSaveFormat(exe);
                ok &= ForeignSettings(exe);
                ok &= BiosCopying(exe);
                ok &= Listing(plugin, exe, romDir);
                ok &= TheDsiSwitches(exe);
                ok &= TheirOwnImage(exe);

                Console.WriteLine();
                Console.WriteLine("  " + (ok ? "OK - the plugin matches what no$gba was measured to do"
                                             : "NOT OK - see above"));
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION: " + ex);
                return false;
            }
            finally
            {
                PluginHelper.DataManager = previous;
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            }
        }

        // ── the fixture ──────────────────────────────────────────────────────

        private static (string exe, string romDir) Forge(string root)
        {
            string install = Path.Combine(root, "no$gba");
            string romDir = Path.Combine(root, "roms");
            // The shared BIOS folder, where it really is relative to the emulator: a sibling of the
            // install, under RetroArch\system.
            string shared = Path.Combine(root, "RetroArch", "system");
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(romDir);
            Directory.CreateDirectory(shared);

            string exe = Path.Combine(install, "NO$GBA.EXE");
            File.WriteAllBytes(exe, Array.Empty<byte>());

            // Two BIOS files out of four, so the check can tell "copied what was there" from
            // "invented what was not". Right sizes, so no amber badge muddies the result.
            File.WriteAllBytes(Path.Combine(shared, "biosnds7.bin"), new byte[16 * 1024]);
            File.WriteAllBytes(Path.Combine(shared, "gba_bios.bin"), new byte[16 * 1024]);

            // A ROM in a zip, because that is how a LaunchBox library usually holds one, and because
            // the save is named after the entry INSIDE it.
            string archive = Path.Combine(romDir, Game + ".zip");
            using (var zip = new System.IO.Compression.ZipArchive(
                       File.Create(archive), System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(Game + ".nds");
                using var stream = entry.Open();
                stream.Write(new byte[4096], 0, 4096);
            }

            return (exe, romDir);
        }

        // ── the checks ───────────────────────────────────────────────────────

        /// <summary>The two settings that put no$gba into DSi mode, and the fact that they are
        /// GLOBAL.
        ///
        /// MEASURED, 2026-09-24, against the real emulator: with these two written and nothing else,
        /// no$gba booted the DSi menu out of an eMMC image this repository's tooling had prepared -
        /// the console showed the name set on the melonDS side. The values are the drop-down's own
        /// labels, read off Options > Emulation Setup, because a value no$gba does not recognise is
        /// ignored IN SILENCE: there is no error, no fallback, and no way to tell from the file.
        ///
        /// THERE IS NO COMMAND LINE. no$gba takes no switches, so these cannot be set per launch the
        /// way melonDS's console type is - they are written into the INI before the emulator starts
        /// and written back afterwards. A Game Boy Advance game must not be left booting through a
        /// BIOS it has no use for.</summary>
        private static bool TheDsiSwitches(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the two settings that make no$gba a DSi");

            var dsi = TypeIn("NoGbaDsi");
            var setMode = dsi?.GetMethod("SetMode", BindingFlags.Public | BindingFlags.Static);
            var resolve = TypeIn("NoGbaPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            if (setMode == null) { Console.WriteLine("    no NoGbaDsi.SetMode to call"); return false; }

            var layout = resolve.Invoke(null, new object[] { exe });
            string ini = Path.Combine(Path.GetDirectoryName(exe), "NO$GBA.INI");

            // Something of the user's, to prove it survives both ways round.
            File.WriteAllText(ini, ";no$gba generated config file - do not edit\r\n"
                                 + "KEYB_1 == 101E1819390F1D1C38912D2C\r\n");

            bool ok = true;

            setMode.Invoke(null, new object[] { layout, true });
            var after = File.ReadAllText(ini);
            ok &= Check("DSi mode is the drop-down's own label, character for character",
                        after.Contains("NDS Mode/Colors == DSi (retail/16MB)"));
            ok &= Check("and the boot entrypoint goes through the BIOS, which is what reads the eMMC",
                        after.Contains("Reset/Startup Entrypoint == GBA/NDS BIOS (Nintendo logo)"));
            ok &= Check("the user's key mapping is untouched",
                        after.Contains("KEYB_1 == 101E1819390F1D1C38912D2C"));

            // AND BACK, which is the half that is easy to forget: these are global.
            setMode.Invoke(null, new object[] { layout, false });
            var back = File.ReadAllText(ini);
            ok &= Check("a non-DSi launch puts the machine back to a DS",
                        back.Contains("NDS Mode/Colors == Nintendo DS (retail/4MB)"));
            ok &= Check("and back to booting the cartridge directly",
                        back.Contains("Reset/Startup Entrypoint == Start Cartridge directly"));
            ok &= Check("the settings are rewritten in place, not appended twice",
                        Occurrences(back, "NDS Mode/Colors ==") == 1
                        && Occurrences(back, "Reset/Startup Entrypoint ==") == 1);

            try { File.Delete(ini); } catch { }
            return ok;
        }

        /// <summary>A DSi-1.mmc that is not ours is set aside, never built over.
        ///
        /// melonDS never faces this: its working image lives in a folder this plugin owns, so the
        /// PATH answers "is this mine". no$gba reads a fixed name beside its own executable and will
        /// not be told otherwise, so the path says nothing - and somebody who set a DSi up by hand
        /// has their own file sitting exactly there. The marker beside the working image answers
        /// instead: work.title is written whenever WE build one, so an image with no marker was not
        /// built by us.</summary>
        private static bool TheirOwnImage(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- a DSi-1.mmc somebody else put there");

            var protect = TypeIn("NoGbaDsi")?.GetMethod("ProtectTheirImage",
                              BindingFlags.NonPublic | BindingFlags.Static);
            var resolve = TypeIn("NoGbaPaths").GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            if (protect == null) { Console.WriteLine("    no ProtectTheirImage to call"); return false; }

            var layout = resolve.Invoke(null, new object[] { exe });
            string install = Path.GetDirectoryName(exe);
            string mmc = Path.Combine(install, "DSi-1.mmc");
            string marker = Path.Combine(install, "dsi", "work.title");

            bool ok = true;
            try
            {
                // Theirs: an image with no marker beside it.
                try { File.Delete(marker); } catch { }
                File.WriteAllText(mmc, "the console somebody set up by hand");

                ok &= Check("the launch is allowed to go on", (bool)protect.Invoke(null, new[] { layout }));
                ok &= Check("but their image is NOT where it was", !File.Exists(mmc));

                var parked = Path.Combine(install, "DSi-1.mmc.yours");
                ok &= Check("it was set aside under a name that says whose it is", File.Exists(parked));
                ok &= Check("with its contents intact - moved, not copied and not emptied",
                            File.Exists(parked)
                            && File.ReadAllText(parked) == "the console somebody set up by hand");

                // Ours: a marker says we built it, so it is left alone to be built over.
                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                File.WriteAllText(marker, "000300044b513945\tsomewhere\t1\t2\tconsole.bin");
                File.WriteAllText(mmc, "our own working image");

                ok &= Check("an image we made is left where it is", (bool)protect.Invoke(null, new[] { layout }));
                ok &= Check("because the marker beside it says so, which is the only thing that can",
                            File.Exists(mmc) && File.ReadAllText(mmc) == "our own working image");
                return ok;
            }
            finally
            {
                foreach (var leftover in new[] { mmc, Path.Combine(install, "DSi-1.mmc.yours") })
                    try { File.Delete(leftover); } catch { }
                try { Directory.Delete(Path.Combine(install, "dsi"), recursive: true); } catch { }
            }
        }

        /// <summary>How many times a needle appears.</summary>
        private static int Occurrences(string haystack, string needle)
        {
            int n = 0, at = 0;
            while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
            return n;
        }

        private static bool Platforms(EmulatorPlugin plugin)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the two platforms it emulates");

            bool ok = true;
            ok &= Check("Game Boy Advance is supported",
                        plugin.IsPlatformSupported("Nintendo Game Boy Advance")?.Supported == true);
            ok &= Check("Nintendo DS is supported",
                        plugin.IsPlatformSupported("Nintendo DS")?.Supported == true);
            ok &= Check("and DSiWare, which it runs out of a NAND rather than off a cartridge",
                        plugin.IsPlatformSupported("Nintendo DSiware")?.Supported == true);
            ok &= Check("and nothing else is",
                        plugin.IsPlatformSupported("Nintendo 64")?.Supported != true);
            return ok;
        }

        private static bool Claiming(EmulatorPlugin plugin, string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- claiming the emulator, and the one setting it forces");

            var mine = new StubEmulator { Title = "no$gba", ApplicationPath = exe };
            var theirs = new StubEmulator { Title = "melonDS", ApplicationPath = "C:\\melonDS\\melonDS.exe" };
            var claimed = plugin.GetApplicableEmulators(new IEmulator[] { mine, theirs })?.ToList()
                          ?? new List<IEmulator>();

            bool ok = true;
            ok &= Check("exactly one of the two is claimed", claimed.Count == 1);
            ok &= Check("and it is the no$gba one",
                        claimed.Count == 1 && ReferenceEquals(claimed[0], mine));

            // THE ONE USER SETTING THIS PLUGIN OVERRIDES. no$gba cannot open an archive: handed one
            // it shows a modal "Cartridge not found" and waits, which from a frontend is a hung
            // launch rather than a failed one. There is no configuration in which leaving this off
            // is what somebody wanted.
            ok &= Check("Auto-Extract is turned on - an archive would hang the launch otherwise",
                        mine.AutoExtract);
            ok &= Check("the exit script sends Escape, which is no$gba's own Quit key",
                        (mine.ExitAutoHotkeyScript ?? "").Contains("{Esc down}"));
            ok &= Check("the save-state script sends F8, as its File menu says",
                        (mine.SaveStateAutoHotkeyScript ?? "").Contains("{F8 down}"));
            ok &= Check("and the load-state script sends F7",
                        (mine.LoadStateAutoHotkeyScript ?? "").Contains("{F7 down}"));

            // A script the user wrote is his answer to the same question.
            var custom = new StubEmulator
            {
                Title = "no$gba",
                ApplicationPath = exe,
                ExitAutoHotkeyScript = "; mine",
            };
            plugin.GetApplicableEmulators(new IEmulator[] { custom });
            ok &= Check("a script the user wrote is left alone", custom.ExitAutoHotkeyScript == "; mine");
            return ok;
        }

        private static bool TheSaveFormat(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the setting the whole plugin exists for");

            var ini = Path.Combine(Path.GetDirectoryName(exe), "NO$GBA.INI");
            try { File.Delete(ini); } catch { }

            bool ok = true;

            // no$gba never writes an INI by itself - not at first run, not on exit - so the plugin
            // has to be able to create one from nothing.
            Apply(exe);
            ok &= Check("an INI is created where there was none", File.Exists(ini));
            ok &= Check("and it asks for raw saves",
                        Value(ini, SaveFormatLine) == "Raw");

            // The trap this encodes: no$gba accepts the EXACT label of the drop-down and silently
            // ignores anything else. "Raw" is right; a plugin that wrote "raw " or "Uncompressed"
            // would change nothing and report success.
            ok &= Check("spelled exactly as the emulator's menu spells it",
                        File.ReadAllText(ini).Contains(SaveFormatLine + " == Raw"));

            var before = File.ReadAllBytes(ini);
            Apply(exe);
            ok &= Check("a second pass leaves the file byte for byte identical",
                        File.ReadAllBytes(ini).SequenceEqual(before));

            // Somebody opened Options > Save Options, which rewrites the whole file from the running
            // configuration and reverts us. The next launch has to notice.
            File.WriteAllText(ini, ";no$gba 3.0 generated config file - do not edit\r\n"
                                   + SaveFormatLine + " == Compressed\r\n");
            Apply(exe);
            ok &= Check("a reverted setting is set again", Value(ini, SaveFormatLine) == "Raw");
            return ok;
        }

        private static bool ForeignSettings(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the fifty settings this plugin knows nothing about");

            var ini = Path.Combine(Path.GetDirectoryName(exe), "NO$GBA.INI");
            var original =
                ";no$gba 3.0 generated config file - do not edit\r\n"
                + "\r\n"
                + "GBA Mode/Colors == GBA SP (backlight)\r\n"
                + "Reset/Startup Entrypoint == GBA/NDS BIOS (Nintendo logo)\r\n"
                + SaveFormatLine + " == Compressed\r\n"
                + "KEYB_1 == 101E1819390F1D1C38912D2C\r\n"
                + "Mixer_vol == 0000BFFF\r\n";
            File.WriteAllText(ini, original);

            Apply(exe);
            var after = File.ReadAllText(ini);

            bool ok = true;
            ok &= Check("the \"do not edit\" banner survives",
                        after.Contains(";no$gba 3.0 generated config file - do not edit"));
            ok &= Check("a setting we do not know is untouched",
                        after.Contains("GBA Mode/Colors == GBA SP (backlight)"));
            ok &= Check("a value containing a parenthesis survives whole",
                        after.Contains("Reset/Startup Entrypoint == GBA/NDS BIOS (Nintendo logo)"));
            ok &= Check("the key mapping is not mangled",
                        after.Contains("KEYB_1 == 101E1819390F1D1C38912D2C"));
            ok &= Check("our key is rewritten in place, not appended",
                        after.IndexOf(SaveFormatLine, StringComparison.Ordinal)
                        < after.IndexOf("KEYB_1", StringComparison.Ordinal));
            ok &= Check("and it now says Raw", Value(ini, SaveFormatLine) == "Raw");
            ok &= Check("nothing was lost - same number of settings",
                        Settings(original) == Settings(after));
            return ok;
        }

        private static bool BiosCopying(string exe)
        {
            Console.WriteLine();
            Console.WriteLine("  -- the BIOS files, which no$gba can only read from its own folder");

            var install = Path.GetDirectoryName(exe);
            Sync(exe);

            bool ok = true;
            ok &= Check("a DS ARM7 BIOS in RetroArch\\system is copied as BIOSNDS7.ROM",
                        File.Exists(Path.Combine(install, "BIOSNDS7.ROM")));
            ok &= Check("and RetroArch's own gba_bios.bin name is recognised too",
                        File.Exists(Path.Combine(install, "BIOSGBA.ROM")));
            ok &= Check("a file nobody has is NOT invented",
                        !File.Exists(Path.Combine(install, "BIOSNDS9.ROM"))
                        && !File.Exists(Path.Combine(install, "FIRMWARE.BIN")));

            var stamp = File.GetLastWriteTimeUtc(Path.Combine(install, "BIOSNDS7.ROM"));
            System.Threading.Thread.Sleep(15);
            Sync(exe);
            ok &= Check("a second pass copies nothing again",
                        File.GetLastWriteTimeUtc(Path.Combine(install, "BIOSNDS7.ROM")) == stamp);
            return ok;
        }

        private static bool Listing(EmulatorPlugin plugin, string exe, string romDir)
        {
            Console.WriteLine();
            Console.WriteLine("  -- listing a save");

            var battery = Path.Combine(Path.GetDirectoryName(exe), "BATTERY");
            Directory.CreateDirectory(battery);
            // 256 KB exactly, the shape a raw DS save has. Named after the entry INSIDE the zip,
            // which is what no$gba sees once the host has unpacked it.
            File.WriteAllBytes(Path.Combine(battery, Game + ".SAV"), new byte[262144]);
            // A save belonging to nobody in the library, to be sure the listing is driven by the
            // games it was given and not by whatever is lying in the folder.
            File.WriteAllBytes(Path.Combine(battery, "Somebody Else.SAV"), new byte[512]);

            var game = StubGame.Create("g1", Game, Path.Combine(romDir, Game + ".zip"), "e1");
            var response = plugin.GetSaves(new GetSavesArgs
            {
                Emulator = new StubEmulator { Title = "no$gba", ApplicationPath = exe },
                Games = new[] { game },
            });

            var all = (response?.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>())
                      .ToList();

            bool ok = true;
            ok &= Check("exactly one save is listed", all.Count == 1);
            if (all.Count != 1)
            {
                foreach (var s in all) Console.WriteLine("      " + s.FileLocation);
                return false;
            }

            var save = all[0];
            ok &= Check("it is the one named after the ROM INSIDE the zip, not after the zip",
                        Path.GetFileName(save.FileLocation) == Game + ".SAV");
            ok &= Check("it is a file, not a container", !plugin.IsSaveContainer(save));
            ok &= Check("and it has no companions",
                        (plugin.GetCompanionSaveFiles(save.FileLocation)?.Count ?? 0) == 0);
            ok &= Check("nothing is a secondary file", !plugin.IsSecondarySaveFile(save.FileLocation));
            ok &= Check("it is active where it is", plugin.IsSaveActive(save, exe));

            // Saying a save is a container is a promise the host takes literally: it makes a
            // destination folder, asks for an extraction, and records a backup from what appears.
            // A one-file save must never make that promise.
            ok &= Check("it does not pretend to be extractable",
                        !plugin.TryBackupSave(save, exe, Path.Combine(romDir, "vault"), out _));
            return ok;
        }

        // ── reaching into the plugin ─────────────────────────────────────────

        private static void Apply(string exe)
        {
            var layout = Layout(exe);
            TypeIn("NoGbaConfig")
                .GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { layout });
        }

        private static void Sync(string exe)
        {
            var layout = Layout(exe);
            TypeIn("NoGbaBios")
                .GetMethod("Sync", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { layout });
        }

        private static object Layout(string exe)
            => TypeIn("NoGbaPaths")
               .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)
               .Invoke(null, new object[] { exe });

        private static Assembly _anchor;

        /// <summary>A type from the plugin's own assembly, by simple name. The plugin is loaded from
        /// a path the harness was given, so its types cannot be named at compile time.</summary>
        private static Type TypeIn(string simpleName)
        {
            _anchor ??= AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => Safe(a).Any(t => t.Name == "NoGbaPlugin"));
            return _anchor == null
                ? null
                : Safe(_anchor).FirstOrDefault(t => t.Name == simpleName);
        }

        /// <summary>GetTypes throws on an assembly whose references cannot all be resolved, which is
        /// routine for the ones a host has loaded. Only the plugin's own has to be readable.</summary>
        private static IEnumerable<Type> Safe(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }

        // ── small things ─────────────────────────────────────────────────────

        private static string Value(string ini, string key)
        {
            try
            {
                foreach (var line in File.ReadAllLines(ini))
                {
                    var at = line.IndexOf("==", StringComparison.Ordinal);
                    if (at <= 0) continue;
                    if (line.Substring(0, at).Trim() != key) continue;
                    return line.Substring(at + 2).Trim();
                }
            }
            catch { }
            return null;
        }

        private static int Settings(string text)
        {
            int n = 0;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith(";", StringComparison.Ordinal)) continue;
                if (t.IndexOf("==", StringComparison.Ordinal) > 0) n++;
            }
            return n;
        }

        private static bool Check(string what, bool ok)
        {
            if (!ok) _failures++;
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what);
            return ok;
        }
    }
}
