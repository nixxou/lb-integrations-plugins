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
                Console.Error.WriteLine("usage: Probe <plugin.dll> [--emu <emulator.exe>] [--platform <name>]");
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
                var mine = new StubEmulator { Title = "PPSSPP", ApplicationPath = emuPath };
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
                        : "UNEXPECTED - should claim exactly the PPSSPP entry"));
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
                var discIdType = asm.GetType("LbIntegrations.Ppsspp.PspDiscId");
                var of = discIdType?.GetMethod("Of", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                Console.WriteLine(of == null
                    ? "  this plugin exposes no PspDiscId"
                    : "  " + ((string)of.Invoke(null, new object[] { discIdRom }) ?? "(none)"));
            }

            // The interop assertion. Writes only into a temp folder of its own.
            string saveDataDir = Arg(args, "--save-unit");
            if (saveDataDir != null)
            {
                string discId = Arg(args, "--disc-id");
                string expected = Arg(args, "--expect-hash");
                if (!SaveUnitCheck.Run(plugin, saveDataDir, discId, expected, emuPath)) return 1;
            }

            Console.WriteLine();
            var wroteTo = new List<string>();
            if (Has(args, "--inject-ra-test")) wroteTo.Add("the emulator's RetroAchievements configuration");
            if (saveDataDir != null && emuPath != null) wroteTo.Add("SAVEDATA (the restore round-trip)");
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
