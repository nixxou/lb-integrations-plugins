// The FCEUX plugin's pieces, run without a host:
//   --fceux-systems [<fceux source folder>]   the systems / platforms table (and, with the source at the tag, the open
//                                             dialog's filter and the archive extensions read off it)
//   --fceux-cfg <fceux.cfg>                   the editor on a COPY: nothing changed when nothing is set, a key set in place,
//                                             a new key in FCEUX's byte order, LF, no BOM, a CR kept, ANSI text
//   --fceux-hotkeys <first-run fceux.cfg>     the default table rebuilt = the first-run file's; the pack's keys bound, once;
//                                             a key of the user's never taken
//   --fceux-launch [<fceux.cfg>]              a launch as LaunchBox makes it, on a COPY of an install (the exe a stand-in):
//                                             claiming, platform rows only added, folders per platform, the BIOS refusal,
//                                             the ANSI refusal, a .unf in a zip, the files moved out of FCEUX's folders
//   --fceux-prepare <fceux64.exe> --rom <rom> --platform <name> [--settings <ini>]
//                                             fceux.cfg of a REAL install written as a launch does
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
            Check("one catalogue row, Nixx-FCEUX, fceux64.exe, no auto-extract", rows.Count == 1 && rows[0].Name == "Nixx-FCEUX" && rows[0].BinaryFileName == "fceux64.exe" && !rows[0].AutoExtract);
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
                Check("\"fs\" read as 0, \"odnonvol\" as empty, CurrentState as 0", Get(cfg, "\"fs\"") == "0" && Get(cfg, "\"odnonvol\"") == "" && Get(cfg, "CurrentState") == "0",
                      Get(cfg, "\"fs\"") + " / [" + Get(cfg, "\"odnonvol\"") + "] / " + Get(cfg, "CurrentState"));
                Set(cfg, "\"fs\"", "0");
                Check("a value set to what it is: nothing changed", !(bool)cfgType.GetProperty("Changed").GetValue(cfg));
                var lines = Encoding.Latin1.GetString(original).Split('\n');
                Set(cfg, "\"odnonvol\"", @"G:\A folder\Saves é");
                Set(cfg, "\"aaaNewKey\"", "7");      // between "988fssync"... and the others, by byte order
                Set(cfg, "zzzLast", "1");
                Save(cfg);
                var after = File.ReadAllBytes(copy);
                var afterLines = Encoding.Latin1.GetString(after).Split('\n');
                Check("one line more each for the two new keys", afterLines.Length == lines.Length + 2, lines.Length + " -> " + afterLines.Length);
                int Line(string[] ls, string key) => Array.FindIndex(ls, l => l.StartsWith(key + " "));
                Check("a key set where it stands", Line(afterLines, "\"odnonvol\"") == Line(lines, "\"odnonvol\"") + 1 || Line(afterLines, "\"odnonvol\"") == Line(lines, "\"odnonvol\""),
                      Line(lines, "\"odnonvol\"") + " -> " + Line(afterLines, "\"odnonvol\""));
                var keys = afterLines.Where(l => l.Length > 0).Select(l => l.Split(' ')[0]).ToList();
                Check("the keys still in byte order", keys.SequenceEqual(keys.OrderBy(k => k, StringComparer.Ordinal)));
                Check("LF only, no BOM", after[0] != 0xEF && !Encoding.Latin1.GetString(after).Contains("\r\n") && after[after.Length - 1] == (byte)'\n');
                Check("an accented path written in the ANSI code page (é = 0xE9)", afterLines.Any(l => l == "\"odnonvol\" G:\\A folder\\Saves \u00e9"));
                Check("read back the same", Get(Load(), "\"odnonvol\"") == @"G:\A folder\Saves é");
                // A file someone edited with CRLF: FCEUX reads CR as an end of line; the CR is kept on the line set.
                File.WriteAllBytes(copy, Encoding.ASCII.GetBytes("!version 1\r\n\"fs\" 0\r\nfoo bar baz\r\n"));
                cfg = Load();
                Check("CRLF file: values read without the CR", Get(cfg, "\"fs\"") == "0" && Get(cfg, "foo") == "bar baz");
                Set(cfg, "\"fs\"", "1"); Save(cfg);
                Check("CRLF file: the line set keeps its CR, the others untouched", File.ReadAllText(copy, Encoding.ASCII) == "!version 1\r\n\"fs\" 1\r\nfoo bar baz\r\n");
                // Made from nothing.
                File.Delete(copy);
                cfg = Load();
                Set(cfg, "\"odstates\"", @"C:\x"); Set(cfg, "\"fs\"", "1"); Set(cfg, "CurrentState", "0"); Save(cfg);
                Check("made from nothing: !version first, in byte order", File.ReadAllText(copy, Encoding.ASCII) == "!version 1\n\"fs\" 1\n\"odstates\" C:\\x\nCurrentState 0\n", File.ReadAllText(copy).Replace("\n", " | "));
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
                byte[] Table(object c) => (byte[])cfgType.GetMethod("GetBlob", Inst).Invoke(c, new object[] { "FCEUD_CommandMapping_V2" });
                List<int> Keys(byte[] t, int i) => (List<int>)hk.GetMethod("KeysOf", Any).Invoke(null, new object[] { t, i });
                List<string> Bind(object c) => (List<string>)hk.GetMethod("Bind", Any).Invoke(null, new[] { c });
                void Save(object c) => cfgType.GetMethod("SaveIfChanged", Inst).Invoke(c, null);
                string Hex(List<int> k) => "{" + string.Join(",", k.Select(x => "0x" + x.ToString("X"))) + "}";

                var shipped = Table(Load());
                var rebuilt = (byte[])hk.GetMethod("DefaultTable", Any).Invoke(null, null);
                Check("the default table rebuilt is the first-run file's, byte for byte", shipped != null && rebuilt.SequenceEqual(shipped), shipped?.Length + " / " + rebuilt.Length + " bytes");

                var cfg = Load();
                foreach (var s in Bind(cfg)) Console.WriteLine("    " + s);
                Save(cfg);
                var t = Table(Load());
                Check("SAVE_STATE = I, F2", Hex(Keys(t, 26)) == "{0x17,0x3C}", Hex(Keys(t, 26)));
                Check("LOAD_STATE = P, F4", Hex(Keys(t, 38)) == "{0x19,0x3E}", Hex(Keys(t, 38)));
                Check("SAVE_SLOT_PREV = F6, SAVE_SLOT_NEXT = F7", Hex(Keys(t, 25)) == "{0x40}" && Hex(Keys(t, 24)) == "{0x41}");
                Check("EXIT = Esc", Hex(Keys(t, 6)) == "{0x1}");
                Check("load slots 2, 4, 6, 7 and hide-menu given up", Keys(t, 42).Count == 0 && Keys(t, 44).Count == 0 && Keys(t, 46).Count == 0 && Keys(t, 47).Count == 0 && Keys(t, 5).Count == 0);
                Check("the other load slots, the save slots and the TAS editor's Esc kept", Hex(Keys(t, 41)) == "{0x3B}" && Hex(Keys(t, 43)) == "{0x3D}" && Hex(Keys(t, 30)) == "{0x3003C}" && Hex(Keys(t, 113)) == "{0x1}");
                Check("full screen still Alt+Enter, F11 not bound", Hex(Keys(t, 100)) == "{0x30001C}" && !Enumerable.Range(0, 129).Any(i => Keys(t, i).Contains(0x57)));
                var stamp = File.GetLastWriteTimeUtc(copy);
                System.Threading.Thread.Sleep(20);
                cfg = Load(); Bind(cfg); Save(cfg);
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(copy) == stamp);
                var only = File.ReadAllBytes(copy);
                var orig = File.ReadAllBytes(firstRun);
                Check("only the hotkey line changed", Encoding.Latin1.GetString(only).Split('\n').Zip(Encoding.Latin1.GetString(orig).Split('\n')).Count(p => p.First != p.Second) == 1);

                // The user's: SAVE_STATE moved to K, F7 on a pad button, Esc on PAUSE - nothing of theirs taken.
                File.Copy(firstRun, copy, overwrite: true);
                cfg = Load();
                var mine = Table(cfg);
                void Put(byte[] table, int index, int key) { int at = index * 92; Array.Clear(table, at, 92); BitConverter.GetBytes(key).CopyTo(table, at + 8); BitConverter.GetBytes(1u).CopyTo(table, at + 24); }
                Put(mine, 26, 0x25);       // SAVE_STATE = K
                Put(mine, 2, 0x01);        // PAUSE = Esc
                cfgType.GetMethod("SetBlob", Inst).Invoke(cfg, new object[] { "FCEUD_CommandMapping_V2", mine });
                var pads = (byte[])cfgType.GetMethod("GetBlob", Inst).Invoke(cfg, new object[] { "GamePadConfig_V2" });
                Put(pads, 1, 0x41);        // pad 1's B = F7
                cfgType.GetMethod("SetBlob", Inst).Invoke(cfg, new object[] { "GamePadConfig_V2", pads });
                foreach (var s in Bind(cfg)) Console.WriteLine("    " + s);
                t = Table(cfg);
                Check("a command the user rebound is not touched (SAVE_STATE = K)", Hex(Keys(t, 26)) == "{0x25}" && Hex(Keys(t, 42)) == "{0x3C}", Hex(Keys(t, 26)));
                Check("a key on a pad button is not bound (F7)", Keys(t, 24).Count == 0 && Hex(Keys(t, 47)) == "{0x41}");
                Check("a key on another command is not bound (Esc on PAUSE)", Keys(t, 6).Count == 0 && Hex(Keys(t, 5)) == "{0x1}");
                Check("the rest still bound (F4, F6)", Hex(Keys(t, 38)) == "{0x19,0x3E}" && Hex(Keys(t, 25)) == "{0x40}");

                // Another version's table (2.6.4 has 128 commands): left alone.
                cfg = Load();
                cfgType.GetMethod("SetBlob", Inst).Invoke(cfg, new object[] { "FCEUD_CommandMapping_V2", new byte[128 * 92] });
                var said = Bind(cfg);
                Check("a table of another length is left alone", Table(cfg).Length == 128 * 92 && Table(cfg).All(b => b == 0), said.FirstOrDefault());
                // No table yet (FCEUX never ran): the defaults, then the pack's keys.
                File.WriteAllText(copy, "\"fs\" 0\n");
                cfg = Load(); Bind(cfg); Save(cfg);
                t = Table(Load());
                Check("no table yet: FCEUX's defaults with the pack's keys", t != null && t.Length == 129 * 92 && Hex(Keys(t, 26)) == "{0x17,0x3C}" && Hex(Keys(t, 4)) == "{0x58}");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── a launch ─────────────────────────────────────────────────────────

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
            var wasDm = PluginHelper.DataManager;
            var wasPlatforms = StubDataManager.Platforms;
            try
            {
                var install = Path.Combine(dir, "Emulators", "Nixx-FCEUX");
                Directory.CreateDirectory(install);
                var exe = Path.Combine(install, "fceux64.exe");
                File.WriteAllText(exe, "");
                if (cfgPath != null) File.Copy(cfgPath, Path.Combine(install, "fceux.cfg"));
                var cfgFile = Path.Combine(install, "fceux.cfg");
                var cfgType = T(asm, "FceuxCfg");
                string Get(string k) => (string)cfgType.GetMethod("Get", Inst).Invoke(cfgType.GetMethod("Load", Any).Invoke(null, new object[] { cfgFile }), new object[] { k });
                void SetKey(string k, string v) { var c = cfgType.GetMethod("Load", Any).Invoke(null, new object[] { cfgFile }); cfgType.GetMethod("Set", Inst).Invoke(c, new object[] { k, v }); cfgType.GetMethod("SaveIfChanged", Inst).Invoke(c, null); }
                var games = Path.Combine(dir, "My Games");
                Directory.CreateDirectory(games);

                var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract));
                // ── claiming, and the rows of a user's entry ──
                StubDataManager.Platforms = new IPlatform[] { StubGame.Platform("Nintendo Entertainment System"), StubGame.Platform("Nintendo Famicom Disk System"), StubGame.Platform("My NES Hacks") };
                var emu = new StubEmulator { Title = "FCEUX (mine)", ApplicationPath = exe };
                var nes = emu.AddNewEmulatorPlatform(); nes.Platform = "Nintendo Entertainment System"; nes.IsDefault = true;
                var custom = emu.AddNewEmulatorPlatform(); custom.Platform = "My NES Hacks"; custom.IsDefault = true;
                var other = new StubEmulator { Title = "Nestopia", ApplicationPath = @"C:\Emu\nestopia.exe" };
                var otherFds = other.AddNewEmulatorPlatform(); otherFds.Platform = "Nintendo Famicom Disk System"; otherFds.IsDefault = true;
                PluginHelper.DataManager = new StubDataManager(emu, other);
                var qt = new StubEmulator { Title = "qt", ApplicationPath = @"C:\Emu\qfceux.exe" };
                var x32 = new StubEmulator { Title = "32", ApplicationPath = @"C:\Emu\FCEUX\fceux.exe" };
                var claimed = plugin.GetApplicableEmulators(new IEmulator[] { emu, other, qt, x32 }).ToList();
                Check("claimed: fceux64.exe and fceux.exe, not qfceux.exe nor nestopia.exe", claimed.Count == 2 && claimed.Contains(emu) && claimed.Contains(x32));
                Check("hotkey scripts filled, the Exit one for the entry's exe", emu.ExitAutoHotkeyScript?.Contains("ahk_exe fceux64.exe") == true && x32.ExitAutoHotkeyScript?.Contains("ahk_exe fceux.exe") == true
                      && emu.SaveStateAutoHotkeyScript?.Contains("PostMessage, 0x111, 40344, 0,, ahk_class FCEUXWindowClass ahk_exe fceux64.exe") == true
                      && emu.LoadStateAutoHotkeyScript?.Contains("40343") == true);
                var rows = emu.GetAllEmulatorPlatforms();
                Console.WriteLine("    rows: " + string.Join(", ", rows.Select(r => r.Platform + (r.IsDefault ? "*" : ""))));
                Check("the user's rows kept, their ticks too", rows.Any(r => r.Platform == "My NES Hacks" && r.IsDefault) && rows.Any(r => r.Platform == "Nintendo Entertainment System" && r.IsDefault));
                Check("the disk system added (the library has it), not the default (Nestopia is)", rows.Any(r => r.Platform == "Nintendo Famicom Disk System" && !r.IsDefault));
                Check("no row for a platform the library lacks", !rows.Any(r => r.Platform == "Nintendo Famicom" || r.Platform == "Nes - Super Mario Bros. Hacks"));
                var count = rows.Length;
                plugin.GetApplicableEmulators(new IEmulator[] { emu });
                Check("asked again: nothing added twice", emu.GetAllEmulatorPlatforms().Length == count);
                var editing = new StubEmulator { Title = "being typed", ApplicationPath = exe };
                editing.AddNewEmulatorPlatform().Platform = "";
                plugin.GetApplicableEmulators(new IEmulator[] { editing });
                Check("nothing added beside a row being typed", editing.GetAllEmulatorPlatforms().Length == 1);

                // ── launches ──
                PrepareForLaunchResponse Go(string platform, string rom, string id = null)
                {
                    var game = StubGame.With(StubGame.Create(id ?? ("g-" + Path.GetFileNameWithoutExtension(rom)), Path.GetFileNameWithoutExtension(rom), rom), "Platform", platform);
                    return plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(emu, game, "\"" + rom + "\"", null, null));
                }
                // A save of this game in FCEUX's own folders, from before the plugin: moved at its launch.
                var zelda = Path.Combine(games, "Zelda II - The Adventure of Link (Europe).nes");
                File.WriteAllBytes(zelda, Encoding.ASCII.GetBytes("NES\x1a").Concat(new byte[1020]).ToArray());
                Directory.CreateDirectory(Path.Combine(install, "sav")); Directory.CreateDirectory(Path.Combine(install, "fcs")); Directory.CreateDirectory(Path.Combine(install, "snaps"));
                File.WriteAllBytes(Path.Combine(install, "sav", "Zelda II - The Adventure of Link (Europe).sav"), new byte[8192]);
                File.WriteAllBytes(Path.Combine(install, "fcs", "Zelda II - The Adventure of Link (Europe).fc1"), new byte[100]);
                File.WriteAllBytes(Path.Combine(install, "fcs", "Zelda II - The Adventure of Link (Europe)-bak.fc1"), new byte[100]);
                File.WriteAllBytes(Path.Combine(install, "fcs", "Zelda II - The Adventure of Link (Europe) 2.fc1"), new byte[100]);   // another game's
                File.WriteAllBytes(Path.Combine(install, "snaps", "Zelda II - The Adventure of Link (Europe)-0.png"), new byte[10]);
                File.WriteAllBytes(Path.Combine(games, "Zelda II - The Adventure of Link (Europe).sav"), new byte[8192]);   // another emulator's, beside the game

                var r = Go("Nintendo Entertainment System", zelda);
                var nesDir = Path.Combine(install, "Saves", "Nintendo Entertainment System");
                Check("a NES game launches, the line unchanged", r.WasSuccess && r.NewCommandLine == null, r.NewCommandLine);
                Check("\"odnonvol\" = Saves\\Nintendo Entertainment System", Get("\"odnonvol\"") == nesDir, Get("\"odnonvol\""));
                Check("\"odstates\" / \"odsnaps\" / \"odcheats\" under it", Get("\"odstates\"") == Path.Combine(nesDir, "States") && Get("\"odsnaps\"") == Path.Combine(nesDir, "Screenshots") && Get("\"odcheats\"") == Path.Combine(nesDir, "Cheats"));
                Check("full screen by default, the display's mode written", Get("\"fs\"") == "1" && int.TryParse(Get("\"vmcx\""), out var w) && w > 0 && int.TryParse(Get("\"vmcy\""), out var h) && h > 0,
                      Get("\"vmcx\"") + "x" + Get("\"vmcy\"") + "x" + Get("\"vmcb\""));
                Check("the hotkeys bound", Get("FCEUD_CommandMapping_V2") != null);
                Check("its .sav moved out of sav\\", File.Exists(Path.Combine(nesDir, "Zelda II - The Adventure of Link (Europe).sav")) && !File.Exists(Path.Combine(install, "sav", "Zelda II - The Adventure of Link (Europe).sav")));
                Check("its state and the state's backup moved to States\\", File.Exists(Path.Combine(nesDir, "States", "Zelda II - The Adventure of Link (Europe).fc1")) && File.Exists(Path.Combine(nesDir, "States", "Zelda II - The Adventure of Link (Europe)-bak.fc1")));
                Check("another game's state left", File.Exists(Path.Combine(install, "fcs", "Zelda II - The Adventure of Link (Europe) 2.fc1")));
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
                Check("a disk game without disksys.rom is refused, said", !r.WasSuccess && said.Count == 1 && said[0].Contains("disksys.rom") && said[0].Contains(install), said.FirstOrDefault()?.Split('\n')[0]);
                File.WriteAllBytes(Path.Combine(install, "disksys.rom"), new byte[100]);
                said.Clear();
                r = Go("Nintendo Famicom Disk System", disk);
                Check("one of the wrong size is refused, said", !r.WasSuccess && said.Count == 1 && said[0].Contains("not 8 192 bytes"));
                File.WriteAllBytes(Path.Combine(install, "disksys.rom"), new byte[8192]);
                r = Go("Nintendo Famicom Disk System", disk);
                Check("with it the disk launches, in the disk platform's folders", r.WasSuccess && Get("\"odnonvol\"") == Path.Combine(install, "Saves", "Nintendo Famicom Disk System"), Get("\"odnonvol\""));
                r = Go("Nintendo Entertainment System", Path.Combine(games, "Zelda no Densetsu (Japan).fds"));
                Check("a .fds among NES games launches too, in the NES folders", r.WasSuccess && Get("\"odnonvol\"") == nesDir);

                // The user's own folder is kept, for every platform; a stale pack folder is set again.
                SetKey("\"odstates\"", @"D:\My States");
                SetKey("\"odsnaps\"", Path.Combine(dir, "Old LaunchBox", "Emulators", "Nixx-FCEUX", "Saves", "Nintendo Entertainment System", "Screenshots"));
                Go("Nes - Super Mario Bros. Hacks", zelda);
                Check("a folder the user chose is kept", Get("\"odstates\"") == @"D:\My States");
                Check("a pack folder of an install that moved is set again", Get("\"odsnaps\"") == Path.Combine(install, "Saves", "Nes - Super Mario Bros. Hacks", "Screenshots"), Get("\"odsnaps\""));
                Check("the SMB hacks get their own folder", Get("\"odnonvol\"") == Path.Combine(install, "Saves", "Nes - Super Mario Bros. Hacks"));

                // Windowed, for this game only.
                T(asm, "FceuxSettings").GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g-window", "Fullscreen", "false" });
                Go("Nintendo Entertainment System", zelda, "g-window");
                Check("a game set windowed: \"fs\" 0", Get("\"fs\"") == "0");

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
                Check("a zip of a .unf: unpacked into Unpacked\\, under the ROM's name, the line pointed at it", r.WasSuccess && unpacked != null && Path.GetFileName(unpacked) == "Unif game (USA).unf"
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
                try { notice.GetField("Instead", Any).SetValue(null, null); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
            return Done();
        }

        /// <summary>A REAL install's fceux.cfg written as a launch of that game writes it (no other change), for an oracle run.</summary>
        public static bool Prepare(Assembly asm, string exe, string rom, string platform, string settings)
        {
            if (settings != null) T(asm, "FceuxSettings").GetField("PathOverride", Any).SetValue(null, Path.GetFullPath(settings));
            Console.WriteLine("-- fceux.cfg of " + exe + " prepared for " + Path.GetFileName(rom) + " (" + platform + ") --");
            Call(asm, "FceuxLaunch", "PrepareConfig", exe, "probe-oracle", platform, rom);
            var cfg = Path.Combine(Path.GetDirectoryName(exe), "fceux.cfg");
            foreach (var line in File.ReadAllLines(cfg).Where(l => l.StartsWith("\"od") && !l.EndsWith(" ") || l.StartsWith("\"fs\"") || l.StartsWith("\"vmc")))
                Console.WriteLine("    " + line);
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
            Check("a version x.y.z, the win64 asset", System.Text.RegularExpressions.Regex.IsMatch((string)Field(r, "Version") ?? "", @"^\d+\.\d+\.\d+$") && ((string)Field(r, "Url")).EndsWith("-win64.zip"));
            Check("tags as GitHub has them", (string)Call(asm, "FceuxBuild", "VersionOf", "fceux-2.7.0") == "2.7.0" && (string)Call(asm, "FceuxBuild", "VersionOf", "v2.6.6") == "2.6.6"
                  && Call(asm, "FceuxBuild", "VersionOf", "interim-build") == null);
            var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract));
            Check("IsUpdateAvailable(empty path) = true: the Download button", plugin.IsUpdateAvailable("", out var v) && v != null, v?.Label);
            var dir = Scratch("latest");
            try
            {
                var exe = Path.Combine(dir, "fceux64.exe");
                File.WriteAllText(exe, "");
                Check("an install that is not ours: false", !plugin.IsUpdateAvailable(exe, out _));
                File.WriteAllText(Path.Combine(dir, "lbip-fceux-build.txt"), "2.6.6\r\nfceux-2.6.6-win64.zip\r\n");
                Check("ours, older: true", plugin.IsUpdateAvailable(exe, out var newer) && newer?.Label == (string)Field(r, "Version"));
                File.WriteAllText(Path.Combine(dir, "lbip-fceux-build.txt"), Field(r, "Version") + "\r\n");
                Check("ours, the latest: false", !plugin.IsUpdateAvailable(exe, out _));
                Check("GetCurrentVersion reads the stamp", plugin.GetCurrentVersion(exe) == (string)Field(r, "Version"));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        public static bool Extract(Assembly asm, string zip, string folder)
        {
            _bad = 0;
            Console.WriteLine("-- " + Path.GetFileName(zip) + " -> " + folder + " --");
            Directory.CreateDirectory(folder);
            var cfg = Path.Combine(folder, "fceux.cfg");
            File.WriteAllText(cfg, "zzzMine 1\n");
            Call(asm, "Archives", "ExtractOver", zip, folder);
            Check("fceux64.exe there", File.Exists(Path.Combine(folder, "fceux64.exe")));
            Check("7z_64.dll there (FCEUX's archives)", File.Exists(Path.Combine(folder, "7z_64.dll")));
            Check("the user's fceux.cfg untouched", File.ReadAllText(cfg) == "zzzMine 1\n");
            Console.WriteLine("  FCEUX says it is " + Call(asm, "FceuxBuild", "DescribedVersion", Path.Combine(folder, "fceux64.exe")));
            return Done();
        }
    }
}
