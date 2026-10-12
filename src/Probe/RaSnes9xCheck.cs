// The RASnes9x plugin's pieces, run without a host:
//   --rasnes9x-conf <RASnes9x.exe> [--settings <settings.ini>]   the game's options written into snes9x.conf (a COPY of the
//                                                                folder is worked on), the lines that changed printed
//   --rasnes9x-saves <RASnes9x.exe> <rom>                        the folders and the files listed for that ROM
//   --rasnes9x-7z <archive.7z>                                   the ROM it holds, unpacked under the archive's name
//   --rasnes9x-cheevos                                           the RetroAchievements login written and read back, in %TEMP%
//   --rasnes9x-fetch                                             the pinned build downloaded (site, then repository), checked

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class RaSnes9xCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.RaSnes9x." + name, true);

        public static bool Conf(Assembly asm, string exe, string settings)
        {
            _bad = 0;
            Console.WriteLine("-- snes9x.conf at launch --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-rasnes9x-conf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            try
            {
                var conf = Path.Combine(Path.GetDirectoryName(exe), "snes9x.conf");
                var target = Path.Combine(copy, "snes9x.conf");
                if (File.Exists(conf)) File.Copy(conf, target);
                File.WriteAllText(Path.Combine(copy, "RASnes9x.exe"), "");
                if (settings != null) T(asm, "RaSnes9xSettings").GetField("PathOverride", Any).SetValue(null, Path.GetFullPath(settings));
                var before = File.Exists(target) ? File.ReadAllLines(target) : new string[0];
                T(asm, "RaSnes9xLaunch").GetMethod("PrepareConf", Any).Invoke(null, new object[] { Path.Combine(copy, "RASnes9x.exe"), "probe-game", null });
                var after = File.ReadAllLines(target);
                var changed = after.Where((l, i) => i >= before.Length || before[i] != l).ToList();
                foreach (var l in changed) Console.WriteLine("    " + l);
                Check("as many lines as before (none dropped)", before.Length == 0 || after.Length == before.Length, before.Length + " -> " + after.Length);
                Check("CRLF, no BOM", File.ReadAllBytes(target) is var b && b.Length > 2 && b[0] != 0xEF && System.Text.Encoding.ASCII.GetString(b).Contains("\r\n"));
                // A second pass changes nothing.
                var stamp = File.GetLastWriteTimeUtc(target);
                System.Threading.Thread.Sleep(20);
                T(asm, "RaSnes9xLaunch").GetMethod("PrepareConf", Any).Invoke(null, new object[] { Path.Combine(copy, "RASnes9x.exe"), "probe-game", null });
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(target) == stamp);
                if (before.Length > 0)
                {
                    var comments = before.Count(l => l.Contains('#'));
                    Check("the comments kept", after.Count(l => l.Contains('#')) == comments, comments + " lines with a comment");
                }
                // An EMPTY save / state folder (beside the game, Mehdi 12/10): set to .\Saves, what it left moved in, never over a file.
                File.WriteAllText(target, "[Settings\\Win\\Files]\r\nDir:SRAM = \r\nDir:Savestates =   # beside the game\r\n");
                var games = Path.Combine(copy, "games"); Directory.CreateDirectory(games);
                var saves = Path.Combine(copy, "Saves"); Directory.CreateDirectory(saves);
                foreach (var f in new[] { "Zelda.srm", "Zelda.rtc", "Zelda.000", "Zelda.000.png", "Zelda.003", "Zelda.oops", "Zelda.sfc", "Other.srm" })
                    File.WriteAllText(Path.Combine(games, f), f);
                File.WriteAllText(Path.Combine(saves, "Zelda.003"), "already there");
                T(asm, "RaSnes9xLaunch").GetMethod("PrepareConf", Any).Invoke(null, new object[] { Path.Combine(copy, "RASnes9x.exe"), "probe-game", Path.Combine(games, "Zelda.sfc") });
                var text = File.ReadAllText(target);
                Check("empty Dir:SRAM set to .\\Saves", text.Contains("Dir:SRAM = .\\Saves"), text.Split('\n')[1].Trim());
                Check("empty Dir:Savestates set to .\\Saves, its comment kept", text.Contains("Dir:Savestates = .\\Saves") && text.Contains("# beside the game"), text.Split('\n')[2].Trim());
                Check("the save, its clock, a state and its picture moved", new[] { "Zelda.srm", "Zelda.rtc", "Zelda.000", "Zelda.000.png" }.All(f => File.Exists(Path.Combine(saves, f)) && !File.Exists(Path.Combine(games, f))));
                Check("a state already in Saves kept, the old one left beside the game", File.ReadAllText(Path.Combine(saves, "Zelda.003")) == "already there" && File.Exists(Path.Combine(games, "Zelda.003")));
                Check("the ROM, the undo state and another game's save not moved", new[] { "Zelda.sfc", "Zelda.oops", "Other.srm" }.All(f => File.Exists(Path.Combine(games, f))));
                File.WriteAllText(target, "[Settings\\Win\\Files]\r\nDir:SRAM = \r\n");
                var folders = T(asm, "RaSnes9xPlugin").GetMethod("FoldersFor", Any).Invoke(null, new object[] { Path.Combine(copy, "RASnes9x.exe"), Path.Combine(games, "Zelda.sfc") });
                var sramDir = (string)folders.GetType().GetField("SaveDir").GetValue(folders);
                Check("an empty Dir:SRAM read as .\\Saves (where the next launch puts it)", string.Equals(sramDir, saves, StringComparison.OrdinalIgnoreCase), sramDir);
            }
            finally { try { Directory.Delete(copy, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Saves(Assembly asm, string exe, string rom)
        {
            Console.WriteLine("-- saves of " + Path.GetFileName(rom) + " --");
            var plugin = T(asm, "RaSnes9xPlugin");
            var folders = plugin.GetMethod("FoldersFor", Any).Invoke(null, new object[] { exe, rom });
            string F(string n) => (string)folders.GetType().GetField(n).GetValue(folders);
            Console.WriteLine("  SRAM folder: " + F("SaveDir"));
            Console.WriteLine("  states folder: " + F("StateDir"));
            var collect = plugin.GetMethod("Collect", Any);
            var list = Activator.CreateInstance(collect.GetParameters()[0].ParameterType);
            var states = (int)collect.Invoke(null, new object[] { list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exe, rom, "probe-game", null });
            foreach (var row in (System.Collections.IEnumerable)list)
                Console.WriteLine("    " + row.GetType().Name + "  " + row.GetType().GetProperty("FileLocation")?.GetValue(row)
                                  + "  slot " + (row.GetType().GetProperty("Slot")?.GetValue(row) ?? "-") + "  group " + row.GetType().GetProperty("SaveGroupId")?.GetValue(row));
            Console.WriteLine("  " + states + " state(s)");
            return true;
        }

        public static bool SevenZip(Assembly asm, string archive)
        {
            Console.WriteLine("-- " + Path.GetFileName(archive) + " --");
            var args = new object[] { archive, null };
            var rom = (string)T(asm, "RaSnes9xLaunch").GetMethod("RomFor", Any).Invoke(null, args);
            Console.WriteLine("  -> " + (rom ?? "(none)") + (args[1] != null ? "  problem: " + args[1] : ""));
            if (rom != null) Console.WriteLine("  " + new FileInfo(rom).Length + " bytes");
            return rom != null || args[1] != null;
        }

        public static bool Cheevos(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- RetroAchievements login --");
            var dir = Path.Combine(Path.GetTempPath(), "lbip-rasnes9x-ra-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var exe = Path.Combine(dir, "RASnes9x.exe");
                File.WriteAllText(exe, "");
                var t = T(asm, "RaSnes9xCheevos");
                var prefs = Path.Combine(dir, "RAPrefs_RASnes9X.cfg");
                File.WriteAllText(prefs, "{\n  \"Username\": \"old\",\n  \"Leaderboards Active\": false,\n  \"Window Positions\": { \"Overlay\": 3 }\n}");
                var why = (string)t.GetMethod("Write", Any).Invoke(null, new object[] { exe, "probeuser", "probetoken", (bool?)false });
                Check("written", why == null, why);
                var read = t.GetMethod("Read", Any).Invoke(null, new object[] { exe });
                var (user, hasToken, hard) = ((string, bool, bool))read;
                Check("read back", user == "probeuser" && hasToken && !hard, user + ", token " + hasToken + ", hardcore " + hard);
                var text = File.ReadAllText(prefs);
                Check("the other keys kept", text.Contains("Leaderboards Active") && text.Contains("Window Positions"));
                why = (string)t.GetMethod("Write", Any).Invoke(null, new object[] { exe, null, null, (bool?)null });
                Check("nothing given, nothing lost", why == null && File.ReadAllText(prefs).Contains("probeuser"));
                File.Delete(prefs);
                t.GetMethod("Write", Any).Invoke(null, new object[] { exe, "fresh", "tok", (bool?)true });
                var (u2, _, h2) = ((string, bool, bool))t.GetMethod("Read", Any).Invoke(null, new object[] { exe });
                Check("made from nothing", u2 == "fresh" && h2);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>RetroAchievements' toolset fetched beside <paramref name="exe"/> as the emulator would, twice: the second
        /// pass must find it the latest and download nothing.</summary>
        public static bool Toolset(Assembly asm, string exe)
        {
            _bad = 0;
            Console.WriteLine("-- RetroAchievements' toolset beside " + exe + " --");
            var t = T(asm, "RaSnes9xToolset");
            var installed = (string)t.GetMethod("Installed", Any).Invoke(null, new object[] { exe });
            Console.WriteLine("  before: " + (installed ?? "none"));
            var why = (string)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
            Check("first pass", why == null, why);
            var after = (string)t.GetMethod("Installed", Any).Invoke(null, new object[] { exe });
            Check("installed", after != null, after);
            var dll = Path.Combine(Path.GetDirectoryName(exe), "RA_Integration.dll");
            var stamp = File.Exists(dll) ? File.GetLastWriteTimeUtc(dll) : default;
            why = (string)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
            Check("second pass downloads nothing", why == null && File.GetLastWriteTimeUtc(dll) == stamp, why);
            Check("no .download nor .old left", !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "RA_Integration.download"))
                                                && !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "RA_Integration.old")));
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Fetch(Assembly asm)
        {
            Console.WriteLine("-- the pinned build --");
            var dest = Path.Combine(Path.GetTempPath(), "lbip-rasnes9x-fetch.zip");
            try
            {
                T(asm, "RaSnes9xBuild").GetMethod("Fetch", Any).Invoke(null, new object[] { dest, null, null });
                Console.WriteLine("  ok - " + new FileInfo(dest).Length + " bytes, SHA-256 checked");
                return true;
            }
            catch (TargetInvocationException ex) { Console.WriteLine("  FAIL - " + ex.InnerException?.Message); return false; }
            finally { try { File.Delete(dest); } catch { } }
        }
    }
}
