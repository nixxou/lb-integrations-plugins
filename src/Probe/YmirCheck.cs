// The Ymir plugin's pieces, run without a host:
//   --ymir-selftest [--lab <folder>] [--csv <a tab of the compatibility list, as CSV>]
//        offline, temp folders only (the lab's files read, never written): the disc hash of the lab's two Atlantis (FR) CHDs
//        against the compatibility list's (measured 20/10), the same disc rebuilt as .cue / .bin and as .iso giving the same
//        hash, the Saturn header; the BIOS known by Ymir's hash; Ymir.toml - the pack's keys where Ymir has its defaults, its
//        indentation kept, a second pass changing nothing, the user's own keys left; the session's backup RAM path forced and
//        put back after Ymir wrote it relative; the .m3u, the discs of a "(Disc N)" set, the backup RAM's name, the line; the
//        compatibility list parsed and looked up by hash, then by product code
//   --ymir-latest     Ymir's latest stable release as GitHub has it

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace LbIntegrations.Probe
{
    internal static class YmirCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Ymir." + name, true);
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
            var d = Path.Combine(Path.GetTempPath(), "lbip-ymir-" + what + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private const string Cd1 = "05ED3C66A53212C9DFD7F86B625C723D", Cd2 = "6CBE114C153C7F5A5A0F821CE73D12AE";

        public static bool SelfTest(Assembly asm, string lab, string csv)
        {
            _bad = 0;
            lab ??= @"G:\LB1326\lbip-ymir-lab";
            var temp = NewTemp("selftest");
            SetS(asm, "YmirSettings", "PathOverride", Path.Combine(temp, "data", "settings.ini"));
            try
            {
                Console.WriteLine("== the disc hash and the header");
                var chds = Directory.Exists(@"G:\saturn") ? Directory.GetFiles(@"G:\saturn", "Atlantis*.chd", SearchOption.AllDirectories).OrderBy(p => p).ToList() : new List<string>();
                Check("the lab's two Atlantis CHDs are there", chds.Count == 2, string.Join(", ", chds.Select(Path.GetFileName)));
                var discs = chds.Select(p => Call(asm, "YmirDisc", "Read", p)).ToList();
                var hashes = discs.Select(d => (string)F(d, "Hash")).ToList();
                Check("their hashes are the list's (Ymir's own, measured 20/10)", hashes.Contains(Cd1) && hashes.Contains(Cd2), string.Join(" ", hashes));
                if (discs.Count > 0)
                {
                    Check("the header: product, area, title", (string)F(discs[0], "Product") == "MK-8109109" || (string)F(discs[0], "Product") == "MK-8109150",
                          F(discs[0], "Product") + " / " + F(discs[0], "Area") + " / " + F(discs[0], "Title"));
                    Check("the BIOS region of a PAL disc", (string)Call(asm, "YmirDisc", "BiosRegion", discs[0]) == "us-eu", (string)F(discs[0], "Area"));
                    // The same disc as .cue / .bin (raw 2352, sync + header + user data) and as .iso (2048): the same hash.
                    var reader = Call(asm, "YmirDisc", "Open", chds[0]);
                    var bin = Path.Combine(temp, "Atlantis (Disc 1).bin");
                    var iso = Path.Combine(temp, "Atlantis.iso");
                    using (var b = File.Create(bin))
                    using (var i = File.Create(iso))
                        for (long fad = 150; fad < 150 + 64; fad++)
                        {
                            var user = (byte[])CallOn(reader, "User", fad);
                            var sector = new byte[2352];
                            sector[0] = 0; for (int k = 1; k < 11; k++) sector[k] = 0xFF; sector[11] = 0;
                            sector[12] = Bcd(fad / 75 / 60); sector[13] = Bcd(fad / 75 % 60); sector[14] = Bcd(fad % 75); sector[15] = 1;
                            Array.Copy(user, 0, sector, 16, 2048);
                            b.Write(sector, 0, sector.Length);
                            i.Write(user, 0, user.Length);
                        }
                    ((IDisposable)reader).Dispose();
                    var cue = Path.Combine(temp, "Atlantis (Disc 1).cue");
                    File.WriteAllText(cue, "FILE \"Atlantis (Disc 1).bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
                    var fromCue = (string)F(Call(asm, "YmirDisc", "Read", cue), "Hash");
                    var fromIso = (string)F(Call(asm, "YmirDisc", "Read", iso), "Hash");
                    Check(".cue / .bin gives the CHD's hash", fromCue == hashes[0], fromCue);
                    Check(".iso gives the CHD's hash", fromIso == hashes[0], fromIso);
                    var notSaturn = Path.Combine(temp, "blank.iso");
                    File.WriteAllBytes(notSaturn, new byte[2048 * 20]);
                    var blank = Call(asm, "YmirDisc", "Read", notSaturn);
                    Check("a disc that is not a Saturn's has no hash, and says why", F(blank, "Hash") == null && F(blank, "Problem") != null, (string)F(blank, "Problem"));
                }

                Console.WriteLine("== the BIOS");
                var ipl = Path.Combine(lab, "prof1", "roms", "ipl", "mpr-17933.bin");
                if (File.Exists(ipl))
                {
                    var h = (string)Call(asm, "YmirBios", "HashOf", ipl);
                    var known = Call(asm, "YmirBios", "Known", h);
                    Check("mpr-17933.bin is Ymir's US/EU 1.01a (its seeded XXH128)", known != null && (string)F(known, "Label") == "Saturn US/EU 1.01a", h + " " + F(known, "Label"));
                }
                else Check("the lab's BIOS is there", false, ipl);

                Console.WriteLine("== Ymir.toml: the pack's keys");
                var src = Path.Combine(lab, "prof1", "Ymir.toml");
                var profile = Path.Combine(temp, "Nixx-Ymir");
                Directory.CreateDirectory(profile);
                var toml = Path.Combine(profile, "Ymir.toml");
                var text = File.ReadAllText(src).Replace("QuickLoadState = [ 'F4' ]", "QuickLoadState = [ 'F3' ]")
                                                 .Replace("InternalBackupRAMImagePath = '..\\..\\saves\\Atlantis - The Lost Tales (FR).bin'", "InternalBackupRAMImagePath = 'bup-int.bin'");
                File.WriteAllText(toml, text);
                var doc = Call(asm, "YmirTomlDoc", "Load", toml);
                var bound = (IList)Call(asm, "YmirLaunch", "BindPackKeys", doc);
                CallOn(doc, "Save", toml);
                var after = File.ReadAllText(toml);
                Check("F4 loads, indented as Ymir wrote it", after.Contains("\n    QuickLoadState = [ 'F4' ]\n"), bound.Count + " bound");
                Check("F11 full screen, Alt+Enter kept, the debug trace unbound", after.Contains("\nToggleFullScreen = [ 'Alt+Return', 'F11' ]\n") && after.Contains("\nToggleDebugTrace = []\n"));
                Check("only those lines changed", Diff(text, after) == 3, Diff(text, after) + " line(s)");
                var again = (IList)Call(asm, "YmirLaunch", "BindPackKeys", Call(asm, "YmirTomlDoc", "Load", toml));
                Check("a second pass binds nothing", again.Count == 0);
                File.WriteAllText(toml, text.Replace("QuickLoadState = [ 'F3' ]", "QuickLoadState = [ 'F8' ]").Replace("ToggleDebugTrace = [ 'F11' ]", "ToggleDebugTrace = [ 'Ctrl+F11' ]"));
                var userDoc = Call(asm, "YmirTomlDoc", "Load", toml);
                var userBound = (IList)Call(asm, "YmirLaunch", "BindPackKeys", userDoc);
                Check("keys the user changed are left alone; F11 still goes to full screen (free)", userBound.Count == 1
                      && (string)CallOn(userDoc, "Get", "Hotkeys.SaveStates", "QuickLoadState") == "[ 'F8' ]", string.Join(", ", userBound.Cast<object>()));
                Check("the slot keys are Ymir's defaults", (bool)Call(asm, "YmirLaunch", "SlotKeysAreDefault", userDoc));
                var moved = Call(asm, "YmirTomlDoc", "Parse", text.Replace("LoadDisc = [ 'Ctrl+O' ]", "LoadDisc = [ 'Ctrl+L' ]"));
                Call(asm, "YmirLaunch", "BindPackKeys", moved);
                Check("Load Disc moved by the user: Ctrl+O added beside it (the next-disc key needs it)", (string)CallOn(moved, "Get", "Hotkeys", "LoadDisc") == "[ 'Ctrl+L', 'Ctrl+O' ]",
                      (string)CallOn(moved, "Get", "Hotkeys", "LoadDisc"));

                Console.WriteLine("== a newer Ymir's settings");
                var v6 = Call(asm, "YmirTomlDoc", "Parse", "ConfigVersion = 6\n[General]\nCheckForUpdates = false\n");
                Check("ConfigVersion 6 (0.4.1's): every key known here may be written", (bool)Call(asm, "YmirLaunch", "Knows", v6, "Cartridge.BackupRAM", "Capacity"));
                var v7 = Call(asm, "YmirTomlDoc", "Parse", text.Replace("ConfigVersion = 6", "ConfigVersion = 7").Replace("    Capacity = '32Mbit'\n", "    Size = '32Mbit'\n"));
                Check("a newer ConfigVersion without Capacity: the cartridge's capacity is not written (it would be formatted)",
                      !(bool)Call(asm, "YmirLaunch", "Knows", v7, "Cartridge.BackupRAM", "Capacity") && (bool)Call(asm, "YmirLaunch", "Knows", v7, "Cartridge.BackupRAM", "ImagePath"));
                var v7keys = (IList)Call(asm, "YmirLaunch", "BindPackKeys", Call(asm, "YmirTomlDoc", "Parse", text.Replace("ConfigVersion = 6", "ConfigVersion = 7").Replace("    QuickLoadState = [ 'F3' ]\n", "")));
                Check("a newer ConfigVersion without QuickLoadState: the keys left alone", v7keys.Count == 0);

                Console.WriteLine("== the session: the game's backup RAM");
                File.WriteAllText(toml, text);
                var exe = Path.Combine(profile, "ymir-sdl3.exe");
                var ram = Path.Combine(profile, "Saves", "Sega Saturn", "Atlantis - The Lost Tales (FR).bin");
                var sdoc = Call(asm, "YmirTomlDoc", "Load", toml);
                var tupleType = typeof(ValueTuple<string, string, string, bool>);
                var keys = Array.CreateInstance(tupleType, 2);
                keys.SetValue(("System", "InternalBackupRAMImagePath", "'" + ram + "'", true), 0);
                keys.SetValue(("System", "InternalBackupRAMPerGame", "false", false), 1);
                var forced = (IList)Call(asm, "YmirSession", "Force", sdoc, keys);
                CallOn(sdoc, "Save", toml);
                Check("the RAM path forced (PerGame already false: left out)", forced.Count == 1, forced.Count + " forced");
                Call(asm, "YmirSession", "Begin", exe, toml, forced, null);
                // What Ymir writes when it quits: the path relative to its state folder.
                var relative = Path.GetRelativePath(Path.Combine(profile, "state"), ram);
                File.WriteAllText(toml, File.ReadAllText(toml).Replace("InternalBackupRAMImagePath = '" + ram + "'", "InternalBackupRAMImagePath = '" + relative + "'"));
                Call(asm, "YmirSession", "Recover");
                Check("put back once Ymir has quit, though Ymir wrote it relative", File.ReadAllText(toml).Contains("\nInternalBackupRAMImagePath = 'bup-int.bin'\n"), relative);
                Check("the plan is done", !File.Exists((string)T(asm, "YmirSession").GetProperty("PlanPath", Any).GetValue(null)));

                Console.WriteLine("== discs, names, the line");
                if (chds.Count == 2)
                {
                    var m3u = Path.Combine(temp, "Atlantis - The Lost Tales (FR).m3u");
                    File.WriteAllLines(m3u, new[] { "# discs", chds[0], chds[1] });
                    var plan = Call(asm, "YmirLaunch", "DiscFor", m3u, exe);
                    Check("a .m3u starts on its first disc", (string)F(plan, "Disc") == chds[0], (string)F(plan, "Problem"));
                    Check("its discs, for the next-disc key", ((IList)F(plan, "Discs")).Count == 2);
                    var set = (IList)Call(asm, "YmirLaunch", "DiscsOf", chds[0], null);
                    Check("the files of a \"(Disc N)\" set, in order", set.Count == 2 && (string)set[0] == chds[0], string.Join(", ", set.Cast<string>().Select(Path.GetFileName)));
                    Check("the backup RAM's name: the .m3u's", (string)Call(asm, "YmirPlugin", "RamName", m3u) == "Atlantis - The Lost Tales (FR)");
                    Check("the backup RAM's name: the disc's without \"(Disc N)\"", (string)Call(asm, "YmirPlugin", "RamName", chds[1]) == "Atlantis - The Lost Tales (FR)");
                    var line = (string)Call(asm, "YmirLaunch", "CommandLine", "\"" + m3u + "\"", profile, true, m3u, chds[0]);
                    Check("the line: -p, -f, the disc in place of the .m3u", line == "-p " + Q(profile) + " -f " + Q(chds[0]), line);
                    var line2 = (string)Call(asm, "YmirLaunch", "CommandLine", "-P \"" + m3u + "\" -p x -f", profile, false, m3u, chds[0]);
                    Check("the user's switches kept, ours not doubled", line2 == "-p " + Q(profile) + " -P " + Q(chds[0]), line2);
                }
                var bad = (string)F(Call(asm, "YmirLaunch", "DiscFor", Path.Combine(temp, "Atlantis (Disc 1).bin"), exe), "Problem");
                Check("a lone .bin is refused, the .cue named", bad != null && bad.Contains(".cue"), bad);

                Console.WriteLine("== the compatibility list");
                var csvFile = csv != null && File.Exists(csv) ? csv : null;
                if (csvFile != null)
                {
                    var dir = (string)T(asm, "YmirCompat").GetProperty("Dir", Any).GetValue(null);
                    Directory.CreateDirectory(dir);
                    File.Copy(csvFile, Path.Combine(dir, "PAL.csv"));
                    var rows = (IList)Call(asm, "YmirCompat", "Parse", File.ReadAllText(csvFile), "PAL");
                    Check("the PAL tab parsed by its column names, the description row skipped", rows.Count > 300, rows.Count + " games");
                    if (discs.Count > 0)
                    {
                        var r = Call(asm, "YmirCompat", "Lookup", discs.First(d => (string)F(d, "Hash") == Cd1));
                        Check("Atlantis (France) Disc 1 by its hash: Playable", F(r, "Exact") != null && (string)F(r, "State") == "Playable", (string)Call(asm, "YmirCompat", "Line", r));
                    }
                    var fake = Activator.CreateInstance(T(asm, "SaturnDisc"));
                    fake.GetType().GetField("Hash").SetValue(fake, "00000000000000000000000000000000");
                    fake.GetType().GetField("Product").SetValue(fake, "MK-8109150");
                    var byCode = Call(asm, "YmirCompat", "Lookup", fake);
                    Check("another dump of the same product code: said as such, never as its own line", F(byCode, "Exact") == null && ((IList)F(byCode, "SameProduct")).Count >= 2,
                          (string)Call(asm, "YmirCompat", "Line", byCode));
                }
                else Console.WriteLine("  (no --csv given: the list is not checked)");
            }
            catch (Exception ex) { Check("no exception", false, ex.ToString()); }
            finally { try { Directory.Delete(temp, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;
        }

        private static string Q(string a) => a.Contains(' ') ? "\"" + a + "\"" : a;

        private static byte Bcd(long v) => (byte)((v / 10) << 4 | (v % 10));

        private static int Diff(string a, string b)
        {
            var x = a.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var y = b.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (x.Length != y.Length) return Math.Abs(x.Length - y.Length) + 1000;
            return x.Zip(y, (p, q) => p == q ? 0 : 1).Sum();
        }

        // ══════════════════════════════════════════════════════════════════════
        // --ymir-real <folder of an extracted Ymir release> [--lab <folder>] [--csv <tab>]
        //   THE REAL EMULATOR, windowed: a copy of the release in a fresh folder of <lab>, the lab's BIOS in a "RetroArch\system"
        //   beside it (copied by the plugin), a .m3u of the two Atlantis CHDs, a launch prepared as LaunchBox would, Ymir started
        //   with that line, closed by WM_CLOSE, then the session put back by the plugin's own watcher.

        public static bool Real(Assembly asm, string release, string lab, string csv)
        {
            _bad = 0;
            lab ??= @"G:\LB1326\lbip-ymir-lab";
            var run = Path.Combine(lab, "probe-real-" + DateTime.Now.ToString("HHmmss"));
            var dir = Path.Combine(run, "Emulators", "Nixx-Ymir");
            var ra = Path.Combine(run, "Emulators", "RetroArch", "system");
            var data = Path.Combine(run, "plugin-data");
            Directory.CreateDirectory(dir); Directory.CreateDirectory(ra); Directory.CreateDirectory(data);
            foreach (var f in Directory.GetFiles(release, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(dir, Path.GetRelativePath(release, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to);
            }
            File.Copy(Path.Combine(lab, "prof1", "roms", "ipl", "mpr-17933.bin"), Path.Combine(ra, "mpr-17933.bin"));
            var exe = Path.Combine(dir, "ymir-sdl3.exe");
            Console.WriteLine("-- THE REAL YMIR: " + exe + " --");
            SetS(asm, "YmirSettings", "PathOverride", Path.Combine(data, "settings.ini"));
            File.WriteAllText(Path.Combine(data, "settings.ini"), "Fullscreen=false\r\nCompatAtLaunch=always\r\n");
            if (csv != null && File.Exists(csv)) { Directory.CreateDirectory(Path.Combine(data, "compat")); File.Copy(csv, Path.Combine(data, "compat", "PAL.csv")); }
            var said = new List<string>();
            asm.GetType("LbIntegrations.Lbip.LbipNotice", true).GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            var shown = new List<string>();
            SetS(asm, "YmirCompatToast", "Instead", new Action<string, string>((t, x) => shown.Add(t + ": " + x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);

            var chds = Directory.GetFiles(@"G:\saturn", "Atlantis*.chd", SearchOption.AllDirectories).OrderBy(p => p).ToList();
            var games = Path.Combine(run, "games"); Directory.CreateDirectory(games);
            var m3u = Path.Combine(games, "Atlantis - The Lost Tales (FR).m3u");
            File.WriteAllLines(m3u, chds);

            // The game's backup cartridge: RetroArch's Beetle Saturn .bcr, a mark in its third block where a save would start - Ymir
            // formatting it (a file it did not take) would clear the mark.
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
            var emu = new StubEmulator { Title = "Nixx-Ymir", ApplicationPath = exe };
            var g = StubGame.With(StubGame.Create("real-ymir", "Atlantis - The Lost Tales", m3u), "Platform", "Sega Saturn");
            var r = plugin.PrepareEmulatorForLaunch(new Unbroken.LaunchBox.Plugins.PrepareForLaunchArgs(emu, g, "\"" + m3u + "\"", null, null));
            Check("prepared", r.WasSuccess, r.NewCommandLine ?? said.FirstOrDefault());
            if (!r.WasSuccess) return false;
            Console.WriteLine("  line: " + r.NewCommandLine);
            Check("the BIOS copied into roms\\ipl from RetroArch's system", File.Exists(Path.Combine(dir, "roms", "ipl", "mpr-17933.bin")));
            Check("the update popup's marker", File.Exists(Path.Combine(dir, "state", "updates", ".onboarded")));
            Check("the compatibility state shown", shown.Count == 1, shown.FirstOrDefault());
            var toml = Path.Combine(dir, "Ymir.toml");
            Console.WriteLine("  Ymir.toml before Ymir:\n    " + string.Join("\n    ", File.ReadAllLines(toml)));
            var ram = Path.Combine(dir, "Saves", "Sega Saturn", "Atlantis - The Lost Tales (FR).bin");

            var psi = new System.Diagnostics.ProcessStartInfo(exe, r.NewCommandLine) { WorkingDirectory = run, UseShellExecute = false };
            var t0 = DateTime.Now;
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                System.Threading.Thread.Sleep(12000);
                p.Refresh();
                Console.WriteLine("  window \"" + p.MainWindowTitle + "\"");
                Check("Ymir runs the disc (its title names it)", p.MainWindowTitle.IndexOf("ATLANTIS", StringComparison.OrdinalIgnoreCase) >= 0, p.MainWindowTitle);
                Check("the game's own backup RAM made by Ymir", File.Exists(ram), File.Exists(ram) ? new FileInfo(ram).Length + " bytes" : ram);
                PostMessage(p.MainWindowHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                var closed = p.WaitForExit(30000);
                Check("WM_CLOSE: gone (" + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + " s after start), exit " + (closed ? p.ExitCode.ToString() : "-"), closed);
                if (!closed) { p.Kill(); return false; }
            }
            var plan = (string)T(asm, "YmirSession").GetProperty("PlanPath", Any).GetValue(null);
            for (int i = 0; i < 40 && File.Exists(plan); i++) System.Threading.Thread.Sleep(250);
            Check("the session put back by the plugin's watcher", !File.Exists(plan));
            var after = File.ReadAllText(toml);
            Check("Ymir wrote its whole Ymir.toml", after.Length > 5000, after.Length + " chars");
            Check("the RAM path taken out again (Ymir's own next time)", !after.Contains("Atlantis"), after.Split('\n').FirstOrDefault(l => l.Contains("InternalBackupRAMImagePath")));
            if (bcr != null)
            {
                var c = File.ReadAllBytes(cart);
                Check("Ymir took Beetle's cartridge as it is: 512 KB, the mark still there (not formatted)",
                      c.Length == 512 * 1024 && c[1024] == 0x80 && Encoding.ASCII.GetString(c, 1028, 11) == "LBIPTEST___", c.Length + " bytes");
                Check("the cartridge unplugged after the game (Type None again)", !after.Contains("Type = 'BackupRAM'"),
                      after.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("Type =")));
            }
            Check("the pack's keys kept by Ymir", after.Contains("QuickLoadState = [ 'F4' ]") && after.Contains("ToggleFullScreen = [ 'Alt+Return', 'F11' ]") && after.Contains("ToggleDebugTrace = []"));
            Check("its update check off", after.Contains("CheckForUpdates = false"));
            Check("nothing beside the game", Directory.GetFiles(games).Length == 1);
            Console.WriteLine("  (left for inspection: " + run + ")");
            SetS(asm, "YmirSettings", "PathOverride", null);
            Console.WriteLine(_bad == 0 ? "ALL OK" : _bad + " FAILED");
            return _bad == 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

        public static bool Latest(Assembly asm)
        {
            var r = Call(asm, "YmirBuild", "Latest");
            Console.WriteLine("flavour " + Call(asm, "YmirBuild", "Flavour") + ", latest: " + (r == null ? "none" : F(r, "Version") + " " + F(r, "FileName") + " sha256 " + F(r, "Sha256") + "\n  " + F(r, "Url")));
            return r != null;
        }
    }
}
