// A host stand-in for exercising an integration plugin without LaunchBox or LiteBox.
//
// It loads a plugin DLL, finds its EmulatorPlugin subclass, and calls the read-only half of the
// contract against inputs you give it, printing what comes back. That is the difference between
// "it compiles" and "it answers correctly", and it costs seconds instead of a host restart — which
// matters because plugins are loaded once at start-up, so every change otherwise means closing the
// frontend the user may be in the middle of using.
//
// It calls nothing that writes: no InstallEmulator, no InjectRetroAchievementsCredentials. Those
// change the user's disk and are exercised deliberately, against a throwaway emulator copy.
//
//   dotnet run --project src\Probe -- <plugin.dll> [--emu <path to emulator exe>] [--platform <name>]

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0].StartsWith("-"))
            {
                Console.Error.WriteLine("usage: Probe <plugin.dll> [--emu <emulator.exe>] [--platform <name>] [--states --rom <rom>] [--flycast] [--melonds] [--melonds-real ...] [--rows] [--hotkeys] [--saves --emu <exe> --rom <rom>] [--ahk --emu <exe>]");
                return 2;
            }

            string dll = Path.GetFullPath(args[0]);
            string emuPath = Arg(args, "--emu");
            string platform = Arg(args, "--platform") ?? "Sony PSP";

            if (!File.Exists(dll)) { Console.Error.WriteLine("no such file: " + dll); return 2; }

            // Resolve the plugin's dependencies from its own folder, the way a host does.
            var probeDir = Path.GetDirectoryName(dll);
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var candidate = Path.Combine(probeDir, name.Name + ".dll");
                return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
            };

            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            var type = asm.GetTypes().FirstOrDefault(t => !t.IsAbstract && typeof(EmulatorPlugin).IsAssignableFrom(t));
            if (type == null)
            {
                Console.Error.WriteLine("no EmulatorPlugin subclass in " + Path.GetFileName(dll));
                return 1;
            }

            var plugin = (EmulatorPlugin)Activator.CreateInstance(type);
            Console.WriteLine("plugin      : " + type.FullName + "  [" + Path.GetFileName(dll) + "]");
            Console.WriteLine("EmulatorName: " + Safe(() => plugin.EmulatorName));
            Console.WriteLine();

            Section("IsPlatformSupported(\"" + platform + "\")");
            var support = Safe(() => plugin.IsPlatformSupported(platform));
            Console.WriteLine(support == null ? "  threw" : $"  supported={support.Supported} recommended={support.Recommended}");
            var noSupport = Safe(() => plugin.IsPlatformSupported("Nintendo 64"));
            Console.WriteLine(noSupport == null ? "  threw" : $"  (control) \"Nintendo 64\" -> supported={noSupport.Supported}");

            Section("GetApplicableEmulators");
            if (emuPath == null) Console.WriteLine("  skipped (pass --emu <emulator.exe>)");
            else
            {
                var mine = new StubEmulator { Title = Safe(() => plugin.EmulatorName) ?? "ours", ApplicationPath = emuPath };
                var other = new StubEmulator { Title = "Some other emulator", ApplicationPath = @"C:\emus\retroarch.exe" };
                var claimed = Safe(() => plugin.GetApplicableEmulators(new IEmulator[] { mine, other }));
                if (claimed == null) Console.WriteLine("  threw");
                else
                {
                    var list = claimed.ToList();
                    Console.WriteLine("  claimed " + list.Count + " of 2: "
                                      + string.Join(", ", list.Select(e => "\"" + e.Title + "\"")));
                    Console.WriteLine("  " + (list.Count == 1 && ReferenceEquals(list[0], mine)
                        ? "OK - claimed ours and refused the other"
                        : "UNEXPECTED - should claim exactly its own entry"));
                }
            }

            Section("GetCurrentVersion");
            Console.WriteLine(emuPath == null
                ? "  skipped (pass --emu <emulator.exe>)"
                : "  " + (Safe(() => plugin.GetCurrentVersion(emuPath)) ?? "(null)"));

            Section("GetBiosFilesForPlatform(\"" + platform + "\")");
            var bios = Safe(() => plugin.GetBiosFilesForPlatform(platform));
            if (bios == null) Console.WriteLine("  threw");
            else
            {
                var list = bios.ToList();
                Console.WriteLine("  " + list.Count + " file(s)");
                foreach (var b in list)
                    Console.WriteLine($"    {b.FileName}  required={b.Required}  md5={b.Md5}  {b.Description}");
            }

            Section("SupportsSaveManagement");
            Console.WriteLine("  " + Safe(() => (object)plugin.SupportsSaveManagement()));

            Section("SupportsRetroAchievements");
            if (emuPath == null) Console.WriteLine("  skipped (pass --emu <emulator.exe>)");
            else
            {
                var ra = Safe(() => plugin.SupportsRetroAchievements(emuPath));
                Console.WriteLine(ra == null
                    ? "  threw"
                    : $"  supported={ra.IsSupported} enabled={ra.IsEnabled} hardcore={ra.IsHardcore} "
                      + $"user=\"{ra.CurrentUsername}\" "
                      // Never print the token itself, only whether one is there.
                      + $"token={(ra.CurrentToken == null ? "(none)" : "(present)")}");
            }

            Section("GetInstallableVersions  [hits the network]");
            var versions = Safe(() => plugin.GetInstallableVersions());
            if (versions == null) Console.WriteLine("  null - see the log lines above");
            else
                foreach (var v in versions.ToList())
                    Console.WriteLine($"    {v.Label,-12} {v.Description}\n                 {v.Identifier}");

            Section("PrepareEmulatorForLaunch");
            if (emuPath == null) Console.WriteLine("  skipped (pass --emu <emulator.exe>)");
            else
            {
                // What the host hands over right before the spawn. A plugin may rewrite the command
                // line here; most leave it alone, and either is worth seeing.
                var emu = new StubEmulator { Title = Safe(() => plugin.EmulatorName), ApplicationPath = emuPath };
                const string launchCmd = "--fullscreen";
                var prepared = Safe(() => plugin.PrepareEmulatorForLaunch(
                    new PrepareForLaunchArgs(emu, null, launchCmd)));
                if (prepared == null) Console.WriteLine("  threw");
                else
                {
                    Console.WriteLine($"  WasSuccess={prepared.WasSuccess}");
                    Console.WriteLine("  \"" + launchCmd + "\"");
                    Console.WriteLine("    -> \"" + (prepared.NewCommandLine ?? launchCmd) + "\""
                                      + (prepared.NewCommandLine == null ? "   (unchanged)" : "   (REWRITTEN)"));
                    Console.WriteLine("  " + (prepared.WasSuccess
                        ? "OK - a launch is never blocked here"
                        : "UNEXPECTED - this must not fail a launch"));
                }
            }

            Section("NormalizeCommandLineForExecutable");
            if (emuPath == null) Console.WriteLine("  skipped (pass --emu <emulator.exe>)");
            else
            {
                const string cmd = "--fullscreen --pause-menu-exit";
                var normalized = Safe(() => plugin.NormalizeCommandLineForExecutable(cmd, emuPath));
                Console.WriteLine("  \"" + cmd + "\"  ->  \"" + normalized + "\""
                                  + (cmd == normalized ? "   (unchanged)" : "   (REWRITTEN)"));
            }

            // The one WRITING test, behind an explicit flag: it edits the emulator's configuration and
            // drops a credentials file. Point it at a throwaway install and a throwaway account.
            if (Has(args, "--inject-ra-test"))
            {
                Section("InjectRetroAchievementsCredentials  [WRITES to " + emuPath + "]");
                if (emuPath == null) Console.WriteLine("  needs --emu");
                else
                {
                    const string user = "probe-user";
                    const string token = "probe-token-not-a-real-credential";
                    var wrote = Safe(() => plugin.InjectRetroAchievementsCredentials(
                        new InjectRetroAchievementsCredentialsArgs(emuPath, user, token, enable: true, hardcore: false)));
                    Console.WriteLine(wrote == null
                        ? "  threw"
                        : $"  WasSuccess={wrote.WasSuccess}  {wrote.Message}");

                    var after = Safe(() => plugin.SupportsRetroAchievements(emuPath));
                    Console.WriteLine(after == null
                        ? "  read-back threw"
                        : $"  read back: enabled={after.IsEnabled} hardcore={after.IsHardcore} "
                          + $"user=\"{after.CurrentUsername}\" token={(after.CurrentToken == null ? "(none)" : "(present)")}");

                    // Only judge the round-trip when the write claimed to succeed. Reading back values
                    // an EARLIER run left behind and calling that a pass is how a test harness lies:
                    // the refusal path below is a correct outcome, not a failed round-trip.
                    if (wrote == null) { }
                    else if (!wrote.WasSuccess)
                        Console.WriteLine("  write was refused - that is a valid outcome; the values above are whatever was already on disk");
                    else
                        Console.WriteLine("  " + (after != null && after.IsEnabled && !after.IsHardcore
                                                  && after.CurrentUsername == user && after.CurrentToken != null
                            ? "OK - every field round-tripped"
                            : "UNEXPECTED - the write succeeded but the values did not come back"));
                }
            }

            // Disc id extraction, reached by reflection: an implementation detail of the plugin rather
            // than part of the SDK contract, but it decides which save belongs to which game, so being
            // able to point it at one ROM is worth the reflection.
            string discIdRom = Arg(args, "--disc-id-of");
            if (discIdRom != null)
            {
                Section("disc id of " + Path.GetFileName(discIdRom));
                var discIdType = asm.GetType("LbIntegrations.Ppsspp.PspDiscId")
                                 ?? asm.GetType("LbIntegrations.Xenia.XeniaTitleId");
                var of = discIdType?.GetMethod("Of", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Console.WriteLine(of == null
                    ? "  this plugin exposes no PspDiscId"
                    : "  " + ((string)of.Invoke(null, new object[] { discIdRom }) ?? "(none)"));
            }

            // The generic interop assertion: hand the plugin a FileLocation and a SaveGroupId, exactly
            // what LiteBox hands it, and check the shape and the hash of what comes back.
            string unit = Arg(args, "--unit");
            if (unit != null)
            {
                string groupId = Arg(args, "--group-id");
                string expect = Arg(args, "--expect-hash");
                bool roundTrip = Has(args, "--round-trip");
                if (!UnitCheck.Run(plugin, unit, groupId, expect, emuPath, roundTrip)) return 1;
            }

            // The PPSSPP-shaped assertion. Writes only into a temp folder of its own.
            string saveDataDir = Arg(args, "--save-unit");
            if (saveDataDir != null)
            {
                string discId = Arg(args, "--disc-id");
                string expected = Arg(args, "--expect-hash");
                if (!SaveUnitCheck.Run(plugin, saveDataDir, discId, expected, emuPath)) return 1;
            }

            // The save-state assertion. Reads the real install; writes only in a temp sandbox.
            bool states = Has(args, "--states");
            if (states)
            {
                string rom = Arg(args, "--rom");
                if (!StateCheck.Run(plugin, emuPath, rom)) return 1;
            }

            // The Flycast assertion. Builds its own forged install in the temp folder and cleans up.
            // What the plugin says the LIVE saves are, for one game on one emulator. The question
            // "the vault copy shows but the active save does not" has no other honest answer.
            if (Has(args, "--saves"))
            {
                var rom = Arg(args, "--rom");
                if (emuPath == null || rom == null)
                {
                    Console.Error.WriteLine("--saves needs --emu <emulator.exe> and --rom <rom>");
                    return 2;
                }
                Section("GetSaves  [" + Path.GetFileName(rom) + "]");
                var emulator = new StubEmulator { Title = "emulator", ApplicationPath = emuPath };
                var game = StubGame.Create(Guid.NewGuid().ToString(), Path.GetFileNameWithoutExtension(rom),
                                           rom, emulator.Id);
                var saves = plugin.GetSaves(new GetSavesArgs { Emulator = emulator, Games = new[] { game } });
                if (saves?.WasSuccess != true)
                {
                    Console.WriteLine("  GetSaves failed: " + (saves?.Message ?? "no reason given"));
                    return 1;
                }
                // The other half of the contract: a save the plugin then calls a "secondary file" is
                // one the host is told to ignore. Asked about each save's OWN location.
                // A PLUGIN MUST NEVER CALL ITS OWN SAVE SECONDARY. "Secondary" tells the host to
                // ignore the path, so a plugin that answers true for something it just returned in
                // GetSaves is instructing the host to drop it - measured on Xenia, whose unit IS the
                // folder named 00000001 and whose test matched any segment of that name, its own
                // last one included. The host obeyed and the game showed only its vault copies.
                var selfDenied = false;
                foreach (var one in saves.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>())
                {
                    var secondary = plugin.IsSecondarySaveFile(one.FileLocation);
                    selfDenied |= secondary;
                    Console.WriteLine("  IsSecondarySaveFile(\"" + one.FileLocation + "\") -> " + secondary
                                      + (secondary ? "   <<< the plugin disowns its own save" : "")
                                      + "      IsSaveContainer -> " + plugin.IsSaveContainer(one));
                }
                if (selfDenied)
                {
                    Console.WriteLine();
                    Console.WriteLine("  FAIL - a save returned by GetSaves is declared a secondary file");
                    return 1;
                }

                var list = saves.FoundSaves ?? (IReadOnlyCollection<GameSaveBase>)Array.Empty<GameSaveBase>();
                Console.WriteLine("  " + list.Count + " save(s)");
                foreach (var one in list)
                {
                    // EVERY property, because the interesting difference between two plugins is
                    // whichever field one of them leaves null.
                    Console.WriteLine("    " + one.GetType().Name);
                    foreach (var prop in one.GetType().GetProperties()
                                            .OrderBy(pr => pr.Name, StringComparer.Ordinal))
                    {
                        object v;
                        try { v = prop.GetValue(one); } catch (Exception ex) { v = "<" + ex.GetType().Name + ">"; }
                        Console.WriteLine("        " + prop.Name.PadRight(28)
                                          + (v == null ? "(null)" : v.ToString()));
                    }
                }
                return 0;
            }

            // The AutoHotkey fields a plugin puts on an emulator entry, and the three ways that goes
            // wrong - none of them caught by reading the code.
            if (Has(args, "--ahk"))
            {
                if (emuPath == null) { Console.Error.WriteLine("--ahk needs --emu <emulator.exe>"); return 2; }

                // --write asks the plugin to put its bindings in place first, the way installing does.
                if (Has(args, "--write"))
                {
                    var hk = plugin.GetType().Assembly.GetTypes()
                                   .FirstOrDefault(t => t.Name.EndsWith("Hotkeys", StringComparison.Ordinal));
                    var paths = plugin.GetType().Assembly.GetTypes()
                                      .FirstOrDefault(t => t.Name.EndsWith("Paths", StringComparison.Ordinal));
                    if (hk != null && paths != null)
                    {
                        var layout = paths.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)
                                          .Invoke(null, new object[] { emuPath });
                        hk.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static)
                          .Invoke(null, new[] { layout, (object)true });
                    }
                }
                Section("AutoHotkey scripts  [" + Path.GetFileName(emuPath) + "]");

                // What the plugin's own hotkey pass says it did, which is the only way to tell
                // "nothing to add" from "could not write" from "not this plugin's business".
                var hkType = plugin.GetType().Assembly.GetTypes()
                                   .FirstOrDefault(t => t.Name.EndsWith("Hotkeys", StringComparison.Ordinal));
                var pathsType = plugin.GetType().Assembly.GetTypes()
                                      .FirstOrDefault(t => t.Name.EndsWith("Paths", StringComparison.Ordinal));
                if (hkType != null && pathsType != null)
                {
                    var layout = pathsType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)
                                          .Invoke(null, new object[] { emuPath });
                    var t = hkType.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static)
                                  .Invoke(null, new[] { layout, (object)false });
                    foreach (var pr in t.GetType().GetProperties().Concat<System.Reflection.MemberInfo>(
                                        t.GetType().GetFields()).OrderBy(m => m.Name, StringComparer.Ordinal))
                    {
                        object v = null;
                        try
                        {
                            v = pr is System.Reflection.PropertyInfo pi ? pi.GetValue(t)
                              : ((System.Reflection.FieldInfo)pr).GetValue(t);
                        }
                        catch { }
                        if (v is System.Collections.IDictionary d)
                        {
                            var parts = new List<string>();
                            foreach (System.Collections.DictionaryEntry de in d)
                                parts.Add(de.Key + "=" + de.Value);
                            v = parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
                        }
                        Console.WriteLine("  hotkeys." + pr.Name.PadRight(12) + (v?.ToString() ?? "(null)"));
                    }
                }
                int bad = 0;
                void Check(string what, bool good)
                {
                    if (!good) bad++;
                    Console.WriteLine("  " + what.PadRight(50) + (good ? "OK" : "FAIL"));
                }

                var blank = new StubEmulator { Title = "emulator", ApplicationPath = emuPath };
                plugin.GetApplicableEmulators(new IEmulator[] { blank });
                foreach (var (name, value) in new[]
                         {
                             ("Running", blank.AutoHotkeyScript), ("Exit", blank.ExitAutoHotkeyScript),
                             ("SaveState", blank.SaveStateAutoHotkeyScript),
                             ("LoadState", blank.LoadStateAutoHotkeyScript),
                         })
                {
                    Console.WriteLine("  " + name + ":");
                    foreach (var line in (value ?? "(none)").Replace("\r", "").Split('\n'))
                        Console.WriteLine("      " + line);
                }

                // A SECOND object for the same emulator: the host hands us the same entry under a new
                // object every time a window asks, and remembering "this executable is done" fills the
                // first and leaves the rest empty - measured on Flycast.
                var second = new StubEmulator { Title = "emulator", ApplicationPath = emuPath };
                plugin.GetApplicableEmulators(new IEmulator[] { second });
                // Whatever the first object got, the second must get: a plugin that sets only the
                // state scripts is as valid as one that sets the exit script too.
                Check("a second object for the same emulator is filled too",
                      second.AutoHotkeyScript == blank.AutoHotkeyScript
                      && second.ExitAutoHotkeyScript == blank.ExitAutoHotkeyScript
                      && second.SaveStateAutoHotkeyScript == blank.SaveStateAutoHotkeyScript
                      && second.LoadStateAutoHotkeyScript == blank.LoadStateAutoHotkeyScript);
                Check("the plugin set at least one script",
                      new[] { blank.AutoHotkeyScript, blank.ExitAutoHotkeyScript,
                              blank.SaveStateAutoHotkeyScript, blank.LoadStateAutoHotkeyScript }
                          .Any(v => !string.IsNullOrWhiteSpace(v)));

                // A stale fallback OUR OWN earlier version wrote must be repaired, not kept: it says
                // no key is bound over bindings that are now in place.
                var stale = new StubEmulator
                {
                    Title = "emulator", ApplicationPath = emuPath,
                    SaveStateAutoHotkeyScript = "; PPSSPP binds no key to \"Save State\" by default, and this plugin could not add one.",
                };
                plugin.GetApplicableEmulators(new IEmulator[] { stale });
                // Reported, not asserted: only a plugin that HAS written such a fallback can
                // recognise its own, and most have nothing to repair. The text below is PPSSPP's.
                Console.WriteLine("  a stale fallback of PPSSPP's shape: "
                                  + ((stale.SaveStateAutoHotkeyScript ?? "")
                                         .IndexOf("could not", StringComparison.Ordinal) < 0
                                     ? "replaced" : "left alone (not this plugin's)"));

                var mine = new StubEmulator
                {
                    Title = "emulator", ApplicationPath = emuPath,
                    ExitAutoHotkeyScript = "Send {F9}",
                };
                plugin.GetApplicableEmulators(new IEmulator[] { mine });
                Check("a script the user wrote is not replaced", mine.ExitAutoHotkeyScript == "Send {F9}");

                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "  OK - the scripts reach the entry"
                                           : "  " + bad + " FAILURE(S)");
                return bad == 0 ? 0 : 1;
            }

            if (Has(args, "--rows"))
            {
                if (!RowInjectionCheck.Run(plugin)) return 1;
                return 0;
            }

            bool hotkeys = Has(args, "--hotkeys");
            if (hotkeys && !HotkeyCheck.Run(plugin)) return 1;

            bool flycast = Has(args, "--flycast");
            if (flycast)
            {
                if (!FlycastCheck.Run(plugin)) return 1;
            }

            bool melonds = Has(args, "--melonds");
            if (melonds)
            {
                MelonDsCheck.Anchor(plugin);
                if (!MelonDsCheck.Run(plugin)) return 1;
            }

            // The same plugin against a real installation: --flycast-real --emu <exe> --rom <rom>.
            if (Has(args, "--flycast-real"))
            {
                if (!FlycastCheck.AgainstReal(plugin, emuPath, Arg(args, "--rom"))) return 1;
            }

            // The DSiWare path against a REAL NAND, on copies:
            //   --melonds-real --melonds-base <nand.bin> --melonds-bios7 <f> --melonds-bios9 <f>
            //                  --rom <dsiware.nds> [--melonds-played <nand.bin>]
            if (Has(args, "--melonds-real"))
            {
                MelonDsCheck.Anchor(plugin);
                if (!MelonDsCheck.AgainstReal(plugin,
                        Arg(args, "--melonds-base"), Arg(args, "--rom"),
                        Arg(args, "--melonds-bios7"), Arg(args, "--melonds-bios9"),
                        Arg(args, "--melonds-played"))) return 1;
            }

            Console.WriteLine();
            var wroteTo = new List<string>();
            if (Has(args, "--inject-ra-test")) wroteTo.Add("the emulator's RetroAchievements configuration");
            if (saveDataDir != null && emuPath != null) wroteTo.Add("SAVEDATA (the restore round-trip)");
            if (unit != null && Has(args, "--round-trip")) wroteTo.Add("the emulator's save folder (the restore round-trip)");
            if (states) wroteTo.Add("a throwaway PPSSPP in the temp folder (states)");
            if (flycast) wroteTo.Add("a forged Flycast in the temp folder");
            if (melonds) wroteTo.Add("a forged melonDS in the temp folder");
            if (Has(args, "--melonds-real")) wroteTo.Add("COPIES of the NANDs given, in the temp folder");
            if (hotkeys) wroteTo.Add("a forged Flycast in the temp folder (keyboard mapping)");
            Console.WriteLine(wroteTo.Count == 0
                ? "done. Nothing was written."
                : "done. This run WROTE to: " + string.Join(", ", wroteTo) + ".");
            return 0;
        }

        private static bool Has(string[] args, string name)
            => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + title + " " + new string('-', Math.Max(0, 60 - title.Length)));
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Run it, print the exception instead of dying. A plugin that throws is a finding,
        /// not a reason to stop probing the rest of the contract.</summary>
        private static T Safe<T>(Func<T> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                Console.WriteLine("  !! " + ex.GetType().Name + ": " + ex.Message);
                return default;
            }
        }
    }
}
