// The Flycast plugin's per-game settings, on a FORGED install in the temp folder: nothing real is touched.
//
//   --flycast-settings              the engine: kept, turned into -config in front of the line, emu.cfg untouched
//   --flycast-options-shot <png>    the options window, one picture per tab
//   --flycast-import-repair --lb <r> the sets LaunchBox leaves out of an import, looked up READ ONLY in <r>\Metadata

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class FlycastSettingsCheck
    {
        private static Assembly _asm;
        private static int _bad;

        private static Type T(string name) => _asm.GetType("LbIntegrations.Flycast." + name, throwOnError: true);

        private static object Call(string type, string method, params object[] args)
        {
            // The optional parameters left out are given their defaults.
            var m = T(type).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                           .First(x => x.Name == method && x.GetParameters().Length >= args.Length && x.GetParameters().Skip(args.Length).All(p => p.IsOptional));
            var all = args.Concat(m.GetParameters().Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
            var result = m.Invoke(null, all);
            Array.Copy(all, args, args.Length);   // the out parameters, back to the caller
            return result;
        }

        private static object Kind(string name) => Enum.Parse(T("FlycastGameSettings").GetNestedType("Games", BindingFlags.NonPublic | BindingFlags.Public), name);

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        private static string Ids(object keys)
            => keys == null ? "(none)" : string.Join(",", ((IEnumerable)keys).Cast<object>().Select(k => (string)k.GetType().GetProperty("Id").GetValue(k)));

        public static bool Settings(Assembly pluginAssembly)
        {
            _asm = pluginAssembly;
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- Flycast, a game's own settings, on a FORGED install  [WRITES, in the temp folder] --");
            var root = Path.Combine(Path.GetTempPath(), "lbip-flycast-" + Guid.NewGuid().ToString("N"));
            try
            {
                var install = Path.Combine(root, "Nixx-Flycast");
                Directory.CreateDirectory(install);
                var exe = Path.Combine(install, "flycast.exe");
                File.WriteAllText(exe, "not really an executable");
                var cfgPath = Path.Combine(install, "emu.cfg");
                var cfg = "[T-8120N]\r\nconfig.rend.Resolution = 1440\r\n\r\n[achievements]\r\nEnabled = yes\r\n\r\n[config]\r\nrend.Resolution = 960\r\nrend.vsync = no\r\n\r\n[window]\r\nfullscreen = no\r\n";
                File.WriteAllText(cfgPath, cfg);
                var layout = Call("FlycastPaths", "Resolve", exe);
                Console.WriteLine("  install " + install);

                var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["config:rend.Resolution"] = "1920", ["config:rend.vsync"] = "yes", ["config:Dreamcast.Cable"] = "0", ["config:ForceFreePlay"] = "no",
                };
                Call("FlycastGameSettings", "Save", layout, "g1", own);
                var loaded = (Dictionary<string, string>)Call("FlycastGameSettings", "Load", layout, "g1");
                Check("kept, and read back", loaded != null && loaded.Count == 4 && loaded["config:rend.Resolution"] == "1920");

                // What a launch gives.
                var a = new object[] { layout, "g1", Kind("Dreamcast"), null };
                var dc = Call("FlycastGameSettings", "KeysOf", a);
                Console.WriteLine("            Dreamcast: " + Ids(dc));
                Check("a Dreamcast game: its cable, not the Naomi free play", Ids(dc) == "config:Dreamcast.Cable,config:rend.Resolution,config:rend.vsync");
                var b = new object[] { layout, "g1", Kind("Arcade"), null };
                var arcade = Call("FlycastGameSettings", "KeysOf", b);
                Check("an arcade game: its free play, not the Dreamcast's cable", Ids(arcade) == "config:ForceFreePlay,config:rend.Resolution,config:rend.vsync", Ids(arcade));
                var line = (string)Call("FlycastGameSettings", "WithSettings", "-config window:fullscreen=yes", dc);
                Console.WriteLine("            " + line);
                Check("-config in front, the line's own after it - so a key the line names wins",
                      line == "-config config:Dreamcast.Cable=0,config:rend.Resolution=1920,config:rend.vsync=yes -config window:fullscreen=yes", line);
                Check("nothing set: the line stays as it is", Call("FlycastGameSettings", "WithSettings", "-config window:fullscreen=yes", null) == null);
                Check("emu.cfg not touched", File.ReadAllText(cfgPath) == cfg);

                // What the game runs on without them.
                var d = new object[] { layout, "T-8120N", null };
                var defaults = (Dictionary<string, string>)Call("FlycastGameSettings", "DefaultsOf", d);
                var wins = (HashSet<string>)d[2];
                Check("'default': its game config's 1440, emu.cfg's VSync off, Flycast's own fog on",
                      defaults["config:rend.Resolution"] == "1440" && defaults["config:rend.vsync"] == "no" && defaults["config:rend.Fog"] == "yes");
                Check("its game config's keys are known: ours must be given over them", wins.Count == 1 && wins.Contains("config:rend.Resolution"));
                var d2 = new object[] { layout, "OTHER-1", null };
                var other = (Dictionary<string, string>)Call("FlycastGameSettings", "DefaultsOf", d2);
                Check("another game: emu.cfg's 960", other["config:rend.Resolution"] == "960" && ((HashSet<string>)d2[2]).Count == 0);

                // Set by hand.
                Call("FlycastGameSettings", "SaveAdvanced", layout, "g1", "[config]\r\nrend.Resolution = 2880\r\n[audio]\r\nbackend = auto\r\n[achievements]\r\nEnabled = no\r\n", true);
                var h = new object[] { layout, "g1", Kind("Arcade"), null };
                var hand = Call("FlycastGameSettings", "KeysOf", h);
                Check("set by hand, in use: its keys, not the tabs' - [achievements] left out", Ids(hand) == "config:rend.Resolution,audio:backend", Ids(hand));
                var p = new object[] { layout, "T-8120N", "-config window:fullscreen=yes", hand, Kind("All"), null, null };
                var preview = (string)Call("FlycastGameSettings", "Preview", p);
                Console.WriteLine("            " + (preview ?? (string)p[6]).Replace("\r\n", " | "));
                Check("the preview: its game config sets the resolution - taken out for the session, ours given globally only",
                      preview != null && preview.Contains("-config config:rend.Resolution=2880,audio:backend=auto -config window:fullscreen=yes")
                      && !preview.Contains("T-8120N:") && preview.Contains("taken out of its own game config for the session") && ((string)p[5]).Contains("rend.Resolution = 1440"), preview);

                // This plugin's > the game's own config > Flycast's: the keys its section sets, given there too.
                var over = Call("FlycastGameSettings", "SetByGame", layout, "T-8120N", hand);
                Check("of the keys, the ones its own [T-8120N] sets: the resolution only", Ids(over) == "config:rend.Resolution", Ids(over));
                var launch = (string)Call("FlycastGameSettings", "WithSettings", "", hand);
                Check("the launch line never names a game's section (Flycast would save it as the user's - 30/09)",
                      launch == "-config config:rend.Resolution=2880,audio:backend=auto", launch);
                Check("a game with no section of its own: its section never named (Flycast's per-game mode stays off)",
                      Ids(Call("FlycastGameSettings", "SetByGame", layout, "OTHER-1", hand)) == "(none)" || ((IEnumerable)Call("FlycastGameSettings", "SetByGame", layout, "OTHER-1", hand)).Cast<object>().Count() == 0);

                // Values with a space, and what cannot go on a command line.
                var sp = new object[] { "[config]\r\nrend.Foo = a b\r\n", null };
                var spaced = Call("FlycastGameSettings", "ParseHand", sp);
                Check("a value with a space: the whole -config quoted", (string)Call("FlycastGameSettings", "Argument", spaced) == "-config \"config:rend.Foo=a b\"");
                foreach (var (text, what) in new[] { ("[config]\r\nrend.Foo = a,b", "a comma"), ("[config]\r\nrend.Foo = \"x\"", "a quote"), ("rend.Foo = 1", "a key before any section"), ("[T 8120]\r\nx = 1", "a section with a space") })
                {
                    var e = new object[] { text, null };
                    Check(what + ": refused", Call("FlycastGameSettings", "ParseHand", e) == null && e[1] != null, e[1] as string);
                }
                var c = new object[] { layout, "[config]\r\nrend.vsync = maybe\r\nrend.Resolution = 1000\r\nrend.ScreenStretching = 400\r\nNoSuchKey = 1\r\nrend.Resolution2 = 1\r\n[achievements]\r\nToken = x", null };
                var w = (List<string>)Call("FlycastGameSettings", "CheckHand", c);
                Console.WriteLine("            " + string.Join(" | ", w));
                Check("said: a bool not yes/no, a choice out of the list, a number out of range, an unknown key, a section kept elsewhere",
                      c[2] == null && w.Count == 6 && w.Any(x => x.Contains("vsync")) && w.Any(x => x.Contains("= 1000")) && w.Any(x => x.Contains("ScreenStretching"))
                      && w.Any(x => x.Contains("NoSuchKey")) && w.Any(x => x.Contains("achievements")));
                Check("emu.cfg still not touched", File.ReadAllText(cfgPath) == cfg);

                Identity(layout, install, cfgPath);
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + ex); _bad++; }
            finally { try { Directory.Delete(root, true); } catch { } }
            Console.WriteLine();
            Console.WriteLine(_bad == 0 ? "  OK - a game's settings go on Flycast's command line, and nothing is written" : "  " + _bad + " FAILURE(S) - see above");
            return _bad == 0;
        }

        public sealed class FakeRecord { public string ApplicationPath { get; set; } public string Title { get; set; } }
        public sealed class FakeList { public System.Collections.ObjectModel.ObservableCollection<FakeRecord> Games { get; } = new System.Collections.ObjectModel.ObservableCollection<FakeRecord>(); }

        /// <summary>The import wizard's last list, sorted for Flycast - on copies of real sets.</summary>
        private static FakeList _lastList;

        public static bool ImportFilter(Assembly pluginAssembly)
        {
            _asm = pluginAssembly;
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- Flycast, LaunchBox's import list sorted  [copies in the temp folder] --");
            var roms = @"G:\MAME 0.288 ROMs (non-merged)";
            string tool = null;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && tool == null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "build", "flycast-id", "flycast-id.exe"))) tool = Path.Combine(d.FullName, "build", "flycast-id", "flycast-id.exe");
            if (tool == null || !Directory.Exists(roms)) { Console.WriteLine("  (no flycast-id.exe or no MAME folder here: skipped)"); return true; }
            Environment.SetEnvironmentVariable("LBIP_FLYCAST_ID_TOOL", tool);
            T("FlycastLbImport").GetField("Quiet", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
            var root = Path.Combine(Path.GetTempPath(), "lbip-fcimport-" + Guid.NewGuid().ToString("N"));
            try
            {
                var install = Path.Combine(root, "Nixx-Flycast");
                Directory.CreateDirectory(install);
                File.WriteAllText(Path.Combine(install, "flycast.exe"), "x");
                var layout = Call("FlycastPaths", "Resolve", Path.Combine(install, "flycast.exe"));
                var dir = Path.Combine(root, "roms");
                Directory.CreateDirectory(dir);
                foreach (var s in new[] { "mvsc2", "18wheelr", "ggisuka", "sf2", "naomi", "azumanga" })
                    if (File.Exists(Path.Combine(roms, s + ".zip"))) File.Copy(Path.Combine(roms, s + ".zip"), Path.Combine(dir, s + ".zip"));
                File.Copy(Path.Combine(roms, "mvsc2.zip"), Path.Combine(dir, "Marvel vs Capcom 2 (Europe).zip"));
                File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");

                string Run(string system, bool quick, bool crc)
                {
                    var list = new FakeList();
                    foreach (var f in Directory.GetFiles(dir).OrderBy(x => x)) list.Games.Add(new FakeRecord { ApplicationPath = f, Title = Path.GetFileNameWithoutExtension(f) });
                    Call("FlycastLbImport", "Filter", null, list, system, quick, crc, layout);
                    _lastList = list;
                    return string.Join(", ", list.Games.Select(g => Path.GetFileNameWithoutExtension(g.ApplicationPath)));
                }
                var naomiQuick = Run("Naomi", true, false);
                Check("Naomi, quick: the Naomi sets by their name, a GD-ROM game without its image too - not the Atomiswave one, the MAME one, the BIOS, the renamed one, the text",
                      naomiQuick == "18wheelr, azumanga, mvsc2", naomiQuick);
                var titles = string.Join(" | ", _lastList.Games.Select(g => g.Title));
                Check("a title LaunchBox left as the file's name: Flycast's own name for the set", titles.Contains("Marvel vs. Capcom 2 New Age of Heroes (Export, Korea)") && titles.Contains("18 Wheeler"), titles);

                // A whole MAME folder: tens of thousands of lines, in seconds.
                var big = new FakeList();
                for (int i = 0; i < 40000; i++) big.Games.Add(new FakeRecord { ApplicationPath = Path.Combine(dir, "notflycast" + i + ".zip"), Title = "notflycast" + i });
                big.Games.Add(new FakeRecord { ApplicationPath = Path.Combine(dir, "mvsc2.zip"), Title = "Kept As LaunchBox Named It" });
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Call("FlycastLbImport", "Filter", null, big, "Naomi", true, false, layout);
                watch.Stop();
                Check("40 000 lines sorted in a few seconds (" + watch.ElapsedMilliseconds + " ms), the one Flycast set kept, its LaunchBox title untouched",
                      watch.ElapsedMilliseconds < 5000 && big.Games.Count == 1 && big.Games[0].Title == "Kept As LaunchBox Named It",
                      big.Games.Count + " left, " + watch.ElapsedMilliseconds + " ms");

                var naomiCrc = Run("Naomi", true, true);
                Check("Naomi, quick + CRC: the same, each loaded (the GD-ROM game without its image kept, not loaded) - the renamed one told by its files and left out",
                      naomiCrc == "18wheelr, azumanga, mvsc2", naomiCrc);
                var aw = Run("Atomiswave", true, false);
                Check("Atomiswave, quick: only ggisuka", aw == "ggisuka", aw);
                var crcOnly = Run("Naomi", false, true);
                Check("Naomi, CRC only: the same verdict by the content", crcOnly == "18wheelr, azumanga, mvsc2", crcOnly);

                // Dreamcast: by the extension, then by the disc's IP.BIN.
                var dc = @"G:\Bang! Gunship Elite (USA).chd";
                var saturn = @"G:\saturn\Atlantis - The Lost Tales (FR) (Disc 1).chd";
                if (File.Exists(dc) && File.Exists(saturn))
                {
                    string RunDisc(bool quick, bool crc)
                    {
                        var list = new FakeList();
                        foreach (var f in new[] { dc, saturn, Path.Combine(dir, "mvsc2.zip") }) list.Games.Add(new FakeRecord { ApplicationPath = f });
                        Call("FlycastLbImport", "Filter", null, list, "Dreamcast", quick, crc, layout);
                        return string.Join(", ", list.Games.Select(g => Path.GetFileNameWithoutExtension(g.ApplicationPath)));
                    }
                    var q = RunDisc(true, false);
                    Check("Dreamcast, quick: the two discs by their extension, not the arcade zip", q == "Bang! Gunship Elite (USA), Atlantis - The Lost Tales (FR) (Disc 1)", q);
                    var c = RunDisc(true, true);
                    Check("Dreamcast, quick + CRC: the Saturn disc out by its IP.BIN", c == "Bang! Gunship Elite (USA)", c);
                }
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + ex); _bad++; }
            finally { try { Directory.Delete(root, true); } catch { } Environment.SetEnvironmentVariable("LBIP_FLYCAST_ID_TOOL", null); }
            Console.WriteLine(_bad == 0 ? "  OK - an import list keeps what Flycast runs for its platform" : "  " + _bad + " FAILURE(S) - see above");
            return _bad == 0;
        }

        private static object Raws(params string[] ids)
        {
            var raw = T("FlycastGameSettings").GetNestedType("Raw", BindingFlags.NonPublic | BindingFlags.Public);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(raw));
            foreach (var id in ids)
            {
                var r = Activator.CreateInstance(raw);
                int at = id.IndexOf(':'), eq = id.IndexOf('=');
                raw.GetField("Section").SetValue(r, id.Substring(0, at));
                raw.GetField("Key").SetValue(r, id.Substring(at + 1, eq - at - 1));
                raw.GetField("Value").SetValue(r, id.Substring(eq + 1));
                list.Add(r);
            }
            return list;
        }

        /// <summary>An arcade game's id (flycast-id.exe, else Flycast's log) and this plugin's keys over its own
        /// config when the command line cannot name it.</summary>
        private static void Identity(object layout, string install, string cfgPath)
        {
            Console.WriteLine("  -- the game's id, and over its own config when its id has a space --");
            string P(int code, string text) => (string)Call("FlycastGameIdentity", "Parse", code, System.Text.Encoding.Latin1.GetBytes(text), null);
            Check("the tool's answer: its id, the leading spaces kept", P(0, "id\t  18WHEELER\n") == "  18WHEELER");
            Check("NOT an id: an error, nothing, an empty or blank id, a \\r, two lines, another line",
                  P(1, "id\tX\n") == null && P(0, "") == null && P(0, "id\t\n") == null && P(0, "id\t   \n") == null
                  && P(0, "id\tX\r\n") == null && P(0, "id\tX\nY\n") == null && P(0, "X\n") == null && P(0, "id\tX") == null);
            Check("from Flycast's log: the last \"Game ID is [..]\", an empty one skipped",
                  (string)Call("FlycastGameIdentity", "FromLog", "00:01 emulator.cpp:60 N[BOOT]: Game ID is [GUILTY GEAR isuka]\r\n00:02 emulator.cpp:60 N[BOOT]: Game ID is []\r\n") == "GUILTY GEAR isuka"
                  && Call("FlycastGameIdentity", "FromLog", "nothing here") == null);
            Check("a section named as Flycast names it: its leading spaces kept", (string)Call("FlycastIni", "SectionName", "[  18WHEELER]") == "  18WHEELER"
                  && (string)Call("FlycastIni", "SectionName", "  [config]  ") == "config" && Call("FlycastIni", "SectionName", "rend.Fog = yes") == null);

            // The tool itself, on a real set when there is one.
            string tool = null;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && tool == null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "build", "flycast-id", "flycast-id.exe"))) tool = Path.Combine(d.FullName, "build", "flycast-id", "flycast-id.exe");
            tool ??= "";
            var set = @"G:\MAME 0.288 ROMs (non-merged)\ggisuka.zip";
            if (File.Exists(tool) && File.Exists(set))
            {
                Environment.SetEnvironmentVariable("LBIP_FLYCAST_ID_TOOL", tool);
                var a = new object[] { layout, set, 15000, null };
                var id = (string)Call("FlycastGameIdentity", "Of", a);
                Check("flycast-id.exe on a real Atomiswave set: [GUILTY GEAR isuka]", id == "GUILTY GEAR isuka", id + " / " + a[3]);
                Check("kept for next time", (string)Call("FlycastGameIdentity", "Cached", layout, set) == "GUILTY GEAR isuka");
                var bad = Path.Combine(install, "nosuchset.zip");
                File.WriteAllText(bad, "not a zip");
                var b = new object[] { layout, bad, 15000, null };
                Check("a set it cannot read: no id, and nothing kept", Call("FlycastGameIdentity", "Of", b) == null && Call("FlycastGameIdentity", "Cached", layout, bad) == null, b[3] as string);
                Environment.SetEnvironmentVariable("LBIP_FLYCAST_ID_TOOL", null);
            }
            else Console.WriteLine("    (flycast-id.exe or the test set is not here: the tool is not run)");

            // Over a game's own config - whatever its id (30/09: never named on the command line).
            var mine = "[  18WHEELER]\r\nconfig.rend.Fog = no\r\nconfig.rend.Resolution = 1440\r\n\r\n[config]\r\nrend.Resolution = 960\r\n";
            File.WriteAllText(cfgPath, mine);
            var keys = Raws("config:rend.Resolution=2880");
            var note = Path.Combine(install, "lbip-gameconfig.restore");
            Check("taken out: the note first, then only that key - the section and its other key stay",
                  (bool)Call("FlycastGameConfigSession", "Apply", layout, "  18WHEELER", keys) && File.Exists(note)
                  && File.ReadAllText(cfgPath).Contains("[  18WHEELER]") && File.ReadAllText(cfgPath).Contains("config.rend.Fog = no")
                  && !File.ReadAllText(cfgPath).Contains("config.rend.Resolution = 1440"), File.ReadAllText(cfgPath));
            Call("FlycastGameConfigSession", "Restore", layout, "the session is over");
            var back = File.ReadAllText(cfgPath);
            Check("once over: the key back in its section, the note gone", back.Contains("config.rend.Resolution = 1440") && !File.Exists(note)
                  && back.IndexOf("config.rend.Resolution = 1440") < back.IndexOf("[config]"), back);

            Call("FlycastGameConfigSession", "Apply", layout, "  18WHEELER", keys);
            Call("FlycastIni", "Write", cfgPath, "  18WHEELER", new Dictionary<string, string> { ["config.rend.Resolution"] = "720" });
            Call("FlycastGameConfigSession", "Restore", layout, "the session is over");
            Check("set again in Flycast during the session: the user's kept, not ours put back", File.ReadAllText(cfgPath).Contains("config.rend.Resolution = 720")
                  && !File.ReadAllText(cfgPath).Contains("= 1440"), File.ReadAllText(cfgPath));

            File.WriteAllText(cfgPath, mine);
            Call("FlycastGameConfigSession", "Apply", layout, "  18WHEELER", keys);
            File.WriteAllText(cfgPath, "[config]\r\nrend.Resolution = 960\r\n");   // "Delete Game Config" in Flycast
            Call("FlycastGameConfigSession", "Restore", layout, "the session is over");
            Check("the section deleted in Flycast during the session: not brought back", !File.ReadAllText(cfgPath).Contains("18WHEELER") && !File.Exists(note));

            // A late watcher (adversarial review, 30/09): a later launch's session is not its to put back.
            File.WriteAllText(cfgPath, mine);
            Call("FlycastGameConfigSession", "Apply", layout, "  18WHEELER", keys);
            var later = (string)T("FlycastGameConfigSession").GetField("LastId", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Call("FlycastGameConfigSession", "Restore", layout, "a late watcher", "not-this-session");
            Check("a late watcher: the later session left as it is", File.Exists(note) && !File.ReadAllText(cfgPath).Contains("= 1440"));
            Check("  ...pending while it is on", (bool)Call("FlycastGameConfigSession", "Pending", layout));
            Call("FlycastGameConfigSession", "Restore", layout, "the session is over", later);
            Check("  ...then its own end: the key back", File.ReadAllText(cfgPath).Contains("config.rend.Resolution = 1440") && !File.Exists(note)
                  && !(bool)Call("FlycastGameConfigSession", "Pending", layout), File.ReadAllText(cfgPath));

            // "Delete game config": the section whole, the rest byte for byte.
            File.WriteAllText(cfgPath, "[config]\r\nrend.Resolution = 960\r\n\r\n[  18WHEELER]\r\nconfig.rend.Fog = no\r\n\r\n[audio]\r\nbackend = auto\r\n");
            Check("delete game config: its section out, header and all, the others as they were",
                  Call("FlycastIni", "RemoveSection", cfgPath, "  18WHEELER") == null
                  && File.ReadAllText(cfgPath) == "[config]\r\nrend.Resolution = 960\r\n\r\n[audio]\r\nbackend = auto\r\n", File.ReadAllText(cfgPath));

            // Learned from the log: ours deleted once read, the user's left alone and read from where it stood.
            var log = Path.Combine(install, "flycast.log");
            File.WriteAllText(cfgPath, "[config]\r\nrend.Resolution = 960\r\n");
            File.WriteAllText(log, "an old log of ours");
            var rom = Path.Combine(install, "somegame.zip");
            File.WriteAllText(rom, "x");
            var l = new object[] { layout, rom, null };
            var learning = Call("FlycastGameIdentity", "BeforeLaunch", l);
            Check("no log of the user's: asked for with -config, a stale one cleared", (string)l[2] == "log:LogToFile=yes" && !File.Exists(log));
            File.WriteAllText(log, "00:00:800 emulator.cpp:60 N[BOOT]: Game ID is [INITIAL D Ver.2]\r\n");
            Call("FlycastGameIdentity", "AfterExit", layout, learning);
            Check("after the session: the id kept, the log deleted", (string)Call("FlycastGameIdentity", "Cached", layout, rom) == "INITIAL D Ver.2" && !File.Exists(log));

            File.WriteAllText(cfgPath, "[log]\r\nLogToFile = yes\r\n");
            File.WriteAllText(log, "the user's own log: Game ID is [NOT THIS ONE]\r\n");
            var rom2 = Path.Combine(install, "other.zip");
            File.WriteAllText(rom2, "y");
            var l2 = new object[] { layout, rom2, null };
            var learning2 = Call("FlycastGameIdentity", "BeforeLaunch", l2);
            File.AppendAllText(log, "N[BOOT]: Game ID is [MAXIMUM SPEED]\r\n");
            Call("FlycastGameIdentity", "AfterExit", layout, learning2);
            Check("a log of the user's: nothing added to the line, read from where it stood, left in place",
                  l2[2] == null && (string)Call("FlycastGameIdentity", "Cached", layout, rom2) == "MAXIMUM SPEED" && File.Exists(log)
                  && File.ReadAllText(log).StartsWith("the user's own log"));
        }

        /// <summary>--flycast-import-repair --lb &lt;LaunchBox root&gt;: what puts back the sets LaunchBox leaves out of an
        /// import - its own database and MAME list, READ ONLY. The four of 30/09: vf4b, vf4o, vf4cart ("Virtua
        /// Fighter 4") and clubkprz ("Club Kart Prize"), on Sega Naomi 2.</summary>
        public static bool ImportRepair(Assembly pluginAssembly, string lbRoot)
        {
            _asm = pluginAssembly;
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- Flycast, the sets LaunchBox leaves out of an import  [READS " + lbRoot + "\\Metadata] --");
            if (string.IsNullOrEmpty(lbRoot) || !File.Exists(Path.Combine(lbRoot, "Metadata", "LaunchBox.Metadata.db")))
            {
                Console.WriteLine("    skipped: no LaunchBox.Metadata.db under " + lbRoot);
                return true;
            }
            var watch = _asm.GetType("LbIntegrations.Lbip.LbipImportWatch", throwOnError: true);
            watch.GetField("_root", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, lbRoot);
            var db = _asm.GetType("LbIntegrations.Lbip.LbipMetadataDb", throwOnError: true);

            string Id(string title)
            {
                var args = new object[] { title, "Sega Naomi 2", null };
                var id = db.GetMethod("DatabaseIdOf", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
                return (id == null ? "null" : id.ToString()) + " " + args[2];
            }
            var compare = db.GetMethod("CompareValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var cv = (string)compare.Invoke(null, new object[] { "Virtua Fighter 4: Evolution" });
            Check("compared as the database does: \"Virtua Fighter 4: Evolution\" -> VIRTUA FIGHTER 4 EVOLUTION", cv == "VIRTUA FIGHTER 4 EVOLUTION", cv);
            var a = Id("Virtua Fighter 4");
            Check("\"Virtua Fighter 4\" on Sega Naomi 2 -> 152524, by its name (not the PS2 or Arcade game)", a == "152524 by its name", a);
            var b = Id("Club Kart Prize");
            Check("\"Club Kart Prize\" -> 36060, by its name", b == "36060 by its name", b);
            var c = Id("Virtua Fighter 4 Version C");
            Check("\"Virtua Fighter 4 Version C\" -> 152524, by an alternate title (how vf4 got there)", c == "152524 by an alternate title", c);
            var d = Id("Virtua Fighter 4: Evolution");
            Check("\"Virtua Fighter 4: Evolution\" -> 152545, the colon compared away", d == "152545 by its name", d);
            var e = Id("No Such Game Anywhere");
            Check("a title of no game -> nothing", e.StartsWith("null "), e);

            var mame = T("FlycastImportFinished").GetMethod("MameEntries", BindingFlags.NonPublic | BindingFlags.Static)
                           .Invoke(null, new object[] { new[] { "vf4b", "vf4o", "vf4cart", "clubkprz" } }) as IDictionary;
            string V(string set)
            {
                var entry = mame?[set];
                return entry == null ? "(none)" : (string)entry.GetType().GetField("Version").GetValue(entry)
                    + " | " + (string)entry.GetType().GetField("Region").GetValue(entry) + " | " + (string)entry.GetType().GetField("Year").GetValue(entry);
            }
            Check("MAME.xml: vf4b -> (Rev B) (GDS-0012B), North America, 2001", V("vf4b") == "(Rev B) (GDS-0012B) | North America | 2001", V("vf4b"));
            Check("MAME.xml: vf4o -> (GDS-0012)", V("vf4o").StartsWith("(GDS-0012) |"), V("vf4o"));
            Check("MAME.xml: vf4cart -> (World), World, 2002", V("vf4cart") == "(World) | World | 2002", V("vf4cart"));
            Check("MAME.xml: clubkprz -> (Export, Japan, Rev A), Japan, 2003", V("clubkprz") == "(Export, Japan, Rev A) | Japan | 2003", V("clubkprz"));

            Console.WriteLine(_bad == 0 ? "\n  OK - each set names its game, and its version the way LaunchBox writes one." : "\n  " + _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>The options window, fed two fake games (a Dreamcast disc and a Naomi board), one picture
        /// per tab. Nothing written.</summary>
        public static bool OptionsShot(Assembly pluginAssembly, string outPath)
        {
            _asm = pluginAssembly;
            System.Windows.Forms.Application.EnableVisualStyles();
            var formType = T("FlycastOptionsForm");
            var entryType = formType.GetNestedType("Entry", BindingFlags.NonPublic | BindingFlags.Public);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
            var only = Environment.GetEnvironmentVariable("LBIP_SHOT_ONE") == "1";
            foreach (var (title, product, kind) in only ? new[] { ("Soulcalibur", "T-1401N", "Dreamcast") } : new[] { ("Soulcalibur", "T-1401N", "Dreamcast"), ("Marvel vs. Capcom 2", (string)null, "Arcade") })
            {
                var e = Activator.CreateInstance(entryType);
                void Set(string f, object v) => entryType.GetField(f).SetValue(e, v);
                Set("Title", title); Set("GameId", title); Set("Product", product); Set("Games", Kind(kind)); Set("Line", "-config window:fullscreen=yes");
                var d = new object[] { null, product, null };
                Set("Defaults", Call("FlycastGameSettings", "DefaultsOf", d));
                // The Dreamcast one as if its own Flycast game config set the resolution (ours then loses: red) and the fog (amber).
                Set("GameConfig", product == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                                  : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "config:rend.Resolution", "config:rend.Fog" });
                Set("Own", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["config:rend.Resolution"] = "1440", ["config:pvr.rend"] = "4" });
                list.Add(e);
            }
            using var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { list }, null);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-3000, -3000);
            form.Show();
            var tabs = form.Controls.OfType<System.Windows.Forms.TabControl>().First();
            IEnumerable<System.Windows.Forms.Control> All(System.Windows.Forms.Control c) => new[] { c }.Concat(c.Controls.Cast<System.Windows.Forms.Control>().SelectMany(All));
            foreach (var bar in All(form).Where(c => c is System.Windows.Forms.Panel && c.Width == 4))
                Console.WriteLine("  bar " + bar.BackColor.Name + " at " + bar.Bounds + " visible=" + bar.Visible + " in " + bar.Parent?.GetType().Name + " " + bar.Parent?.Text);
            var shots = new List<System.Drawing.Bitmap>();
            foreach (System.Windows.Forms.TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                System.Windows.Forms.Application.DoEvents();
                var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                shots.Add(bmp);
            }
            // LBIP_SHOT_RESET=1: "Reset to defaults" pressed once the pictures are taken - see PpssppCheck.CheckReset.
            if (Environment.GetEnvironmentVariable("LBIP_SHOT_RESET") == "1") PpssppCheck.CheckReset(form, "_handText");
            form.Close();
            using var all = new System.Drawing.Bitmap(shots.Count * shots[0].Width, shots[0].Height);
            using (var g = System.Drawing.Graphics.FromImage(all))
            {
                int x = 0;
                foreach (var s in shots) { g.DrawImage(s, x, 0); x += s.Width; s.Dispose(); }
            }
            all.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("  " + outPath);
            return true;
        }
    }
}
