// --demul-gpu <gpuDX11.ini>: the picture settings of the Nixx window written into Demul's video plugin .ini as a launch
// writes them (DemulLaunch.PrepareGpu) - in a temp folder, from the gpuDX11.ini the archive ships: the defaults, the
// settings changed, the plugin by board and forced, every other line and the byte-order mark kept. Writes only there.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class DemulCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
        }

        public static bool Gpu(Assembly asm, string shipped)
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-demul-gpu-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                var settings = asm.GetType("LbIntegrations.Demul.DemulSettings", true);
                settings.GetField("PathOverride", Any).SetValue(null, Path.Combine(dir, "settings.ini"));
                var launch = asm.GetType("LbIntegrations.Demul.DemulLaunch", true);
                void Prepare(string system, string game = null) { try { launch.GetMethod("PrepareGpu", Any).Invoke(null, new object[] { dir, system, game }); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
                void Set(string m, object v) => settings.GetMethod(m, Any).Invoke(null, new[] { v });
                string Value(string file, string key) => File.ReadAllLines(Path.Combine(dir, file)).Select(l => l.Split('=')).Where(p => p.Length == 2 && p[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => p[1].Trim()).FirstOrDefault();

                var original = File.ReadAllLines(shipped);
                File.Copy(shipped, Path.Combine(dir, "gpuDX11.ini"));
                var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

                Console.WriteLine("  the defaults, a Naomi game:");
                Prepare("naomi");
                Check("UseFullscreen = 1", Value("gpuDX11.ini", "UseFullscreen") == "1");
                Check("aspect = 1 (4:3)", Value("gpuDX11.ini", "aspect") == "1");
                Check("Vsync = 0", Value("gpuDX11.ini", "Vsync") == "0");
                Check("scaling = 1", Value("gpuDX11.ini", "scaling") == "1");
                Check("Width x Height = the screen's (" + screen.Width + "x" + screen.Height + ")", Value("gpuDX11.ini", "Width") == screen.Width.ToString() && Value("gpuDX11.ini", "Height") == screen.Height.ToString(),
                      Value("gpuDX11.ini", "Width") + "x" + Value("gpuDX11.ini", "Height"));
                var after = File.ReadAllLines(Path.Combine(dir, "gpuDX11.ini"));
                var changedKeys = new[] { "UseFullscreen", "Width", "Height" };
                var others = original.Where(l => !changedKeys.Any(k => l.TrimStart().StartsWith(k + " ", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith(k + "=", StringComparison.OrdinalIgnoreCase))).ToList();
                Check("every other line kept, in its order", others.SequenceEqual(after.Where(l => !changedKeys.Any(k => l.TrimStart().StartsWith(k + " ", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith(k + "=", StringComparison.OrdinalIgnoreCase)))),
                      after.Length + " lines vs " + original.Length);
                var head = File.ReadAllBytes(Path.Combine(dir, "gpuDX11.ini")).Take(3).ToArray();
                Check("still UTF-8 with its byte-order mark", head.Length == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF);

                Console.WriteLine("  settings changed: 16:9, vsync, Gaelco 3x, windowed:");
                Set("SetAspect", 2); Set("SetVSync", true); Set("SetGaelcoScaling", 3); Set("SetFullscreen", false);
                Prepare("naomi");
                Check("aspect = 2, Vsync = 1, UseFullscreen = 0", Value("gpuDX11.ini", "aspect") == "2" && Value("gpuDX11.ini", "Vsync") == "1" && Value("gpuDX11.ini", "UseFullscreen") == "0");
                Check("scaling stays 1 off Gaelco", Value("gpuDX11.ini", "scaling") == "1");
                Prepare("gaelco");
                Check("a Gaelco game: the OLD plugin's file, gpuDX11old.ini, scaling = 3", File.Exists(Path.Combine(dir, "gpuDX11old.ini")) && Value("gpuDX11old.ini", "scaling") == "3");
                var old = (bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "cave3rd", null }) && !(bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "hikaru", null });
                Check("by board: old plugin for CV1000, new for Hikaru", old);
                Set("SetVideoPlugin", "new");
                Check("forced new: not even Gaelco gets the old one", !(bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "gaelco", null }));
                Set("SetVideoPlugin", "old");
                Check("forced old: Naomi gets it too", (bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "naomi", null }));

                Console.WriteLine("  a game's own options (games\\<id>.ini) win over every game's:");
                Set("SetVideoPlugin", "auto");
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "game-1", "Aspect", "0" });
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "game-1", "VSync", "false" });
                Prepare("naomi", "game-1");
                Check("its aspect 0 and vsync off, every game's 16:9 and vsync on elsewhere", Value("gpuDX11.ini", "aspect") == "0" && Value("gpuDX11.ini", "Vsync") == "0");
                Prepare("naomi", "game-2");
                Check("another game: every game's (aspect 2, vsync 1)", Value("gpuDX11.ini", "aspect") == "2" && Value("gpuDX11.ini", "Vsync") == "1");
                settings.GetMethod("SetGameValue", Any).Invoke(null, new object[] { "game-1", "Aspect", null });
                Check("a value taken away: it follows every game's again", (int)settings.GetMethod("AspectFor", Any).Invoke(null, new object[] { "game-1" }) == 2);

                Console.WriteLine("  the DEmul team's compatibility list (embedded):");
                var compat = asm.GetType("LbIntegrations.Demul.DemulCompat", true);
                var all = (System.Collections.IDictionary)compat.GetProperty("All", Any).GetValue(null);
                Console.WriteLine("    " + all.Count + " sets");
                object E(string n) => compat.GetMethod("Of", Any).Invoke(null, new object[] { @"C:\roms\" + n + ".zip" });
                string P(object e, string m) => e?.GetType().GetProperty(m)?.GetValue(e)?.ToString() ?? e?.GetType().GetField(m)?.GetValue(e)?.ToString();
                Check("over 250 sets read", all.Count > 250, all.Count.ToString());
                Check("18wheelr: Playable, its note: motorboard unemulated", P(E("18wheelr"), "Verdict") == "Playable" && P(E("18wheelr"), "Notes") == "motorboard unemulated", P(E("18wheelr"), "Verdict"));
                Check("capsnk: Works, Naomi cartridge system, 2 players", P(E("capsnk"), "Verdict") == "Works" && P(E("capsnk"), "Section") == "Naomi cartridge system" && P(E("capsnk"), "Players") == "2");
                Check("derbyo2k: Boots (link unemulated), wants a US BIOS", P(E("derbyo2k"), "Verdict") == "Boots" && P(E("derbyo2k"), "Bios") == "Usa");
                Check("vf4cart: Naomi 2 cartridge system", P(E("vf4cart"), "Section") == "Naomi 2 cartridge system");
                Check("a set not in it: null", E("notaset") == null);
                Console.WriteLine("  the import settings:");
                Check("sorting on by default, for every board", ((string[])settings.GetField("Boards", Any).GetValue(null)).All(b => (bool)settings.GetMethod("SortImport", Any).Invoke(null, new object[] { b })));
                Set("SetUnknownSets", "drop");
                Check("unknown sets: drop, read back", (string)settings.GetProperty("UnknownSets", Any).GetValue(null) == "drop");
                Console.WriteLine("  settings.ini:\n    " + string.Join("\n    ", File.ReadAllLines(Path.Combine(dir, "settings.ini"))));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " failed");
            return _bad == 0;
        }

        /// <summary>--demul-shot <png> --rom <set or disc> --platform <platform>: a game's window drawn off screen (a stub game).</summary>
        public static bool Shot(Assembly asm, string png, string rom, string platform)
        {
            var settings = asm.GetType("LbIntegrations.Demul.DemulSettings", true);
            settings.GetField("PathOverride", Any).SetValue(null, Path.Combine(Path.GetTempPath(), "lbip-demul-shot-settings.ini"));
            var game = StubGame.With(StubGame.Create("demul-shot", Path.GetFileNameWithoutExtension(rom), rom), "Platform", platform);
            var formType = asm.GetType("LbIntegrations.Demul.DemulGameForm", true);
            using var form = (System.Windows.Forms.Form)Activator.CreateInstance(formType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { new Unbroken.LaunchBox.Plugins.Data.IGame[] { game } }, null);
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual; form.Location = new System.Drawing.Point(-4000, 0);
            form.Show(); System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(300); System.Windows.Forms.Application.DoEvents();
            using var bmp = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(png);
            Console.WriteLine("  " + png);
            return true;
        }
    }
}