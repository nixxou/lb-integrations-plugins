// The puNES plugin's pieces, run without a host (each on COPIES under %TEMP%, nothing of the user's written):
//   --punes-systems <puNES source folder>   the systems table against puNES's own Open filter (src\gui\mainWindow.cpp)
//   --punes-cfg <puNES.cfg>                 the editor on a copy: read, set in place, add, CRLF, no BOM, nothing dropped
//   --punes-line                            the options on the line, before the host's; '=' refused; the game swapped
//   --punes-archives                        an archive given as it is, unpacked under its own name, or refused
//   --punes-hotkeys <input.cfg>             the pack's keys set over puNES's defaults, never the user's (a copy of a real input.cfg)
//   --punes-bios                            disksys.rom found where puNES looks, copied from another emulator's folder
//   --punes-versions <punes64.exe>          the version of a known exe, IsUpdateAvailable's three answers
//   --punes-e2e                             a launch as LaunchBox makes it (claim, line, FDS without/with BIOS, refusals)
//   --punes-fetch                           the pinned build downloaded from GitHub, its SHA-256 checked, its listing
//   --punes-shot <out.png>                  the Nixx page and a game's window, drawn
//   --punes-savedirs                        prb\ save\ diff\ screenshot\ made junctions to Saves\<platform>, files moved, never overwritten
//   --punes-saves [<real .p00>]             a game's rows: cartridge save, disk writes, the twelve slots; the thumbnail of a real state

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class PunesCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static bool Done()
        {
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Punes." + name, true);
        private static object Call(Assembly asm, string type, string method, params object[] args) => T(asm, type).GetMethod(method, Any).Invoke(null, args);
        private static object F(object o, string name) => o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o)
                                                         ?? o?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o);

        private static string Scratch(string what)
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-punes-" + what + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static (int CrLf, int Lf, bool Bom) Eol(string path)
        {
            var b = File.ReadAllBytes(path);
            int crlf = 0, lf = 0;
            for (int i = 0; i < b.Length; i++) if (b[i] == '\n') { lf++; if (i > 0 && b[i - 1] == '\r') crlf++; }
            return (crlf, lf, b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF);
        }

        // ── the systems ──────────────────────────────────────────────────────

        public static bool Systems(Assembly asm, string source)
        {
            _bad = 0;
            Console.WriteLine("-- the systems table against puNES's source --");
            var all = (Array)T(asm, "PunesSystems").GetField("All", Any).GetValue(null);
            var platforms = (Array)T(asm, "PunesSystems").GetField("Platforms", Any).GetValue(null);
            var main = Path.Combine(source, "src", "gui", "mainWindow.cpp");
            if (!File.Exists(main)) { Check("mainWindow.cpp found", false, main); return Done(); }
            var text = File.ReadAllText(main);
            // The Open dialog's filters: "*.nes *.NES" ... - every extension quoted as "*.<ext>" in the file.
            var inSource = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(text, @"\*\.([A-Za-z0-9]+)").Cast<System.Text.RegularExpressions.Match>()
                                               .Select(m => "." + m.Groups[1].Value.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("    puNES's filter: " + string.Join(" ", inSource.OrderBy(x => x)));
            foreach (var s in all)
            {
                var exts = (string[])F(s, "Extensions");
                var missing = exts.Where(e => !inSource.Contains(e)).ToList();
                Check("system " + F(s, "Id") + ": every extension is one puNES's Open dialog offers", missing.Count == 0, missing.Count == 0 ? string.Join(" ", exts) : "not in it: " + string.Join(" ", missing));
            }
            var mine = new HashSet<string>(all.Cast<object>().SelectMany(s => (string[])F(s, "Extensions")).Concat((string[])T(asm, "PunesSystems").GetField("Archives", Any).GetValue(null)), StringComparer.OrdinalIgnoreCase);
            var game = new[] { ".nes", ".unf", ".unif", ".fds", ".qd", ".nsf", ".nsfe", ".zip", ".7z", ".rar" };
            var left = inSource.Where(e => game.Contains(e) && !mine.Contains(e)).ToList();
            Check("every game type of puNES's filter is in the table", left.Count == 0, left.Count == 0 ? null : string.Join(" ", left));
            var fds = File.ReadAllText(Path.Combine(source, "src", "core", "fds.c"));
            Check("fds.c asks for disksys.rom", fds.Contains("disksys.rom"));
            var cmd = File.ReadAllText(Path.Combine(source, "src", "gui", "cmd_line.cpp"));
            Check("no --system switch in puNES (the system comes from the file)", !cmd.Contains("\"system\""));
            var names = platforms.Cast<object>().Select(p => (string)F(p, "Platform")).ToList();
            Check("the platforms: LaunchBox's NES and FDS first, then LiteBox's", names.Take(2).SequenceEqual(new[] { "Nintendo Entertainment System", "Nintendo Famicom Disk System" }), string.Join(", ", names));
            var sysFor = T(asm, "PunesSystems").GetMethod("SystemFor", Any);
            string Sys(string platform, string rom, string inner = null) { var r = sysFor.Invoke(null, new object[] { platform, rom, inner }); var s = r.GetType().GetField("Item1").GetValue(r); return s == null ? null : (string)F(s, "Id"); }
            Check("a .fds among NES games: fds", Sys("Nintendo Entertainment System", @"C:\g\Zelda.fds") == "fds");
            Check("a .nes under the FDS platform: nes (puNES goes by the file)", Sys("Nintendo Famicom Disk System", @"C:\g\Zelda.nes") == "nes");
            Check("a .zip of a .fds: fds", Sys("Nintendo Entertainment System", @"C:\g\Zelda.zip", ".fds") == "fds");
            Check("a .zip with nothing known inside, FDS platform: its default", Sys("Nintendo Famicom Disk System", @"C:\g\Zelda.zip") == "fds");
            Check("a custom platform with a .nes: nes", Sys("My NES Hacks", @"C:\g\x.nes") == "nes");
            Check("a custom platform with a .bin: nothing", Sys("My NES Hacks", @"C:\g\x.bin") == null);
            var every = (string)Call(asm, "PunesSystems", "EveryExtension");
            Check("the emulator row's extensions: the union", every.Contains(".fds") && every.Contains(".nes") && every.Contains(".zip"), every);
            return Done();
        }

        // ── puNES.cfg ────────────────────────────────────────────────────────

        public static bool Cfg(Assembly asm, string cfgPath)
        {
            _bad = 0;
            Console.WriteLine("-- puNES.cfg edited in place (a copy) --");
            var dir = Scratch("cfg");
            try
            {
                var copy = Path.Combine(dir, "puNES.cfg");
                File.Copy(cfgPath, copy);
                var before = File.ReadAllLines(copy);
                var type = T(asm, "PunesCfg");
                var cfg = type.GetMethod("Load", Any).Invoke(null, new object[] { copy });
                object Get(object c, string g, string k) => type.GetMethod("Get").Invoke(c, new object[] { g, k });
                void Set(object c, string g, string k, string v) => type.GetMethod("Set").Invoke(c, new object[] { g, k, v });
                void Save(object c) => type.GetMethod("SaveIfChanged").Invoke(c, null);
                Check("reads a key with spaces", Get(cfg, "system", "preferred mode") != null, "preferred mode=" + Get(cfg, "system", "preferred mode"));
                Check("reads an empty value as empty", (string)Get(cfg, "system", "fds bios file") == "", "fds bios file=" + Get(cfg, "system", "fds bios file"));
                Save(cfg);
                var stamp = File.GetLastWriteTimeUtc(copy);
                Check("nothing changed: nothing written", (bool)type.GetProperty("Changed").GetValue(cfg) == false);
                Set(cfg, "video", "size window", "3x");
                Set(cfg, "video", "lbip probe key", "1");
                Set(cfg, "lbip probe group", "k", "v");
                Save(cfg);
                var after = File.ReadAllLines(copy);
                Check("the value set where it stands", after.Contains("size window=3x"));
                Check("nothing dropped: as many lines plus the three added (blank + group + key + key)", after.Length == before.Length + 4, before.Length + " -> " + after.Length);
                int newKey = Array.IndexOf(after, "lbip probe key=1"), videoAt = Array.IndexOf(after, "[video]"), nextGroup = Array.FindIndex(after, videoAt + 1, l => l.StartsWith("["));
                Check("a new key inside its group", newKey > videoAt && newKey < nextGroup, newKey + " in " + videoAt + ".." + nextGroup);
                Check("a new group at the end", after[after.Length - 2] == "[lbip probe group]" && after[after.Length - 1] == "k=v");
                var (crlf, lf, bom) = Eol(copy);
                Check("CRLF throughout, no BOM", crlf == lf && !bom, crlf + " CRLF / " + lf + " LF, BOM " + bom);
                Check("the comments kept", after.Count(l => l.StartsWith("#")) == before.Count(l => l.StartsWith("#")));
                // An empty file (the portable marker): read as nothing, a value added makes a group.
                var empty = Path.Combine(dir, "empty.cfg"); File.WriteAllBytes(empty, new byte[0]);
                var e = type.GetMethod("Load", Any).Invoke(null, new object[] { empty });
                Check("an empty puNES.cfg reads as nothing", Get(e, "video", "fullscreen") == null);
                // Portable: an empty puNES.cfg made beside the exe, once; and never over a %LOCALAPPDATA% configuration of the user's.
                var exe = Path.Combine(dir, "inst", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
                var appCfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "puNES", "puNES.cfg");
                bool userHas = File.Exists(appCfg);
                var made = (bool)type.GetMethod("EnsurePortable", Any).Invoke(null, new object[] { exe });
                if (userHas) Check("a %LOCALAPPDATA%\\puNES\\puNES.cfg is there: the install left as it is", !made);
                else Check("no configuration anywhere: an empty puNES.cfg made beside the exe", made && File.Exists(Path.Combine(Path.GetDirectoryName(exe), "puNES.cfg")) && new FileInfo(Path.Combine(Path.GetDirectoryName(exe), "puNES.cfg")).Length == 0);
                Check("a second call makes nothing", !(bool)type.GetMethod("EnsurePortable", Any).Invoke(null, new object[] { exe }));
                var p = Path.Combine(dir, "inst2", "punes64_p.exe"); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, "");
                Check("a \"_p\" exe is portable by its name", (bool)type.GetMethod("IsPortable", Any).Invoke(null, new object[] { p }));
                Check("its data folder is its own", (string)type.GetMethod("DataDir", Any).Invoke(null, new object[] { p }) == Path.GetDirectoryName(p));
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── the line ─────────────────────────────────────────────────────────

        public static bool Line(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- the line --");
            var dir = Scratch("line");
            try
            {
                var settings = T(asm, "PunesSettings");
                settings.GetField("PathOverride", Any).SetValue(null, Path.Combine(dir, "settings.ini"));
                List<string> Args(string game, string sys) => (List<string>)settings.GetMethod("Args", Any, null, new[] { typeof(string), typeof(string) }, null).Invoke(null, new object[] { game, sys });
                Check("nothing chosen: full screen alone (the page's default)", string.Join(" ", Args("g1", "nes")) == "--fullscreen=yes", string.Join(" ", Args("g1", "nes")));
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g1", "Region", "pal" });
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g1", "Filter", "hq2x" });
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g1", "SaveBattery", "yes" });
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g1", "Screen", "windowed" });
                var nes = string.Join(" ", Args("g1", "nes"));
                Check("a game's own: windowed, PAL, hq2x, battery every 3 min", nes == "--fullscreen=no --mode=pal --filter=hq2x --save-battery-ram-file=yes", nes);
                Check("the cartridge-only option left out for a disk", !string.Join(" ", Args("g1", "fds")).Contains("save-battery"), string.Join(" ", Args("g1", "fds")));
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "g1", "Screen", "punes" });
                Check("\"As set in puNES\" for the screen passes nothing", !string.Join(" ", Args("g1", "nes")).Contains("--fullscreen"));
                settings.GetMethod("SetValue", Any).Invoke(null, new object[] { "Screen", "windowed" });
                Check("every game's screen from the page", string.Join(" ", Args("g2", "nes")) == "--fullscreen=no");
                var line = (string)Call(asm, "PunesLaunch", "CommandLine", "-u yes \"C:\\g\\Zelda.nes\"", new List<string> { "--fullscreen=no", "--mode=pal" }, null, null);
                Check("ours in front, the platform's line and the game untouched", line == "--fullscreen=no --mode=pal -u yes \"C:\\g\\Zelda.nes\"", line);
                line = (string)Call(asm, "PunesLaunch", "CommandLine", "\"C:\\g\\Zelda.zip\"", new List<string>(), "C:\\g\\Zelda.zip", "C:\\e\\Unpacked\\Zelda-1\\Zelda.nes");
                Check("an unpacked game in place of the archive", line == "\"C:\\e\\Unpacked\\Zelda-1\\Zelda.nes\"", line);
                Check("a path with '=' is not givable", !(bool)Call(asm, "PunesLaunch", "Givable", "C:\\g\\a=b Zelda.nes") && (bool)Call(asm, "PunesLaunch", "Givable", "C:\\g\\Zelda.nes"));
                Check("--portable seen on a line", (bool)Call(asm, "PunesLaunch", "PortableOnLine", "--portable \"x.nes\"") && !(bool)Call(asm, "PunesLaunch", "PortableOnLine", "\"--portable x.nes\""));
                // Every value is one of puNES's own names (settings.h opt_* tables, read at v0.111).
                var all = (Array)settings.GetField("All", Any).GetValue(null);
                Check("every option has a switch or is the screen", all.Cast<object>().All(o => F(o, "Switch") != null || (string)F(o, "Key") == "Screen"));
                Check("every option offers \"As set in puNES\"", all.Cast<object>().All(o => ((string[])F(o, "Values")).Contains("punes")));
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { T(asm, "PunesSettings").GetField("PathOverride", Any).SetValue(null, null); try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── archives ─────────────────────────────────────────────────────────

        private static void Zip(string path, params (string Name, int Size)[] entries)
        {
            using var z = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, size) in entries)
                using (var s = z.CreateEntry(name).Open()) s.Write(new byte[size], 0, size);
        }

        public static bool ArchivesCheck(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- archives --");
            var dir = Scratch("archives");
            try
            {
                var exe = Path.Combine(dir, "Nixx-puNES", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
                var games = Path.Combine(dir, "games"); Directory.CreateDirectory(games);
                (string Played, string Inner, string Problem) RomFor(string rom)
                {
                    var a = new object[] { exe, rom, null, null };
                    var r = (string)T(asm, "PunesLaunch").GetMethod("RomFor", Any).Invoke(null, a);
                    return (r, (string)a[2], (string)a[3]);
                }
                var plain = Path.Combine(games, "Zelda.nes"); File.WriteAllBytes(plain, new byte[16]);
                Check("a plain file as it is", RomFor(plain).Played == plain);
                var same = Path.Combine(games, "Zelda II.zip"); Zip(same, ("Zelda II.nes", 4096));
                var r1 = RomFor(same);
                Check("one game named after the archive: given as it is", r1.Played == same && r1.Inner == ".nes", r1.Played);
                var other = Path.Combine(games, "Outer Zip.zip"); Zip(other, ("inner name.nes", 4096));
                var r2 = RomFor(other);
                Check("named otherwise: unpacked under the archive's name, in the install's Unpacked\\", r2.Played != null && Path.GetFileName(r2.Played) == "Outer Zip.nes"
                      && r2.Played.StartsWith(Path.Combine(Path.GetDirectoryName(exe), "Unpacked")) && new FileInfo(r2.Played).Length == 4096, r2.Played ?? r2.Problem);
                var stamp = File.GetLastWriteTimeUtc(r2.Played);
                System.Threading.Thread.Sleep(30);
                Check("unpacked once: the same file the next time", RomFor(other).Played == r2.Played && File.GetLastWriteTimeUtc(r2.Played) == stamp);
                var withText = Path.Combine(games, "Kid.zip"); Zip(withText, ("Kid.nes", 2048), ("readme.txt", 10));
                var r3 = RomFor(withText);
                Check("named after it but with another file inside: unpacked (puNES's choice dialog never shown)", r3.Played != null && r3.Played != withText, r3.Played);
                var two = Path.Combine(games, "Two.zip"); Zip(two, ("a.nes", 16), ("b.nes", 16));
                var r4 = RomFor(two);
                Check("two games, none named after the archive: refused, said", r4.Played == null && r4.Problem.Contains("2 games"), r4.Problem);
                var none = Path.Combine(games, "Nothing.zip"); Zip(none, ("readme.txt", 16));
                Check("no game inside: refused", RomFor(none).Played == null);
                var disk = Path.Combine(games, "Disk.zip"); Zip(disk, ("Zelda no Densetsu.fds", 65500));
                var r5 = RomFor(disk);
                Check("a disk in a zip: its extension read", r5.Inner == ".fds" && r5.Played.EndsWith("Disk.fds"), r5.Played);
                var lab = @"G:\LB1326\lbip-punes-lab\roms\Outer 7z.7z";
                if (File.Exists(lab))
                {
                    var copy7 = Path.Combine(games, "Outer 7z.7z"); File.Copy(lab, copy7);
                    var r6 = RomFor(copy7);
                    Check("the lab's .7z (inner name.nes): unpacked under the archive's name", r6.Played != null && Path.GetFileName(r6.Played) == "Outer 7z.nes", r6.Played ?? r6.Problem);
                }
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── hotkeys ──────────────────────────────────────────────────────────

        public static bool Hotkeys(Assembly asm, string input)
        {
            _bad = 0;
            Console.WriteLine("-- the pack's hotkeys in input.cfg (a copy) --");
            var dir = Scratch("hotkeys");
            try
            {
                var exe = Path.Combine(dir, "punes64.exe"); File.WriteAllText(exe, "");
                File.WriteAllBytes(Path.Combine(dir, "puNES.cfg"), new byte[0]);   // portable: input.cfg beside it
                var copy = Path.Combine(dir, "input.cfg");
                void Bind() => Call(asm, "PunesLaunch", "BindHotkeys", exe);
                string Put(string t, string action, string value) => System.Text.RegularExpressions.Regex.Replace(t, "(?m)^" + System.Text.RegularExpressions.Regex.Escape(action) + "=.*$", action + "=" + value);
                void Write(string t) => File.WriteAllText(copy, t.Replace("\r\n", "\n").Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
                string[] Lines() => File.ReadAllLines(copy);
                var pack = new[]
                {
                    "save state=F2,NULL", "load state=F4,NULL", "decrement state slot=F6,NULL", "increment state slot=F7,NULL",
                    "video fullscreen=F11,NULL", "quit=Esc,NULL", "hard reset=Shift+Esc,NULL", "mode pal=F10,NULL", "mode ntsc=Shift+F10,NULL",
                    "toggle menubar in fullscreen=NULL,NULL",
                    "pause=P,NULL", "fast forward=Space,NULL", "hold fast forward=L,NULL", "save screenshot=F8,NULL", "audio enable=F9,NULL",
                    "soft reset=H,NULL", "mode dendy=Ctrl+F10,NULL", "mode auto=Alt+F10,NULL",
                };
                // Every keyboard key of [shortcuts] held once (what run5c measured on the real exe).
                bool NoKeyTwice() => Lines().SkipWhile(l => l != "[shortcuts]").Skip(1).TakeWhile(l => !l.StartsWith("["))
                    .Where(l => !l.StartsWith("#") && l.Contains('=')).Select(l => l.Substring(l.IndexOf('=') + 1).Split(',')[0].Trim())
                    .Where(k => k.Length > 0 && k != "NULL").GroupBy(k => k, StringComparer.OrdinalIgnoreCase).All(g => g.Count() == 1);

                Bind();
                Check("no input.cfg yet: made with the pack's lines (puNES writes the rest at its first start - measured)", File.Exists(copy)
                      && Lines().SequenceEqual(new[] { "[shortcuts]" }.Concat(pack)), File.Exists(copy) ? string.Join(" | ", Lines()) : "none");
                var (crlf0, lf0, bom0) = Eol(copy);
                Check("made in CRLF, no BOM", crlf0 == lf0 && !bom0);

                // A real input.cfg of puNES 0.111, its defaults; a pad button on save state.
                var defaults = Put(File.ReadAllText(input), "save state", "F1,JOY0_BTN3");
                Write(defaults);
                var lineCount = Lines().Length;
                Bind();
                var after = Lines();
                Check("puNES's defaults: all moved to the pack's keys", pack.Skip(1).All(after.Contains) && after.Contains("save state=F2,JOY0_BTN3"), string.Join(" | ", pack.Where(l => !after.Contains(l))));
                Check("the pad button on an action kept", after.Contains("save state=F2,JOY0_BTN3"));
                Check("no key on two actions", NoKeyTwice());
                Check("nothing else touched (open Alt+O, eject disk Alt+E)", after.Contains("open=Alt+O,NULL") && after.Contains("eject disk=Alt+E,NULL"));
                var (crlf, lf, bom) = Eol(copy);
                Check("CRLF, no BOM, as many lines", crlf == lf && !bom && after.Length == lineCount, crlf + "/" + lf);
                var once = File.ReadAllBytes(copy);
                Bind();
                Check("a second launch: the file untouched", File.ReadAllBytes(copy).SequenceEqual(once));

                // The user's own keys.
                Write(Put(Put(defaults, "quit", "CTRL+Q,NULL"), "decrement state slot", "F2,NULL"));
                Bind();
                after = Lines();
                Check("an action the user rebound: kept (quit = CTRL+Q)", after.Contains("quit=CTRL+Q,NULL"));
                Check("a pack key on an action of the user's... decrement state slot on F2 is puNES's default: moved to F6", after.Contains("decrement state slot=F6,NULL"));
                Write(Put(defaults, "quit", "F2,NULL"));
                Bind();
                after = Lines();
                Check("the user put quit on F2: quit kept, save state left as it was (F1)", after.Contains("quit=F2,NULL") && after.Contains("save state=F1,JOY0_BTN3"));
                Check("still no key on two actions", NoKeyTwice());
                Write(Put(defaults, "turn off", "F10,NULL"));
                Bind();
                after = Lines();
                Check("F10 on another action of the user's (turn off): mode pal off F6 (the slot's now), no key, the rest set", after.Contains("mode pal=NULL,NULL") && after.Contains("mode ntsc=Shift+F10,NULL") && after.Contains("turn off=F10,NULL"));
                Check("and no key on two actions", NoKeyTwice());
                // A keyboard button of a pad on a pack key.
                Write(Put(defaults, "P1K Start", "Esc"));
                Bind();
                after = Lines();
                Check("Esc on the pad's Start: quit left on Alt+Q", after.Contains("quit=Alt+Q,NULL"));

                Write(defaults);
                Bind();
                Check("the AutoHotkey keys read from input.cfg (save state = F2)", (string)Call(asm, "PunesLaunch", "KeyOf", exe, "save state", "F1") == "F2");
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── BIOS ─────────────────────────────────────────────────────────────

        public static bool Bios(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- the Famicom Disk System BIOS --");
            var dir = Scratch("bios");
            try
            {
                T(asm, "PunesFirmware").GetField("NoLibrary", Any).SetValue(null, true);
                var emus = Path.Combine(dir, "Emulators");
                var exe = Path.Combine(emus, "Nixx-puNES", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(exe), "puNES.cfg"), new byte[0]);
                var fds = Call(asm, "PunesSystems", "System", "fds");
                var bios = ((Array)F(fds, "Bios")).GetValue(0);
                var systems = Array.CreateInstance(fds.GetType(), 1); systems.SetValue(fds, 0);
                List<object> Ensure(string rom = null) => ((System.Collections.IEnumerable)T(asm, "PunesFirmware").GetMethod("Ensure", Any).Invoke(null, new object[] { exe, systems, null, rom })).Cast<object>().ToList();
                string PathOf(object state) => (string)state.GetType().GetField("Item2").GetValue(state);
                Check("none anywhere: not found", PathOf(Ensure()[0]) == null);
                var ra = Path.Combine(emus, "RetroArch", "system"); Directory.CreateDirectory(ra);
                File.WriteAllBytes(Path.Combine(ra, "disksys.rom"), new byte[8192]);
                var got = PathOf(Ensure()[0]);
                Check("copied from RetroArch's system folder into bios\\", got == Path.Combine(Path.GetDirectoryName(exe), "bios", "disksys.rom") && File.Exists(Path.Combine(ra, "disksys.rom")), got);
                File.Delete(got);
                File.Delete(Path.Combine(ra, "disksys.rom"));
                var other = Path.Combine(emus, "Mesen", "Firmware"); Directory.CreateDirectory(other);
                File.WriteAllBytes(Path.Combine(other, "FdsBios.bin"), new byte[8192]);
                got = PathOf(Ensure()[0]);
                Check("found under another name (FdsBios.bin), copied as disksys.rom", got != null && Path.GetFileName(got) == "disksys.rom", got);
                File.Delete(got); File.Delete(Path.Combine(other, "FdsBios.bin"));
                var games = Path.Combine(dir, "games"); Directory.CreateDirectory(games);
                File.WriteAllBytes(Path.Combine(games, "disksys.rom"), new byte[8192]);
                got = PathOf(Ensure(Path.Combine(games, "Zelda.fds"))[0]);
                Check("beside the game: puNES's own place 3, used, nothing copied", got == Path.Combine(games, "disksys.rom") && !File.Exists(Path.Combine(Path.GetDirectoryName(exe), "bios", "disksys.rom")), got);
                var platform = Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t)));
                var list = ((EmulatorPlugin)platform).GetBiosFilesForPlatform("Nintendo Famicom Disk System").ToList();
                Check("the FDS platform asks for bios\\disksys.rom, required", list.Count == 1 && list[0].FileName == "disksys.rom" && list[0].Required && list[0].Location == "bios",
                      string.Join(", ", list.Select(b => b.Location + "\\" + b.FileName + " " + b.Required)));
                var nesList = ((EmulatorPlugin)platform).GetBiosFilesForPlatform("Nintendo Entertainment System").ToList();
                Check("the NES platform: disksys.rom optional (for the disks among them)", nesList.Count == 1 && !nesList[0].Required);
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── versions ─────────────────────────────────────────────────────────

        public static bool Versions(Assembly asm, string realExe)
        {
            _bad = 0;
            Console.WriteLine("-- versions and the Download button --");
            var dir = Scratch("versions");
            try
            {
                var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t)));
                Check("the real punes64.exe 0.111 known by its MD5", plugin.GetCurrentVersion(realExe) == "0.111", plugin.GetCurrentVersion(realExe));
                Check("an empty path (Add Emulator): the Download button shows", plugin.IsUpdateAvailable("", out var v) && v != null && v.Label == "0.111", v?.Label);
                var mine = Path.Combine(dir, "user", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(mine)); File.WriteAllText(mine, "a build of the user's");
                Check("a puNES the plugin did not install: no version, no update", plugin.GetCurrentVersion(mine) == null && !plugin.IsUpdateAvailable(mine, out _));
                var old = Path.Combine(dir, "old", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(old)); File.WriteAllText(old, "an older build");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(old), "lbip-punes-build.txt"), "0.110\r\npunes-0.110-x86_64-win-d3d9.zip\r\n");
                Check("ours, older (stamped 0.110): an update", plugin.GetCurrentVersion(old) == "0.110" && plugin.IsUpdateAvailable(old, out _));
                Check("ours, current: no update", !plugin.IsUpdateAvailable(realExe, out _));
                Check("claims punes64.exe, punes.exe, the _p ones; not fceux.exe", new[] { "punes64.exe", "PUNES.EXE", "punes64_p.exe", "punes_p.exe" }.All(n => (bool)Call(asm, "PunesLaunch", "IsPunesExe", @"C:\e\" + n))
                      && !(bool)Call(asm, "PunesLaunch", "IsPunesExe", @"C:\e\fceux.exe"));
                Check("never recommended, the four platforms supported", new[] { "Nintendo Entertainment System", "Nintendo Famicom Disk System", "Nes - Super Mario Bros. Hacks", "nintendo famicom " }
                      .All(p => plugin.IsPlatformSupported(p).Supported && !plugin.IsPlatformSupported(p).Recommended) && !plugin.IsPlatformSupported("Super Nintendo Entertainment System").Supported);
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── a launch ─────────────────────────────────────────────────────────

        public static bool E2E(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- a launch through Nixx-puNES --");
            var dir = Scratch("e2e");
            var said = new List<string>();
            var gate = asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", true);
            var notice = asm.GetType("LbIntegrations.Lbip.LbipNotice", true);
            gate.GetField("Off", Any).SetValue(null, true);
            notice.GetField("Instead", Any).SetValue(null, new Action<string, string>((t, x) => said.Add(x)));
            T(asm, "PunesSettings").GetField("PathOverride", Any).SetValue(null, Path.Combine(dir, "settings.ini"));
            T(asm, "PunesFirmware").GetField("NoLibrary", Any).SetValue(null, true);
            try
            {
                var exe = Path.Combine(dir, "Emulators", "Nixx-puNES", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
                var games = Path.Combine(dir, "My Games"); Directory.CreateDirectory(games);
                var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t)));
                var emu = new StubEmulator { Title = "under test", ApplicationPath = exe };
                var row = emu.AddNewEmulatorPlatform(); row.Platform = "My custom NES"; row.IsDefault = true; row.CommandLine = "--vsync=off";
                var claimed = plugin.GetApplicableEmulators(new IEmulator[] { emu, new StubEmulator { Title = "other", ApplicationPath = @"C:\x\fceux.exe" } }).ToList();
                Check("claimed: ours only", claimed.Count == 1 && claimed[0] == emu);
                Check("hotkey scripts filled, the exit closes punes64.exe", emu.ExitAutoHotkeyScript?.Contains("WinClose, ahk_exe punes64.exe") == true && emu.SaveStateAutoHotkeyScript?.Contains("{F2 down}") == true && emu.LoadStateAutoHotkeyScript?.Contains("{F4 down}") == true);
                var rows = emu.GetAllEmulatorPlatforms();
                Check("an existing platform row untouched (its tick, its line)", rows.Length == 1 && rows[0].IsDefault && rows[0].CommandLine == "--vsync=off");
                // An entry an earlier build filled (puNES's own F1): brought to the pack's F2.
                var old = asm.GetType("LbIntegrations.Punes.PunesAhk", true).GetField("Superseded", Any).GetValue(null) as string[];
                emu.SaveStateAutoHotkeyScript = old[1]; plugin.GetApplicableEmulators(new IEmulator[] { emu });
                Check("an earlier build's save script (F1) replaced by the pack's (F2)", emu.SaveStateAutoHotkeyScript?.Contains("{F2 down}") == true);
                emu.ExitAutoHotkeyScript = "my own"; plugin.GetApplicableEmulators(new IEmulator[] { emu });
                Check("a script the user wrote is kept", emu.ExitAutoHotkeyScript == "my own");

                PrepareForLaunchResponse Launch(string platform, string rom, string rowLine = null, string id = null)
                {
                    said.Clear();
                    var game = StubGame.With(StubGame.Create(id ?? "e2e-" + Path.GetFileNameWithoutExtension(rom), Path.GetFileNameWithoutExtension(rom), rom), "Platform", platform);
                    return plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(emu, game, ((rowLine ?? "") + " \"" + rom + "\"").Trim(), null, null));
                }

                var nes = Path.Combine(games, "Zelda II - The Adventure of Link (Europe).nes"); File.WriteAllBytes(nes, new byte[16]);
                var r = Launch("Nintendo Entertainment System", nes);
                Check("a NES game: full screen in front, the game kept", r.WasSuccess && r.NewCommandLine == "--fullscreen=yes \"" + nes + "\"", r.NewCommandLine);
                var prbLink = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(exe), "prb"));
                Check("its saves folder: a junction to Saves\\Nintendo Entertainment System\\prb", prbLink.Attributes.HasFlag(FileAttributes.ReparsePoint) && (prbLink.LinkTarget ?? "").TrimEnd('\\').EndsWith(Path.Combine("Saves", "Nintendo Entertainment System", "prb"), StringComparison.OrdinalIgnoreCase), prbLink.LinkTarget);
                var appCfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "puNES", "puNES.cfg");
                if (!File.Exists(appCfg)) Check("made portable (an empty puNES.cfg beside the exe)", File.Exists(Path.Combine(Path.GetDirectoryName(exe), "puNES.cfg")));
                r = Launch("My custom NES", nes, "--vsync=off");
                Check("a custom platform, a .nes: launched, the row's line after ours", r.WasSuccess && r.NewCommandLine == "--fullscreen=yes --vsync=off \"" + nes + "\"", r.NewCommandLine);
                var bin = Path.Combine(games, "x.bin"); File.WriteAllBytes(bin, new byte[16]);
                r = Launch("My custom NES", bin);
                Check("a custom platform, a file that says nothing: refused, said", !r.WasSuccess && said.Count == 1, said.FirstOrDefault()?.Split('\n')[0]);
                var eq = Path.Combine(games, "a=b Zelda.nes"); File.WriteAllBytes(eq, new byte[16]);
                r = Launch("Nintendo Entertainment System", eq);
                Check("a path with '=': refused, said why", !r.WasSuccess && said.Count == 1 && said[0].Contains("'='"));
                var fds = Path.Combine(games, "Zelda no Densetsu (Japan).fds"); File.WriteAllBytes(fds, new byte[65500]);
                r = Launch("Nintendo Famicom Disk System", fds);
                Check("a disk without disksys.rom: refused, the file and the folder named", !r.WasSuccess && said.Count == 1 && said[0].Contains("disksys.rom") && said[0].Contains(Path.Combine(Path.GetDirectoryName(exe), "bios")), said.FirstOrDefault()?.Split('\n')[0]);
                var ra = Path.Combine(dir, "Emulators", "RetroArch", "system"); Directory.CreateDirectory(ra); File.WriteAllBytes(Path.Combine(ra, "disksys.rom"), new byte[8192]);
                r = Launch("Nintendo Famicom Disk System", fds);
                Check("with RetroArch's disksys.rom: copied, launched", r.WasSuccess && File.Exists(Path.Combine(Path.GetDirectoryName(exe), "bios", "disksys.rom")), r.NewCommandLine);
                var zip = Path.Combine(games, "Kirby.zip"); Zip(zip, ("Kirby's Adventure (USA).nes", 4096));
                r = Launch("Nintendo Entertainment System", zip);
                var unpacked = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(exe), "Unpacked"), "Kirby.nes", SearchOption.AllDirectories).FirstOrDefault();
                Check("a zip named otherwise: the unpacked file on the line, not the zip", r.WasSuccess && unpacked != null && r.NewCommandLine == "--fullscreen=yes \"" + unpacked + "\"", r.NewCommandLine);
                T(asm, "PunesSettings").GetMethod("SetGameValue", Any).Invoke(null, new object[] { "e2e-pal", "Region", "pal" });
                r = Launch("Nintendo Entertainment System", nes, null, "e2e-pal");
                Check("a game's own region on its line", r.NewCommandLine == "--fullscreen=yes --mode=pal \"" + nes + "\"", r.NewCommandLine);
                r = Launch("Super Nintendo Entertainment System", nes);
                Check("not our exe's game? still ours (the file says NES): launched", r.WasSuccess);
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                notice.GetField("Instead", Any).SetValue(null, null);
                gate.GetField("Off", Any).SetValue(null, false);
                T(asm, "PunesSettings").GetField("PathOverride", Any).SetValue(null, null);
                Wipe(dir);   // the launches made prb\ save\ diff\ screenshot\ junctions
            }
            return Done();
        }

        // ── the pinned build ─────────────────────────────────────────────────

        public static bool Fetch(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- the pinned build, downloaded --");
            var dir = Scratch("fetch");
            try
            {
                var zip = Path.Combine(dir, "punes.zip");
                Call(asm, "PunesBuild", "Fetch", zip, new Action<double>(_ => { }), new Func<bool>(() => false));
                Check("downloaded, SHA-256 the pinned one", File.Exists(zip), new FileInfo(zip).Length + " bytes");
                var into = Path.Combine(dir, "install");
                Directory.CreateDirectory(into);
                File.WriteAllText(Path.Combine(into, "puNES.cfg"), "[video]\r\nsize window=4x\r\n");
                Call(asm, "Archives", "ExtractOver", zip, into);
                Check("punes64.exe at the root (a flat zip)", File.Exists(Path.Combine(into, "punes64.exe")));
                Check("the user's puNES.cfg kept", File.ReadAllText(Path.Combine(into, "puNES.cfg")).Contains("size window=4x"));
                Check("puNES's own dip.cfg and nes20db.xml there", File.Exists(Path.Combine(into, "dip.cfg")) && File.Exists(Path.Combine(into, "nes20db.xml")));
                var plugin = (EmulatorPlugin)Activator.CreateInstance(asm.GetTypes().First(t => typeof(EmulatorPlugin).IsAssignableFrom(t)));
                Check("the extracted exe is 0.111 by its MD5", plugin.GetCurrentVersion(Path.Combine(into, "punes64.exe")) == "0.111");
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return Done();
        }

        // ── pictures ─────────────────────────────────────────────────────────

        public static bool Shot(Assembly asm, string output, string exe)
        {
            _bad = 0;
            Console.WriteLine("-- the Nixx page and a game's window, drawn --");
            try
            {
                if (exe != null) T(asm, "PunesGameForm").GetField("ExeForProbe", Any).SetValue(null, exe);
                var settingsType = T(asm, "Settings");
                var page = (System.Windows.Forms.Control)settingsType.GetMethod("CreatePage").Invoke(null, null);
                var host = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(700, 720), Font = new System.Drawing.Font("Segoe UI", 9f) };
                page.Dock = System.Windows.Forms.DockStyle.Fill; host.Controls.Add(page); host.Show();
                System.Windows.Forms.Application.DoEvents();
                using (var bmp = new System.Drawing.Bitmap(host.Width, host.Height)) { host.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, host.Width, host.Height)); bmp.Save(Path.ChangeExtension(output, ".page.png")); }
                host.Close();
                var g1 = StubGame.With(StubGame.Create("shot-1", "Zelda II - The Adventure of Link (Europe)", @"G:\LB\FakeCatalog\nes\Zelda II - The Adventure of Link (Europe).nes"), "Platform", "Nintendo Entertainment System");
                var g2 = StubGame.With(StubGame.Create("shot-2", "Zelda no Densetsu", @"C:\games\Zelda no Densetsu.fds"), "Platform", "Nintendo Famicom Disk System");
                foreach (var (name, games) in new[] { ("one", new[] { g1 }), ("two", new[] { g1, g2 }) })
                {
                    var form = (System.Windows.Forms.Form)Activator.CreateInstance(T(asm, "PunesGameForm"), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { games }, null);
                    form.Show(); System.Windows.Forms.Application.DoEvents();
                    using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height)); bmp.Save(Path.ChangeExtension(output, "." + name + ".png")); }
                    form.Close();
                }
                Check("drawn", true, Path.GetDirectoryName(Path.GetFullPath(output)));
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            return Done();
        }

        // ── the save folders ─────────────────────────────────────────────────

        private static string LinkOf(string dir)
        {
            var info = new DirectoryInfo(dir);
            if (!info.Exists || !info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            var t = info.LinkTarget ?? "";
            return t.StartsWith(@"\??\", StringComparison.Ordinal) ? t.Substring(4) : t;
        }

        /// <summary>Junctions only, never what they lead to - then the rest.</summary>
        private static void Wipe(string dir)
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                    if (new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(d); else Wipe(d);
                Directory.Delete(dir, true);
            }
            catch { }
        }

        public static bool SaveDirs(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- puNES's four folders made junctions to Saves\\<platform> --");
            var dir = Scratch("savedirs");
            var t = T(asm, "PunesSaveDirs");
            const string Nes = "Nintendo Entertainment System", Fds = "Nintendo Famicom Disk System", Hacks = "Nes - Super Mario Bros. Hacks";
            try
            {
                var exe = Path.Combine(dir, "Nixx-puNES", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
                var inst = Path.GetDirectoryName(exe);
                File.WriteAllText(Path.Combine(inst, "puNES.cfg"), "");   // portable: the data folder is the exe's
                string Live(string k) => Path.Combine(inst, k);
                string Mine(string p, string k) => Path.Combine(inst, "Saves", p, k);
                t.GetField("LibraryForProbe", Any).SetValue(null, new Func<IEnumerable<(string, string)>>(() => new[]
                {
                    ("Zelda II.nes", Nes), ("Other.zip", Hacks), ("Twin.nes", Nes), ("Twin.nes", Hacks),
                }));

                // puNES ran before the plugin: its folders are real ones, with files of several platforms.
                foreach (var k in new[] { "prb", "save", "diff", "screenshot" }) Directory.CreateDirectory(Live(k));
                File.WriteAllBytes(Path.Combine(Live("prb"), "Zelda II.prb"), new byte[] { 1, 2, 3 });
                File.WriteAllBytes(Path.Combine(Live("prb"), "Other.prb"), new byte[] { 4 });
                File.WriteAllBytes(Path.Combine(Live("prb"), "Twin.prb"), new byte[] { 5 });
                File.WriteAllBytes(Path.Combine(Live("prb"), "Unknown.prb"), new byte[] { 6 });
                File.WriteAllBytes(Path.Combine(Live("save"), "Zelda II.p00"), new byte[] { 7 });
                File.WriteAllBytes(Path.Combine(Live("save"), "Zelda II.p00.png"), new byte[] { 8 });
                File.WriteAllBytes(Path.Combine(Live("diff"), "Kaette.ipf"), new byte[] { 9 });
                File.WriteAllBytes(Path.Combine(Live("screenshot"), "Other_000001.png"), new byte[] { 10 });
                // One already in the platform folder, different: never overwritten.
                Directory.CreateDirectory(Mine(Nes, "prb"));
                File.WriteAllBytes(Path.Combine(Mine(Nes, "prb"), "Zelda II.prb"), new byte[] { 9, 9 });

                var said = (string)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe, Hacks });
                Console.WriteLine("    " + said);
                bool JunctionTo(string k, string p) => string.Equals(LinkOf(Live(k))?.TrimEnd('\\'), Path.GetFullPath(Mine(p, k)), StringComparison.OrdinalIgnoreCase);
                Check("the four folders are junctions to the launching platform's", new[] { "prb", "save", "diff", "screenshot" }.All(k => JunctionTo(k, Hacks)), said);
                Check("a save of a library game of another platform: into that platform", File.Exists(Path.Combine(Mine(Nes, "save"), "Zelda II.p00")) && File.Exists(Path.Combine(Mine(Nes, "save"), "Zelda II.p00.png")));
                Check("a game of two platforms: the launching one's", File.Exists(Path.Combine(Mine(Hacks, "prb"), "Twin.prb")));
                Check("a game of the library under an archive's name: its platform", File.Exists(Path.Combine(Mine(Hacks, "prb"), "Other.prb")) && File.Exists(Path.Combine(Mine(Hacks, "screenshot"), "Other_000001.png")));
                Check("a file of no library game: the NES's", File.Exists(Path.Combine(Mine(Nes, "prb"), "Unknown.prb")));
                Check("a disk's writes of no library game: the Famicom Disk System's", File.Exists(Path.Combine(Mine(Fds, "diff"), "Kaette.ipf")));
                Check("a different file already there: kept, the other to Saves\\Unsorted", File.ReadAllBytes(Path.Combine(Mine(Nes, "prb"), "Zelda II.prb")).Length == 2
                      && File.ReadAllBytes(Path.Combine(inst, "Saves", "Unsorted", "prb", "Zelda II.prb")).SequenceEqual(new byte[] { 1, 2, 3 }));

                // puNES writes through the junction (what it does, measured 18/10 in the lab's run4).
                File.WriteAllBytes(Path.Combine(Live("prb"), "New.prb"), new byte[] { 1 });
                Check("a file written into puNES's folder lands in the platform's", File.Exists(Path.Combine(Mine(Hacks, "prb"), "New.prb")));

                // The next launch, another platform: the links move, what they led to stays.
                said = (string)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe, Fds });
                Check("repointed at the next platform", new[] { "prb", "save", "diff", "screenshot" }.All(k => JunctionTo(k, Fds)), said);
                Check("the previous platform's files untouched", File.Exists(Path.Combine(Mine(Hacks, "prb"), "New.prb")) && File.Exists(Path.Combine(Mine(Hacks, "prb"), "Twin.prb")));
                said = (string)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe, Fds });
                Check("the same platform again: nothing done", said.StartsWith("already"), said);

                // A link of the user's: never touched.
                var elsewhere = Path.Combine(dir, "MyShots"); Directory.CreateDirectory(elsewhere);
                Directory.Delete(Live("screenshot"));
                var made = (bool)t.GetMethod("Junction", Any).Invoke(null, new object[] { Live("screenshot"), elsewhere, null });
                said = (string)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe, Nes });
                Check("a junction of the user's elsewhere: kept", made && string.Equals(LinkOf(Live("screenshot"))?.TrimEnd('\\'), Path.GetFullPath(elsewhere), StringComparison.OrdinalIgnoreCase), said);
                Check("the others repointed", new[] { "prb", "save", "diff" }.All(k => JunctionTo(k, Nes)));
                var dirs = (List<string>)t.GetMethod("Dirs", Any).Invoke(null, new object[] { exe, Nes, "screenshot" });
                Check("its folder listed first (where puNES writes)", dirs.Count == 2 && string.Equals(dirs[0], Live("screenshot"), StringComparison.OrdinalIgnoreCase), string.Join(" | ", dirs));

                // Names Windows refuses in a folder name.
                var name = (string)t.GetMethod("FolderName", Any).Invoke(null, new object[] { "My: NES?" });
                Check("a platform name with ':' and '?': a valid folder", name == "My_ NES_", name);
                Check("no platform: the NES's folder", (string)t.GetMethod("FolderName", Any).Invoke(null, new object[] { null }) == Nes);
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                t.GetField("LibraryForProbe", Any).SetValue(null, null);
                Wipe(dir);
            }
            return Done();
        }
    
        // ── the rows of a game ───────────────────────────────────────────────

        public static bool Saves(Assembly asm, string realState)
        {
            _bad = 0;
            Console.WriteLine("-- a game's rows: cartridge save, disk writes, states --");
            var dir = Scratch("saves");
            var dirs = T(asm, "PunesSaveDirs");
            var plugin = T(asm, "PunesPlugin");
            const string Nes = "Nintendo Entertainment System", Fds = "Nintendo Famicom Disk System";
            try
            {
                var exe = Path.Combine(dir, "Nixx-puNES", "punes64.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, "");
                var inst = Path.GetDirectoryName(exe);
                File.WriteAllText(Path.Combine(inst, "puNES.cfg"), "");
                string Mine(string p, string k) { var d = Path.Combine(inst, "Saves", p, k); Directory.CreateDirectory(d); return d; }
                void Put(string folder, string file, int size = 4) => File.WriteAllBytes(Path.Combine(folder, file), new byte[size]);
                // puNES's own folders made junctions to the NES's, as a launch leaves them.
                dirs.GetField("LibraryForProbe", Any).SetValue(null, new Func<IEnumerable<(string, string)>>(() => Array.Empty<(string, string)>()));
                dirs.GetMethod("Ensure", Any).Invoke(null, new object[] { exe, Nes });

                Put(Mine(Nes, "prb"), "Zelda II.prb", 8192);
                Put(Mine(Nes, "save"), "Zelda II.p00");
                Put(Mine(Nes, "save"), "Zelda II.p00.png");
                Put(Mine(Nes, "save"), "Zelda II.p0B");
                Put(Mine(Nes, "save"), "Zelda II.p0C");   // no 13th slot
                Put(Mine(Nes, "save"), "Zelda II 2.p01"); // another game
                Put(Mine(Fds, "diff"), "Kaette.ipf");
                Put(Mine(Fds, "save"), "Kaette.p03");

                List<object> Rows(string rom, string platform)
                {
                    var collect = plugin.GetMethod("Collect", Any);
                    var list = Activator.CreateInstance(collect.GetParameters()[0].ParameterType);
                    collect.Invoke(null, new object[] { list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exe, rom, "probe-game", platform, null });
                    return ((System.Collections.IEnumerable)list).Cast<object>().ToList();
                }
                string Show(object r) => r.GetType().Name + " " + Path.GetFileName((string)F(r, "FileLocation")) + " slot " + (F(r, "Slot") ?? "-") + " " + F(r, "SaveGroupId");

                var zelda = Rows(Path.Combine(dir, "roms", "Zelda II.nes"), Nes);
                foreach (var r in zelda) Console.WriteLine("    " + Show(r));
                Check("Zelda: its .prb, slots 0 and B - not the 13th, not its thumbnail, not Zelda II 2's", zelda.Count == 3
                      && zelda.OfType<GameSaveState>().Select(s => s.Slot ?? -1).OrderBy(x => x).SequenceEqual(new[] { 0, 11 }));
                var prb = zelda.OfType<GameSaveGame>().FirstOrDefault();
                Check("the cartridge save's id: emu punes, kind save, sys nes, by name", prb != null && prb.SaveGroupId.StartsWith("{\"emu\":\"punes\",\"kind\":\"save\",\"sys\":\"nes\",\"by\":\"name\",\"id\":\"Zelda II\"", StringComparison.Ordinal), prb?.SaveGroupId);
                Check("the row names the platform folder, never puNES's junction", prb != null && prb.FileLocation.IndexOf("\\Saves\\" + Nes + "\\prb\\", StringComparison.OrdinalIgnoreCase) > 0, prb?.FileLocation);

                var disk = Rows(Path.Combine(dir, "roms", "Kaette.fds"), Fds);
                foreach (var r in disk) Console.WriteLine("    " + Show(r));
                Check("a disk: its writes (kind disk, sys fds) and its state in the FDS's folders", disk.Count == 2
                      && disk.OfType<GameSaveGame>().Any(r => r.SaveGroupId.Contains("\"kind\":\"disk\",\"sys\":\"fds\""))
                      && disk.OfType<GameSaveState>().Any(s => s.Slot == 3));
                Check("the same disk on the NES platform: not the FDS's files", Rows(Path.Combine(dir, "roms", "Kaette.fds"), Nes).Count == 0);

                var me = Activator.CreateInstance(plugin, true);
                var potential = (IReadOnlyDictionary<int, string>)plugin.GetMethod("GetPotentialSaveSlots").Invoke(me, null);
                Check("twelve slots, named as puNES shows them (0-9, A, B)", potential.Count == 12 && potential[10] == "A" && potential[11] == "B");
                var secondary = plugin.GetMethod("IsSecondarySaveFile");
                Check("a state's thumbnail is secondary, the state is not",
                      (bool)secondary.Invoke(me, new object[] { Path.Combine(Mine(Nes, "save"), "Zelda II.p00.png") })
                      && !(bool)secondary.Invoke(me, new object[] { Path.Combine(Mine(Nes, "save"), "Zelda II.p00") }));

                if (realState != null && File.Exists(realState))
                {
                    var png = (byte[])Call(asm, "PunesStateThumbs", "Preview", File.ReadAllBytes(realState));
                    bool ok = png != null && png.Length > 33 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G';
                    int w = ok ? (png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19]) : 0, h = ok ? (png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23]) : 0;
                    Check("a real state's picture: a PNG of the NES's frame", ok && w == 256 && h == 240, png == null ? "none" : png.Length + " bytes, " + w + "x" + h);
                }
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                dirs.GetField("LibraryForProbe", Any).SetValue(null, null);
                Wipe(dir);
            }
            return Done();
        }
    }
}
