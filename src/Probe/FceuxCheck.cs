// The FCEUX plugin's pieces, run without a host:
//   --fceux-systems [<fceux source folder>]   the systems / platforms table (and, with the source at the tag, the open
//                                             dialog's filter and the archive extensions read off it)
//   --fceux-cfg <fceux.cfg>                   the Qt build's editor on a COPY: nothing changed when nothing is set, a name
//                                             set in place, a new one at the end, CRLF, no BOM, bytes kept
//   --fceux-hotkeys <first-run fceux.cfg>     the table's defaults = the first-run file's; the pack's keys bound, once;
//                                             a key of the user's never taken, no key on two actions
//   --fceux-launch [<fceux.cfg>]              a launch as LaunchBox makes it, on a COPY of an install (the exe a stand-in):
//                                             claiming, platform rows only added, junctions per platform, the files of
//                                             FCEUX's real folders moved, the BIOS refusal, the ANSI refusal, a .unf in a zip
//   --fceux-prepare <bin\qfceux.exe> --rom <rom> --platform <name> [--settings <ini>]
//                                             fceux.cfg and the folders of a REAL install prepared as a launch does
//                                             (for an oracle run of FCEUX afterwards - it writes there)
//   --fceux-latest                            the latest release as GitHub has it, and IsUpdateAvailable's three answers
//   --fceux-extract <zip> <folder>            the release zip extracted as an install does

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class FceuxCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static bool Done() { Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED"); return _bad == 0; }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Fceux." + name, true);
        private static object Call(Assembly asm, string type, string method, params object[] args) => T(asm, type).GetMethod(method, Any).Invoke(null, args);
        private static object Field(object o, string name) => o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o);
        private static string Scratch(string what) { var d = Path.Combine(Path.GetTempPath(), "lbip-fceux-" + what + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(d); return d; }

        // ── the table ────────────────────────────────────────────────────────

        public static bool Systems(Assembly asm, string source)
        {
            _bad = 0;
            Console.WriteLine("-- FCEUX's systems and platforms --");
            var sys = T(asm, "FceuxSystems");
            var platforms = (Array)sys.GetField("Platforms", Any).GetValue(null);
            var names = platforms.Cast<object>().Select(p => (string)Field(p, "Platform")).ToList();
            foreach (var p in platforms.Cast<object>())
                Console.WriteLine("    " + Field(p, "Platform") + " -> " + Field(p, "System") + "  [" + Call(asm, "FceuxSystems", "AllExtensions", p) + "]");
            Check("the NES first, the disk system, the Famicom, the SMB hacks",
                  names.SequenceEqual(new[] { "Nintendo Entertainment System", "Nintendo Famicom Disk System", "Nintendo Famicom", "Nes - Super Mario Bros. Hacks" }), string.Join(" | ", names));
            string Ext(string platform) => (string)Call(asm, "FceuxSystems", "AllExtensions", Call(asm, "FceuxSystems", "Platform", platform));
            Check("the NES row has .nes .unf .unif .nsf .fds and the archives", new[] { ".nes", ".unf", ".unif", ".nsf", ".fds", ".zip", ".7z", ".rar", ".gz" }.All(e => Ext("Nintendo Entertainment System").Split("; ").Contains(e)), Ext("Nintendo Entertainment System"));
            Check("the disk system's row: .fds and the archives only", Ext("Nintendo Famicom Disk System") == ".fds; .zip; .7z; .rar; .gz", Ext("Nintendo Famicom Disk System"));
            Check("a platform name is matched trimmed and in any case", Call(asm, "FceuxSystems", "Platform", "  nintendo famicom disk system ") != null);
            string SysFor(string platform, string rom, string inside = null) => (string)Field(Call(asm, "FceuxSystems", "SystemFor", platform, rom, inside), "Key");
            Check("a .fds among NES games is a disk", SysFor("Nintendo Entertainment System", @"C:\g\Zelda.fds") == "fds");
            Check("a zip holding a .fds is a disk", SysFor("Nintendo Entertainment System", @"C:\g\Zelda.zip", "Zelda (Japan).fds") == "fds");
            Check("a zip on the disk platform with no known ROM is a disk", SysFor("Nintendo Famicom Disk System", @"C:\g\x.zip") == "fds");
            Check("a .nes on the disk platform is a cartridge (FCEUX tells by the content)", SysFor("Nintendo Famicom Disk System", @"C:\g\x.nes") == "nes");
            Check("an unknown platform: the NES", SysFor("Arcade", @"C:\g\x.bin") == "nes");
            Check("a platform's folder name has no character Windows refuses", (string)Call(asm, "FceuxSystems", "FolderOf", "A: B/C*?") == "A_ B_C__", (string)Call(asm, "FceuxSystems", "FolderOf", "A: B/C*?"));
            var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract));
            foreach (var n in names) Check("IsPlatformSupported(" + n + "), never recommended", plugin.IsPlatformSupported(n).Supported && !plugin.IsPlatformSupported(n).Recommended);
            Check("not the SNES", !plugin.IsPlatformSupported("Super Nintendo Entertainment System").Supported);
            var fdsBios = plugin.GetBiosFilesForPlatform("Nintendo Famicom Disk System").ToList();
            var nesBios = plugin.GetBiosFilesForPlatform("Nintendo Entertainment System").ToList();
            var hackBios = plugin.GetBiosFilesForPlatform("Nes - Super Mario Bros. Hacks").ToList();
            Check("disksys.rom required on the disk system", fdsBios.Count == 1 && fdsBios[0].FileName == "disksys.rom" && fdsBios[0].Required);
            Check("disksys.rom optional on the NES (its .fds), none on the SMB hacks", nesBios.Count == 1 && !nesBios[0].Required && hackBios.Count == 0);
            var rows = ((LbIntegrations.Catalog.ILbCatalogSource)plugin).EmulatorRows().ToList();
            Check("one catalogue row, Nixx-FCEUX, qfceux.exe, no auto-extract", rows.Count == 1 && rows[0].Name == "Nixx-FCEUX" && rows[0].BinaryFileName == "qfceux.exe" && !rows[0].AutoExtract);
            Check("a catalogue platform per platform, none recommended, the disk's BIOS named", rows[0].Platforms.Count == names.Count && rows[0].Platforms.All(p => !p.Recommended)
                  && rows[0].Platforms.Single(p => p.Platform == "Nintendo Famicom Disk System").RequiredBiosFile == "disksys.rom");
            if (source != null)
            {
                var window = File.ReadAllText(Path.Combine(source, "src", "drivers", "win", "window.cpp"));
                var filter = System.Text.RegularExpressions.Regex.Match(window, @"\*\.nes;\*\.nsf;\*\.fds;\*\.unf;[^""\\]*");
                var exts = filter.Success ? filter.Value.Split(';').Select(e => e.Trim().TrimStart('*').ToLowerInvariant()).Where(e => e.Length > 1).ToList() : new List<string>();
                Check("the open dialog's filter (window.cpp) is covered", exts.Count > 0 && exts.All(e => rows[0].ApplicableFileExtensions.Split("; ").Contains(e)), string.Join(" ", exts));
                var fceu = File.ReadAllText(Path.Combine(source, "src", "fceu.cpp"));
                var romext = System.Text.RegularExpressions.Regex.Match(fceu, @"romextensions\[\]\s*=\s*\{([^}]*)\}");
                var inArchive = romext.Success ? System.Text.RegularExpressions.Regex.Matches(romext.Groups[1].Value, "\"([a-z0-9]+)\"").Select(m => "." + m.Groups[1].Value).ToList() : new List<string>();
                var ours = ((string[])sys.GetField("ArchiveRomExtensions", Any).GetValue(null)).ToList();
                Check("the archive entries FCEUX looks at (fceu.cpp romextensions)", inArchive.Count > 0 && inArchive.OrderBy(x => x).SequenceEqual(ours.OrderBy(x => x)), string.Join(" ", inArchive));
            }
            return Done();
        }

        // ── fceux.cfg ────────────────────────────────────────────────────────

        public static bool Cfg(Assembly asm, string cfgPath)
        {
            _bad = 0;
            Console.WriteLine("-- fceux.cfg, edited in place (a copy of " + cfgPath + ") --");
            var dir = Scratch("cfg");
            try
            {
                var copy = Path.Combine(dir, "fceux.cfg");
                File.Copy(cfgPath, copy);
                var original = File.ReadAllBytes(copy);
                var cfgType = T(asm, "FceuxCfg");
                object Load() => cfgType.GetMethod("Load", Any).Invoke(null, new object[] { copy });
                void Set(object c, string k, string v) => cfgType.GetMethod("Set", Inst).Invoke(c, new object[] { k, v });
                string Get(object c, string k) => (string)cfgType.GetMethod("Get", Inst).Invoke(c, new object[] { k });
                void Save(object c) => cfgType.GetMethod("SaveIfChanged", Inst).Invoke(c, null);

                var cfg = Load();
                Save(cfg);
                Check("loaded and saved with nothing set: the same bytes", File.ReadAllBytes(copy).SequenceEqual(original));
                Check("SDL.Fullscreen read as 0, SDL.Hotkeys.SaveState as I", Get(cfg, "SDL.Fullscreen") == "0" && Get(cfg, "SDL.Hotkeys.SaveState") == "I",
                      Get(cfg, "SDL.Fullscreen") + " / " + Get(cfg, "SDL.Hotkeys.SaveState"));
                Check("a name that is a prefix of another is not taken for it", Get(cfg, "SDL.Hotkeys.LoadState") != Get(cfg, "SDL.Hotkeys.LoadState2") || Get(cfg, "SDL.Hotkeys.LoadState") == "P");
                Set(cfg, "SDL.Fullscreen", "0");
                Check("a value set to what it is: nothing changed", !(bool)cfgType.GetProperty("Changed").GetValue(cfg));
                var lines = Encoding.Latin1.GetString(original).Split("\r\n");
                int Line(string[] ls, string key) => Array.FindIndex(ls, l => l.StartsWith(key + " ="));
                Set(cfg, "SDL.Fullscreen", "1");
                Set(cfg, "SDL.Hotkeys.LoadState2", "");
                Set(cfg, "SDL.zzzNew", @"G:\A folder\é");
                Save(cfg);
                var after = File.ReadAllBytes(copy);
                var afterLines = Encoding.Latin1.GetString(after).Split("\r\n");
                Check("one line more for the new name, at the end", afterLines.Length == lines.Length + 1 && afterLines[afterLines.Length - 2] == "SDL.zzzNew = G:\\A folder\\\u00e9", lines.Length + " -> " + afterLines.Length);
                Check("a name set where it stands", Line(afterLines, "SDL.Fullscreen") == Line(lines, "SDL.Fullscreen") && afterLines[Line(afterLines, "SDL.Fullscreen")] == "SDL.Fullscreen = 1");
                Check("an emptied key: \"name = \", read back as empty", afterLines[Line(afterLines, "SDL.Hotkeys.LoadState2")] == "SDL.Hotkeys.LoadState2 = " && Get(Load(), "SDL.Hotkeys.LoadState2") == "");
                Check("CRLF, no BOM, ending in CRLF", after[0] != 0xEF && !Encoding.Latin1.GetString(after).Replace("\r\n", "").Contains('\n') && after[after.Length - 1] == (byte)'\n' && after[after.Length - 2] == (byte)'\r');
                Check("the other lines untouched", afterLines.Take(lines.Length - 1).Zip(lines).Count(p => p.First != p.Second) == 2);
                Check("é kept as its one byte (0xE9)", after.Contains((byte)0xE9) && Get(Load(), "SDL.zzzNew") == @"G:\A folder\é");
                // Made from nothing.
                File.Delete(copy);
                cfg = Load();
                Check("no file: not Existed", !(bool)cfgType.GetProperty("Existed").GetValue(cfg));
                Set(cfg, "SDL.Fullscreen", "1"); Set(cfg, "SDL.Hotkeys.Quit", "Esc"); Save(cfg);
                Check("made from nothing: the lines set, CRLF", File.ReadAllText(copy, Encoding.ASCII) == "SDL.Fullscreen = 1\r\nSDL.Hotkeys.Quit = Esc\r\n", File.ReadAllText(copy).Replace("\r\n", " | "));
                Check("an ANSI path is representable, a Japanese one is not",
                      (bool)cfgType.GetMethod("Representable", Any).Invoke(null, new object[] { @"G:\Jeux\Zelda é.nes" })
                      && !(bool)cfgType.GetMethod("Representable", Any).Invoke(null, new object[] { "G:\\Jeux\\\u30bc\u30eb\u30c0.nes" }));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── hotkeys ──────────────────────────────────────────────────────────

        public static bool Hotkeys(Assembly asm, string firstRun)
        {
            _bad = 0;
            Console.WriteLine("-- FCEUX's hotkeys (" + firstRun + ") --");
            var dir = Scratch("hotkeys");
            try
            {
                var copy = Path.Combine(dir, "fceux.cfg");
                File.Copy(firstRun, copy);
                var cfgType = T(asm, "FceuxCfg");
                var hk = T(asm, "FceuxHotkeys");
                object Load() => cfgType.GetMethod("Load", Any).Invoke(null, new object[] { copy });
                string Get(object c, string k) => (string)cfgType.GetMethod("Get", Inst).Invoke(c, new object[] { "SDL.Hotkeys." + k });
                void Set(object c, string k, string v) => cfgType.GetMethod("Set", Inst).Invoke(c, new object[] { "SDL.Hotkeys." + k, v });
                List<string> Bind(object c) => (List<string>)hk.GetMethod("Bind", Any).Invoke(null, new[] { c });
                void Save(object c) => cfgType.GetMethod("SaveIfChanged", Inst).Invoke(c, null);

                // The table's defaults are FCEUX 2.7.0's, as its first-run file has them.
                var first = Load();
                var table = ((Array)hk.GetField("Table", Any).GetValue(null)).Cast<object>().Select(t => ((string)t.GetType().GetField("Item1").GetValue(t), (string)t.GetType().GetField("Item2").GetValue(t), (string)t.GetType().GetField("Item3").GetValue(t))).ToList();
                var off = table.Where(t => (Get(first, t.Item1) ?? "") != t.Item3).Select(t => t.Item1 + "=" + Get(first, t.Item1)).ToList();
                Check("every default of the table is the first-run file's", off.Count == 0, string.Join(", ", off));

                var cfg = Load();
                foreach (var s in Bind(cfg)) Console.WriteLine("    " + s);
                Save(cfg);
                var c2 = Load();
                Check("SaveState F2, LoadState F4, F6 / F7 the slot", Get(c2, "SaveState") == "F2" && Get(c2, "LoadState") == "F4" && Get(c2, "SelectStatePrev") == "F6" && Get(c2, "SelectStateNext") == "F7");
                Check("FullScreen F11, Quit Esc, HardReset Shift+Esc, SoftReset H", Get(c2, "FullScreen") == "F11" && Get(c2, "Quit") == "Esc" && Get(c2, "HardReset") == "Shift+Esc" && Get(c2, "SoftReset") == "H");
                Check("Pause P, Turbo L, Screenshot F8, VolumeMute F9", Get(c2, "Pause") == "P" && Get(c2, "Turbo") == "L" && Get(c2, "Screenshot") == "F8" && Get(c2, "VolumeMute") == "F9");
                Check("LoadState2 / 4 / 6 / 7 / 8 / 9 given up", new[] { "2", "4", "6", "7", "8", "9" }.All(n => Get(c2, "LoadState" + n) == ""));
                Check("the other slots kept (LoadState3 = F3, SaveState1 = Shift+F1)", Get(c2, "LoadState3") == Get(first, "LoadState3") && Get(c2, "SaveState1") == Get(first, "SaveState1"), Get(c2, "LoadState3") + " / " + Get(c2, "SaveState1"));
                var keys = cfgType.GetMethod("Entries", Inst).Invoke(c2, new object[] { "SDL.Hotkeys." }) as System.Collections.IEnumerable;
                var bound = keys.Cast<object>().Select(e => ((string)e.GetType().GetField("Item2").GetValue(e)).Trim()).Where(v => v.Length > 0).ToList();
                var twice = bound.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Check("no key on two actions", twice.Count == 0, string.Join(", ", twice));
                var stamp = File.GetLastWriteTimeUtc(copy);
                System.Threading.Thread.Sleep(20);
                cfg = Load(); Bind(cfg); Save(cfg);
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(copy) == stamp);
                var changed = File.ReadAllLines(copy).Zip(File.ReadAllLines(firstRun)).Count(p => p.First != p.Second);
                Check("only hotkey lines changed", File.ReadAllLines(copy).Zip(File.ReadAllLines(firstRun)).All(p => p.First == p.Second || p.First.StartsWith("SDL.Hotkeys.")), changed + " lines");

                // The user's: SaveState moved to K, Esc on Pause, F11 on Screenshot - nothing of theirs taken.
                File.Copy(firstRun, copy, overwrite: true);
                cfg = Load();
                Set(cfg, "SaveState", "K"); Set(cfg, "Pause", "Esc"); Set(cfg, "Screenshot", "F11");
                foreach (var s in Bind(cfg)) Console.WriteLine("    " + s);
                Check("an action the user rebound is not touched (SaveState = K)", Get(cfg, "SaveState") == "K");
                Check("a key on another action of the user's is not bound (Esc on Pause): Quit left", Get(cfg, "Pause") == "Esc" && Get(cfg, "Quit") == "Ctrl+Q");
                Check("F11 the user's Screenshot: FullScreen left on Alt+Return", Get(cfg, "Screenshot") == "F11" && Get(cfg, "FullScreen") == "Alt+Return");
                Check("the rest still bound (F4, F6, Shift+Esc)", Get(cfg, "LoadState") == "F4" && Get(cfg, "SelectStatePrev") == "F6" && Get(cfg, "HardReset") == "Shift+Esc");

                // An action that keeps a key another takes (Pause left as the user's on P? no: Turbo the user's on F2).
                File.Copy(firstRun, copy, overwrite: true);
                cfg = Load();
                Set(cfg, "Turbo", "F2");
                Bind(cfg);
                Check("a pack key the user put on another action: that action kept, SaveState left on I", Get(cfg, "Turbo") == "F2" && Get(cfg, "SaveState") == "I");

                // No file yet (FCEUX never ran): the pack's lines alone, FCEUX fills the rest.
                File.Delete(copy);
                cfg = Load(); Bind(cfg); Save(cfg);
                var made = File.ReadAllLines(copy);
                Check("no file yet: the pack's keys written, nothing else", made.Length == table.Count && made.All(l => l.StartsWith("SDL.Hotkeys.")) && made.Contains("SDL.Hotkeys.SaveState = F2") && made.Contains("SDL.Hotkeys.LoadState2 = "), made.Length + " lines");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── a launch ─────────────────────────────────────────────────────────

        private static bool IsJunctionTo(string link, string target)
        {
            try
            {
                var info = new DirectoryInfo(link);
                if (!info.Exists || !info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
                var t = info.LinkTarget ?? "";
                if (t.StartsWith(@"\??\")) t = t.Substring(4);
                return string.Equals(Path.GetFullPath(t).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool Launch(Assembly asm, string cfgPath)
        {
            _bad = 0;
            Console.WriteLine("-- a launch through Nixx-FCEUX, on a copy --");
            var dir = Scratch("launch");
            var said = new List<string>();
            var notice = asm.GetType("LbIntegrations.Lbip.LbipNotice", true);
            notice.GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true).GetField("Off", Any).SetValue(null, true);
            T(asm, "FceuxSettings").GetField("PathOverride", Any).SetValue(null, Path.Combine(dir, "settings.ini"));
            var paths = T(asm, "FceuxSavePaths");
            var library = new List<(string, string)>();
            paths.GetField("LibraryForProbe", Any).SetValue(null, (Func<IEnumerable<(string, string)>>)(() => library));
            var wasDm = PluginHelper.DataManager;
            var wasPlatforms = StubDataManager.Platforms;
            var junctions = new List<string>();
            try
            {
                var install = Path.Combine(dir, "Emulators", "Nixx-FCEUX");
                var bin = Path.Combine(install, "bin");
                Directory.CreateDirectory(bin);
                var exe = Path.Combine(bin, "qfceux.exe");
                File.WriteAllText(exe, "");
                if (cfgPath != null) File.Copy(cfgPath, Path.Combine(bin, "fceux.cfg"));
                var cfgFile = Path.Combine(bin, "fceux.cfg");
                var cfgType = T(asm, "FceuxCfg");
                string Get(string k) => (string)cfgType.GetMethod("Get", Inst).Invoke(cfgType.GetMethod("Load", Any).Invoke(null, new object[] { cfgFile }), new object[] { k });
                var games = Path.Combine(dir, "My Games");
                Directory.CreateDirectory(games);
                foreach (var k in new[] { "sav", "fcs", "snaps", "cheats" }) junctions.Add(Path.Combine(bin, k));

                var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract));
                // ── claiming, and the rows of a user's entry ──
                StubDataManager.Platforms = new IPlatform[] { StubGame.Platform("Nintendo Entertainment System"), StubGame.Platform("Nintendo Famicom Disk System"), StubGame.Platform("My NES Hacks") };
                var emu = new StubEmulator { Title = "FCEUX (mine)", ApplicationPath = exe };
                var nes = emu.AddNewEmulatorPlatform(); nes.Platform = "Nintendo Entertainment System"; nes.IsDefault = true;
                var custom = emu.AddNewEmulatorPlatform(); custom.Platform = "My NES Hacks"; custom.IsDefault = true;
                var other = new StubEmulator { Title = "Nestopia", ApplicationPath = @"C:\Emu\nestopia.exe" };
                var otherFds = other.AddNewEmulatorPlatform(); otherFds.Platform = "Nintendo Famicom Disk System"; otherFds.IsDefault = true;
                PluginHelper.DataManager = new StubDataManager(emu, other);
                var classic = new StubEmulator { Title = "classic", ApplicationPath = @"C:\Emu\fceux64.exe" };
                var x32 = new StubEmulator { Title = "32", ApplicationPath = @"C:\Emu\FCEUX\fceux.exe" };
                var claimed = plugin.GetApplicableEmulators(new IEmulator[] { emu, other, classic, x32 }).ToList();
                Check("claimed: qfceux.exe, not the classic fceux64.exe / fceux.exe nor nestopia.exe", claimed.Count == 1 && claimed.Contains(emu));
                Check("hotkey scripts filled for the entry's exe: Exit closes it, Save / Load send F2 / F4",
                      emu.ExitAutoHotkeyScript?.Contains("WinClose, ahk_exe qfceux.exe") == true
                      && emu.SaveStateAutoHotkeyScript?.Contains("WinActivate, ahk_exe qfceux.exe") == true && emu.SaveStateAutoHotkeyScript.Contains("Send, {F2}")
                      && emu.LoadStateAutoHotkeyScript?.Contains("Send, {F4}") == true);
                var oldSave = "; FCEUX's File > Savestate > Save State: the current slot (posted to its window, no key needed).\r\nPostMessage, 0x111, 40344, 0,, ahk_class FCEUXWindowClass ahk_exe fceux64.exe";
                var moved = new StubEmulator { Title = "moved", ApplicationPath = exe, SaveStateAutoHotkeyScript = oldSave, LoadStateAutoHotkeyScript = "; mine\r\nSend, {F9}" };
                plugin.GetApplicableEmulators(new IEmulator[] { moved });
                Check("the classic build's script replaced, the user's own kept", moved.SaveStateAutoHotkeyScript.Contains("Send, {F2}") && moved.LoadStateAutoHotkeyScript == "; mine\r\nSend, {F9}");
                var rows = emu.GetAllEmulatorPlatforms();
                Console.WriteLine("    rows: " + string.Join(", ", rows.Select(r => r.Platform + (r.IsDefault ? "*" : ""))));
                Check("the user's rows kept, their ticks too", rows.Any(r => r.Platform == "My NES Hacks" && r.IsDefault) && rows.Any(r => r.Platform == "Nintendo Entertainment System" && r.IsDefault));
                Check("the disk system added (the library has it), not the default (Nestopia is)", rows.Any(r => r.Platform == "Nintendo Famicom Disk System" && !r.IsDefault));
                Check("no row for a platform the library lacks", !rows.Any(r => r.Platform == "Nintendo Famicom" || r.Platform == "Nes - Super Mario Bros. Hacks"));

                // ── launches ──
                PrepareForLaunchResponse Go(string platform, string rom, string id = null)
                {
                    var game = StubGame.With(StubGame.Create(id ?? ("g-" + Path.GetFileNameWithoutExtension(rom)), Path.GetFileNameWithoutExtension(rom), rom), "Platform", platform);
                    return plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(emu, game, "\"" + rom + "\"", null, null));
                }
                // Files in FCEUX's own (real) folders, from before the plugin: moved at the first launch.
                var zelda = Path.Combine(games, "Zelda II - The Adventure of Link (Europe).nes");
                File.WriteAllBytes(zelda, Encoding.ASCII.GetBytes("NES\x1a").Concat(new byte[1020]).ToArray());
                var hack = Path.Combine(games, "Super Mario Hack.nes");
                File.WriteAllBytes(hack, Encoding.ASCII.GetBytes("NES\x1a").Concat(new byte[1020]).ToArray());
                library.Add((zelda, "Nintendo Entertainment System"));
                library.Add((hack, "My NES Hacks"));
                foreach (var k in new[] { "sav", "fcs", "snaps" }) Directory.CreateDirectory(Path.Combine(bin, k));
                File.WriteAllBytes(Path.Combine(bin, "sav", "Zelda II - The Adventure of Link (Europe).sav"), new byte[8192]);
                File.WriteAllBytes(Path.Combine(bin, "fcs", "Zelda II - The Adventure of Link (Europe).fc1"), new byte[100]);
                File.WriteAllBytes(Path.Combine(bin, "fcs", "Zelda II - The Adventure of Link (Europe)-bak.fc1"), new byte[100]);
                File.WriteAllBytes(Path.Combine(bin, "fcs", "Super Mario Hack.fc0"), new byte[100]);
                File.WriteAllBytes(Path.Combine(bin, "fcs", "Nobody's.fc3"), new byte[100]);
                File.WriteAllBytes(Path.Combine(bin, "snaps", "Zelda II - The Adventure of Link (Europe)-0.png"), new byte[10]);
                var nesDir = Path.Combine(install, "Saves", "Nintendo Entertainment System");
                Directory.CreateDirectory(Path.Combine(nesDir, "States"));
                File.WriteAllBytes(Path.Combine(nesDir, "States", "Zelda II - The Adventure of Link (Europe)-bak.fc1"), new byte[99]);   // already there, different
                File.WriteAllBytes(Path.Combine(games, "Zelda II - The Adventure of Link (Europe).sav"), new byte[8192]);   // another emulator's, beside the game

                var r = Go("Nintendo Entertainment System", zelda);
                Check("a NES game launches, the line unchanged", r.WasSuccess && r.NewCommandLine == null, r.NewCommandLine);
                Check("bin\\sav a junction to Saves\\Nintendo Entertainment System", IsJunctionTo(Path.Combine(bin, "sav"), nesDir));
                Check("fcs / snaps / cheats junctions to States / Screenshots / Cheats", IsJunctionTo(Path.Combine(bin, "fcs"), Path.Combine(nesDir, "States"))
                      && IsJunctionTo(Path.Combine(bin, "snaps"), Path.Combine(nesDir, "Screenshots")) && IsJunctionTo(Path.Combine(bin, "cheats"), Path.Combine(nesDir, "Cheats")));
                Check("full screen by default (SDL.Fullscreen = 1)", Get("SDL.Fullscreen") == "1", Get("SDL.Fullscreen"));
                Check("the hotkeys bound", Get("SDL.Hotkeys.SaveState") == "F2" && Get("SDL.Hotkeys.Quit") == "Esc");
                Check("its .sav moved", File.Exists(Path.Combine(nesDir, "Zelda II - The Adventure of Link (Europe).sav")));
                Check("its state moved to States\\", File.Exists(Path.Combine(nesDir, "States", "Zelda II - The Adventure of Link (Europe).fc1")));
                Check("a backup that differs from one already there goes to Saves\\Unsorted, the one there kept",
                      new FileInfo(Path.Combine(nesDir, "States", "Zelda II - The Adventure of Link (Europe)-bak.fc1")).Length == 99
                      && File.Exists(Path.Combine(install, "Saves", "Unsorted", "fcs", "Zelda II - The Adventure of Link (Europe)-bak.fc1")));
                Check("the hack's state moved to its own platform's folder", File.Exists(Path.Combine(install, "Saves", "My NES Hacks", "States", "Super Mario Hack.fc0")));
                Check("a state of no game of the library: the NES's", File.Exists(Path.Combine(nesDir, "States", "Nobody's.fc3")));
                Check("its screenshot moved to Screenshots\\", File.Exists(Path.Combine(nesDir, "Screenshots", "Zelda II - The Adventure of Link (Europe)-0.png")));
                Check("the .sav beside the game (another emulator's) left where it is", File.Exists(Path.Combine(games, "Zelda II - The Adventure of Link (Europe).sav")));
                var stamp = File.GetLastWriteTimeUtc(cfgFile);
                System.Threading.Thread.Sleep(20);
                Go("Nintendo Entertainment System", zelda);
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(cfgFile) == stamp);

                // A disk: no BIOS, refused and said; with one, launched into the disk platform's folders.
                var disk = Path.Combine(games, "Zelda no Densetsu (Japan).fds");
                File.WriteAllBytes(disk, Encoding.ASCII.GetBytes("FDS\x1a").Concat(new byte[65500]).ToArray());
                said.Clear();
                r = Go("Nintendo Famicom Disk System", disk);
                Check("a disk game without disksys.rom is refused, said", !r.WasSuccess && said.Count == 1 && said[0].Contains("disksys.rom") && said[0].Contains(bin), said.FirstOrDefault()?.Split('\n')[0]);
                File.WriteAllBytes(Path.Combine(bin, "disksys.rom"), new byte[100]);
                said.Clear();
                r = Go("Nintendo Famicom Disk System", disk);
                Check("one of the wrong size is refused, said", !r.WasSuccess && said.Count == 1 && said[0].Contains("not 8 192 bytes"));
                File.WriteAllBytes(Path.Combine(bin, "disksys.rom"), new byte[8192]);
                r = Go("Nintendo Famicom Disk System", disk);
                var fdsDir = Path.Combine(install, "Saves", "Nintendo Famicom Disk System");
                Check("with it the disk launches, the junctions pointed at the disk platform's folders", r.WasSuccess && IsJunctionTo(Path.Combine(bin, "sav"), fdsDir) && IsJunctionTo(Path.Combine(bin, "fcs"), Path.Combine(fdsDir, "States")));
                Check("the NES's folders still there, their files too", File.Exists(Path.Combine(nesDir, "Zelda II - The Adventure of Link (Europe).sav")));
                r = Go("Nintendo Entertainment System", disk);
                Check("a .fds among NES games launches too, in the NES folders", r.WasSuccess && IsJunctionTo(Path.Combine(bin, "sav"), nesDir));

                // A link of the user's is kept, for every platform.
                var mine = Path.Combine(dir, "My Shots");
                Directory.CreateDirectory(mine);
                Directory.Delete(Path.Combine(bin, "snaps"));
                Call(asm, "FceuxSavePaths", "Junction", Path.Combine(bin, "snaps"), mine, null);
                Go("My NES Hacks", hack);
                Check("a junction the user made elsewhere is kept", IsJunctionTo(Path.Combine(bin, "snaps"), mine));
                Check("the custom platform gets its own folders", IsJunctionTo(Path.Combine(bin, "sav"), Path.Combine(install, "Saves", "My NES Hacks")));
                var dirs = (List<string>)Call(asm, "FceuxSavePaths", "Dirs", exe, "My NES Hacks", "snaps");
                Check("the user's folder listed first for its kind", dirs.Count >= 1 && string.Equals(dirs[0], Path.Combine(bin, "snaps"), StringComparison.OrdinalIgnoreCase), string.Join(" | ", dirs));

                // Windowed, for this game only.
                T(asm, "FceuxSettings").GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g-window", "Fullscreen", "false" });
                Go("Nintendo Entertainment System", zelda, "g-window");
                Check("a game set windowed: SDL.Fullscreen = 0", Get("SDL.Fullscreen") == "0");

                // A game's own options: written for its session, FCEUX's own values put back after.
                var session = Path.Combine(bin, "lbip-fceux-session.txt");
                var was = (Get("SDL.PAL"), Get("SDL.AutoDetectPAL"), Get("SDL.VideoDriver"));
                var setGame = T(asm, "FceuxSettings").GetMethod("SetGameValue", Any);
                setGame.Invoke(null, new object[] { "g-own", "Region", "pal" });
                setGame.Invoke(null, new object[] { "g-own", "VideoDriver", "3" });
                Go("Nintendo Entertainment System", zelda, "g-own");
                Check("a game's region and driver written: SDL.PAL 1, SDL.AutoDetectPAL 0, SDL.VideoDriver 3",
                      Get("SDL.PAL") == "1" && Get("SDL.AutoDetectPAL") == "0" && Get("SDL.VideoDriver") == "3", Get("SDL.PAL") + " / " + Get("SDL.AutoDetectPAL") + " / " + Get("SDL.VideoDriver"));
                Check("FCEUX's own values noted beside fceux.cfg", File.Exists(session) && File.ReadAllText(session).Contains("SDL.PAL=" + (was.Item1 ?? "0")), File.Exists(session) ? File.ReadAllText(session).Replace("\r\n", " | ") : "none");
                Call(asm, "FceuxLaunch", "PutBackNow", exe);
                Check("FCEUX quit: its own values back, the note gone", Get("SDL.PAL") == (was.Item1 ?? "0") && Get("SDL.AutoDetectPAL") == (was.Item2 ?? "1") && Get("SDL.VideoDriver") == (was.Item3 ?? "0") && !File.Exists(session),
                      Get("SDL.PAL") + " / " + Get("SDL.AutoDetectPAL") + " / " + Get("SDL.VideoDriver"));
                Go("Nintendo Entertainment System", zelda, "g-own");
                Go("Nintendo Entertainment System", zelda);   // the end of the first was not seen
                Check("an end not seen: put back at the next launch of another game", Get("SDL.PAL") == (was.Item1 ?? "0") && Get("SDL.VideoDriver") == (was.Item3 ?? "0") && !File.Exists(session));
                Go("Nintendo Entertainment System", zelda, "g-own");
                Go("Nintendo Entertainment System", zelda, "g-own");
                Call(asm, "FceuxLaunch", "PutBackNow", exe);
                Check("two launches of the game in a row: FCEUX's own values still the ones put back", Get("SDL.PAL") == (was.Item1 ?? "0") && Get("SDL.VideoDriver") == (was.Item3 ?? "0"));

                // A path FCEUX cannot open: refused, said.
                var japanese = Path.Combine(games, "\u30bc\u30eb\u30c0\u306e\u4f1d\u8aac.nes");
                File.WriteAllBytes(japanese, new byte[16]);
                said.Clear();
                r = Go("Nintendo Entertainment System", japanese);
                Check("a path outside the ANSI code page is refused, said", !r.WasSuccess && said.Count == 1 && said[0].Contains("ANSI"));

                // A zip FCEUX opens itself: passed as it is. A zip of a .unf: unpacked for it, the line pointed there.
                var zip = Path.Combine(games, "Zelda zipped.zip");
                using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntryFromFile(zelda, Path.GetFileName(zelda));
                r = Go("Nintendo Entertainment System", zip);
                Check("a zip of a .nes: passed as it is", r.WasSuccess && r.NewCommandLine == null);
                var unfZip = Path.Combine(games, "Unif game.zip");
                var unf = Path.Combine(games, "Unif game (USA).unf");
                File.WriteAllBytes(unf, Encoding.ASCII.GetBytes("UNIF").Concat(new byte[512]).ToArray());
                using (var z = ZipFile.Open(unfZip, ZipArchiveMode.Create)) z.CreateEntryFromFile(unf, Path.GetFileName(unf));
                File.Delete(unf);
                r = Go("Nintendo Entertainment System", unfZip);
                var unpacked = Directory.Exists(Path.Combine(install, "Unpacked")) ? Directory.GetFiles(Path.Combine(install, "Unpacked"), "*.unf", SearchOption.AllDirectories).FirstOrDefault() : null;
                Check("a zip of a .unf: unpacked into <install>\\Unpacked\\, under the ROM's name, the line pointed at it", r.WasSuccess && unpacked != null && Path.GetFileName(unpacked) == "Unif game (USA).unf"
                      && r.NewCommandLine == "\"" + unpacked + "\"", r.NewCommandLine);

                // Not ours: nothing.
                var mednafen = new StubEmulator { Title = "other", ApplicationPath = Path.Combine(dir, "mednafen.exe") };
                File.WriteAllText(mednafen.ApplicationPath, "");
                var game = StubGame.With(StubGame.Create("g-x", "x", zelda), "Platform", "Nintendo Entertainment System");
                r = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(mednafen, game, "\"" + zelda + "\"", null, null));
                Check("another emulator's launch: nothing done", r.WasSuccess && r.NewCommandLine == null);
            }
            finally
            {
                PluginHelper.DataManager = wasDm;
                StubDataManager.Platforms = wasPlatforms;
                paths.GetField("LibraryForProbe", Any).SetValue(null, null);
                try { notice.GetField("Instead", Any).SetValue(null, null); } catch { }
                // The junctions first (a recursive delete must never walk into what they lead to).
                foreach (var j in junctions) try { if (new DirectoryInfo(j).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(j); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
            return Done();
        }

        /// <summary>A REAL install's fceux.cfg and folders prepared as a launch of that game does (no other change), for an
        /// oracle run.</summary>
        public static bool Prepare(Assembly asm, string exe, string rom, string platform, string settings)
        {
            if (settings != null) T(asm, "FceuxSettings").GetField("PathOverride", Any).SetValue(null, Path.GetFullPath(settings));
            Console.WriteLine("-- fceux.cfg of " + exe + " prepared for " + Path.GetFileName(rom) + " (" + platform + ") --");
            Call(asm, "FceuxLaunch", "PrepareConfig", exe, "probe-oracle", platform, rom);
            var cfg = Path.Combine(Path.GetDirectoryName(exe), "fceux.cfg");
            foreach (var line in File.ReadAllLines(cfg).Where(l => l.StartsWith("SDL.Fullscreen ") || l.StartsWith("SDL.Hotkeys.")))
                Console.WriteLine("    " + line);
            foreach (var k in new[] { "sav", "fcs", "snaps", "cheats" })
                Console.WriteLine("    " + k + " -> " + (new DirectoryInfo(Path.Combine(Path.GetDirectoryName(exe), k)).LinkTarget ?? "(a real folder)"));
            return true;
        }

        // ── the release ──────────────────────────────────────────────────────

        public static bool Latest(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- FCEUX's latest release --");
            var r = Call(asm, "FceuxBuild", "Latest");
            if (r == null) { Console.WriteLine("  FAIL - none"); return false; }
            foreach (var f in new[] { "Version", "Tag", "Url", "Sha256" }) Console.WriteLine("  " + f + ": " + Field(r, f));
            Check("a version x.y.z, the win64 Qt asset", System.Text.RegularExpressions.Regex.IsMatch((string)Field(r, "Version") ?? "", @"^\d+\.\d+\.\d+$") && ((string)Field(r, "Url")).EndsWith("-win64-QtSDL.zip"));
            Check("tags as GitHub has them", (string)Call(asm, "FceuxBuild", "VersionOf", "fceux-2.7.0") == "2.7.0" && (string)Call(asm, "FceuxBuild", "VersionOf", "v2.6.6") == "2.6.6"
                  && Call(asm, "FceuxBuild", "VersionOf", "interim-build") == null);
            var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract));
            Check("IsUpdateAvailable(empty path) = true: the Download button", plugin.IsUpdateAvailable("", out var v) && v != null, v?.Label);
            var dir = Scratch("latest");
            try
            {
                var exe = Path.Combine(dir, "qfceux.exe");
                File.WriteAllText(exe, "");
                Check("an install that is not ours: false", !plugin.IsUpdateAvailable(exe, out _));
                File.WriteAllText(Path.Combine(dir, "lbip-fceux-build.txt"), "2.6.6\r\nfceux-2.6.6-win64-QtSDL.zip\r\n");
                Check("ours, older: true", plugin.IsUpdateAvailable(exe, out var newer) && newer?.Label == (string)Field(r, "Version"));
                File.WriteAllText(Path.Combine(dir, "lbip-fceux-build.txt"), Field(r, "Version") + "\r\n");
                Check("ours, the latest: false", !plugin.IsUpdateAvailable(exe, out _));
                Check("GetCurrentVersion reads the stamp", plugin.GetCurrentVersion(exe) == (string)Field(r, "Version"));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        /// <summary>The Nixx page and a game's window (one game, two games), drawn into PNGs beside <paramref name="output"/>.</summary>
        public static bool Shot(Assembly asm, string output, string exe)
        {
            _bad = 0;
            Console.WriteLine("-- the Nixx page and a game's window, drawn --");
            try
            {
                if (exe != null) T(asm, "FceuxGameForm").GetField("ExeForProbe", Any).SetValue(null, exe);
                var page = (System.Windows.Forms.Control)T(asm, "Settings").GetMethod("CreatePage").Invoke(null, null);
                var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(700, 720), Font = new System.Drawing.Font("Segoe UI", 9f) };
                page.Dock = System.Windows.Forms.DockStyle.Fill; host.Controls.Add(page); host.Show();
                System.Windows.Forms.Application.DoEvents();
                using (var bmp = new System.Drawing.Bitmap(host.Width, host.Height)) { host.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, host.Width, host.Height)); bmp.Save(Path.ChangeExtension(output, ".page.png")); }
                host.Close();
                var g1 = StubGame.With(StubGame.Create("shot-1", "Zelda II - The Adventure of Link (Europe)", @"G:\LB\FakeCatalog\nes\Zelda II - The Adventure of Link (Europe).nes"), "Platform", "Nintendo Entertainment System");
                var g2 = StubGame.With(StubGame.Create("shot-2", "Zelda no Densetsu", @"C:\games\Zelda no Densetsu.fds"), "Platform", "Nintendo Famicom Disk System");
                foreach (var (name, games) in new[] { ("one", new[] { g1 }), ("two", new[] { g1, g2 }) })
                {
                    var form = (System.Windows.Forms.Form)Activator.CreateInstance(T(asm, "FceuxGameForm"), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { games }, null);
                    form.Show(); System.Windows.Forms.Application.DoEvents();
                    using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); bmp.Save(Path.ChangeExtension(output, "." + name + ".png")); }
                    form.Close();
                }
                Check("drawn", true, Path.GetDirectoryName(Path.GetFullPath(output)));
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            return Done();
        }

        public static bool Extract(Assembly asm, string zip, string folder)
        {
            _bad = 0;
            Console.WriteLine("-- " + Path.GetFileName(zip) + " -> " + folder + " --");
            var cfg = Path.Combine(folder, "bin", "fceux.cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(cfg));
            File.WriteAllText(cfg, "SDL.zzzMine = 1\r\n");
            Call(asm, "Archives", "ExtractOver", zip, folder);
            var exe = Path.Combine(folder, "bin", "qfceux.exe");
            Check(@"bin\qfceux.exe there", File.Exists(exe));
            Check(@"bin\Qt6Core.dll and palettes\FCEUX.pal there", File.Exists(Path.Combine(folder, "bin", "Qt6Core.dll")) && File.Exists(Path.Combine(folder, "palettes", "FCEUX.pal")));
            Check("the user's fceux.cfg untouched", File.ReadAllText(cfg) == "SDL.zzzMine = 1\r\n");
            Check("the install's folder is bin's parent", string.Equals((string)Call(asm, "FceuxSavePaths", "InstallDir", exe), Path.GetFullPath(folder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("  FCEUX says it is " + Call(asm, "FceuxBuild", "DescribedVersion", exe));
            return Done();
        }
    }
}
