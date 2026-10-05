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
                void Prepare(string system) { try { launch.GetMethod("PrepareGpu", Any).Invoke(null, new object[] { dir, system }); } catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; } }
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
                var old = (bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "cave3rd" }) && !(bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "hikaru" });
                Check("by board: old plugin for CV1000, new for Hikaru", old);
                Set("SetVideoPlugin", "new");
                Check("forced new: not even Gaelco gets the old one", !(bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "gaelco" }));
                Set("SetVideoPlugin", "old");
                Check("forced old: Naomi gets it too", (bool)launch.GetMethod("OldGpu", Any).Invoke(null, new object[] { "naomi" }));

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
    }
}
