// Confronting the SUPER ZSNES plugin with what was measured off its build and its site.
//
// Everything here runs offline against fixtures: the Downloads section of zsnes.com as it was served
// on 2026-09-30, the three lines of its version.txt, and a forged install in the temp folder whose
// scene file carries the About text the real 0.310 build carries. The network is exercised by the
// probe's ordinary sections (GetInstallableVersions, IsUpdateAvailable), not here.
//
// Nothing here touches a real SUPER ZSNES.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal static class SuperZsnesCheck
    {
        private static int _fail;

        private static bool Check(string what, bool good, string detail)
        {
            var ok = Check(what, good);
            if (!ok && detail != null) Console.WriteLine("     got: " + detail);
            return ok;
        }

        private static bool Check(string what, bool good)
        {
            if (!good) _fail++;
            Console.WriteLine("  " + what.PadRight(60) + (good ? "OK" : "FAIL"));
            return good;
        }

        private const string Snes = "Super Nintendo Entertainment System";

        /// <summary>The Downloads section of https://www.zsnes.com/, verbatim, 2026-09-30. The Mac
        /// and Linux cards are kept so the check proves they are NOT what gets taken.</summary>
        private const string PageFixture =
            "<section id=\"downloads\">\n<h2>Downloads</h2>\n<div class=\"download-grid\">\n"
            + "  <div class=\"download-card\">\n    <h3>Windows</h3>\n    <a href=\"files/SuperZSNES_v0.310.zip\">Download</a>\n  </div>\n"
            + "  <div class=\"download-card\">\n    <h3>Mac</h3>\n    <a href=\"files/SuperZSNES_v0.310.dmg\">Download</a>\n  </div>\n"
            + "  <div class=\"download-card\">\n    <h3>Linux</h3>\n    <a href=\"files/SuperZSNES_v0.310.tar.gz\">Download</a>\n  </div>\n"
            + "  <div class=\"download-card\">\n    <h3>Android</h3>\n"
            + "    <a href=\"https://play.google.com/store/apps/details?id=com.zsnes.superzsnes\" target=\"_blank\" rel=\"noopener\">Google Play</a>\n  </div>\n"
            + "</div>\n</section>\n<section id=\"release-notes\">\n<h2>Latest Release Notes (v0.310)</h2>\n";

        /// <summary>https://www.zsnes.com/version.txt, verbatim, 2026-09-30.</summary>
        private const string FeedFixture = "Windows,0.310\nLinux,0.310\nMac,0.310\n";

        /// <summary>--superzsnes-deploy-real --emu SUPERZSNES.exe: THE REAL DEPLOY into that folder - BepInEx downloaded from
        /// builds.bepinex.dev and checked against its pinned sha256, the silent config, the embedded plugin and docs. What
        /// the install from LaunchBox does, with nothing forged. Writes into that folder only (and the temp folder).</summary>
        public static bool DeployReal(EmulatorPlugin plugin, string exe)
        {
            _fail = 0;
            Console.WriteLine();
            Console.WriteLine("-- SUPER ZSNES, BepInEx deployed for real into " + exe + "  [WRITES there, downloads] ---");
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var t = plugin.GetType().Assembly.GetType("LbIntegrations.SuperZsnes.SuperZsnesBepInEx", true);
            if (!Check("the executable is there", exe != null && File.Exists(exe))) return false;
            var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
            int lastTen = -1;
            var r = t.GetMethod("Deploy", flags).Invoke(null, new object[] { exe, (Action<string, double?>)((m, p) =>
            {
                int ten = p == null ? -1 : (int)(p.Value * 10);
                if (p == null || ten != lastTen) { lastTen = ten; Console.WriteLine("    " + m + (p == null ? "" : " " + (int)(p.Value * 100) + "%")); }
            }), (Func<bool>)(() => false) });
            bool ok = (bool)r.GetType().GetProperty("Ok").GetValue(r);
            foreach (var step in (System.Collections.IEnumerable)r.GetType().GetField("Steps").GetValue(r)) Console.WriteLine("    step: " + step);
            if (!Check("the deploy succeeds", ok)) Console.WriteLine("     " + (string)r.GetType().GetField("Problem").GetValue(r));
            Check("BepInEx, Doorstop and the runtime are there", File.Exists(Path.Combine(dir, "winhttp.dll")) && File.Exists(Path.Combine(dir, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")) && Directory.Exists(Path.Combine(dir, "dotnet")));
            var cfg = Path.Combine(dir, "BepInEx", "config", "BepInEx.cfg");
            Check("the config is silent", File.Exists(cfg) && File.ReadAllText(cfg).Replace("\r\n", "\n").Contains("[Logging.Console]\n\nEnabled = false"));
            Check("the plugin is in BepInEx\\plugins", File.Exists(Path.Combine(dir, "BepInEx", "plugins", "SuperZsnes.BepInEx.dll")));
            Check("the docs are in BepInEx\\nixx-docs", File.Exists(Path.Combine(dir, "BepInEx", "nixx-docs", "README.md")));
            Console.WriteLine(_fail == 0 ? "  OK - BepInEx deployed for real" : "  " + _fail + " FAILED");
            return _fail == 0;
        }

        /// <summary>--superzsnes-settings-shot out.png: the SUPER ZSNES tab of the Nixx window, drawn off screen at a tall
        /// size, then scrolled down page by page until its end - the pictures side by side. Nothing on screen, nothing
        /// written but the picture.</summary>
        public static bool SettingsShot(EmulatorPlugin plugin, string outPath)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            var settings = plugin.GetType().Assembly.GetType("LbIntegrations.SuperZsnes.Settings", true);
            var shots = new List<System.Drawing.Bitmap>();
            using (var form = new System.Windows.Forms.Form
            {
                StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000),
                ClientSize = new System.Drawing.Size(684, 1000), Font = new System.Drawing.Font("Segoe UI", 9f),
            })
            {
                var page = (System.Windows.Forms.Control)settings.GetMethod("CreatePage").Invoke(null, null);
                page.Dock = System.Windows.Forms.DockStyle.Fill;
                form.Controls.Add(page);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var scroll = page.Controls.OfType<System.Windows.Forms.Panel>().First(p => p.AutoScroll);
                for (int y = 0, last = -1; shots.Count < 12; y += scroll.ClientSize.Height - 40)
                {
                    scroll.AutoScrollPosition = new System.Drawing.Point(0, y);
                    System.Windows.Forms.Application.DoEvents();
                    if (-scroll.AutoScrollPosition.Y == last) break;
                    last = -scroll.AutoScrollPosition.Y;
                    var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                    shots.Add(bmp);
                }
                form.Close();
            }
            // And a game's own options window, as its right-click opens it.
            var formType = plugin.GetType().Assembly.GetType("LbIntegrations.SuperZsnes.SuperZsnesGameOptionsForm", true);
            var games = new List<IGame> { StubGame.Create("probe-shot", "Star Fox", "C:\\starfox.sfc") };
            using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { games }, null))
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var page = form.Controls.OfType<System.Windows.Forms.UserControl>().First();
                var scroll = page.Controls.OfType<System.Windows.Forms.Panel>().First(p => p.AutoScroll);
                for (int y = 0, last = -1, n = 0; n < 6; y += scroll.ClientSize.Height - 40, n++)
                {
                    scroll.AutoScrollPosition = new System.Drawing.Point(0, y);
                    System.Windows.Forms.Application.DoEvents();
                    if (-scroll.AutoScrollPosition.Y == last) break;
                    last = -scroll.AutoScrollPosition.Y;
                    var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                    shots.Add(bmp);
                }
                form.Close();
            }
            using var all = new System.Drawing.Bitmap(shots.Sum(s => s.Width), shots.Max(s => s.Height));
            using (var g = System.Drawing.Graphics.FromImage(all))
            {
                g.Clear(System.Drawing.Color.DimGray);
                int x = 0;
                foreach (var s in shots) { g.DrawImage(s, x, 0); x += s.Width; s.Dispose(); }
            }
            all.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("  " + outPath);
            return true;
        }

        /// <summary>--superzsnes-current --emu SUPERZSNES.exe: what the options window reads as SUPER ZSNES's own values -
        /// its settings file (NRBF) and Unity's registry values. Read only.</summary>
        public static bool Current(EmulatorPlugin plugin, string exe)
        {
            var t = plugin.GetType().Assembly.GetType("LbIntegrations.SuperZsnes.SuperZsnesCurrent", true);
            var values = (Dictionary<string, string>)t.GetMethod("Read", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { exe });
            Console.WriteLine();
            Console.WriteLine("-- SUPER ZSNES's own values, read off " + exe + " ---");
            foreach (var kv in values.OrderBy(kv => kv.Key)) Console.WriteLine("  " + kv.Key.PadRight(32) + kv.Value);
            Console.WriteLine("  " + values.Count + " value(s)");
            return values.Count > 0;
        }

        public static bool Run(EmulatorPlugin plugin)
        {
            _fail = 0;
            Console.WriteLine();
            Console.WriteLine("-- SUPER ZSNES, offline, on a FORGED install  [WRITES, in the temp folder] ---");

            var asm = plugin.GetType().Assembly;
            var site = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesSite");
            var paths = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesPaths");
            var ahk = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesAhk");
            var pluginType = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesPlugin");
            if (site == null || paths == null || ahk == null || pluginType == null)
            {
                Console.WriteLine("  FAIL - this is not the SUPER ZSNES plugin");
                return false;
            }
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            // ── the site ─────────────────────────────────────────────────────
            Console.WriteLine("  the Downloads section of zsnes.com");
            var build = site.GetMethod("ParsePage", flags).Invoke(null, new object[] { PageFixture, "https://www.zsnes.com/" });
            Check("the Windows card is found", build != null);
            if (build != null)
            {
                string Field(string n) => build.GetType().GetField(n).GetValue(build) as string;
                Check("its link is the zip, made absolute",
                      Field("Url") == "https://www.zsnes.com/files/SuperZSNES_v0.310.zip");
                Check("the version comes out of the file name", Field("Version") == "0.310");
                Check("and so does the file name", Field("FileName") == "SuperZSNES_v0.310.zip");
                Check("neither the .dmg nor the .tar.gz is taken",
                      !Field("Url").EndsWith(".dmg") && !Field("Url").EndsWith(".tar.gz"));
            }

            // Without the heading: any SuperZSNES zip is still the Windows build.
            var headless = PageFixture.Replace("<h3>Windows</h3>", "<h3>PC</h3>");
            var fallback = site.GetMethod("ParsePage", flags).Invoke(null, new object[] { headless, "https://www.zsnes.com/" });
            Check("a renamed heading still finds the zip",
                  fallback != null && (fallback.GetType().GetField("Url").GetValue(fallback) as string ?? "").EndsWith("SuperZSNES_v0.310.zip"));

            // An absolute href resolves to itself.
            var absolute = PageFixture.Replace("href=\"files/SuperZSNES_v0.310.zip\"", "href=\"https://cdn.zsnes.com/files/SuperZSNES_v0.320.zip\"");
            var abs = site.GetMethod("ParsePage", flags).Invoke(null, new object[] { absolute, "https://www.zsnes.com/" });
            Check("an absolute link is kept as it is",
                  abs != null && (abs.GetType().GetField("Url").GetValue(abs) as string) == "https://cdn.zsnes.com/files/SuperZSNES_v0.320.zip"
                  && (abs.GetType().GetField("Version").GetValue(abs) as string) == "0.320");

            var nothing = site.GetMethod("ParsePage", flags).Invoke(null, new object[] { "<html><body>Coming soon</body></html>", "https://www.zsnes.com/" });
            Check("a page with no download gives null, not a guess", nothing == null);

            Console.WriteLine("  version.txt");
            var feed = site.GetMethod("ParseVersionFeed", flags);
            Check("the Windows line is read", (string)feed.Invoke(null, new object[] { FeedFixture }) == "0.310");
            Check("a feed without a Windows line gives null", feed.Invoke(null, new object[] { "Linux,0.310\nMac,0.310\n" }) == null);
            Check("an HTML error page gives null", feed.Invoke(null, new object[] { "<html>502</html>" }) == null);

            // ── the update decision ───────────────────────────────────────────
            Console.WriteLine("  is it newer");
            var newer = pluginType.GetMethod("IsNewer", flags);
            bool IsNewer(string a, string b) => (bool)newer.Invoke(null, new object[] { a, b });
            Check("0.320 is newer than 0.310", IsNewer("0.320", "0.310"));
            Check("0.310 is not newer than 0.310", !IsNewer("0.310", "0.310"));
            Check("0.310 is newer than 0.230 (numerically, not as text)", IsNewer("0.310", "0.230"));
            Check("1.0 is newer than 0.999", IsNewer("1.0", "0.999"));
            Check("0.230 is not newer than 0.310", !IsNewer("0.230", "0.310"));
            Check("nothing installed means an update", IsNewer("0.310", null));
            Check("a site version that is not a number never nags", !IsNewer("latest", "0.310"));

            // A link whose name lost its number: the version comes from the feed, not from nowhere.
            var nameless = PageFixture.Replace("files/SuperZSNES_v0.310.zip", "files/SuperZSNES-windows.zip");
            var fromNameless = site.GetMethod("ParsePage", flags).Invoke(null, new object[] { nameless, "https://www.zsnes.com/" });
            Check("a zip without a number in its name is still the download, version unknown",
                  fromNameless != null && (fromNameless.GetType().GetField("Url").GetValue(fromNameless) as string ?? "").EndsWith("SuperZSNES-windows.zip")
                  && fromNameless.GetType().GetField("Version").GetValue(fromNameless) == null);

            // ── a forged install ──────────────────────────────────────────────
            var root = Path.Combine(Path.GetTempPath(), "lbip-superzsnes-" + Guid.NewGuid().ToString("N"));
            try
            {
                Console.WriteLine("  a forged install");
                var install = Path.Combine(root, "Nixx-SuperZSNES");
                var data = Path.Combine(install, "SUPERZSNES_Data");
                Directory.CreateDirectory(data);
                var exe = Path.Combine(install, "SUPERZSNES.exe");
                File.WriteAllBytes(exe, Array.Empty<byte>());

                // The About caption as level0 carries it: binary around, the text in the middle, the
                // enhanced-games list right after. Plus what the REAL 0.310 scene also carries - two
                // stale label texts, "v0.001" and "v0.100a", framed like real strings - and a decoy
                // in the middle of a longer string that must NOT be read as a version at all.
                var scene = new byte[4096];
                new Random(7).NextBytes(scene);
                var labels = Encoding.ASCII.GetBytes("\0\0\0\0v0.001\0\0\0nrek\0\0\0\0v0.100a\0\0\0");
                var about = Encoding.ASCII.GetBytes("\0\0\0\0v0.310b\0\0\0\0Chrono Trigger\0\0\0F-Zero\0\0dsp2.rom\0");
                var decoy = Encoding.ASCII.GetBytes("\0Unity.RenderPipelines.Universal@v17.200\0shader_v2.100.x\0");
                Array.Copy(labels, 0, scene, 500, labels.Length);
                Array.Copy(about, 0, scene, 1000, about.Length);
                Array.Copy(decoy, 0, scene, 2000, decoy.Length);
                File.WriteAllBytes(Path.Combine(data, "level0"), scene);

                var installed = paths.GetMethod("InstalledVersion", flags);
                Check("the About text gives 0.310: highest of three, hotfix letter dropped, decoy ignored",
                      (string)installed.Invoke(null, new object[] { exe }) == "0.310");
                Check("the plugin answers the same through GetCurrentVersion",
                      plugin.GetCurrentVersion(exe) == "0.310");

                // A scene without any caption - the random bytes between the About block and the
                // decoy, none of the three labels: the install stamp is the fallback, then nothing.
                File.WriteAllBytes(Path.Combine(data, "level0"), scene.Skip(1100).Take(900).ToArray());
                Check("no caption and no stamp gives null", plugin.GetCurrentVersion(exe) == null);
                File.WriteAllText(Path.Combine(install, "lbip-superzsnes-build.txt"), "0.230\r\nSuperZSNES_v0.230.zip\r\n");
                Check("no caption but a stamp gives the stamp", plugin.GetCurrentVersion(exe) == "0.230");

                Check("no data folder at all gives null",
                      plugin.GetCurrentVersion(Path.Combine(root, "elsewhere", "SUPERZSNES.exe")) == null);

                Console.WriteLine("  the contract");
                Check("the executable is recognised whatever its case",
                      (bool)paths.GetMethod("IsSuperZsnesExecutable", flags).Invoke(null, new object[] { @"C:\x\SuperZSNES.exe" })
                      && (bool)paths.GetMethod("IsSuperZsnesExecutable", flags).Invoke(null, new object[] { exe }));
                Check("and another SNES emulator is not",
                      !(bool)paths.GetMethod("IsSuperZsnesExecutable", flags).Invoke(null, new object[] { @"C:\x\snes9x-x64.exe" })
                      && !(bool)paths.GetMethod("IsSuperZsnesExecutable", flags).Invoke(null, new object[] { @"C:\x\zsnesw.exe" }));
                Check("the executable is found at the root of an extraction",
                      (string)paths.GetMethod("FindExecutable", flags).Invoke(null, new object[] { install }) == exe);

                var mine = new StubEmulator { Title = "Nixx-SuperZSNES", ApplicationPath = exe };
                var other = new StubEmulator { Title = "Snes9x", ApplicationPath = @"C:\emus\snes9x-x64.exe" };
                var claimed = plugin.GetApplicableEmulators(new IEmulator[] { mine, other }).ToList();
                Check("claims its own entry and refuses the other",
                      claimed.Count == 1 && ReferenceEquals(claimed[0], mine));
                Check("fills the blank AutoHotkey fields on the way",
                      !string.IsNullOrWhiteSpace(mine.ExitAutoHotkeyScript) && !string.IsNullOrWhiteSpace(mine.AutoHotkeyScript));

                Check("supports the SNES", plugin.IsPlatformSupported(Snes).Supported && plugin.IsPlatformSupported(Snes).Recommended);
                Check("and nothing else", !plugin.IsPlatformSupported("Nintendo 64").Supported
                                          && !plugin.IsPlatformSupported("Nintendo Entertainment System").Supported);
                Check("declares no BIOS", !plugin.GetBiosFilesForPlatform(Snes).Any()
                                          && !plugin.GetBiosFilesForPlatform(exe, Snes, "").Any());
                Check("RetroAchievements: not by the plugin", !plugin.SupportsRetroAchievements(exe).IsSupported);
                Check("save management is on: the cartridge save and the states", plugin.SupportsSaveManagement());
                Check("the launch is passed through untouched",
                      plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(mine, null, "", null, null))?.NewCommandLine == null);

                Console.WriteLine("  the catalogue row");
                var source = plugin as LbIntegrations.Catalog.ILbCatalogSource;
                if (Check("the plugin is an ILbCatalogSource - the host's own type", source != null))
                {
                    var rows = source.EmulatorRows().ToList();
                    Check("one row", rows.Count == 1);
                    var row = rows.FirstOrDefault();
                    Check("named Nixx-SuperZSNES", row?.Name == "Nixx-SuperZSNES" && plugin.EmulatorName == row.Name);
                    Check("looking for SUPERZSNES.exe", row?.BinaryFileName == "SUPERZSNES.exe");
                    Check("the four SNES extensions the emulator lists, and .zip",
                          row != null && new[] { ".sfc", ".smc", ".swc", ".zip" }.All(e => row.ApplicableFileExtensions.Contains(e))
                          && !row.ApplicableFileExtensions.Contains(".7z"));
                    Check("the archive is handed over unextracted", row != null && !row.AutoExtract);
                    Check("one platform, the SNES, recommended",
                          row?.Platforms.Count == 1 && row.Platforms[0].Platform == Snes && row.Platforms[0].Recommended);
                    Check("pointing at zsnes.com", row?.Url == "https://www.zsnes.com/");
                }

                Console.WriteLine("  the scripts");
                string Script(string name) => (string)ahk.GetField(name, flags).GetValue(null);
                Check("the exit script sends Alt+F4", Script("Exit").Contains("{Alt down}") && Script("Exit").Contains("{F4}"));
                Check("the running script sends nothing (Escape is the emulator's menu)", AllComments(Script("Running")));
                Check("the save-state script sends F2, the load-state one F4 (measured in inputData)",
                      Script("SaveState").Contains("{F2 down}") && Script("LoadState").Contains("{F4 down}"));
                // An entry an earlier version described with "Nothing is sent": given the scripts now.
                var stale = (string[])ahk.GetField("Superseded", flags).GetValue(null);
                var old = new StubEmulator { Title = "Nixx-SuperZSNES", ApplicationPath = exe, AutoHotkeyScript = stale[1], ExitAutoHotkeyScript = Script("Exit"), SaveStateAutoHotkeyScript = stale[0], LoadStateAutoHotkeyScript = stale[0] };
                plugin.GetApplicableEmulators(new IEmulator[] { old }).ToList();
                Check("an older version's placeholders are replaced, nothing else", old.SaveStateAutoHotkeyScript == Script("SaveState") && old.LoadStateAutoHotkeyScript == Script("LoadState") && old.AutoHotkeyScript == Script("Running"));
                var mineAhk = new StubEmulator { Title = "Nixx-SuperZSNES", ApplicationPath = exe, SaveStateAutoHotkeyScript = "Send {F5}" };
                plugin.GetApplicableEmulators(new IEmulator[] { mineAhk }).ToList();
                Check("  ...a script the user wrote is left alone", mineAhk.SaveStateAutoHotkeyScript == "Send {F5}");

                // ── the command line the window builds ────────────────────────
                Console.WriteLine("  the command line");
                var settingsType = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesSettings");
                var optionsType = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesOptions");
                if (Check("SuperZsnesSettings and SuperZsnesOptions are there", settingsType != null && optionsType != null))
                {
                    var pathField = settingsType.GetField("PathOverride", flags);
                    var ini = Path.Combine(root, "settings.ini");
                    pathField.SetValue(null, ini);
                    // The screen session works on a key of its own here, never the real one.
                    var screen = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesScreenSession", true);
                    const string testKey = @"Software\lbip-probe\SUPERZSNES";
                    screen.GetField("KeyOverride", flags).SetValue(null, testKey);
                    using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testKey))
                    {
                        k.SetValue("Screenmanager Fullscreen mode_h3630240806", 0, Microsoft.Win32.RegistryValueKind.DWord);
                        k.SetValue("Screenmanager Resolution Width_h182942802", 1920, Microsoft.Win32.RegistryValueKind.DWord);
                        k.SetValue("unity.player_session_count_h922449978", 7, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    try
                    {
                        // The catalogue itself.
                        var all = (System.Collections.IEnumerable)optionsType.GetField("All", flags).GetValue(null);
                        var iniKeys = new List<string>();
                        int choicesOk = 0, choices = 0, badNames = 0;
                        foreach (var o in all)
                        {
                            var t = o.GetType();
                            iniKeys.Add((string)t.GetProperty("IniKey").GetValue(o));
                            var kind = t.GetField("Kind").GetValue(o).ToString();
                            var family = t.GetField("Family").GetValue(o).ToString();
                            var key = (string)t.GetField("Key").GetValue(o);
                            if (kind == "Choice") { choices++; var c = (string[])t.GetField("Choices").GetValue(o); if (c != null && c.Length >= 2) choicesOk++; }
                            if ((family == "Setting" || family == "Game") && !System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$")) badNames++;
                        }
                        Console.WriteLine("  options    : " + iniKeys.Count);
                        Check("every option has a distinct ini key", iniKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == iniKeys.Count);
                        Check("every choice offers at least two values", choices > 0 && choicesOk == choices);
                        Check("every emulator field name is an identifier", badNames == 0);

                        // What the settings become.
                        File.WriteAllLines(ini, new[]
                        {
                            "setting.srmPath={exec}/saves",
                            "setting.gameSavePath=D:\\my saves",
                            "setting.rewindSpeed=1,5",
                            "setting.noBilinearFiltering=on",
                            "game.overclock=150",
                            "game.disEnhanceWide=true",
                            "plugin.quit-confirm=false",
                            "plugin.menu-key=F3",
                            "native.loadstate=true",
                            "unity.screen-fullscreen=fullscreen",
                            "unity.screen-width=1920",
                            "unity.popupwindow=false",
                            "plugin.display=true",
                            "nonsense.key=1",
                        });
                        // The renderer, on every option whatever its scope.
                        var read = settingsType.GetMethod("Read", flags, null, Type.EmptyTypes, null);
                        var list = (System.Collections.IEnumerable)settingsType.GetMethod("Flags", flags, null, new[] { typeof(IDictionary<string, string>) }, null)
                                                                              .Invoke(null, new[] { read.Invoke(null, null) });
                        var texts = new List<string>(); var names = new List<string>();
                        foreach (var f in list) { texts.Add((string)f.GetType().GetField("Text").GetValue(f)); names.Add((string)f.GetType().GetField("Name").GetValue(f)); }
                        var line = string.Join(" ", texts);
                        Console.WriteLine("  line       : " + line);
                        Check("a setting: --nixx-set:srmPath={exec}/saves", texts.Contains("--nixx-set:srmPath={exec}/saves"));
                        Check("a value with a space is one quoted argument", texts.Contains("\"--nixx-set:gameSavePath=D:\\my saves\""));
                        Check("a French decimal comma becomes a dot", texts.Contains("--nixx-set:rewindSpeed=1.5"));
                        Check("a boolean 'on' becomes true", texts.Contains("--nixx-set:noBilinearFiltering=true"));
                        Check("a game setting: --nixx-game:overclock=150", texts.Contains("--nixx-game:overclock=150") && texts.Contains("--nixx-game:disEnhanceWide=true"));
                        Check("a plugin option: --nixx-quit-confirm=off, --nixx-menu-key=F3", texts.Contains("--nixx-quit-confirm=off") && texts.Contains("--nixx-menu-key=F3"));
                        Check("the emulator's own --loadstate, by presence", texts.Contains("--loadstate"));
                        Check("Unity's switches, with a space: -screen-fullscreen 1, -screen-width 1920", texts.Contains("-screen-fullscreen 1") && texts.Contains("-screen-width 1920"));
                        Check("a Unity boolean set to false sends nothing", !texts.Contains("-popupwindow"));
                        Check("the primary-display switch renders as --nixx-display=primary", texts.Contains("--nixx-display=primary"));
                        Check("an unknown ini key is ignored", !line.Contains("nonsense"));

                        // Not twice.
                        var append = settingsType.GetMethod("Append", flags);
                        var appended = (string)append.Invoke(null, new object[] { "-screen-fullscreen 0 --nixx-set:srmPath=mine", list });
                        Check("a flag the user already typed is not added again",
                              appended.StartsWith("-screen-fullscreen 0 --nixx-set:srmPath=mine") && !appended.Contains("-screen-fullscreen 1") && !appended.Contains("srmPath={exec}"));
                        Check("but the others are, after the user's line", appended.Contains("--nixx-game:overclock=150") && appended.Contains("--loadstate"));

                        // Through the plugin, the way the host asks: settings.ini gives only the pack's own settings (01/10),
                        // the game's file only the game's options; a key of another scope is not sent.
                        var gamesDir = Path.Combine(Path.GetDirectoryName(ini), "games");
                        Directory.CreateDirectory(gamesDir);
                        File.WriteAllLines(Path.Combine(gamesDir, "g-szs.ini"), new[] { "setting.gfxMode=Scanlines", "unity.screen-width=1280", "game.overclock=150", "plugin.quit-confirm=true" });
                        var launched = StubGame.Create("g-szs", "Star Fox", Path.Combine(root, "starfox.sfc"));
                        var prepared = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(mine, launched, "-force-d3d11", null, null));
                        var nl = prepared?.NewCommandLine ?? "";
                        Console.WriteLine("  launch     : " + nl);
                        Check("PrepareEmulatorForLaunch keeps the user's line and adds the pack's own settings",
                              nl.StartsWith("-force-d3d11 ") && nl.Contains("--nixx-quit-confirm=off") && nl.Contains("--nixx-menu-key=F3"));
                        Check("  ...and the game's own options", nl.Contains("--nixx-set:gfxMode=Scanlines") && nl.Contains("-screen-width 1280"));
                        Check("  ...but no option shown nowhere (srmPath, overclock), and nothing of the wrong scope (the game's quit-confirm)",
                              !nl.Contains("srmPath") && !nl.Contains("overclock") && !nl.Contains("--nixx-quit-confirm=on") && !nl.Contains("-screen-fullscreen") && !nl.Contains("--loadstate"));
                        // A game with a Window option: the registry's screen values written down, put back at its end.
                        var pending = screen.GetProperty("Pending", flags);
                        var lastId = (string)screen.GetField("LastId", flags).GetValue(null);
                        Check("a Window option added: the registry's screen values written down", (bool)pending.GetValue(null) && lastId != null);
                        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey, true))
                        {
                            // What Unity would write as it quits after -screen-width 1280 -screen-fullscreen 0.
                            k.SetValue("Screenmanager Fullscreen mode_h3630240806", 3, Microsoft.Win32.RegistryValueKind.DWord);
                            k.SetValue("Screenmanager Resolution Width_h182942802", 1280, Microsoft.Win32.RegistryValueKind.DWord);
                            k.SetValue("Screenmanager Resolution Window Width_h2524650974", 1280, Microsoft.Win32.RegistryValueKind.DWord);
                            k.SetValue("unity.player_session_count_h922449978", 8, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                        var restore = screen.GetMethod("Restore", flags);
                        restore.Invoke(null, new object[] { "a late watcher", "not-this-session" });
                        Check("  ...a late watcher leaves the session's alone", (bool)pending.GetValue(null));
                        restore.Invoke(null, new object[] { "the session is over", lastId });
                        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
                            Check("  ...its end: the screen values back, the one it added gone, Unity's counter untouched",
                                  !(bool)pending.GetValue(null) && (int)k.GetValue("Screenmanager Fullscreen mode_h3630240806") == 0
                                  && (int)k.GetValue("Screenmanager Resolution Width_h182942802") == 1920
                                  && k.GetValue("Screenmanager Resolution Window Width_h2524650974") == null
                                  && (int)k.GetValue("unity.player_session_count_h922449978") == 8);
                        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\lbip-probe", false);
                        var otherGame = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(mine, StubGame.Create("g-other", "Other", Path.Combine(root, "other.sfc")), "", null, null));
                        Check("another game: not this one's options", otherGame?.NewCommandLine == null || !otherGame.NewCommandLine.Contains("gfxMode"));

                        // The window's tab, built and saved the way Nixx-Menus does it - on an STA thread,
                        // since WinForms wants one and the probe's main thread is not.
                        Console.WriteLine("  the window's tab");
                        var settingsClass = asm.GetType("LbIntegrations.SuperZsnes.Settings");
                        Exception uiFailure = null;
                        string title = null, saveAnswer = "(not run)";
                        Dictionary<string, string> shown = null;
                        var ui = new System.Threading.Thread(() =>
                        {
                            try
                            {
                                title = (string)settingsClass.GetProperty("Title").GetValue(null);
                                var page = (System.Windows.Forms.Control)settingsClass.GetMethod("CreatePage").Invoke(null, null);
                                page.CreateControl();
                                shown = (Dictionary<string, string>)page.GetType().GetMethod("Values").Invoke(page, null);
                                saveAnswer = (string)settingsClass.GetMethod("Save").Invoke(null, new object[] { page });
                                page.Dispose();
                            }
                            catch (Exception ex) { uiFailure = ex; }
                        });
                        ui.SetApartmentState(System.Threading.ApartmentState.STA);
                        ui.Start();
                        ui.Join(TimeSpan.FromSeconds(30));
                        Check("the tab builds without throwing", uiFailure == null && title == "SUPER ZSNES");
                        if (uiFailure != null) Console.WriteLine("     " + uiFailure.GetType().Name + ": " + uiFailure.Message);
                        Check("its controls show the saved settings, the pack's own only", shown != null
                              && shown.TryGetValue("plugin.menu-key", out var s4) && s4 == "F3"
                              && shown.TryGetValue("plugin.quit-confirm", out var s5) && s5 == "false"
                              && !shown.ContainsKey("setting.srmPath") && !shown.ContainsKey("unity.screen-fullscreen") && !shown.ContainsKey("nonsense.key"),
                              shown == null ? "null" : string.Join(", ", shown.Select(kv => kv.Key + "=" + kv.Value)));
                        Check("Save accepts the page and rewrites the file", saveAnswer == null && File.ReadAllText(ini).Contains("plugin.menu-key=F3"));

                        File.WriteAllText(ini, "# nothing ticked\r\n");
                        var untouched = plugin.PrepareEmulatorForLaunch(new PrepareForLaunchArgs(mine, null, "", null, null));
                        Check("with nothing ticked, the launch is passed through untouched", untouched?.NewCommandLine == null);
                    }
                    finally { pathField.SetValue(null, null); }
                }

                // ── BepInEx into an emulator folder, from a forged archive ───────
                Console.WriteLine("  deploying BepInEx");
                var deployType = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesBepInEx");
                if (Check("SuperZsnesBepInEx is there", deployType != null))
                {
                    // A 32-bit PE header on the forged exe, so the x86 package is the one chosen.
                    var pe = new byte[0x100];
                    pe[0] = (byte)'M'; pe[1] = (byte)'Z';
                    BitConverter.GetBytes(0x80).CopyTo(pe, 0x3C);
                    pe[0x80] = (byte)'P'; pe[0x81] = (byte)'E';
                    BitConverter.GetBytes((ushort)0x14C).CopyTo(pe, 0x84);
                    File.WriteAllBytes(exe, pe);
                    Check("the executable's architecture is read off its PE header",
                          (string)deployType.GetMethod("ArchOf", flags).Invoke(null, new object[] { exe }) == "x86");

                    // The archive BepInEx would be: the files the deployer checks for, and nothing real.
                    var zipDir = Path.Combine(root, "zip-src");
                    Directory.CreateDirectory(Path.Combine(zipDir, "BepInEx", "core"));
                    Directory.CreateDirectory(Path.Combine(zipDir, "dotnet"));
                    File.WriteAllText(Path.Combine(zipDir, "winhttp.dll"), "doorstop");
                    File.WriteAllText(Path.Combine(zipDir, ".doorstop_version"), "4.5.0");
                    File.WriteAllText(Path.Combine(zipDir, "doorstop_config.ini"), "[General]\nenabled = true\n");
                    File.WriteAllText(Path.Combine(zipDir, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"), "chainloader");
                    File.WriteAllText(Path.Combine(zipDir, "dotnet", "coreclr.dll"), "runtime");
                    var zip = Path.Combine(root, "bepinex.zip");
                    System.IO.Compression.ZipFile.CreateFromDirectory(zipDir, zip);

                    var pluginSrc = Path.Combine(root, "SuperZsnes.BepInEx.dll");
                    File.WriteAllText(pluginSrc, "the in-process plugin, version A");
                    var docsSrc = Path.Combine(root, "docs");
                    Directory.CreateDirectory(docsSrc);
                    File.WriteAllText(Path.Combine(docsSrc, "README.md"), "# runbook");

                    deployType.GetField("ZipOverride", flags).SetValue(null, zip);
                    deployType.GetField("PluginOverride", flags).SetValue(null, pluginSrc);
                    deployType.GetField("DocsOverride", flags).SetValue(null, docsSrc);
                    try
                    {
                        var deploy = deployType.GetMethod("Deploy", flags);
                        var steps = new List<string>();
                        object Run() => deploy.Invoke(null, new object[] { exe, (Action<string, double?>)((m, p) => steps.Add(m)), (Func<bool>)(() => false) });
                        bool Ok(object r) => (bool)r.GetType().GetProperty("Ok").GetValue(r);
                        bool Changed(object r) => (bool)r.GetType().GetField("Changed").GetValue(r);
                        string Problem(object r) => (string)r.GetType().GetField("Problem").GetValue(r);

                        var first = Run();
                        Check("a first deploy succeeds", Ok(first) && Changed(first));
                        if (!Ok(first)) Console.WriteLine("     " + Problem(first));
                        Check("BepInEx's files are in the emulator folder", File.Exists(Path.Combine(install, "winhttp.dll")) && File.Exists(Path.Combine(install, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")) && File.Exists(Path.Combine(install, "dotnet", "coreclr.dll")));
                        var cfg = Path.Combine(install, "BepInEx", "config", "BepInEx.cfg");
                        Check("a SILENT BepInEx.cfg is written: no console, no disk log",
                              File.Exists(cfg) && File.ReadAllText(cfg).Contains("[Logging.Console]\n\nEnabled = false") && File.ReadAllText(cfg).Contains("[Logging.Disk]\n\nEnabled = false"));
                        Check("the in-process plugin is in BepInEx\\plugins", File.ReadAllText(Path.Combine(install, "BepInEx", "plugins", "SuperZsnes.BepInEx.dll")) == "the in-process plugin, version A");
                        Check("the documentation is in BepInEx\\nixx-docs", File.Exists(Path.Combine(install, "BepInEx", "nixx-docs", "README.md")));
                        Check("the emulator's own files are untouched", File.Exists(exe) && File.Exists(Path.Combine(data, "level0")));
                        Check("progress was reported", steps.Count > 0);

                        var again = Run();
                        Check("a second deploy changes nothing", Ok(again) && !Changed(again));

                        // The user turned logging on to diagnose: their config is theirs.
                        File.WriteAllText(cfg, "[Logging.Console]\n\nEnabled = true\n");
                        File.WriteAllText(pluginSrc, "the in-process plugin, version B");
                        var third = Run();
                        Check("a newer plugin replaces the old one", Ok(third) && Changed(third)
                              && File.ReadAllText(Path.Combine(install, "BepInEx", "plugins", "SuperZsnes.BepInEx.dll")) == "the in-process plugin, version B");
                        Check("an existing BepInEx.cfg is never overwritten", File.ReadAllText(cfg).Contains("Enabled = true"));

                        Check("Describe says it is installed", ((string)deployType.GetMethod("Describe", flags).Invoke(null, new object[] { install })).StartsWith("BepInEx installed"));
                        Check("IsDeployed agrees", (bool)deployType.GetMethod("IsDeployed", flags).Invoke(null, new object[] { install }));

                        // PutBack alone, the launch-time path: no zip, no network, and it refuses where BepInEx is absent.
                        var bare = Path.Combine(root, "bare");
                        Directory.CreateDirectory(bare);
                        var putBack = deployType.GetMethod("PutBack", flags).Invoke(null, new object[] { bare, null });
                        Check("PutBack refuses a folder without BepInEx rather than half-installing", !Ok(putBack));
                    }
                    finally
                    {
                        deployType.GetField("ZipOverride", flags).SetValue(null, null);
                        deployType.GetField("PluginOverride", flags).SetValue(null, null);
                        deployType.GetField("DocsOverride", flags).SetValue(null, null);
                    }

                    // The real embedded documentation, not the forged one: a pack that ships without the
                    // runbook is a pack nobody can repair after an emulator update.
                    var realDocs = (System.Collections.IDictionary)deployType.GetMethod("Docs", flags).Invoke(null, null);
                    Check("the plugin embeds the SUPER ZSNES documentation folder (README + 9 chapters)", realDocs.Count >= 10);
                    Check("the runbook for a broken update travels with the emulator", realDocs.Contains("07-runbook.md") && realDocs.Contains("README.md"));
                }

                // ── save management: no settings file, so beside the ROM ─────────
                Console.WriteLine("  saves and states");
                var roms = Path.Combine(root, "roms");
                var szData = Path.Combine(roms, "Game (USA).data.szsnes");
                Directory.CreateDirectory(szData);
                var romFile = Path.Combine(roms, "Game (USA).sfc");
                File.WriteAllBytes(romFile, new byte[1024]);
                File.WriteAllBytes(Path.Combine(roms, "Game (USA).srm"), new byte[8192]);
                File.WriteAllBytes(Path.Combine(szData, "Game (USA).szst1"), new byte[100]);
                File.WriteAllBytes(Path.Combine(szData, "Game (USA).szst-last"), new byte[100]);
                File.WriteAllBytes(Path.Combine(szData, "Game (USA).szhistory"), new byte[10]);
                var emuForSaves = new StubEmulator { Title = "Nixx-SuperZSNES", ApplicationPath = exe };
                var gameForSaves = StubGame.Create("g-saves", "Game", romFile, emuForSaves.Id);
                var listed = plugin.GetSaves(new GetSavesArgs { Emulator = emuForSaves, Games = new[] { gameForSaves } });
                var szRows = listed?.FoundSaves?.ToList() ?? new List<GameSaveBase>();
                Check("the cartridge save beside the ROM is listed", szRows.Count(r => !(r is GameSaveState) && r.FileLocation.EndsWith("Game (USA).srm")) == 1,
                      string.Join(" | ", szRows.Select(r => r.FileLocation)));
                Check("slot 1's state is listed, as slot 1", szRows.OfType<GameSaveState>().Count() == 1 && szRows.OfType<GameSaveState>().First().Slot == 1);
                Check("neither the resume state (-last) nor the history is", !szRows.Any(r => r.FileLocation.Contains("-last") || r.FileLocation.Contains("szhistory")));
                var removed = plugin.RemoveSave(szRows.OfType<GameSaveState>().First());
                Check("deleting the state deletes its file, and only it", removed != null && removed.WasSuccess && !File.Exists(Path.Combine(szData, "Game (USA).szst1"))
                      && File.Exists(Path.Combine(szData, "Game (USA).szst-last")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "  OK - SUPER ZSNES agrees with what was measured" : "  " + _fail + " FAILURE(S)");
            return _fail == 0;
        }

        private static bool AllComments(string script)
            => !string.IsNullOrEmpty(script)
               && script.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).All(l => l.StartsWith(";"));
    }
}
