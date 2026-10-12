// --expected: where a plugin's restore writes, for the emulator's config key set relative, absolute and empty
// (docs\saves-two-installs.md §5). A relative value is from the emulator's own folder - its current directory under
// LaunchBox (measured 12/10) -, never the current directory of the host: the probe moves its own elsewhere first, so a
// value read from it shows.
//
//   Probe <Bsnes.dll> --expected       (ares: Probe <Ares.dll> --ares-expected)

using System;
using System.IO;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class ExpectedCheck
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static object F(object o, string n) => o?.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o)
                                                        ?? o?.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

        private static bool Same(string a, string b) => string.Equals(a?.TrimEnd('\\'), b?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        public static bool Run(Assembly asm)
        {
            _bad = 0;
            var name = asm.GetName().Name;
            var root = Path.Combine(Path.GetTempPath(), "lbip-expected-" + Guid.NewGuid().ToString("N"));
            var emu = Path.Combine(root, "emu"); var games = Path.Combine(root, "games");
            Directory.CreateDirectory(emu); Directory.CreateDirectory(games);
            var cwd = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = root;
                switch (name)
                {
                    case "Bsnes": Bsnes(asm, root, emu, games); break;
                    default: Console.WriteLine("  no expected-folder cases for " + name); return false;
                }
            }
            finally { Environment.CurrentDirectory = cwd; try { Directory.Delete(root, true); } catch { } }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        // A config file rewritten for each case: a new size and date, so a cache kept on them reads it again.
        private static int _stamp;
        private static void Write(string path, string text)
        {
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1).AddMinutes(++_stamp));
        }

        private static void Bsnes(Assembly asm, string root, string emu, string games)
        {
            Console.WriteLine("-- bsnes: Path/Saves and Path/States as bsnes resolves them --");
            var exe = Path.Combine(emu, "bsnes.exe"); File.WriteAllBytes(exe, Array.Empty<byte>());
            var rom = Path.Combine(games, "Zelda.sfc"); File.WriteAllBytes(rom, new byte[] { 0 });
            var bml = Path.Combine(emu, "settings.bml");
            var foldersFor = asm.GetType("LbIntegrations.Bsnes.BsnesPlugin", true).GetMethod("FoldersFor", Any);
            (string Save, string State) At(string saves, string states)
            {
                Write(bml, "Path\n  Saves: " + saves + "\n  States: " + states + "\n");
                var f = foldersFor.Invoke(null, new object[] { exe, rom });
                return ((string)F(f, "SaveDir"), (string)F(f, "StateDir"));
            }
            var rel = At("Saves/", "States/");
            Check("relative \"Saves/\" → under bsnes's folder", Same(rel.Save, Path.Combine(emu, "Saves")), rel.Save);
            Check("relative \"States/\" → under bsnes's folder", Same(rel.State, Path.Combine(emu, "States")), rel.State);
            var abs = At(Path.Combine(root, "s").Replace('\\', '/') + "/", Path.Combine(root, "st").Replace('\\', '/') + "/");
            Check("absolute → as it is", Same(abs.Save, Path.Combine(root, "s")) && Same(abs.State, Path.Combine(root, "st")), abs.Save + " | " + abs.State);
            var none = At("", "");
            Check("empty → beside the game (bsnes's default)", Same(none.Save, games) && Same(none.State, games), none.Save + " | " + none.State);
        }
    }
}
