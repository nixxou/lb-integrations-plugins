// The ares plugin's pieces, run without a host:
//   --ares-names <ares.exe>                   every system of the plugin's table against the exe's own "ares --help" list
//   --ares-line                               the system chosen and the line built, offline, for the three levels
//   --ares-settings <ares.exe> [--settings f] the options written into settings.bml (a COPY), the lines that changed
//   --ares-options <ares.exe> [out.bml]       every game's and a game's own options, each node as ares writes it (a COPY)
//   --ares-firmware <ares.exe>              the BIOS found and given - WRITES into that install's Firmware\ and settings.bml
//   --ares-saves <ares.exe> <system> <rom>    the files listed for that ROM run as that system
//   --ares-latest                             the latest release as GitHub has it

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class AresCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static Type T(Assembly asm, string name) => asm.GetType("LbIntegrations.Ares." + name, true);
        private static object F(object o, string n) => o?.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o)
                                                        ?? o?.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

        public static bool Names(Assembly asm, string exe)
        {
            _bad = 0;
            Console.WriteLine("-- the plugin's systems against " + exe + " --help --");
            var psi = new ProcessStartInfo(exe, "--help") { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            using var p = Process.Start(psi);
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            var line = text.Split('\n').SkipWhile(l => !l.StartsWith("Available Systems")).Skip(1).FirstOrDefault() ?? "";
            var known = new HashSet<string>(line.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.Ordinal);
            Console.WriteLine("  ares knows " + known.Count + " systems");
            foreach (var s in (IEnumerable)T(asm, "AresSystems").GetField("All", Any).GetValue(null))
            {
                var name = (string)F(s, "Name");
                Check(name, known.Contains(name));
            }
            foreach (var pl in (IEnumerable)T(asm, "AresSystems").GetField("Platforms", Any).GetValue(null))
                foreach (var name in ((string[])F(pl, "Systems")).Concat(((IDictionary)F(pl, "ByExtension")).Values.Cast<string>()))
                    if (!known.Contains(name)) Check(F(pl, "Platform") + " -> " + name, false);
            var unmapped = known.Where(k => ((IEnumerable)T(asm, "AresSystems").GetField("All", Any).GetValue(null)).Cast<object>().All(s => (string)F(s, "Name") != k)).ToList();
            Console.WriteLine("  in ares, not in the plugin: " + (unmapped.Count == 0 ? "none" : string.Join(", ", unmapped)));
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Line(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- the system and the line --");
            var launch = T(asm, "AresLaunch");
            (string, string) Sys(string platform, string rom, string line)
            {
                var r = launch.GetMethod("SystemFor", Any).Invoke(null, new object[] { null, platform, rom, line });
                return ((string)r.GetType().GetField("Item1").GetValue(r), (string)r.GetType().GetField("Item2").GetValue(r));
            }
            var a = Sys("Sega CD", @"C:\g\Sonic CD.chd", "--system \"Mega CD 32X\" \"C:\\g\\Sonic CD.chd\"");
            Check("the platform's line wins over the table", a.Item1 == "Mega CD 32X", a.Item1 + " from " + a.Item2);
            a = Sys("Sega CD", @"C:\g\Sonic CD.chd", "\"C:\\g\\Sonic CD.chd\"");
            Check("no line: the platform's default", a.Item1 == "Mega CD", a.Item1 + " from " + a.Item2);
            a = Sys("Sega Genesis", @"C:\g\Knuckles Chaotix.32x", "");
            Check("an extension that says it", a.Item1 == "Mega 32X", a.Item1 + " from " + a.Item2);
            a = Sys("Sega Genesis", @"C:\g\Knuckles Chaotix.32x", "--system \"Mega Drive\" x");
            Check("the plugin's own line (the default) does not hide an extension", a.Item1 == "Mega 32X", a.Item1 + " from " + a.Item2);
            a = Sys("Sega CD", @"C:\g\Fahrenheit.chd", "--system \"Mega CD 32X\" x");
            Check("a line the user changed wins", a.Item1 == "Mega CD 32X", a.Item1 + " from " + a.Item2);
            a = Sys("Sega Genesis", @"C:\g\x.md", "--system \"Mega Drive Next\" x");
            Check("an unknown --system is passed as it is", a.Item1 == "Mega Drive Next", a.Item1 + " from " + a.Item2);
            a = Sys("Commodore 64", @"C:\g\x.d64", "");
            Check("a platform the plugin does not map: nothing", a.Item1 == null);

            var built = (string)launch.GetMethod("CommandLine", Any).Invoke(null, new object[] {
                "--system \"Mega Drive\" --fullscreen \"C:\\My Games\\Sonic.md\"", @"C:\LB\Emulators\Nixx-ares\ares.exe", "Mega Drive", "pseudo", @"C:\My Games\Sonic.md", null, true, null });
            Console.WriteLine("    " + built);
            Check("one --system, the screen switch replaced, --no-file-prompt, the game last",
                  built == "--settings-file C:\\LB\\Emulators\\Nixx-ares\\settings.bml --system \"Mega Drive\" --pseudofullscreen --no-file-prompt \"C:\\My Games\\Sonic.md\"");
            built = (string)launch.GetMethod("CommandLine", Any).Invoke(null, new object[] { "\"C:\\g\\Zelda.7z\"", @"C:\a\ares.exe", "Super Famicom", "windowed", @"C:\g\Zelda.7z", @"C:\t\Zelda.sfc", true, null });
            Check("a .7z swapped for what was unpacked", built.EndsWith("--no-file-prompt C:\\t\\Zelda.sfc") && !built.Contains("--fullscreen"), built);
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Settings(Assembly asm, string exe, string settings)
        {
            _bad = 0;
            Console.WriteLine("-- settings.bml at launch --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-ares-bml-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            try
            {
                var target = Path.Combine(copy, "settings.bml");
                var original = Path.Combine(Path.GetDirectoryName(exe), "settings.bml");
                if (File.Exists(original)) File.Copy(original, target);
                var fake = Path.Combine(copy, "ares.exe");
                File.WriteAllText(fake, "");
                if (settings != null) T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, Path.GetFullPath(settings));
                var before = File.Exists(target) ? File.ReadAllLines(target) : new string[0];
                T(asm, "AresLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fake, "probe-game" });
                var after = File.ReadAllLines(target);
                for (int i = 0; i < Math.Max(before.Length, after.Length); i++)
                    if (i >= before.Length || i >= after.Length || before[i] != after[i])
                        Console.WriteLine("    " + (i < before.Length ? before[i].Trim() : "") + "  ->  " + (i < after.Length ? after[i].Trim() : ""));
                Check("as many lines as before", before.Length == 0 || after.Length == before.Length, before.Length + " -> " + after.Length);
                var stamp = File.GetLastWriteTimeUtc(target);
                System.Threading.Thread.Sleep(20);
                T(asm, "AresLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fake, "probe-game" });
                Check("a second launch writes nothing", File.GetLastWriteTimeUtc(target) == stamp);
            }
            finally { try { Directory.Delete(copy, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>--ares-options &lt;ares.exe&gt; [out.bml]: every game's and a game's own values written into a COPY of the
        /// install's settings.bml - each node as ares writes it, "as set in ares" untouched. The result is kept in out.bml
        /// when given (for a run of the real ares on it: does it keep them?).</summary>
        public static bool Options(Assembly asm, string exe, string keep)
        {
            _bad = 0;
            Console.WriteLine("-- the options, written --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-ares-opt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(copy, "games"));
            try
            {
                var target = Path.Combine(copy, "settings.bml");
                File.Copy(Path.Combine(Path.GetDirectoryName(exe), "settings.bml"), target);
                var fake = Path.Combine(copy, "ares.exe");
                File.WriteAllText(fake, "");
                var shaders = (System.Collections.Generic.List<string>)T(asm, "AresSettings").GetMethod("Shaders", Any).Invoke(null, new object[] { exe });
                Console.WriteLine("  " + shaders.Count + " shaders in the install, e.g. " + string.Join(", ", shaders.Take(3)));
                Check("the install's shaders listed, as ares names them", shaders.Count > 10 && shaders.All(s => s.EndsWith(".slangp") && !s.Contains('\\')));
                var shader = shaders.FirstOrDefault(s => s.StartsWith("crt/")) ?? shaders.FirstOrDefault();
                // Every game's: a few; the game's own: more, one of them over every game's.
                var own = new[] { "Output=fixed3", "Shader=" + shader, "Aspect=anamorphic", "AudioDriver=xaudio", "N64ControllerPak=1984", "MdTmss=true", "Defocus=allow",
                                  "Driver=opengl46", "Region=pal-ntsc-j", "Luminance=0.75", "N64Quality=hd" };
                // Every game's: the screen alone is the page's; a value an older build kept for another option is not read.
                File.WriteAllLines(Path.Combine(copy, "settings.ini"), new[] { "Screen=windowed", "Driver=d3d", "Output=integer" });
                File.WriteAllLines(Path.Combine(copy, "games", "probe-game.ini"), own);
                T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, Path.Combine(copy, "settings.ini"));
                var before = AresBmlLines(target);
                var args = (System.Collections.Generic.List<string>)T(asm, "AresSettings").GetMethod("SettingArgs", Any, null, new[] { typeof(string) }, null).Invoke(null, new object[] { "probe-game" });
                Console.WriteLine("  " + string.Join("  ", args));
                foreach (var kv in new (string, string)[] {
                    ("Video/Driver", "OpenGL 4.6"), ("Boot/Prefer", "PAL,NTSC-J,NTSC-U"), ("Video/Luminance", "0.75"), ("Video/Quality", "HD"),
                    ("Video/Output", "IntegerFixed"), ("Video/FixedScale", "3"), ("Video/Shader", shader), ("Video/AspectCorrectionMode", "Anamorphic"),
                    ("Audio/Driver", "XAudio 2.9"), ("Nintendo64/ControllerPakBankString", "1984KiB (Maximum)"), ("MegaDrive/TMSS", "true"), ("Input/Defocus", "Allow") })
                    Check("--setting " + kv.Item1 + "=" + kv.Item2, args.Contains(kv.Item1 + "=" + kv.Item2));
                foreach (var untouched in new[] { "Video/Saturation", "Video/Gamma", "Video/ColorEmulation", "Video/Exclusive", "Audio/Volume", "Audio/Mute",
                                                  "General/Rewind", "General/RunAhead", "Nintendo64/ExpansionPak", "GameBoyAdvance/Player", "Video/DeepBlackBoost" })
                    Check(untouched + " not given (as set in ares)", !args.Any(a => a.StartsWith(untouched + "=")));
                T(asm, "AresLaunch").GetMethod("PrepareSettings", Any).Invoke(null, new object[] { fake, "probe-game" });
                var after = AresBmlLines(target);
                Check("settings.bml: no option written any more (the hotkeys alone may be)",
                      before.Where(kv => !kv.Key.StartsWith("Hotkey/")).All(kv => after.TryGetValue(kv.Key, out var v) && v == kv.Value));
                // The line: ours before the platform line's, so a --setting the user put there wins.
                var line = (string)T(asm, "AresLaunch").GetMethod("CommandLine", Any).Invoke(null, new object[] {
                    "--setting Video/Shader=None \"C:\\g\\x.sfc\"", fake, "Super Famicom", "windowed", null, null, true, args });
                Console.WriteLine("    " + line);
                Check("the game's --setting before the user's, the game last",
                      line.IndexOf("--setting Video/Shader=" + shader) >= 0 && line.IndexOf("--setting Video/Shader=" + shader) < line.LastIndexOf("--setting Video/Shader=None")
                      && line.EndsWith("C:\\g\\x.sfc"), line);
                Check("a value with spaces quoted", line.Contains("--setting \"Video/Driver=OpenGL 4.6\"") && line.Contains("\"Nintendo64/ControllerPakBankString=1984KiB (Maximum)\""));
                // A game with none of its own: every game's integer scale, no shader given.
                File.Delete(Path.Combine(copy, "games", "probe-game.ini"));
                var plain = (System.Collections.Generic.List<string>)T(asm, "AresSettings").GetMethod("SettingArgs", Any, null, new[] { typeof(string) }, null).Invoke(null, new object[] { "probe-game" });
                Check("a game with none of its own: nothing passed (every game's settings are ares's own; an old Driver/Output in settings.ini ignored)", plain.Count == 0, string.Join(" ", plain));
                // The real ares on a game with them: it runs, and puts its file back as it was.
                if (keep != null && File.Exists(keep))
                {
                    var rom = Path.Combine(copy, Path.GetFileName(keep)); File.Copy(keep, rom);
                    File.WriteAllLines(Path.Combine(copy, "games", "probe-game.ini"), own);
                    var real = (System.Collections.Generic.List<string>)T(asm, "AresSettings").GetMethod("SettingArgs", Any, null, new[] { typeof(string) }, null).Invoke(null, new object[] { "probe-game" });
                    var fileBefore = File.ReadAllBytes(target);
                    var p = Process.Start(new ProcessStartInfo(exe, "--settings-file \"" + target + "\" " + string.Join(" ", real.Select(a => "--setting \"" + a + "\"")) + " --system \"Super Famicom\" \"" + rom + "\"") { UseShellExecute = false });
                    for (int i = 0; i < 40 && p.MainWindowHandle == IntPtr.Zero && !p.HasExited; i++) { System.Threading.Thread.Sleep(250); p.Refresh(); }
                    Check("the real ares runs with them (no \"Invalid setting\")", !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
                    System.Threading.Thread.Sleep(5000);
                    PostMessage(p.MainWindowHandle, 0x10, IntPtr.Zero, IntPtr.Zero);
                    if (!p.WaitForExit(10000)) p.Kill();
                    var back = AresBmlLines(target);
                    Check("settings.bml back as it was", real.All(a => { var k = a.Substring(0, a.IndexOf('=')); return !before.ContainsKey(k) || (back.TryGetValue(k, out var v) && v == before[k]); }),
                          string.Join(", ", real.Select(a => a.Substring(0, a.IndexOf('='))).Where(k => before.ContainsKey(k) && back.TryGetValue(k, out var v) && v != before[k])));
                }
            }
            finally
            {
                T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, null);
                try { Directory.Delete(copy, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>--ares-console &lt;ares.exe&gt;: each game its own console, on a COPY of the install (its settings.bml)
        /// with a stand-in exe - what ares would write is written by hand between Begin and Recover.</summary>
        public static bool Console_(Assembly asm, string exe)
        {
            _bad = 0;
            Console.WriteLine("-- each game its own console --");
            var copy = Path.Combine(Path.GetTempPath(), "lbip-ares-con-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(copy);
            var session = T(asm, "AresConsoleSession");
            object Sys(string n) => T(asm, "AresSystems").GetMethod("System", Any).Invoke(null, new object[] { n });
            void Begin(string sys, string rom, string played) => session.GetMethod("Begin", Any).Invoke(null, new object[] { Path.Combine(copy, "ares.exe"), Sys(sys), rom, played });
            void Recover() => session.GetMethod("Recover", Any).Invoke(null, null);
            string Read(string p) => File.Exists(p) ? File.ReadAllText(p) : "(none)";
            try
            {
                File.Copy(Path.Combine(Path.GetDirectoryName(exe), "settings.bml"), Path.Combine(copy, "settings.bml"));
                File.WriteAllText(Path.Combine(copy, "ares.exe"), "");
                T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, Path.Combine(copy, "data", "settings.ini"));
                var games = Path.Combine(copy, "games"); Directory.CreateDirectory(games);
                var live = Path.Combine(copy, "Systems", "Mega CD.sys", "backup.ram");
                Directory.CreateDirectory(Path.GetDirectoryName(live));
                File.WriteAllText(live, "SHARED");   // the memory before the plugin took it over

                var sonic = Path.Combine(games, "Sonic CD.cue"); File.WriteAllText(sonic, "x");
                var snatcher = Path.Combine(games, "Snatcher.cue"); File.WriteAllText(snatcher, "x");
                Begin("Mega CD", sonic, sonic);
                Check("the memory as it was kept once", Read(live + ".before-nixx") == "SHARED");
                Check("a game with no copy yet starts from it", Read(live) == "SHARED");
                File.WriteAllText(live, "SONIC");   // ares saved
                Recover();
                Check("taken back to the game", Read(Path.Combine(games, "Sonic CD.bram")) == "SONIC");
                Begin("Mega CD", snatcher, snatcher);
                Check("another game does not see it", Read(live) == "SHARED", Read(live));
                File.WriteAllText(live, "SNATCHER"); Recover();
                Begin("Mega CD", sonic, sonic);
                Check("the first game finds its own again", Read(live) == "SONIC", Read(live));
                Recover();
                Check("the second keeps its own", Read(Path.Combine(games, "Snatcher.bram")) == "SNATCHER");

                // A PlayStation game in a .m3u: the card under the launched disc's name, the newest brought back.
                var d1 = Path.Combine(games, "Final Fantasy VII (Disc 1).cue"); var d2 = Path.Combine(games, "Final Fantasy VII (Disc 2).cue");
                File.WriteAllText(d1, "x"); File.WriteAllText(d2, "x");
                var m3u = Path.Combine(games, "Final Fantasy VII.m3u");
                File.WriteAllLines(m3u, new[] { "Final Fantasy VII (Disc 1).cue", "Final Fantasy VII (Disc 2).cue" });
                var card = Path.Combine(games, "Final Fantasy VII.card");
                Begin("PlayStation", m3u, d1);
                File.WriteAllText(Path.Combine(games, "Final Fantasy VII (Disc 2).card"), "AFTER-CHANGE-DISC");   // ares, at unload on disc 2
                Recover();
                Check("the card of the disc played last is the game's", Read(card) == "AFTER-CHANGE-DISC", Read(card));
                Check("the discs' copies deleted", !File.Exists(Path.Combine(games, "Final Fantasy VII (Disc 2).card")));
                Begin("PlayStation", m3u, d1);
                Check("at the next launch, under disc 1's name", Read(Path.Combine(games, "Final Fantasy VII (Disc 1).card")) == "AFTER-CHANGE-DISC");
                Recover();
                Check("nothing lost when nothing changed", Read(card) == "AFTER-CHANGE-DISC" && !File.Exists(Path.Combine(games, "Final Fantasy VII (Disc 1).card")));

                // The 64DD: the clock the game's, travelling with its disk.
                var ndd = Path.Combine(games, "Sim City 64.ndd"); File.WriteAllText(ndd, "x");
                var clock = Path.Combine(copy, "Systems", "Nintendo 64DD.sys", "time.rtc");
                Begin("Nintendo 64DD", ndd, ndd);
                Directory.CreateDirectory(Path.GetDirectoryName(clock)); File.WriteAllText(clock, "TIME"); File.WriteAllText(Path.ChangeExtension(ndd, ".disk"), "DISK");
                Recover();
                Check("the clock is the game's", Read(Path.Combine(games, "Sim City 64.rtc")) == "TIME");
                var plugin = Activator.CreateInstance(asm.GetTypes().First(t => typeof(Unbroken.LaunchBox.Plugins.EmulatorPlugin).IsAssignableFrom(t) && !t.IsAbstract));
                var companions = (System.Collections.Generic.IReadOnlyList<string>)plugin.GetType().GetMethod("GetCompanionSaveFiles").Invoke(plugin, new object[] { Path.ChangeExtension(ndd, ".disk") });
                Check("it travels with the .disk", companions.Count == 1 && companions[0].EndsWith("Sim City 64.rtc"));

                // LaunchBox gone mid-session: the next Begin finishes the last one first.
                Begin("Mega CD", sonic, sonic);
                File.WriteAllText(live, "SONIC-2");
                Begin("Mega CD", snatcher, snatcher);   // no Recover in between
                Check("an unfinished session finished before the next", Read(Path.Combine(games, "Sonic CD.bram")) == "SONIC-2" && Read(live) == "SNATCHER");
                Recover();
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally
            {
                T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, null);
                try { Directory.Delete(copy, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

        /// <summary>--ares-window &lt;ares.exe&gt; &lt;out dir&gt; &lt;platform&gt; &lt;rom&gt;: a game's window, built off screen with
        /// a stub game - one picture per tab in out dir, the shader menu's first levels printed.</summary>
        public static bool Window(Assembly asm, string exe, string outDir, string platform, string rom)
        {
            _bad = 0;
            Directory.CreateDirectory(outDir);
            var data = Path.Combine(Path.GetTempPath(), "lbip-ares-win-" + Guid.NewGuid().ToString("N"));
            T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, Path.Combine(data, "settings.ini"));
            T(asm, "AresGameForm").GetField("ExeForProbe", Any).SetValue(null, exe);
            Exception failed = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Several games: "a.sfc|b.sfc" - the second set its own scaling and shader, so the first two rows vary.
                    var roms = rom.Split('|');
                    var arr = Array.CreateInstance(typeof(Unbroken.LaunchBox.Plugins.Data.IGame), roms.Length);
                    for (int g = 0; g < roms.Length; g++)
                    {
                        arr.SetValue(StubGame.With(StubGame.Create("probe-window-" + g, Path.GetFileNameWithoutExtension(roms[g]), roms[g]), "Platform", platform), g);
                        if (g == 1)
                        {
                            Directory.CreateDirectory(Path.Combine(data, "games"));
                            File.WriteAllLines(Path.Combine(data, "games", "probe-window-1.ini"), new[] { "Output=fixed3", "Shader=crt/crt-geom.slangp" });
                        }
                    }
                    using var form = (System.Windows.Forms.Form)Activator.CreateInstance(T(asm, "AresGameForm"), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new object[] { arr }, null);
                    form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    form.Location = new System.Drawing.Point(-4000, -4000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    var tabs = Find<System.Windows.Forms.TabControl>(form);
                    for (int i = 0; i < tabs.TabCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        System.Windows.Forms.Application.DoEvents();
                        using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        var png = Path.Combine(outDir, (i + 1) + "-" + tabs.TabPages[i].Text.Replace(' ', '-') + ".png");
                        bmp.Save(png);
                        Console.WriteLine("  tab " + tabs.TabPages[i].Text + " -> " + png);
                    }
                    // The shader menu, as built when the button is clicked.
                    var rows = (System.Collections.IEnumerable)form.GetType().GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                    var shaderRow = rows.Cast<object>().First(r => (string)F(F(r, "O"), "Key") == "Shader");
                    var menu = (System.Windows.Forms.ContextMenuStrip)form.GetType().GetMethod("ShaderMenu", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new[] { shaderRow });
                    var top = menu.Items.Cast<System.Windows.Forms.ToolStripItem>().ToList();
                    Console.WriteLine("  shader menu: " + string.Join(" | ", top.Take(12).Select(it => it.Text + (it is System.Windows.Forms.ToolStripMenuItem m && m.DropDownItems.Count > 0 ? " >" + m.DropDownItems.Count : ""))) + " ... (" + top.Count + " at the top)");
                    var crt = top.OfType<System.Windows.Forms.ToolStripMenuItem>().FirstOrDefault(m => m.Text == "crt");
                    Check("folders as sub-menus, as ares (a crt sub-menu)", crt != null && crt.DropDownItems.Count > 5, crt == null ? "none" : crt.DropDownItems.Count + " in crt");
                    Check("the root presets before the folders (bilinear, nearest)", top.FindIndex(t => t.Text == "bilinear") > 0 && top.FindIndex(t => t.Text == "bilinear") < top.FindIndex(t => t.Text == "crt"));
                    form.Close();
                }
                catch (Exception ex) { failed = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start(); thread.Join();
            T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, null);
            if (failed != null) Check("ran", false, (failed.InnerException ?? failed).ToString());
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        private static TC Find<TC>(System.Windows.Forms.Control root) where TC : System.Windows.Forms.Control
        {
            foreach (System.Windows.Forms.Control c in root.Controls) { if (c is TC hit) return hit; var deeper = Find<TC>(c); if (deeper != null) return deeper; }
            return null;
        }

        /// <summary>--ares-console-real &lt;ares.exe&gt; &lt;system&gt; &lt;game&gt;: the REAL ares of a lab install run on the game
        /// between Begin and Recover - WRITES that install's Systems\ folder (its .before-nixx kept) and the game's own copy.</summary>
        public static bool ConsoleReal(Assembly asm, string exe, string system, string rom)
        {
            _bad = 0;
            Console.WriteLine("-- " + Path.GetFileName(rom) + " as " + system + ", the real ares --");
            var session = T(asm, "AresConsoleSession");
            var sys = T(asm, "AresSystems").GetMethod("System", Any).Invoke(null, new object[] { system });
            var data = Path.Combine(Path.GetTempPath(), "lbip-ares-real-" + Guid.NewGuid().ToString("N"));
            T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, Path.Combine(data, "settings.ini"));
            try
            {
                var files = (System.Collections.IEnumerable)sys.GetType().GetField("ConsoleFiles").GetValue(sys);
                var cf = files.Cast<object>().First();
                var live = (string)T(asm, "AresPlugin").GetMethod("ConsolePath", Any, null, new[] { typeof(string), cf.GetType() }, null).Invoke(null, new[] { exe, cf });
                var own = (string)session.GetMethod("OwnCopy", Any).Invoke(null, new[] { exe, rom, sys, cf });
                Console.WriteLine("  live " + live + "\n  own  " + own);
                if (File.Exists(own)) File.Delete(own);
                var before = File.Exists(live) ? File.GetLastWriteTimeUtc(live) : DateTime.MinValue;
                session.GetMethod("Begin", Any).Invoke(null, new object[] { exe, sys, rom, rom });
                var p = Process.Start(new ProcessStartInfo(exe, "--settings-file \"" + Path.Combine(Path.GetDirectoryName(exe), "settings.bml") + "\" --system \"" + system + "\" \"" + rom + "\"") { UseShellExecute = false });
                for (int i = 0; i < 40 && p.MainWindowHandle == IntPtr.Zero; i++) { System.Threading.Thread.Sleep(250); p.Refresh(); }
                System.Threading.Thread.Sleep(10000);
                PostMessage(p.MainWindowHandle, 0x10, IntPtr.Zero, IntPtr.Zero);
                if (!p.WaitForExit(10000)) { p.Kill(); Check("ares quit on WM_CLOSE", false); }
                System.Threading.Thread.Sleep(500);
                Check("ares wrote the console's memory at unload", File.Exists(live) && File.GetLastWriteTimeUtc(live) > before, File.Exists(live) ? File.GetLastWriteTimeUtc(live).ToString("HH:mm:ss") : "(none)");
                session.GetMethod("Recover", Any).Invoke(null, null);
                Check("taken back to the game, byte for byte", File.Exists(own) && File.ReadAllBytes(own).SequenceEqual(File.ReadAllBytes(live)), own);
                Check("the memory as it was before kept", File.Exists(live + ".before-nixx") || before == DateTime.MinValue);
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { T(asm, "AresSettings").GetField("PathOverride", Any).SetValue(null, null); try { Directory.Delete(data, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>--ares-save-paths &lt;ares folder&gt; &lt;system&gt; &lt;rom&gt; [save ext]: on a COPY of the ares folder and of the game
        /// (its save beside it, as ares wrote it before 07/10): Paths/Saves set, the save moved to Saves\&lt;system&gt;\, and the
        /// real ares, started and closed, writes it there - nothing beside the game.</summary>
        public static bool SavePaths(Assembly asm, string aresDir, string system, string rom, string saveExt)
        {
            _bad = 0;
            var work = Path.Combine(Path.GetTempPath(), "lbip-ares-paths-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var emu = Path.Combine(work, "ares");
            var games = Path.Combine(work, "games");
            Console.WriteLine("-- save folders, on a copy in " + work + " --");
            try
            {
                foreach (var f in Directory.GetFiles(aresDir, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(emu, Path.GetRelativePath(aresDir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(f, to);
                }
                var exe = Path.Combine(emu, "ares.exe");
                var bml = Path.Combine(emu, "settings.bml");
                File.WriteAllText(bml, string.Join("\n", File.ReadAllText(bml).Replace("\r\n", "\n").Split('\n')
                    .Select(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^  (Saves|Screenshots|Debugging):") ? "  " + l.Trim().Split(':')[0] : l)));
                Directory.CreateDirectory(games);
                var game = Path.Combine(games, Path.GetFileName(rom));
                File.Copy(rom, game);
                var oldSave = Path.ChangeExtension(rom, saveExt);
                var besideSave = Path.ChangeExtension(game, saveExt);
                if (File.Exists(oldSave)) File.Copy(oldSave, besideSave);
                var besideState = Path.ChangeExtension(game, ".bs1");
                File.WriteAllBytes(besideState, new byte[] { 1, 2, 3 });
                File.WriteAllBytes(besideState + ".png", new byte[] { 4 });

                var sys = T(asm, "AresSystems").GetMethod("System", Any).Invoke(null, new object[] { system });
                T(asm, "AresSavePaths").GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
                var text = File.ReadAllText(bml);
                Check("Paths/Saves set to the emulator's", text.Contains("Saves: " + emu.Replace('\\', '/') + "/Saves/"));
                Check("Paths/Screenshots set", text.Contains("Screenshots: " + emu.Replace('\\', '/') + "/Screenshots/"));
                T(asm, "AresSavePaths").GetMethod("Migrate", Any).Invoke(null, new object[] { exe, sys, game });
                var folders = T(asm, "AresPlugin").GetMethod("FoldersFor", Any).Invoke(null, new object[] { exe, game, sys });
                var saveDir = (string)F(folders, "SaveTarget");
                var stateDir = (string)F(folders, "StateTarget");
                Console.WriteLine("  saves  " + saveDir + "\n  states " + stateDir);
                var newSave = Path.Combine(saveDir, Path.GetFileName(besideSave));
                Check("save moved", !File.Exists(besideSave) && File.Exists(newSave) == File.Exists(oldSave));
                Check("state and its PNG moved", !File.Exists(besideState) && File.Exists(Path.Combine(stateDir, Path.GetFileName(besideState))) && File.Exists(Path.Combine(stateDir, Path.GetFileName(besideState) + ".png")));

                var before = File.Exists(newSave) ? File.GetLastWriteTimeUtc(newSave) : DateTime.MinValue;
                var p = Process.Start(new ProcessStartInfo(exe, "--settings-file \"" + bml + "\" --system \"" + system + "\" \"" + game + "\"") { UseShellExecute = false });
                for (int i = 0; i < 40 && p.MainWindowHandle == IntPtr.Zero; i++) { System.Threading.Thread.Sleep(250); p.Refresh(); }
                System.Threading.Thread.Sleep(8000);
                PostMessage(p.MainWindowHandle, 0x10, IntPtr.Zero, IntPtr.Zero);
                if (!p.WaitForExit(10000)) { p.Kill(); Check("ares quit on WM_CLOSE", false); }
                System.Threading.Thread.Sleep(500);
                Check("ares wrote the save in Saves\\" + Path.GetFileName(saveDir), File.Exists(newSave) && File.GetLastWriteTimeUtc(newSave) > before, newSave);
                var beside = Directory.GetFiles(games).Select(Path.GetFileName).Where(n => !string.Equals(n, Path.GetFileName(game), StringComparison.OrdinalIgnoreCase)).ToList();
                Check("nothing beside the game", beside.Count == 0, string.Join(", ", beside));
                Check("settings.bml kept the folder after ares quit", File.ReadAllText(bml).Contains("Saves: " + emu.Replace('\\', '/') + "/Saves/"));
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).ToString()); }
            finally { try { Directory.Delete(work, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        /// <summary>--ares-compat &lt;system&gt; &lt;rom&gt; [title]: the game's entry in ares's compatibility list, and the list's size.</summary>
        public static bool Compat(Assembly asm, string system, string rom, string title)
        {
            var compat = T(asm, "AresCompat");
            var info = compat.GetMethod("Info", Any).Invoke(null, null);
            Console.WriteLine("  the list: " + info);
            var all = (System.Collections.IEnumerable)compat.GetMethod("FindAll", Any).Invoke(null, new object[] { system, rom, title, null, null });
            var summary = compat.Assembly.GetType("LbIntegrations.Ares.AresCompatEntry").GetProperty("Summary");
            int n = 0;
            foreach (var m in all)
            {
                var e = F(m, "Entry");
                Console.WriteLine("  " + ((bool)F(m, "Best") ? "* " : "  ") + F(e, "Title") + ": " + summary.GetValue(e) + " - " + F(e, "Notes") + "  (by " + F(m, "How") + ")");
                n++;
            }
            if (n == 0) Console.WriteLine("  not in the list");
            return n > 0;
        }

        /// <summary>A settings.bml's leaves, by path ("Video/Shader").</summary>
        internal static System.Collections.Generic.Dictionary<string, string> AresBmlLines(string path)
        {
            var map = new System.Collections.Generic.Dictionary<string, string>();
            var stack = new System.Collections.Generic.List<string>();
            foreach (var line in File.ReadAllText(path).Replace("\r", "").Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                int depth = 0; while (depth < line.Length && line[depth] == ' ') depth++;
                depth /= 2;
                var t = line.Trim(); int c = t.IndexOf(':');
                var name = c < 0 ? t : t.Substring(0, c).Trim();
                while (stack.Count > depth) stack.RemoveAt(stack.Count - 1);
                stack.Add(name);
                var key = string.Join("/", stack);
                if (!map.ContainsKey(key)) map[key] = c < 0 ? "" : t.Substring(c + 1).Trim();
            }
            return map;
        }

        public static bool Firmware(Assembly asm, string exe)
        {
            Console.WriteLine("-- the BIOS for " + exe + " --");
            var all = T(asm, "AresSystems").GetField("All", Any).GetValue(null);
            var states = (IEnumerable)T(asm, "AresFirmware").GetMethod("Ensure", Any).Invoke(null, new object[] { exe, all, null, true, false });
            foreach (var st in states)
            {
                var spec = F(st, "Spec");
                Console.WriteLine("  " + ((string)F(spec, "Label")).PadRight(28) + " " + ((string)F(st, "Path") ?? (((bool)F(spec, "Optional")) ? "- (optional)" : "MISSING"))
                                  + ((string)F(st, "Path") != null && !(bool)F(st, "HashOk") ? "   (another dump)" : ""));
            }
            return true;
        }

        public static bool Saves(Assembly asm, string exe, string system, string rom)
        {
            Console.WriteLine("-- saves of " + Path.GetFileName(rom) + " as " + system + " --");
            var sys = T(asm, "AresSystems").GetMethod("System", Any).Invoke(null, new object[] { system });
            var collect = T(asm, "AresPlugin").GetMethod("Collect", Any);
            var list = Activator.CreateInstance(collect.GetParameters()[0].ParameterType);
            collect.Invoke(null, new object[] { list, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exe, sys, rom, "probe-game", null });
            foreach (var row in (IEnumerable)list)
                Console.WriteLine("    " + F(row, "FileLocation") + "  " + F(row, "SaveGroupId") + "  slot " + (F(row, "Slot") ?? "-"));
            return true;
        }

        public static bool Latest(Assembly asm)
        {
            Console.WriteLine("-- ares's latest release --");
            var r = T(asm, "AresBuild").GetMethod("Latest", Any).Invoke(null, null);
            if (r == null) { Console.WriteLine("  FAIL - none"); return false; }
            foreach (var n in new[] { "Version", "Url", "Sha256" }) Console.WriteLine("  " + n + ": " + F(r, n));
            return true;
        }
    }
}
