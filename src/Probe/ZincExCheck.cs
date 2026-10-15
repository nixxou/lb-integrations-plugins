// The ZiNc EX plugin's pieces, run without a host:
//   --zincex-identify <folder> [<set>...]   which of ZiNc's games each zip of the folder is (those named, else every zip):
//                                           the games found, how, and the zips that stand for their parent and BIOS
//   --zincex-serve <folder> <zip> [--scratch <dir>]         the zip identified, its folder made in a scratch ZiNc EX folder (links, never a
//                                           file of the folder touched), zinc-settings.cfg pointed there and given back
//   --zincex-explain <zip>                  what a launch of it says: the game, or the refusal's message
//   --zincex-line                           the command line from the lines LaunchBox hands over
//   --zincex-latest                         the latest release as GitHub has it

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class ZincExCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.ZincEx." + name, true);
        private static object Get(object o, string field) => o?.GetType().GetField(field, Any)?.GetValue(o);

        /// <summary>ZincGames.Identify(zip, folders, out miss): the match (null when none) and the miss.</summary>
        private static object Identify(Assembly asm, string zip, string[] folders, out object miss)
        {
            var m = T(asm, "ZincGames").GetMethod("Identify", Any);
            var a = new object[] { zip, folders, null, null };
            var r = m.Invoke(null, a);
            miss = a[2];
            return r;
        }

        private static string Describe(object match, object miss)
        {
            if (match == null)
            {
                var closest = Get(miss, "Closest");
                return "none (" + Get(miss, "Reason") + (closest != null ? "; closest " + Get(closest, "Set") + ", lacking " + string.Join(" ", (string[])Get(miss, "Lacking")) : "") + ")";
            }
            var game = Get(match, "Game");
            var zips = (IDictionary)Get(match, "Zips");
            var parts = new List<string>();
            foreach (DictionaryEntry kv in zips) parts.Add(kv.Key + "<-" + Path.GetFileNameWithoutExtension((string)kv.Value));
            return Get(game, "Set") + ((bool)Get(match, "ByName") ? " (by name)" : " (by ROMs)") + "  " + string.Join(" ", parts);
        }

        public static bool IdentifyFolder(Assembly asm, string folder, string[] names)
        {
            _bad = 0;
            Console.WriteLine("-- which of ZiNc's games, in " + folder + " --");
            var zips = names.Length > 0 ? names.Select(n => Path.Combine(folder, n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? n : n + ".zip")).ToList()
                                        : Directory.EnumerateFiles(folder, "*.zip").ToList();
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var zip in zips)
            {
                if (!File.Exists(zip)) { Console.WriteLine("  " + Path.GetFileName(zip) + ": not there"); continue; }
                var match = Identify(asm, zip, new[] { folder }, out var miss);
                // Every zip: only those that are a game, or the ones asked for by name.
                if (match != null || names.Length > 0) Console.WriteLine("  " + Path.GetFileName(zip).PadRight(18) + " " + Describe(match, miss));
                if (match != null)
                {
                    var set = (string)Get(Get(match, "Game"), "Set");
                    if (!found.TryGetValue(set, out var l)) found[set] = l = new List<string>();
                    l.Add(Path.GetFileNameWithoutExtension(zip));
                }
            }
            Console.WriteLine("  " + zips.Count + " zip(s) in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s: " + found.Count + " of ZiNc's games found");
            if (names.Length == 0)
            {
                var all = ((IEnumerable)T(asm, "ZincGames").GetProperty("All", Any).GetValue(null)).Cast<object>().Select(g => (string)Get(g, "Set")).ToList();
                Console.WriteLine("  not found: " + string.Join(" ", all.Where(s => !found.ContainsKey(s))));
            }
            return _bad == 0;
        }

        public static bool Serve(Assembly asm, string folder, string zip, string scratchRoot = null)
        {
            _bad = 0;
            Console.WriteLine("-- " + Path.GetFileName(zip) + " served --");
            // --scratch: on the games' drive, for hard links (%TEMP% is on another one, and may be emptied under a test).
            var scratch = Path.Combine(scratchRoot ?? Path.GetTempPath(), "lbip-zincex-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var match = Identify(asm, zip, new[] { folder }, out var miss);
                Console.WriteLine("  " + Describe(match, miss));
                if (match == null) { Check("identified", false); return false; }
                var before = Directory.GetFiles(folder).Length;

                var prepare = T(asm, "ZincRoms").GetMethod("Prepare", Any);
                var a = new object[] { scratch, match, null };
                var served = (string)prepare.Invoke(null, a);
                Check("a folder to give ZiNc EX", served != null, served ?? (string)a[2]);
                if (served == null) return false;
                foreach (DictionaryEntry kv in (IDictionary)Get(match, "Zips"))
                {
                    var p = Path.Combine(served, kv.Key + ".zip");
                    // Opened, so a symbolic link is read through (its own FileInfo.Length is the link's, 0).
                    long Size(string f) { using var s = File.OpenRead(f); return s.Length; }
                    Check(kv.Key + ".zip there, the size of " + Path.GetFileName((string)kv.Value), File.Exists(p) && Size(p) == Size((string)kv.Value));
                }
                // Again: kept, not made anew.
                var again = (string)prepare.Invoke(null, new object[] { scratch, match, null });
                Check("the second launch gives the same folder", again == served);
                Console.WriteLine("  " + File.ReadAllText(Path.Combine(scratch, "lbip-roms", "lbip-served.txt")).Trim().Replace("\n", "\n  "));
                Check("nothing added to the games' folder", Directory.GetFiles(folder).Length == before);

                // zinc-settings.cfg: present, absent, no file at all - pointed, then given back.
                var cfg = T(asm, "ZincExConfig");
                var point = cfg.GetMethod("Point", Any); var restore = cfg.GetMethod("Restore", Any); var read = cfg.GetMethod("Read", Any);
                var file = Path.Combine(scratch, "zinc-settings.cfg");
                foreach (var start in new[] { "; ZiNc EX settings\r\n[ZiNc-EX]\r\nsettings_version=2\r\nroms_directory=D:\\ZN roms\r\nrenderer=Direct3D11\r\n", "[ZiNc-EX]\r\nrenderer=OpenGL\r\n", null })
                {
                    if (start == null) { if (File.Exists(file)) File.Delete(file); } else File.WriteAllText(file, start);
                    var was = (string)read.Invoke(null, new object[] { scratch });
                    point.Invoke(null, new object[] { scratch, served });
                    var during = (string)read.Invoke(null, new object[] { scratch });
                    // A second launch before the first gave it back keeps the user's value aside, not ours.
                    point.Invoke(null, new object[] { scratch, served });
                    restore.Invoke(null, new object[] { scratch });
                    var after = File.Exists(file) ? File.ReadAllText(file) : null;
                    var label = start == null ? "no zinc-settings.cfg" : was == null ? "no roms_directory" : "roms_directory=" + was;
                    Check(label + ": pointed during the launch", during == served);
                    Check(label + ": as it was afterwards", after == start, after == null ? "(no file)" : after.Replace("\r\n", " | "));
                    Check(label + ": nothing kept aside", !File.Exists(Path.Combine(scratch, "lbip-roms-directory.txt")));
                }
            }
            finally
            {
                try { Directory.Delete(scratch, recursive: true); } catch (Exception ex) { Console.WriteLine("  (scratch left: " + scratch + " - " + ex.Message + ")"); }
            }
            Console.WriteLine(_bad == 0 ? "OK" : _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>What a launch of <paramref name="zip"/> says: the game it is, or the message a refusal shows.</summary>
        public static bool Explain(Assembly asm, string zip)
        {
            var exe = Path.Combine(Path.GetTempPath(), "lbip-zincex-none", "ZiNc-EX.exe");
            var a = new object[] { exe, zip, null, null };
            var match = T(asm, "ZincExLaunch").GetMethod("Identify", Any).Invoke(null, a);
            Console.WriteLine(match != null ? Describe(match, null) : (string)a[3]);
            return true;
        }

        public static bool Line(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- the command line --");
            var m = T(asm, "ZincExLaunch").GetMethod("CommandLine", Any);
            string L(string current, string rom, string set, string screen) => (string)m.Invoke(null, new object[] { current, rom, set, screen });
            var rom = @"G:\MAME roms\sfexu.zip";
            Check("the game's path replaced", L("\"" + rom + "\"", rom, "sfex", "fullscreen") == "--game sfex --fullscreen", L("\"" + rom + "\"", rom, "sfex", "fullscreen"));
            Check("windowed", L(rom, rom, "sfex", "windowed") == "--game sfex --windowed");
            Check("as set in ZiNc EX: no flag", L(rom, rom, "sfex", "zincex") == "--game sfex");
            Check("an old --game and flag taken out, the user's kept", L("--game tekken --windowed --foo \"" + rom + "\"", rom, "sfex", "fullscreen") == "--game sfex --fullscreen --foo",
                  L("--game tekken --windowed --foo \"" + rom + "\"", rom, "sfex", "fullscreen"));
            Console.WriteLine(_bad == 0 ? "OK" : _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Latest(Assembly asm)
        {
            _bad = 0;
            var r = T(asm, "ZincExBuild").GetMethod("Latest", Any).Invoke(null, null);
            Check("the latest release", r != null, r == null ? null : Get(r, "Version") + " " + Get(r, "FileName") + " asset " + Get(r, "AssetId") + " " + Get(r, "Url"));
            return _bad == 0;
        }
    }
}
