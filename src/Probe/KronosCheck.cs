// The Kronos plugin's pieces, run without a host:
//   --kronos-selftest      offline, temp folders only: kronos.ini as Qt writes it (an accented path escaped and read back, a value
//                          with a comma quoted, [1.0] made when absent, other sections and lines kept); the first settings and the
//                          pack's slot keys, only where absent; the session - the game's RAM / states / cartridge forced, put back
//                          after Kronos rewrote them, a key the user changed left; the line; Tradu-France's archives page parsed
//                          (a saved copy of it, or none); the save names and slots.
//   --kronos-latest        Tradu-France's latest 64-bit build, read live.
//   --kronos-real <folder of an extracted build> [--lab <folder>]
//                          THE REAL EMULATOR: a copy of the build in a fresh folder of <lab>, the lab's BIOS in a "RetroArch\system"
//                          beside it (copied by the plugin), a .m3u of the two Atlantis CHDs, a launch prepared as LaunchBox would,
//                          Kronos started with that line, a state saved with the pack's key (Ctrl+Shift+F1), quit with Ctrl+Q,
//                          then the session put back by the plugin's own watcher.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class KronosCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Kronos." + name, true);
        private static object Call(Assembly asm, string type, string method, params object[] args)
        {
            var ms = T(asm, type).GetMethods(Any).Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (ms.Count == 0) throw new MissingMethodException(type + "." + method + "/" + args.Length);
            try { return ms[0].Invoke(null, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        private static object CallOn(object o, string method, params object[] args)
        {
            var m = o.GetType().GetMethods(Inst).First(x => x.Name == method && x.GetParameters().Length == args.Length);
            try { return m.Invoke(o, args); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
        private static object F(object o, string n) => o?.GetType().GetField(n, Inst)?.GetValue(o) ?? o?.GetType().GetProperty(n, Inst)?.GetValue(o);
        private static void SetS(Assembly asm, string type, string n, object v) => T(asm, type).GetField(n, Any).SetValue(null, v);

        private static string NewTemp(string what)
        {
            var d = Path.Combine(Path.GetTempPath(), "lbip-kronos-" + what + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static object Parse(Assembly asm, string text) => Call(asm, "KronosIniDoc", "Parse", text);
        private static string Text(object doc) => (string)F(doc, "Text");

        public static bool SelfTest(Assembly asm, string page)
        {
            _bad = 0;
            var temp = NewTemp("selftest");
            SetS(asm, "KronosSettings", "PathOverride", Path.Combine(temp, "data", "settings.ini"));
            try
            {
                Console.WriteLine("== kronos.ini as Qt writes it");
                var accented = @"G:/Jeux é/Saves/Sega Saturn/Atlantis.bkr";
                var esc = (string)Call(asm, "KronosIniDoc", "Escape", accented);
                Check("an accented path escaped as Qt does (\\xe9)", esc == @"G:/Jeux \xe9/Saves/Sega Saturn/Atlantis.bkr", esc);
                Check("... and read back", (string)Call(asm, "KronosIniDoc", "Unescape", esc) == accented);
                var hexAfter = (string)Call(asm, "KronosIniDoc", "Escape", "é1");
                Check("a hex digit right after an escape escaped too", hexAfter == @"\xe9\x31" && (string)Call(asm, "KronosIniDoc", "Unescape", hexAfter) == "é1", hexAfter);
                Check("a comma: quoted (Qt would read a list otherwise)", (string)Call(asm, "KronosIniDoc", "Escape", "a, b") == "\"a, b\"");
                Check("a list read back as Qt joins it", (string)Call(asm, "KronosIniDoc", "Unescape", "a, b") == "a, b");

                var doc = Parse(asm, "[1.0]\r\nGeneral\\Geometry=@ByteArray(\\x1\\xd9)\r\nSound\\Volume=100\r\n\r\n[Other]\r\nKey=1\r\n");
                CallOn(doc, "Set", @"Memory\Path", accented);
                var text = Text(doc);
                Check("a new key written in [1.0], before the next section", text.IndexOf(@"Memory\Path=") > 0 && text.IndexOf(@"Memory\Path=") < text.IndexOf("[Other]"), text.Replace("\r\n", " | "));
                Check("Kronos's own lines kept as they are", text.Contains(@"General\Geometry=@ByteArray(\x1\xd9)") && text.Contains("[Other]\r\nKey=1"));
                Check("the value read back", (string)CallOn(doc, "Get", @"Memory\Path") == accented);
                var empty = Parse(asm, "");
                CallOn(empty, "Set", @"General\Bios", "x");
                Check("no [1.0]: made", Text(empty).Contains("[1.0]\r\nGeneral\\Bios=x"), Text(empty).Replace("\r\n", " | "));
                Check("a key at the root is not [1.0]'s", CallOn(Parse(asm, "General\\Bios=root\r\n[1.0]\r\n"), "Get", @"General\Bios") == null);

                Console.WriteLine("== the first settings and the pack's keys");
                var fresh = Parse(asm, "[1.0]\r\nShortcuts\\Save3=F8\r\n");
                Check("SH2Interpreter=8 written when absent", (bool)Call(asm, "KronosLaunch", "Bootstrap", fresh) && (string)CallOn(fresh, "Get", @"Advanced\SH2Interpreter") == "8");
                Check("... and not twice", !(bool)Call(asm, "KronosLaunch", "Bootstrap", fresh));
                var bound = (IList)Call(asm, "KronosLaunch", "BindPackKeys", fresh);
                Check("19 slot keys bound, the user's Save3 left", bound.Count == 19 && (string)CallOn(fresh, "Get", @"Shortcuts\Save3") == "F8", bound.Count + " bound");
                Check("slot 1: Ctrl+Shift+F1 / Shift+F1, slot 0: F10", (string)CallOn(fresh, "Get", @"Shortcuts\Save1") == "Ctrl+Shift+F1"
                      && (string)CallOn(fresh, "Get", @"Shortcuts\Load1") == "Shift+F1" && (string)CallOn(fresh, "Get", @"Shortcuts\Save0") == "Ctrl+Shift+F10");
                Check("a key rebound by the user is said", !(bool)Call(asm, "KronosLaunch", "SlotKeysAreOurs", fresh));
                Check("a second pass binds nothing", ((IList)Call(asm, "KronosLaunch", "BindPackKeys", fresh)).Count == 0);

                Console.WriteLine("== the session");
                var exe = Path.Combine(temp, "Kronos", "kronos.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(exe));
                var ini = Path.Combine(Path.GetDirectoryName(exe), "kronos.ini");
                var before = "[1.0]\r\nMemory\\Path=C:/Users/x/AppData/Roaming/kronos/bkram.bin\r\nMemory\\ExtendMemory=true\r\nCartridge\\Type=6\r\nSound\\Volume=80\r\n";
                File.WriteAllText(ini, before);
                Check("the ini beside the exe is the one read", (string)Call(asm, "KronosLaunch", "SettingsOf", exe) == ini);
                var sdoc = Call(asm, "KronosIniDoc", "Load", ini);
                var ram = Path.Combine(temp, "Kronos", "Saves", "Sega Saturn", "Atlantis é.bkr").Replace('\\', '/');
                var states = Path.Combine(temp, "Kronos", "Saves", "Sega Saturn", "States", "Atlantis é").Replace('\\', '/');
                var tuple = typeof(ValueTuple<string, string, bool, string>);
                var keys = Array.CreateInstance(tuple, 4);
                keys.SetValue((@"Memory\Path", ram, true, (string)null), 0);
                keys.SetValue((@"Memory\ExtendMemory", "false", false, (string)null), 1);
                keys.SetValue((@"General\SaveStates", states, true, (string)null), 2);
                keys.SetValue((@"Cartridge\Type", "2", false, (string)null), 3);
                var forced = (IList)Call(asm, "KronosSession", "Force", sdoc, keys);
                CallOn(sdoc, "Save", ini);
                Check("4 keys forced", forced.Count == 4, forced.Count + "");
                Call(asm, "KronosSession", "Begin", exe, ini, forced, null, "g1");
                // What Kronos writes when it quits: every key as it read it, its geometry added; the user turned the volume
                // down and set the cartridge to a RAM expansion during the game.
                var during = File.ReadAllText(ini);
                File.WriteAllText(ini, during.Replace("Cartridge\\Type=2", "Cartridge\\Type=7").Replace("Sound\\Volume=80", "Sound\\Volume=50") + "General\\Geometry=@ByteArray(\\x1)\r\n");
                Call(asm, "KronosSession", "Recover");
                var after = File.ReadAllText(ini);
                Check("the RAM, its size and the states' folder put back", after.Contains("Memory\\Path=C:/Users/x/AppData/Roaming/kronos/bkram.bin") && after.Contains("Memory\\ExtendMemory=true")
                      && !after.Contains("SaveStates"), after.Replace("\r\n", " | "));
                Check("the cartridge changed in Kronos during the game: the user's, left", after.Contains("Cartridge\\Type=7"));
                Check("Kronos's own changes kept", after.Contains("Sound\\Volume=50") && after.Contains("General\\Geometry"));
                Check("the plan is done", !File.Exists((string)T(asm, "KronosSession").GetProperty("PlanPath", Any).GetValue(null)));

                Console.WriteLine("== a game's own options");
                File.WriteAllText(ini, "[1.0]\r\nVideo\\resolution_mode=1\r\nGeneral\\SystemLanguageID=0\r\nCartridge\\Type=6\r\nCartridge\\Path=C:/old/cart.bin\r\nVideo\\filter_type=0\r\n");
                Call(asm, "KronosGameOptions", "SetGameValue", "g2", "Resolution", "32");
                Call(asm, "KronosGameOptions", "SetGameValue", "g2", "Language", "2");
                Call(asm, "KronosGameOptions", "SetGameValue", "g2", "Expansion", "dram32");
                Call(asm, "KronosGameOptions", "SetGameValue", "g2", "Filter", "raw:11");
                var odoc = Call(asm, "KronosIniDoc", "Load", ini);
                var forG2 = Call(asm, "KronosGameOptions", "KeysFor", "g2", exe, odoc);
                var okeys = ((IEnumerable)forG2.GetType().GetField("Item1").GetValue(forG2)).Cast<object>()
                    .Select(t => (string)t.GetType().GetField("Item1").GetValue(t) + "=" + (string)t.GetType().GetField("Item2").GetValue(t)).ToList();
                Check("the keys: 4x, French, 4 MB RAM (its path held as it is), a value Kronos wrote kept",
                      okeys.SequenceEqual(new[] { "Video\\resolution_mode=32", "Video\\filter_type=11", "General\\SystemLanguageID=2", "Cartridge\\Type=7", "Cartridge\\Path=C:/old/cart.bin" }),
                      string.Join(", ", okeys));
                var oforced = (IList)Call(asm, "KronosSession", "Force", odoc, forG2.GetType().GetField("Item1").GetValue(forG2));
                CallOn(odoc, "Save", ini);
                Call(asm, "KronosSession", "Begin", exe, ini, oforced, null, "g2");
                // During the game the user sets 3x in Kronos's settings window; Kronos writes everything back when it quits.
                File.WriteAllText(ini, File.ReadAllText(ini).Replace("Video\\resolution_mode=32", "Video\\resolution_mode=128"));
                Call(asm, "KronosSession", "Recover");
                var oafter = File.ReadAllText(ini);
                Check("every game's values put back", oafter.Contains("Video\\resolution_mode=1\r\n") && oafter.Contains("General\\SystemLanguageID=0") && oafter.Contains("Cartridge\\Type=6")
                      && oafter.Contains("Video\\filter_type=0"), oafter.Replace("\r\n", " | "));
                Check("changed in Kronos during the game: 3x now the game's own", (string)Call(asm, "KronosGameOptions", "GameValue", "g2", "Resolution") == "128");
                Check("the others its own as they were", (string)Call(asm, "KronosGameOptions", "GameValue", "g2", "Language") == "2" && (string)Call(asm, "KronosGameOptions", "GameValue", "g2", "Expansion") == "dram32");
                // A ROM cartridge: its file in Kronos's cart folder, written whole; gone, the option is left out.
                var cartDir = (string)Call(asm, "KronosLaunch", "CartDirOf", exe);
                Directory.CreateDirectory(Path.Combine(cartDir, "snk"));
                File.WriteAllBytes(Path.Combine(cartDir, "snk", "kof95 é.bin"), new byte[16]);
                var choices = ((IEnumerable)((Delegate)T(asm, "KronosGameOption").GetField("Choices").GetValue(Call(asm, "KronosGameOptions", "Option", "Expansion"))).DynamicInvoke(exe)).Cast<object>()
                    .Select(t => (string)t.GetType().GetField("Item1").GetValue(t)).ToList();
                Check("the cart folder's ROM offered", choices.Contains("rom:snk\\kof95 é.bin"), string.Join(", ", choices));
                Call(asm, "KronosGameOptions", "SetGameValue", "g3", "Expansion", "rom:snk\\kof95 é.bin");
                var forG3 = Call(asm, "KronosGameOptions", "KeysFor", "g3", exe, Call(asm, "KronosIniDoc", "Load", ini));
                var rkeys = ((IEnumerable)forG3.GetType().GetField("Item1").GetValue(forG3)).Cast<object>()
                    .Select(t => (string)t.GetType().GetField("Item1").GetValue(t) + "=" + (string)t.GetType().GetField("Item2").GetValue(t)).ToList();
                var romText = Path.Combine(cartDir, "snk", "kof95 é.bin").Replace('\\', '/');
                Check("ROM cartridge: type 9 and the file whole", rkeys.SequenceEqual(new[] { "Cartridge\\Type=9", "Cartridge\\Path=" + romText }), string.Join(", ", rkeys));
                var rdoc = Call(asm, "KronosIniDoc", "Load", ini);
                Call(asm, "KronosSession", "Force", rdoc, forG3.GetType().GetField("Item1").GetValue(forG3));
                Check("read back from kronos.ini relative to the cart folder", (string)Call(asm, "KronosGameOptions", "FromIni", "Expansion", rdoc, exe) == "rom:snk\\kof95 é.bin",
                      (string)Call(asm, "KronosGameOptions", "FromIni", "Expansion", rdoc, exe));
                File.Delete(Path.Combine(cartDir, "snk", "kof95 é.bin"));
                var gone = Call(asm, "KronosGameOptions", "KeysFor", "g3", exe, rdoc);
                var left = ((IEnumerable)gone.GetType().GetField("Item2").GetValue(gone)).Cast<string>().ToList();
                Check("its file gone: the option left out, said", ((IEnumerable)gone.GetType().GetField("Item1").GetValue(gone)).Cast<object>().Count() == 0 && left.Count == 1, string.Join(", ", left));
                Check("a backup cartridge in the ini is not this option's", (string)((Delegate)T(asm, "KronosGameOption").GetField("Read").GetValue(Call(asm, "KronosGameOptions", "Option", "Expansion")))
                      .DynamicInvoke(new Func<string, string>(k => k == "Cartridge\\Type" ? "2" : null)) == null);

                Console.WriteLine("== the line");
                var line = (string)Call(asm, "KronosLaunch", "CommandLine", "-ns \"C:\\g\\a b.m3u\" -f -i x", true, @"C:\K\bios\mpr-17933.bin", @"C:\g\a b.m3u", @"C:\g\a b (Disc 1).chd", true);
                Check("-a -f --bios= then the user's -ns, -i <disc> last, the .m3u and our switches gone", line == "-a -f --bios=C:\\K\\bios\\mpr-17933.bin -ns -i \"C:\\g\\a b (Disc 1).chd\"", line);
                var line2 = (string)Call(asm, "KronosLaunch", "CommandLine", "", false, null, null, @"C:\g\x.cue", true);
                Check("windowed, no BIOS found: -a -i <disc>", line2 == "-a -i C:\\g\\x.cue", line2);
                var line3 = (string)Call(asm, "KronosLaunch", "CommandLine", "-ns \"C:\\mame\\shienryu.zip\" -f", false, null, @"C:\mame\shienryu.zip", null, false);
                Check("an ST-V game (PrepareStv: no full screen on the line): neither -a nor -f nor a disc, the set gone, the user's -ns kept", line3 == "-ns", line3);

                Console.WriteLine("== Tradu-France's archives page");
                var html = page != null && File.Exists(page) ? File.ReadAllText(page)
                    : "<a href=\"/include/pages/download.php?file=holodeck_admin%2FEmulateur%2Femulateur%2FSEGA%2FSaturn%2FKronos%2Farchives%2FKronos_2.7.5_-_64_Bits.zip\">"
                      + "&#11015; Kronos_2.7.5_-_64_Bits.zip (49,13 Mo)</a> <a href=\"/include/pages/download.php?file=holodeck_admin%2FEmulateur%2Femulateur%2FSEGA%2FSaturn%2FKronos%2Farchives%2FKronos%202.6.1%2064%20Bits.zip\">"
                      + "&#11015; Kronos 2.6.1 64 Bits.zip (20,81 Mo)</a>";
                var builds = (IList)Call(asm, "KronosBuild", "Parse", html);
                var v = builds.Cast<object>().Select(b => (string)F(b, "Version")).ToList();
                Check("both name forms read, with their sizes", v.Contains("2.7.5") && v.Contains("2.6.1") && builds.Cast<object>().All(b => F(b, "MiB") != null), string.Join(", ", builds.Cast<object>().Select(b => F(b, "Version") + " " + F(b, "MiB"))));
                var best = builds.Cast<object>().First(b => (string)F(b, "Version") == "2.7.5");
                Check("2.7.5: 49.13 MiB, its link", (double)F(best, "MiB") == 49.13 && ((string)F(best, "Url")).StartsWith("https://tradu-france.com/include/pages/download.php?file="), (string)F(best, "Url"));
                Check("2.7.5 newer than 2.7.0 and 2.6.1", (bool)Call(asm, "KronosBuild", "IsNewer", "2.7.5", "2.7.0") && !(bool)Call(asm, "KronosBuild", "IsNewer", "2.6.1", "2.7.0"));

                Console.WriteLine("== the saves");
                Check("Kronos's slot 0 is the pack's 10, its 1 the pack's 1", (int)Call(asm, "KronosPlugin", "PackSlot", 0) == 10 && (int)Call(asm, "KronosPlugin", "KronosSlot", 10) == 0
                      && (int)Call(asm, "KronosPlugin", "PackSlot", 1) == 1);
                var rom = @"C:\g\Atlantis - The Lost Tales (FR) (Disc 2).chd";
                Check("the RAM: the game's name without its disc, .bkr", ((string)Call(asm, "KronosPlugin", "RamPath", exe, "Sega Saturn", rom)).EndsWith(@"Saves\Sega Saturn\Atlantis - The Lost Tales (FR).bkr"));
                Check("its states: a folder of its own", ((string)Call(asm, "KronosPlugin", "StatesDir", exe, "Sega Saturn", rom)).EndsWith(@"Saves\Sega Saturn\States\Atlantis - The Lost Tales (FR)"));
                var sd = Path.Combine(temp, "st"); Directory.CreateDirectory(sd);
                File.WriteAllBytes(Path.Combine(sd, "MK-8109109_001.yss"), new byte[4]);
                File.WriteAllBytes(Path.Combine(sd, "MK-8109109_000.yss"), new byte[4]);
                File.WriteAllBytes(Path.Combine(sd, "notes.txt"), new byte[4]);
                var found = ((IEnumerable)Call(asm, "KronosPlugin", "StatesOf", sd)).Cast<object>().Select(t => F(t, "Item2") + "#" + F(t, "Item3")).OrderBy(x => x).ToList();
                Check("the states found: product code and pack slot", found.SequenceEqual(new[] { "MK-8109109#1", "MK-8109109#10" }), string.Join(", ", found));

                Console.WriteLine("== the CHDs Kronos cannot read (Zstandard): handed over uncompressed");
                var refChd = @"G:\saturn\Atlantis - The Lost Tales (FR) (Disc 1).chd";
                var zstdChd = @"G:\LB1326\lbip-kronos-lab\formats\Atlantis-zstd.chd";
                var noneChd = @"G:\LB1326\lbip-kronos-lab\formats\Atlantis-none.chd";
                if (File.Exists(refChd) && File.Exists(zstdChd))
                {
                    Check("cdlz,cdzl,cdfl: read by Kronos, given as it is", Call(asm, "KronosChd", "Unreadable", refChd) == null);
                    if (File.Exists(noneChd)) Check("uncompressed: read by Kronos (measured)", Call(asm, "KronosChd", "Unreadable", noneChd) == null);
                    var why = (string)Call(asm, "KronosChd", "Unreadable", zstdChd);
                    Check("cdzs: not read by Kronos", why != null && why.Contains("cdzs"), why);
                }
                else Console.WriteLine("  (the lab's CHDs are not there: not checked)");
            }
            catch (Exception ex) { Check("no exception", false, ex.ToString()); }
            finally { try { Directory.Delete(temp, true); } catch { } SetS(asm, "KronosSettings", "PathOverride", null); }
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>--kronos-ahk &lt;folder&gt;: the plugin's AHK scripts written there (running.ahk, exit.ahk), to run them by hand.</summary>
        public static bool Ahk(Assembly asm, string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "running.ahk"), (string)T(asm, "KronosAhk").GetField("Running", Any).GetValue(null));
            File.WriteAllText(Path.Combine(folder, "exit.ahk"), (string)T(asm, "KronosAhk").GetField("Exit", Any).GetValue(null));
            Console.WriteLine("written in " + folder);
            return true;
        }

        public static bool Latest(Assembly asm, string fetchTo = null)
        {
            var r = Call(asm, "KronosBuild", "Latest");
            Console.WriteLine("latest: " + (r == null ? "none" : F(r, "Version") + " " + F(r, "FileName") + " " + F(r, "MiB") + " MiB\n  " + F(r, "Url")));
            if (r != null && fetchTo != null)
            {
                // The plugin's own download: the site's headers, the size the page gives, a zip.
                Call(asm, "KronosBuild", "Fetch", r, fetchTo, (Action<double>)null, (Func<bool>)null);
                Console.WriteLine("  fetched " + new FileInfo(fetchTo).Length + " bytes -> " + fetchTo);
            }
            return r != null;
        }

        // ══════════════════════════════════════════════════════════════════════

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte sc, int f, IntPtr x);

        private static void Keys(params byte[] vks)
        {
            foreach (var k in vks) { keybd_event(k, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60); }
            foreach (var k in vks.Reverse()) { keybd_event(k, 0, 2, IntPtr.Zero); System.Threading.Thread.Sleep(40); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
        private struct Rect { public int L, T, R, B; }

        /// <summary>--chd-names --lb &lt;root&gt; &lt;chd&gt;: the helper's name= on view=chd, through the real AIM, with names made to break
        /// it - spaces, = and " id=" (the result line's own words), % [ ] { } &amp; ; # ~ ' , ! ^ $ @ `, accents, a dash, a star,
        /// Japanese, an emoji, a device name, 255 characters and more. For each: the name served is the one the rule gives
        /// (computed here, apart from the helper), the file is there, its size and its bytes at five places are the uncompressed
        /// stream's, and it is gone once released. MOUNTS DISKS, one at a time.</summary>
        public static bool ChdNames(Assembly asm, string lb, string chd)
        {
            _bad = 0;
            asm.GetType("LbIntegrations.RamDisk.RamDiskHost", true).GetMethod("UseRoot", Any).Invoke(null, new object[] { lb });
            var drive = asm.GetType("LbIntegrations.RamDisk.RamDrive", true);
            var attach = drive.GetMethod("AttachChd", Any);
            var release = asm.GetType("LbIntegrations.Kronos.KronosChd", true).GetMethod("Release", Any);
            using var plain = (Stream)asm.GetType("LbIntegrations.Disc.ChdPlain", true).GetMethod("Open", Any).Invoke(null, new object[] { chd });
            long length = plain.Length;
            var at = new[] { 0L, 4096L, length / 3, length / 2 + 12345, Math.Max(0, length - 65536) };
            var expected = new byte[at.Length][];
            for (int i = 0; i < at.Length; i++)
            {
                plain.Position = at[i];
                expected[i] = new byte[65536];
                int got = 0, n;
                while (got < expected[i].Length && (n = plain.Read(expected[i], got, expected[i].Length - got)) > 0) got += n;
                Array.Resize(ref expected[i], got);
            }
            Console.WriteLine("-- name= on view=chd: " + chd + " (" + length + " bytes uncompressed) --");

            var names = new[]
            {
                "Atlantis - The Lost Tales (FR) (Disc 1).chd",
                "   spaces before and after   .chd   ",
                "a  b   c.chd",
                "a=b=c.chd",
                "=starts with equals.chd",
                "#starts with a hash.chd",
                "x id=abc file=y at=z.chd",
                "trailing id=0123abcd.chd",
                "100% [v1.0] {Rev 2} & co; #1 ~ ' , ! ^ $ @ `.chd",
                "Été à Noël – ★ ゲーム.chd",
                "🎮 Saturn 🎮.chd",
                "CON.chd",
                "com7.chd",
                "name ending with dots...chd",
                ".chd",
                "noextension",
                "game.chd",
                new string('L', 251) + ".chd",
                new string('M', 300) + ".chd",
                "",
            };
            foreach (var name in names)
            {
                var want = Rule(name);
                var args = new object[] { chd, null, null, name };
                var file = (string)attach.Invoke(null, args);
                var root = (string)args[1];
                var shown = name.Length > 60 ? name.Substring(0, 30) + "...(" + name.Length + ")" : name;
                if (file == null) { Check("[" + shown + "] attached", false, (string)args[2]); if (root != null) release.Invoke(null, new object[] { root }); continue; }
                var served = Path.GetFileName(file);
                bool same = served == want;
                bool there = File.Exists(file);
                bool sized = there && new FileInfo(file).Length == length;
                bool bytes = there;
                if (there)
                    using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        for (int i = 0; i < at.Length && bytes; i++)
                        {
                            f.Position = at[i];
                            var b = new byte[expected[i].Length];
                            int got = 0, n;
                            while (got < b.Length && (n = f.Read(b, got, b.Length - got)) > 0) got += n;
                            bytes = got == b.Length && b.AsSpan().SequenceEqual(expected[i]);
                        }
                bool listed = there && Directory.GetFiles(root).Select(Path.GetFileName).SequenceEqual(new[] { want });
                release.Invoke(null, new object[] { root });
                for (int i = 0; i < 40 && File.Exists(file); i++) System.Threading.Thread.Sleep(250);
                bool gone = !File.Exists(file);
                var wantShown = want.Length > 60 ? want.Substring(0, 30) + "...(" + want.Length + ")" : want;
                Check("[" + shown + "] -> [" + wantShown + "]: served under it, listed alone, size, bytes, detached",
                      same && there && listed && sized && bytes && gone,
                      (same ? "" : "served [" + served + "] ") + (there ? "" : "absent ") + (listed ? "" : "listing differs ") + (sized ? "" : "size ") + (bytes ? "" : "bytes ") + (gone ? "" : "still there"));
            }
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;

            // The rule, written apart from the helper: printable ASCII but \ / : * ? " < > | (each other UTF-16 unit one _), no
            // space at either end nor dot at the end, a device name given a _ in front, 255 at most keeping the extension;
            // the result line's reader drops spaces at both ends of what it reads, as the helper's cfg reader does.
            static string Rule(string asked)
            {
                var t = (asked ?? "").Trim();
                if (t.Length == 0) return "game.chd";
                var s = new string(t.Select(c => c < 32 || c > 126 || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.').TrimEnd();
                if (s.Length == 0) return "game.chd";
                var dot = s.LastIndexOf('.');
                var stem = dot > 0 ? s.Substring(0, dot) : s;
                var ext = dot > 0 ? s.Substring(dot) : "";
                var bare = stem.TrimEnd().ToUpperInvariant();
                if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(bare) || (bare.Length == 4 && (bare.StartsWith("COM") || bare.StartsWith("LPT")) && char.IsDigit(bare[3]))) stem = "_" + stem;
                if (stem.Length + ext.Length > 255) stem = stem.Substring(0, 255 - ext.Length).TrimEnd().TrimEnd('.');
                return stem + ext;
            }
        }

        /// <summary>--kronos-stv <build> --game <set.zip> [--platform "Sega ST-V"|Arcade] [--lab <folder>]: the real Kronos on an
        /// ST-V set, prepared by the plugin as LaunchBox would (its own folder, kronos.ini pointed at it). Windowed, no key sent:
        /// the window captured with PrintWindow at a few moments, then quit with its own Quit button (UI Automation)
        /// - its EEPROM taken back into the game's saves. One install of the build in <lab>\probe-stv, kept between runs.</summary>
        public static bool Stv(Assembly asm, string release, string game, string platform, string lab)
        {
            _bad = 0;
            lab ??= @"G:\LB1326\lbip-kronos-lab";
            platform ??= "Sega ST-V";
            var run = Path.Combine(lab, "probe-stv");
            var dir = Path.Combine(run, "Emulators", "Nixx-Kronos");
            var data = Path.Combine(run, "plugin-data");
            if (!File.Exists(Path.Combine(dir, "kronos.exe")))
                foreach (var f in Directory.GetFiles(release, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(dir, Path.GetRelativePath(release, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(f, to, true);
                }
            Directory.CreateDirectory(data);
            var exe = Path.Combine(dir, "kronos.exe");
            Console.WriteLine("-- THE REAL KRONOS ON AN ST-V SET: " + game + " (" + platform + ") --");
            SetS(asm, "KronosSettings", "PathOverride", Path.Combine(data, "settings.ini"));
            File.WriteAllText(Path.Combine(data, "settings.ini"), "Fullscreen=false\r\n");
            var said = new List<string>();
            asm.GetType("LbIntegrations.Lbip.LbipNotice", true).GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);
            var ini = Path.Combine(dir, "kronos.ini");
            if (!File.Exists(ini)) File.WriteAllText(ini, "[1.0]\r\nSound\\Volume=90\r\n");
            var before = File.ReadAllText(ini);

            LbIntegrations.Catalog.LbCatalog.HostWillAsk = true;
            var type = asm.GetTypes().Single(t => typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract);
            var plugin = (Unbroken.LaunchBox.Plugins.EmulatorPlugin)Activator.CreateInstance(type);
            var emu = new StubEmulator { Title = "Nixx-Kronos", ApplicationPath = exe };
            var set = Path.GetFileNameWithoutExtension(game);
            var g = StubGame.With(StubGame.Create("stv-" + set, set, game), "Platform", platform);
            var r = plugin.PrepareEmulatorForLaunch(new Unbroken.LaunchBox.Plugins.PrepareForLaunchArgs(emu, g, "\"" + game + "\"", null, null));
            Check("prepared", r.WasSuccess, r.NewCommandLine ?? said.FirstOrDefault());
            if (!r.WasSuccess) { SetS(asm, "KronosSettings", "PathOverride", null); Console.WriteLine(_bad + " FAILED"); return false; }
            Console.WriteLine("  line: " + r.NewCommandLine);
            var stvDir = Path.Combine(dir, "lbip-stv");
            Console.WriteLine("  its folder: " + string.Join(", ", Directory.GetFiles(stvDir).Select(Path.GetFileName)));

            var psi = new System.Diagnostics.ProcessStartInfo(exe, r.NewCommandLine) { WorkingDirectory = run, UseShellExecute = false };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                foreach (var at in new[] { 8, 16, 30, 45 })
                {
                    System.Threading.Thread.Sleep(at == 8 ? 8000 : at == 16 ? 8000 : at == 30 ? 14000 : 15000);
                    p.Refresh();
                    if (p.HasExited) { Console.WriteLine("  Kronos has gone at " + at + " s, exit " + p.ExitCode); break; }
                    Console.WriteLine("  " + at + " s: window \"" + p.MainWindowTitle + "\"");
                    try
                    {
                        GetWindowRect(p.MainWindowHandle, out var wr);
                        using var bmp = new System.Drawing.Bitmap(Math.Max(1, wr.R - wr.L), Math.Max(1, wr.B - wr.T));
                        using (var gr = System.Drawing.Graphics.FromImage(bmp)) { var dc = gr.GetHdc(); PrintWindow(p.MainWindowHandle, dc, 2); gr.ReleaseHdc(dc); }
                        var shot = Path.Combine(run, set + "-" + at + "s.png");
                        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                        Console.WriteLine("    captured: " + shot);
                    }
                    catch (Exception ex) { Console.WriteLine("    (no capture: " + ex.Message + ")"); }
                }
                Check("Kronos still running after 45 s", !p.HasExited);
                if (!p.HasExited)
                {
                    // Windowed, WM_CLOSE quits Kronos cleanly (only full screen crashes on it): the EEPROM written as it goes.
                    p.CloseMainWindow();
                    var closed = p.WaitForExit(20000);
                    Check("closed by WM_CLOSE (windowed), exit " + (closed ? p.ExitCode.ToString() : "-"), closed && p.ExitCode == 0);
                    if (!closed) p.Kill();
                }
            }
            var plan = (string)T(asm, "KronosSession").GetProperty("PlanPath", Any).GetValue(null);
            for (int i = 0; i < 60 && File.Exists(plan); i++) System.Threading.Thread.Sleep(250);
            Check("the session put back by the plugin's watcher", !File.Exists(plan));
            var after = File.ReadAllText(ini);
            Check("no ST-V key of the session left in kronos.ini", !after.Contains("Cartridge\\STVGame=") && !after.Contains("Cartridge\\Type=11") && !after.Contains("STVRomgame"),
                  after.Replace("\r\n", " | "));
            var saves = Path.Combine(dir, "Saves", platform);
            var nv = Path.Combine(saves, set + ".nv");
            Check("its EEPROM in the game's saves (" + nv + ")", File.Exists(nv), File.Exists(nv) ? new FileInfo(nv).Length + " bytes" : "absent; the folder had " + string.Join(", ", Directory.GetFiles(stvDir).Select(Path.GetFileName)));
            SetS(asm, "KronosSettings", "PathOverride", null);
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>The real Kronos on a Zstandard CHD it cannot read: the plugin hands it the disc uncompressed (served or
        /// copied), Kronos boots it, and once it has gone the served disc is detached. Windowed, no key sent: the window is
        /// captured with PrintWindow, then the process ended.</summary>
        public static bool Zstd(Assembly asm, string release, string lab, string lb = null, string disc = null)
        {
            _bad = 0;
            if (lb != null) asm.GetType("LbIntegrations.RamDisk.RamDiskHost", true).GetMethod("UseRoot", Any).Invoke(null, new object[] { lb });
            lab ??= @"G:\LB1326\lbip-kronos-lab";
            var zstd = disc ?? Path.Combine(lab, "formats", "Atlantis-zstd.chd");
            var run = Path.Combine(lab, "probe-zstd-" + DateTime.Now.ToString("HHmmss"));
            var dir = Path.Combine(run, "Emulators", "Nixx-Kronos");
            var ra = Path.Combine(run, "Emulators", "RetroArch", "system");
            var data = Path.Combine(run, "plugin-data");
            Directory.CreateDirectory(dir); Directory.CreateDirectory(ra); Directory.CreateDirectory(data);
            foreach (var f in Directory.GetFiles(release, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir, Path.GetRelativePath(release, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to);
            }
            File.Copy(@"G:\LB1326\lbip-ymir-lab\prof1\roms\ipl\mpr-17933.bin", Path.Combine(ra, "mpr-17933.bin"));
            var exe = Path.Combine(dir, "kronos.exe");
            Console.WriteLine("-- THE REAL KRONOS ON A ZSTANDARD CHD: " + zstd + " --");
            SetS(asm, "KronosSettings", "PathOverride", Path.Combine(data, "settings.ini"));
            File.WriteAllText(Path.Combine(data, "settings.ini"), "Fullscreen=false\r\n");
            var said = new List<string>();
            asm.GetType("LbIntegrations.Lbip.LbipNotice", true).GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);
            File.WriteAllText(Path.Combine(dir, "kronos.ini"), "[1.0]\r\nSound\\Volume=90\r\n");

            LbIntegrations.Catalog.LbCatalog.HostWillAsk = true;
            var type = asm.GetTypes().Single(t => typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract);
            var plugin = (Unbroken.LaunchBox.Plugins.EmulatorPlugin)Activator.CreateInstance(type);
            var emu = new StubEmulator { Title = "Nixx-Kronos", ApplicationPath = exe };
            var g = StubGame.With(StubGame.Create("zstd-kronos", "Atlantis - The Lost Tales", zstd), "Platform", "Sega Saturn");
            // LBIP_PROBE_OPTS="Filter=6;Resolution=32": the game's own options for this run (its window's values).
            foreach (var kv in (Environment.GetEnvironmentVariable("LBIP_PROBE_OPTS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                Call(asm, "KronosGameOptions", "SetGameValue", "zstd-kronos", kv.Split('=')[0], kv.Substring(kv.IndexOf('=') + 1));
            var r = plugin.PrepareEmulatorForLaunch(new Unbroken.LaunchBox.Plugins.PrepareForLaunchArgs(emu, g, "\"" + zstd + "\"", null, null));
            Check("prepared", r.WasSuccess, r.NewCommandLine ?? said.FirstOrDefault());
            if (!r.WasSuccess) return false;
            Console.WriteLine("  line: " + r.NewCommandLine);
            var given = System.Text.RegularExpressions.Regex.Match(r.NewCommandLine, "-i \"?(.+?)\"?$").Groups[1].Value;
            Check("Kronos given another file than the Zstandard CHD", given.Length > 0 && !string.Equals(given, zstd, StringComparison.OrdinalIgnoreCase), given);
            Check("that file is a CHD Kronos reads", File.Exists(given) && Call(asm, "KronosChd", "Unreadable", given) == null, given);

            var psi = new System.Diagnostics.ProcessStartInfo(exe, r.NewCommandLine) { WorkingDirectory = run, UseShellExecute = false };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                foreach (var at in new[] { 6, 10, 14, 20 })
                {
                System.Threading.Thread.Sleep(at == 6 ? 6000 : at == 10 ? 4000 : at == 14 ? 4000 : 6000);
                p.Refresh();
                Console.WriteLine("  window \"" + p.MainWindowTitle + "\"");
                if (at == 20) Check("Kronos runs the disc (its title names the file it was given)", p.MainWindowTitle.IndexOf(Path.GetFileName(given), StringComparison.OrdinalIgnoreCase) >= 0, p.MainWindowTitle);
                try
                {
                    GetWindowRect(p.MainWindowHandle, out var wr);
                    using var bmp = new System.Drawing.Bitmap(Math.Max(1, wr.R - wr.L), Math.Max(1, wr.B - wr.T));
                    using (var gr = System.Drawing.Graphics.FromImage(bmp)) { var dc = gr.GetHdc(); PrintWindow(p.MainWindowHandle, dc, 2); gr.ReleaseHdc(dc); }
                    var shot = Path.Combine(run, "zstd-window-" + at + "s.png");
                    bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("  window captured: " + shot);
                }
                catch (Exception ex) { Console.WriteLine("  (no capture: " + ex.Message + ")"); }
                }
                p.Kill();
                p.WaitForExit(10000);
            }
            var plan = (string)T(asm, "KronosSession").GetProperty("PlanPath", Any).GetValue(null);
            for (int i = 0; i < 60 && File.Exists(plan); i++) System.Threading.Thread.Sleep(250);
            Check("the session put back by the plugin's watcher", !File.Exists(plan));
            var copied = given.Length > 0 && given.IndexOf(@"\lbip-chd\", StringComparison.OrdinalIgnoreCase) >= 0;
            Console.WriteLine("  " + (copied ? "copied into the cache" : "served where it is"));
            if (!copied)
            {
                for (int i = 0; i < 40 && File.Exists(given); i++) System.Threading.Thread.Sleep(250);
                Check("the served disc detached once Kronos has gone", !File.Exists(given), given);
            }
            SetS(asm, "KronosSettings", "PathOverride", null);
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>--kronos-shot <png> --rom <disc>: a game's window drawn off screen (a stub game), tall enough for every card.</summary>
        public static bool Shot(Assembly asm, string png, string rom)
        {
            SetS(asm, "KronosSettings", "PathOverride", Path.Combine(Path.GetTempPath(), "lbip-kronos-shot-settings.ini"));
            var game = StubGame.With(StubGame.Create("kronos-shot", Path.GetFileNameWithoutExtension(rom), rom), "Platform", "Sega Saturn");
            var formType = asm.GetType("LbIntegrations.Kronos.KronosGameForm", true);
            using var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { new Unbroken.LaunchBox.Plugins.Data.IGame[] { game } }, null);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new System.Drawing.Point(-4000, 0);
            form.ShowInTaskbar = false;
            form.ClientSize = new System.Drawing.Size(720, 1900);
            form.Show(); System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(300); System.Windows.Forms.Application.DoEvents();
            using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(png);
            Console.WriteLine("  " + png);
            return true;
        }

        public static bool Real(Assembly asm, string release, string lab)
        {
            _bad = 0;
            lab ??= @"G:\LB1326\lbip-kronos-lab";
            var run = Path.Combine(lab, "probe-real-" + DateTime.Now.ToString("HHmmss"));
            var dir = Path.Combine(run, "Emulators", "Nixx-Kronos");
            var ra = Path.Combine(run, "Emulators", "RetroArch", "system");
            var data = Path.Combine(run, "plugin-data");
            Directory.CreateDirectory(dir); Directory.CreateDirectory(ra); Directory.CreateDirectory(data);
            foreach (var f in Directory.GetFiles(release, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir, Path.GetRelativePath(release, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to);
            }
            File.Copy(@"G:\LB1326\lbip-ymir-lab\prof1\roms\ipl\mpr-17933.bin", Path.Combine(ra, "mpr-17933.bin"));
            var exe = Path.Combine(dir, "kronos.exe");
            Console.WriteLine("-- THE REAL KRONOS: " + exe + " --");
            SetS(asm, "KronosSettings", "PathOverride", Path.Combine(data, "settings.ini"));
            File.WriteAllText(Path.Combine(data, "settings.ini"), "Fullscreen=true\r\n");
            var said = new List<string>();
            asm.GetType("LbIntegrations.Lbip.LbipNotice", true).GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);

            var chds = Directory.GetFiles(@"G:\saturn", "Atlantis*.chd", SearchOption.AllDirectories).OrderBy(p => p).ToList();
            var games = Path.Combine(run, "games"); Directory.CreateDirectory(games);
            var m3u = Path.Combine(games, "Atlantis - The Lost Tales (FR).m3u");
            File.WriteAllLines(m3u, chds);
            var ini = Path.Combine(dir, "kronos.ini");
            var userIni = "[1.0]\r\nSound\\Volume=90\r\n";
            File.WriteAllText(ini, userIni);

            var cart = Path.Combine(dir, "Saves", "Sega Saturn", "Atlantis - The Lost Tales (FR).bcr");
            var bcr = Directory.GetFiles(@"G:\LB1326\Emulators\RetroArch\saves\Beetle Saturn", "Atlantis*.bcr").FirstOrDefault();
            if (bcr != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cart));
                var bytes = File.ReadAllBytes(bcr);
                var mark = Encoding.ASCII.GetBytes("LBIPTEST___");
                bytes[1024] = 0x80; Array.Copy(mark, 0, bytes, 1028, mark.Length);
                File.WriteAllBytes(cart, bytes);
            }
            Check("RetroArch's .bcr made the game's cartridge", bcr != null, bcr);

            LbIntegrations.Catalog.LbCatalog.HostWillAsk = true;
            var type = asm.GetTypes().Single(t => typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract);
            var plugin = (Unbroken.LaunchBox.Plugins.EmulatorPlugin)Activator.CreateInstance(type);
            var emu = new StubEmulator { Title = "Nixx-Kronos", ApplicationPath = exe };
            var g = StubGame.With(StubGame.Create("real-kronos", "Atlantis - The Lost Tales", m3u), "Platform", "Sega Saturn");
            var r = plugin.PrepareEmulatorForLaunch(new Unbroken.LaunchBox.Plugins.PrepareForLaunchArgs(emu, g, "\"" + m3u + "\"", null, null));
            Check("prepared", r.WasSuccess, r.NewCommandLine ?? said.FirstOrDefault());
            if (!r.WasSuccess) return false;
            Console.WriteLine("  line: " + r.NewCommandLine);
            Check("the BIOS copied into bios\\ from RetroArch's system", File.Exists(Path.Combine(dir, "bios", "mpr-17933.bin")));
            Console.WriteLine("  kronos.ini before Kronos:\n    " + string.Join("\n    ", File.ReadAllLines(ini)));
            var ram = Path.Combine(dir, "Saves", "Sega Saturn", "Atlantis - The Lost Tales (FR).bkr");
            var states = Path.Combine(dir, "Saves", "Sega Saturn", "States", "Atlantis - The Lost Tales (FR)");
            var discs = Path.Combine(dir, "lbip-discs.txt");
            Check("the discs written down for the next-disc key", File.Exists(discs) && File.ReadAllLines(discs).Length == 3);

            var psi = new System.Diagnostics.ProcessStartInfo(exe, r.NewCommandLine) { WorkingDirectory = run, UseShellExecute = false };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                System.Threading.Thread.Sleep(20000);
                p.Refresh();
                Console.WriteLine("  window \"" + p.MainWindowTitle + "\"");
                Check("Kronos runs the disc (its title names it)", p.MainWindowTitle.IndexOf("Atlantis", StringComparison.OrdinalIgnoreCase) >= 0, p.MainWindowTitle);
                SetForegroundWindow(p.MainWindowHandle);
                System.Threading.Thread.Sleep(800);
                Keys(0x11, 0x10, 0x70);   // Ctrl+Shift+F1: the pack's save to slot 1
                System.Threading.Thread.Sleep(4000);
                var yss = Directory.Exists(states) ? Directory.GetFiles(states, "*.yss") : new string[0];
                Check("a state in the game's own folder, slot 1 (the pack's key)", yss.Any(y => y.EndsWith("_001.yss")), string.Join(", ", yss.Select(Path.GetFileName)));
                Keys(0x11, 0x51);   // Ctrl+Q: the quit the AHK script sends (a bare WM_CLOSE crashes Kronos in full screen)
                var closed = p.WaitForExit(30000);
                Check("Ctrl+Q: gone, exit " + (closed ? p.ExitCode.ToString() : "-"), closed && p.ExitCode == 0);
                if (!closed) { p.Kill(); return false; }
            }
            var plan = (string)T(asm, "KronosSession").GetProperty("PlanPath", Any).GetValue(null);
            for (int i = 0; i < 40 && File.Exists(plan); i++) System.Threading.Thread.Sleep(250);
            Check("the session put back by the plugin's watcher", !File.Exists(plan));
            Check("the game's own backup RAM: 32 KB, the standard size", File.Exists(ram) && new FileInfo(ram).Length == 32768, File.Exists(ram) ? new FileInfo(ram).Length + " bytes" : ram);
            if (bcr != null)
            {
                var c = File.ReadAllBytes(cart);
                Check("Kronos took Beetle's cartridge as it is: 512 KB, the mark still there", c.Length == 512 * 1024 && c[1024] == 0x80 && Encoding.ASCII.GetString(c, 1028, 11) == "LBIPTEST___", c.Length + " bytes");
            }
            var after = File.ReadAllText(ini);
            Console.WriteLine("  kronos.ini after:\n    " + string.Join("\n    ", File.ReadAllLines(ini)));
            Check("the game's keys taken out again", !after.Contains("Memory\\Path") && !after.Contains("SaveStates") && !after.Contains("Cartridge\\"));
            Check("the user's own setting and the pack's keys kept", after.Contains("Sound\\Volume=90") && after.Contains("Shortcuts\\Save1=Ctrl+Shift+F1") && after.Contains("Advanced\\SH2Interpreter=8"));
            Check("nothing beside the game", Directory.GetFiles(games).Length == 1);
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kronos");
            Check("nothing in %AppData%\\kronos", !Directory.Exists(appData));
            Console.WriteLine("  (left for inspection: " + run + ")");
            SetS(asm, "KronosSettings", "PathOverride", null);
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;
        }
    }
}
