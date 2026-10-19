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

                Console.WriteLine("== the line");
                var line = (string)Call(asm, "KronosLaunch", "CommandLine", "-ns \"C:\\g\\a b.m3u\" -f -i x", true, @"C:\K\bios\mpr-17933.bin", @"C:\g\a b.m3u", @"C:\g\a b (Disc 1).chd");
                Check("-a -f --bios= then the user's -ns, -i <disc> last, the .m3u and our switches gone", line == "-a -f --bios=C:\\K\\bios\\mpr-17933.bin -ns -i \"C:\\g\\a b (Disc 1).chd\"", line);
                var line2 = (string)Call(asm, "KronosLaunch", "CommandLine", "", false, null, null, @"C:\g\x.cue");
                Check("windowed, no BIOS found: -a -i <disc>", line2 == "-a -i C:\\g\\x.cue", line2);

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

        /// <summary>The real Kronos on a Zstandard CHD it cannot read: the plugin hands it the disc uncompressed (served or
        /// copied), Kronos boots it, and once it has gone the served disc is detached. Windowed, no key sent: the window is
        /// captured with PrintWindow, then the process ended.</summary>
        public static bool Zstd(Assembly asm, string release, string lab, string lb = null)
        {
            _bad = 0;
            if (lb != null) asm.GetType("LbIntegrations.RamDisk.RamDiskHost", true).GetMethod("UseRoot", Any).Invoke(null, new object[] { lb });
            lab ??= @"G:\LB1326\lbip-kronos-lab";
            var zstd = Path.Combine(lab, "formats", "Atlantis-zstd.chd");
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
