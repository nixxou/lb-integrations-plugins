// The bsnes plugin's pieces, run without a host:
//   --bsnes-settings <bsnes.exe> [--settings <settings.ini>]   the game's options written into settings.bml (a COPY), the
//                                                              lines that changed printed
//   --bsnes-saves <bsnes.exe> <rom>                            the folders and the files listed for that ROM
//   --bsnes-latest                                             the nightly as GitHub has it
//   --bsnes-extract <bsnes-windows.zip> <folder>               the zip extracted as an install does (its first folder off)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class BsnesCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Bsnes." + name, true);

        public static bool Settings(Assembly asm, string exe, string settings)
        {
            _bad = 0;
            Console.WriteLine("-- settings.bml at launch --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-bsnes-bml-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            try
            {
                var target = Path.Combine(copy, "settings.bml");
                var original = Path.Combine(Path.GetDirectoryName(exe), "settings.bml");
                if (File.Exists(original)) File.Copy(original, target);
                var fakeExe = Path.Combine(copy, "bsnes.exe");
                File.WriteAllText(fakeExe, "");
                if (settings != null) T(asm, "BsnesSettings").GetField("PathOverride", Any).SetValue(null, Path.GetFullPath(settings));
                var before = File.Exists(target) ? File.ReadAllLines(target) : new string[0];
                T(asm, "BsnesLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fakeExe, "probe-game" });
                var after = File.ReadAllLines(target);
                for (int i = 0; i < Math.Max(before.Length, after.Length); i++)
                    if (i >= before.Length || i >= after.Length || before[i] != after[i])
                        Console.WriteLine("    " + (i < before.Length ? before[i].Trim() : "") + "  ->  " + (i < after.Length ? after[i].Trim() : ""));
                Check("as many lines as before", before.Length == 0 || after.Length == before.Length, before.Length + " -> " + after.Length);
                var bytes = File.ReadAllBytes(target);
                Check("LF, no BOM", bytes.Length > 2 && bytes[0] != 0xEF && !System.Text.Encoding.ASCII.GetString(bytes).Contains("\r\n"));
                var stamp = File.GetLastWriteTimeUtc(target);
                System.Threading.Thread.Sleep(20);
                T(asm, "BsnesLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fakeExe, "probe-game" });
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(target) == stamp);
                // A file with nothing: the nodes made from nothing, parents first.
                File.Delete(target);
                T(asm, "BsnesLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fakeExe, "probe-game" });
                var fresh = File.ReadAllText(target);
                Check("made from nothing", fresh.Contains("Video\n  AspectCorrection: true") && fresh.Contains("      Mode7\n        Scale: 1"), fresh.Replace("\n", " | "));
                var line = (string)T(asm, "BsnesLaunch").GetMethod("CommandLine", Any).Invoke(null, new object[] { "\"C:\\g\\Zelda.sfc\"", true });
                Check("--fullscreen added", line == "--fullscreen \"C:\\g\\Zelda.sfc\"", line);
                line = (string)T(asm, "BsnesLaunch").GetMethod("CommandLine", Any).Invoke(null, new object[] { "--fullscreen \"C:\\g\\Zelda.sfc\"", false });
                Check("--fullscreen taken out when windowed", line == "\"C:\\g\\Zelda.sfc\"", line);
            }
            finally { try { Directory.Delete(copy, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Saves(Assembly asm, string exe, string rom)
        {
            Console.WriteLine("-- saves of " + Path.GetFileName(rom) + " --");
            var plugin = T(asm, "BsnesPlugin");
            var folders = plugin.GetMethod("FoldersFor", Any).Invoke(null, new object[] { exe, rom });
            Console.WriteLine("  saves folder: " + folders.GetType().GetField("SaveDir").GetValue(folders));
            Console.WriteLine("  states folder: " + folders.GetType().GetField("StateDir").GetValue(folders));
            var collect = plugin.GetMethod("Collect", Any);
            var list = Activator.CreateInstance(collect.GetParameters()[0].ParameterType);
            collect.Invoke(null, new object[] { list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exe, rom, "probe-game", null });
            foreach (var row in (System.Collections.IEnumerable)list)
                Console.WriteLine("    " + row.GetType().GetProperty("FileLocation")?.GetValue(row) + "  group " + row.GetType().GetProperty("SaveGroupId")?.GetValue(row)
                                  + "  " + row.GetType().GetProperty("DisplayChipText")?.GetValue(row));
            return true;
        }

        public static bool Latest(Assembly asm)
        {
            Console.WriteLine("-- bsnes's nightly --");
            var n = T(asm, "BsnesBuild").GetMethod("Latest", Any).Invoke(null, null);
            if (n == null) { Console.WriteLine("  FAIL - none"); return false; }
            foreach (var f in new[] { "Version", "Url", "Published" }) Console.WriteLine("  " + f + ": " + n.GetType().GetField(f).GetValue(n));
            return true;
        }

        public static bool Extract(Assembly asm, string zip, string folder)
        {
            Console.WriteLine("-- " + Path.GetFileName(zip) + " -> " + folder + " --");
            T(asm, "Archives").GetMethod("ExtractOver", Any).Invoke(null, new object[] { zip, folder });
            var exe = Path.Combine(folder, "bsnes.exe");
            Console.WriteLine("  bsnes.exe " + (File.Exists(exe) ? "there" : "MISSING") + ", settings.bml " + (File.Exists(Path.Combine(folder, "settings.bml")) ? "there" : "missing"));
            return File.Exists(exe);
        }
    }
}
