// --xenia-setup [--xconfig <a xconfig.settings Xenia wrote>] [--account <an Account file Xenia loaded>] [--toml <a TOML Xenia wrote>]:
// what a first install sets up in Xenia Canary (src\Xenia\XeniaSetup.cs) and the options it passes at launch.
//   - the console file: our defaults against a file Xenia wrote with its own, byte for byte; read, written, read back;
//   - the profile: our Account file against one Xenia loaded and signed into, byte for byte; decrypted, renamed;
//   - the TOML: the sign-in key set in a copy of a real one, every other line kept;
//   - the time zone Windows names, the gamertag a Windows name makes, the command line the options make.
// Writes only in the temp folder.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class XeniaSetupCheck
    {
        private static int _bad;

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        /// <summary>--xenia-shot out.png --emu xenia_canary.exe [--rom game]: the Xenia tab of the Nixx window, scrolled page by page,
        /// then a game's options window on each of its tabs - drawn off screen, the pictures side by side.</summary>
        public static bool Shot(Assembly asm, string outPath, string exe, string rom, string compatDir)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            System.Windows.Forms.Application.EnableVisualStyles();
            asm.GetType("LbIntegrations.Xenia.XeniaLibrary", true).GetField("ExeOverride", flags).SetValue(null, Path.GetFullPath(exe));
            if (compatDir != null) asm.GetType("LbIntegrations.Xenia.XeniaCompat", true).GetField("DirOverride", flags).SetValue(null, compatDir);
            var shots = new List<System.Drawing.Bitmap>();
            System.Drawing.Bitmap Snap(System.Windows.Forms.Form f)
            {
                System.Windows.Forms.Application.DoEvents();
                var bmp = new System.Drawing.Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                return bmp;
            }
            using (var form = new System.Windows.Forms.Form
            {
                StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-4000, -4000),
                ClientSize = new System.Drawing.Size(684, 900), Font = new System.Drawing.Font("Segoe UI", 9f),
            })
            {
                var page = (System.Windows.Forms.Control)asm.GetType("LbIntegrations.Xenia.Settings", true).GetMethod("CreatePage").Invoke(null, null);
                page.Dock = System.Windows.Forms.DockStyle.Fill;
                form.Controls.Add(page);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var scroll = page.Controls.OfType<System.Windows.Forms.Panel>().First(p => p.AutoScroll);
                for (int y = 0, last = -1; shots.Count < 8; y += scroll.ClientSize.Height - 40)
                {
                    scroll.AutoScrollPosition = new System.Drawing.Point(0, y);
                    System.Windows.Forms.Application.DoEvents();
                    if (-scroll.AutoScrollPosition.Y == last) break;
                    last = -scroll.AutoScrollPosition.Y;
                    shots.Add(Snap(form));
                }
                form.Close();
            }
            var formType = asm.GetType("LbIntegrations.Xenia.XeniaGameOptionsForm", true);
            var games = new List<Unbroken.LaunchBox.Plugins.Data.IGame> { StubGame.Create("probe-shot", "Bakugan Battle Brawlers", rom ?? "C:\\none.iso") };
            using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { games }, null))
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-4000, -4000);
                form.Show();
                var tabs = form.Controls.OfType<System.Windows.Forms.TabControl>().First();
                shots.Add(Snap(form));
                tabs.SelectedIndex = 1;
                System.Threading.Thread.Sleep(200);
                shots.Add(Snap(form));
                form.Close();
            }
            int w = shots.Sum(s => s.Width + 8), h = shots.Max(s => s.Height);
            using (var all = new System.Drawing.Bitmap(w, h))
            using (var g = System.Drawing.Graphics.FromImage(all))
            {
                g.Clear(System.Drawing.Color.DimGray);
                int x = 0;
                foreach (var s in shots) { g.DrawImage(s, x, 0); x += s.Width + 8; }
                all.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("  " + shots.Count + " picture(s) -> " + outPath);
            return true;
        }

        public static bool Run(Assembly asm, string xconfig, string account, string toml)
        {
            _bad = 0;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var console = asm.GetType("LbIntegrations.Xenia.XeniaConsole", true);
            var profile = asm.GetType("LbIntegrations.Xenia.XeniaProfile", true);
            var tomlT = asm.GetType("LbIntegrations.Xenia.XeniaToml", true);
            var settings = asm.GetType("LbIntegrations.Xenia.XeniaSettings", true);
            object Call(Type t, string name, params object[] a)
            {
                var m = t.GetMethods(flags).First(x => x.Name == name && x.GetParameters().Length == a.Length);
                try { return m.Invoke(null, a); } catch (TargetInvocationException e) { throw e.InnerException; }
            }
            object F(object o, string name) => o.GetType().GetField(name).GetValue(o);

            var dir = Path.Combine(Path.GetTempPath(), "lbip-xenia-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            settings.GetField("DirOverride", flags).SetValue(null, Path.Combine(dir, "data"));
            try
            {
                // ── the console file ──
                Console.WriteLine();
                Console.WriteLine("-- the console file (xconfig.settings) --");
                var defaults = (byte[])Call(console, "Defaults");
                Check("6680 bytes, as the struct", defaults.Length == 6680, defaults.Length.ToString());
                if (xconfig != null && File.Exists(xconfig))
                {
                    var real = File.ReadAllBytes(xconfig);
                    var diff = Enumerable.Range(0, Math.Min(real.Length, defaults.Length)).Where(i => real[i] != defaults[i]).ToList();
                    Check("our defaults are the file Xenia wrote with its own, byte for byte (" + xconfig + ")", real.Length == defaults.Length && diff.Count == 0,
                          diff.Count + " byte(s) differ, first at " + string.Join(", ", diff.Take(8).Select(i => "0x" + i.ToString("X") + " (" + real[i].ToString("X2") + " vs " + defaults[i].ToString("X2") + ")")));
                }
                else Console.WriteLine("    (no --xconfig: the comparison with a real file is skipped)");

                var root = Path.Combine(dir, "xenia");
                Directory.CreateDirectory(root);
                var read = Call(console, "Read", root);
                Console.WriteLine("      no file reads as: " + read.GetType().GetMethod("Describe").Invoke(read, null));
                Check("no file: English, United States, London, NTSC, 1280x720, 0.7",
                      (uint)F(read, "Language") == 1 && (byte)F(read, "Country") == 103 && (int)F(read, "TimeZone") == 0x19
                      && (uint)F(read, "AvRegion") == 0x00400100 && (uint)F(read, "Resolution") == (1280u << 16 | 720) && Math.Abs((float)F(read, "MusicVolume") - 0.7f) < 0.001f);

                var v = read.GetType().GetMethod("Copy").Invoke(read, null);
                read.GetType().GetField("Language").SetValue(v, 4u);
                read.GetType().GetField("Country").SetValue(v, (byte)34);
                read.GetType().GetField("TimeZone").SetValue(v, 28);
                read.GetType().GetField("Hour24").SetValue(v, true);
                read.GetType().GetField("Resolution").SetValue(v, 1024u << 16 | 768);
                Call(console, "Write", root, v);
                var back = Call(console, "Read", root);
                Console.WriteLine("      written and read back: " + back.GetType().GetMethod("Describe").Invoke(back, null));
                Check("French, France, Paris, 24 h, 1024x768: read back the same", (bool)back.GetType().GetMethod("SameAs").Invoke(back, new[] { v }));
                var written = File.ReadAllBytes(Path.Combine(root, "xconfig.settings"));
                var changed = Enumerable.Range(0, written.Length).Where(i => written[i] != defaults[i]).ToList();
                // language (1 byte), country, retail flags (1), video flags (widescreen off: 0 -> 0, none), the zone (names, dates), resolution
                Check("only the bytes of those fields moved (language, country, clock, zone, resolution)",
                      changed.All(i => i == 0x08E6 + 0x2F || i == 0x08E6 + 0x40 || i == 0x08E6 + 0x3B || (i >= 0x08E6 + 0x08 && i < 0x08E6 + 0x24) || (i >= 0x08E6 + 0x15C && i < 0x08E6 + 0x160)),
                      string.Join(", ", changed.Select(i => "0x" + i.ToString("X"))));
                Check("1024x768 is 4:3: no widescreen flag", written[0x08E6 + 0x31] == 0, written[0x08E6 + 0x31].ToString("X2"));

                var zoneOf = console.GetMethod("ZoneOf", flags);
                int Zone(string id) { try { return (int)zoneOf.Invoke(null, new object[] { TimeZoneInfo.FindSystemTimeZoneById(id) }); } catch { return -2; } }
                var zones = (Array)console.GetField("TimeZones", flags).GetValue(null);
                string Name(int i) => i >= 0 ? zones.GetValue(i).ToString() : i.ToString();
                Check("Windows' Romance Standard Time -> GMT+01 Paris, Madrid", Name(Zone("Romance Standard Time")) == "GMT+01 Paris, Madrid", Name(Zone("Romance Standard Time")));
                Check("Windows' GMT Standard Time -> GMT+00 London", Name(Zone("GMT Standard Time")) == "GMT+00 London", Name(Zone("GMT Standard Time")));
                Check("Windows' Tokyo Standard Time -> GMT+09 Tokyo", Name(Zone("Tokyo Standard Time")) == "GMT+09 Tokyo", Name(Zone("Tokyo Standard Time")));
                Check("Windows' Pacific Standard Time -> GMT-08 Pacific", Name(Zone("Pacific Standard Time")).StartsWith("GMT-08 Pacific"), Name(Zone("Pacific Standard Time")));
                var here = Call(console, "FromWindows", read);
                Console.WriteLine("      this Windows: " + here.GetType().GetMethod("Describe").Invoke(here, null));

                // ── the profile ──
                Console.WriteLine();
                Console.WriteLine("-- the profile (Account) --");
                var mine = (byte[])Call(profile, "Encrypt", Call(profile, "NewAccount", "Mehdi"));
                Check("0x194 bytes", mine.Length == 0x194, mine.Length.ToString());
                if (account != null && File.Exists(account))
                {
                    var theirs = File.ReadAllBytes(account);
                    Check("ours for \"Mehdi\" is the file Xenia loaded and signed into, byte for byte (" + account + ")", theirs.SequenceEqual(mine));
                }
                else Console.WriteLine("    (no --account: the comparison with a real file is skipped)");
                var plain = (byte[])Call(profile, "Decrypt", mine);
                Check("decrypts, and the gamertag reads", plain != null && (string)Call(profile, "GamertagOf", plain) == "Mehdi");
                var broken = (byte[])mine.Clone(); broken[0x30] ^= 1;
                Check("a damaged file does not decrypt", Call(profile, "Decrypt", broken) == null);

                var content = Path.Combine(root, "content");
                var xuid = (string)Call(profile, "Create", content, "Player One");
                Check("created: an E03 xuid, its Account where FindProfiles looks", xuid.StartsWith("E03") && File.Exists((string)Call(profile, "AccountPath", content, xuid)), xuid);
                var found = ((IEnumerable)Call(profile, "Find", content)).Cast<object>().ToList();
                Check("found again, with its gamertag", found.Count == 1 && (string)F(found[0], "Gamertag") == "Player One");
                Call(profile, "Rename", content, xuid, "Mehdi");
                found = ((IEnumerable)Call(profile, "Find", content)).Cast<object>().ToList();
                Check("renamed: the same file as one made for \"Mehdi\"", (string)F(found[0], "Gamertag") == "Mehdi"
                      && File.ReadAllBytes((string)Call(profile, "AccountPath", content, xuid)).SequenceEqual(mine));

                foreach (var (name, want) in new[] { ("mehdi", "mehdi"), ("Jean-Pierre_42", "Jean Pierre 42"), ("Élodie", "Elodie"), ("123", "Player"), ("a very long windows name", "a very long win"), ("", "Player"), ("__x", "x") })
                {
                    var got = (string)Call(profile, "GamertagFrom", name);
                    Check("the gamertag of \"" + name + "\" is \"" + want + "\"", got == want && (bool)Call(profile, "IsValidGamertag", got), got);
                }
                Check("\"9lives\", \"two  spaces\" and 16 characters are refused", !(bool)Call(profile, "IsValidGamertag", "9lives") && !(bool)Call(profile, "IsValidGamertag", "two  spaces") && !(bool)Call(profile, "IsValidGamertag", "abcdefghijklmnop"));

                // ── the TOML ──
                Console.WriteLine();
                Console.WriteLine("-- the TOML (the profile signed in at start) --");
                var fresh = Path.Combine(root, "fresh.toml");
                Call(profile, "SignInAtStart", fresh, xuid);
                Check("no file: one made, with [Profiles]", File.ReadAllText(fresh).Contains("[Profiles]") && (string)Call(profile, "SignedIn", fresh) == xuid, File.ReadAllText(fresh));
                if (toml != null && File.Exists(toml))
                {
                    var copy = Path.Combine(root, "copy.toml");
                    File.Copy(toml, copy);
                    Call(profile, "SignInAtStart", copy, xuid);
                    var a = File.ReadAllLines(toml); var b = File.ReadAllLines(copy);
                    var moved = Enumerable.Range(0, Math.Min(a.Length, b.Length)).Where(i => a[i] != b[i]).ToList();
                    Check("a real file: one line changed, the sign-in one", a.Length == b.Length && moved.Count == 1 && b[moved[0]].StartsWith("logged_profile_slot_0_xuid = \"" + xuid + "\""),
                          moved.Count + " line(s): " + string.Join(" | ", moved.Take(3).Select(i => b[i])));
                    Check("and read back", (string)Call(profile, "SignedIn", copy) == xuid);
                    Check("the same line ends as it did", (File.ReadAllText(toml).Contains("\r\n")) == (File.ReadAllText(copy).Contains("\r\n")));
                    var own = (IDictionary<string, string>)asm.GetType("LbIntegrations.Xenia.XeniaOptions", true).GetMethod("Own", flags).Invoke(null, new object[] { toml });
                    Console.WriteLine("      Xenia's own values: " + string.Join(", ", own.Select(kv => kv.Key + "=" + kv.Value)));
                    Check("Xenia's own read for every option (vsync true, scale 1, anti-aliasing none)", own["vsync"] == "true" && own["draw_resolution_scale"] == "1" && own["postprocess_antialiasing"] == "none",
                          string.Join(", ", own.Select(kv => kv.Key + "=" + kv.Value)));
                }
                else Console.WriteLine("    (no --toml: the real file's check is skipped)");

                // ── the command line ──
                Console.WriteLine();
                Console.WriteLine("-- the options at launch --");
                Call(settings, "WriteAll", new Dictionary<string, string> { ["draw_resolution_scale"] = "2", ["vsync"] = "false", ["gpu"] = "vulkan" });
                Call(settings, "WriteGame", "game-1", new Dictionary<string, string> { ["vsync"] = "true", ["postprocess_ffx_cas_additional_sharpness"] = "0,5" });
                var forGame = (List<string>)Call(settings, "Flags", Call(settings, "ForGame", "game-1"));
                Console.WriteLine("      " + string.Join(" ", forGame));
                Check("the game's over every game's; the scale as x and y; a comma made a dot",
                      string.Join(" ", forGame) == "--gpu=vulkan --draw_resolution_scale_x=2 --draw_resolution_scale_y=2 --vsync=true --postprocess_ffx_cas_additional_sharpness=0.5",
                      string.Join(" ", forGame));
                var other = (List<string>)Call(settings, "Flags", Call(settings, "ForGame", "game-2"));
                Check("another game: every game's only", string.Join(" ", other) == "--gpu=vulkan --draw_resolution_scale_x=2 --draw_resolution_scale_y=2 --vsync=false", string.Join(" ", other));
                var line = (string)Call(settings, "Append", "--fullscreen --vsync=false", forGame);
                Check("one the user typed stays his", line == "--fullscreen --vsync=false --gpu=vulkan --draw_resolution_scale_x=2 --draw_resolution_scale_y=2 --postprocess_ffx_cas_additional_sharpness=0.5", line);
                Call(settings, "WriteGame", "game-1", new Dictionary<string, string>());
                Check("a game with nothing set has no file", !File.Exists((string)Call(settings, "GamePath", "game-1")));
            }
            catch (Exception ex) { Console.WriteLine("  EXCEPTION: " + ex); _bad++; }
            finally
            {
                settings.GetField("DirOverride", flags).SetValue(null, null);
                try { Directory.Delete(dir, true); } catch { }
            }
            Console.WriteLine(_bad == 0 ? "\n  OK - a first install's profile, console and options are as Xenia makes them" : "\n  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
