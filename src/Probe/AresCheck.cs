// The ares plugin's pieces, run without a host:
//   --ares-names <ares.exe>                   every system of the plugin's table against the exe's own "ares --help" list
//   --ares-line                               the system chosen and the line built, offline, for the three levels
//   --ares-settings <ares.exe> [--settings f] the options written into settings.bml (a COPY), the lines that changed
//   --ares-firmware <ares.exe>                the BIOS found and given - WRITES into that install's Firmware\ and settings.bml
//   --ares-saves <ares.exe> <system> <rom>    the files listed for that ROM run as that system
//   --ares-latest                             the latest release as GitHub has it

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class AresCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Ares." + name, true);
        private static object F(object o, string n) => o?.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o)
                                                        ?? o?.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

        public static bool Names(Assembly asm, string exe)
        {
            _bad = 0;
            Console.WriteLine("-- the plugin's systems against " + exe + " --help --");
            var psi = new ProcessStartInfo(exe, "--help") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            using var p = Process.Start(psi);
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            var line = text.Split('\n').SkipWhile(l => !l.StartsWith("Available Systems")).Skip(1).FirstOrDefault() ?? "";
            var known = new HashSet<string>(line.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.Ordinal);
            Console.WriteLine("  ares knows " + known.Count + " systems");
            foreach (var s in (IEnumerable)T(asm, "AresSystems").GetField("All", Any).GetValue(null))
            {
                var name = (string)F(s, "Name");
                Check(name, known.Contains(name));
            }
            foreach (var pl in (IEnumerable)T(asm, "AresSystems").GetField("Platforms", Any).GetValue(null))
                foreach (var name in ((string[])F(pl, "Systems")).Concat(((IDictionary)F(pl, "ByExtension")).Values.Cast<string>()))
                    if (!known.Contains(name)) Check(F(pl, "Platform") + " -> " + name, false);
            var unmapped = known.Where(k => ((IEnumerable)T(asm, "AresSystems").GetField("All", Any).GetValue(null)).Cast<object>().All(s => (string)F(s, "Name") != k)).ToList();
            Console.WriteLine("  in ares, not in the plugin: " + (unmapped.Count == 0 ? "none" : string.Join(", ", unmapped)));
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Line(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- the system and the line --");
            var launch = T(asm, "AresLaunch");
            (string, string) Sys(string platform, string rom, string line)
            {
                var r = launch.GetMethod("SystemFor", Any).Invoke(null, new object[] { null, platform, rom, line });
                return ((string)r.GetType().GetField("Item1").GetValue(r), (string)r.GetType().GetField("Item2").GetValue(r));
            }
            var a = Sys("Sega CD", @"C:\g\Sonic CD.chd", "--system \"Mega CD 32X\" \"C:\\g\\Sonic CD.chd\"");
            Check("the platform's line wins over the table", a.Item1 == "Mega CD 32X", a.Item1 + " from " + a.Item2);
            a = Sys("Sega CD", @"C:\g\Sonic CD.chd", "\"C:\\g\\Sonic CD.chd\"");
            Check("no line: the platform's default", a.Item1 == "Mega CD", a.Item1 + " from " + a.Item2);
            a = Sys("Sega Genesis", @"C:\g\Knuckles Chaotix.32x", "");
            Check("an extension that says it", a.Item1 == "Mega 32X", a.Item1 + " from " + a.Item2);
            a = Sys("Sega Genesis", @"C:\g\Knuckles Chaotix.32x", "--system \"Mega Drive\" x");
            Check("the platform's line before the extension", a.Item1 == "Mega Drive", a.Item1 + " from " + a.Item2);
            a = Sys("Commodore 64", @"C:\g\x.d64", "");
            Check("a platform the plugin does not map: nothing", a.Item1 == null);

            var built = (string)launch.GetMethod("CommandLine", Any).Invoke(null, new object[] {
                "--system \"Mega Drive\" --fullscreen \"C:\\My Games\\Sonic.md\"", @"C:\LB\Emulators\Nixx-ares\ares.exe", "Mega Drive", "pseudo", @"C:\My Games\Sonic.md", null });
            Console.WriteLine("    " + built);
            Check("one --system, the screen switch replaced, --no-file-prompt, the game last",
                  built == "--settings-file C:\\LB\\Emulators\\Nixx-ares\\settings.bml --system \"Mega Drive\" --pseudofullscreen --no-file-prompt \"C:\\My Games\\Sonic.md\"");
            built = (string)launch.GetMethod("CommandLine", Any).Invoke(null, new object[] { "\"C:\\g\\Zelda.7z\"", @"C:\a\ares.exe", "Super Famicom", "windowed", @"C:\g\Zelda.7z", @"C:\t\Zelda.sfc" });
            Check("a .7z swapped for what was unpacked", built.EndsWith("--no-file-prompt C:\\t\\Zelda.sfc") && !built.Contains("--fullscreen"), built);
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Settings(Assembly asm, string exe, string settings)
        {
            _bad = 0;
            Console.WriteLine("-- settings.bml at launch --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-ares-bml-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            try
            {
                var target = Path.Combine(copy, "settings.bml");
                var original = Path.Combine(Path.GetDirectoryName(exe), "settings.bml");
                if (File.Exists(original)) File.Copy(original, target);
                var fake = Path.Combine(copy, "ares.exe");
                File.WriteAllText(fake, "");
                if (settings != null) T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, Path.GetFullPath(settings));
                var before = File.Exists(target) ? File.ReadAllLines(target) : new string[0];
                T(asm, "AresLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fake, "probe-game" });
                var after = File.ReadAllLines(target);
                for (int i = 0; i < Math.Max(before.Length, after.Length); i++)
                    if (i >= before.Length || i >= after.Length || before[i] != after[i])
                        Console.WriteLine("    " + (i < before.Length ? before[i].Trim() : "") + "  ->  " + (i < after.Length ? after[i].Trim() : ""));
                Check("as many lines as before", before.Length == 0 || after.Length == before.Length, before.Length + " -> " + after.Length);
                var stamp = File.GetLastWriteTimeUtc(target);
                System.Threading.Thread.Sleep(20);
                T(asm, "AresLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fake, "probe-game" });
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(target) == stamp);
            }
            finally { try { Directory.Delete(copy, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Firmware(Assembly asm, string exe)
        {
            Console.WriteLine("-- the BIOS for " + exe + " --");
            var all = T(asm, "AresSystems").GetField("All", Any).GetValue(null);
            var states = (IEnumerable)T(asm, "AresFirmware").GetMethod("Ensure", Any).Invoke(null, new object[] { exe, all, null });
            foreach (var st in states)
            {
                var spec = F(st, "Spec");
                Console.WriteLine("  " + ((string)F(spec, "Label")).PadRight(28) + " " + ((string)F(st, "Path") ?? (((bool)F(spec, "Optional")) ? "- (optional)" : "MISSING"))
                                  + ((string)F(st, "Path") != null && !(bool)F(st, "HashOk") ? "   (another dump)" : ""));
            }
            return true;
        }

        public static bool Saves(Assembly asm, string exe, string system, string rom)
        {
            Console.WriteLine("-- saves of " + Path.GetFileName(rom) + " as " + system + " --");
            var sys = T(asm, "AresSystems").GetMethod("System", Any).Invoke(null, new object[] { system });
            var collect = T(asm, "AresPlugin").GetMethod("Collect", Any);
            var list = Activator.CreateInstance(collect.GetParameters()[0].ParameterType);
            collect.Invoke(null, new object[] { list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exe, sys, rom, "probe-game", null });
            foreach (var row in (IEnumerable)list)
                Console.WriteLine("    " + F(row, "FileLocation") + "  " + F(row, "SaveGroupId") + "  slot " + (F(row, "Slot") ?? "-"));
            return true;
        }

        public static bool Latest(Assembly asm)
        {
            Console.WriteLine("-- ares's latest release --");
            var r = T(asm, "AresBuild").GetMethod("Latest", Any).Invoke(null, null);
            if (r == null) { Console.WriteLine("  FAIL - none"); return false; }
            foreach (var n in new[] { "Version", "Url", "Sha256" }) Console.WriteLine("  " + n + ": " + F(r, n));
            return true;
        }
    }
}
