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
                    case "MelonDs": MelonDs(asm, root, emu, games); break;
                    case "SuperZsnes": SuperZsnes(asm, root, emu, games); break;
                    case "RaSnes9x": RaSnes9x(asm, root, emu, games); break;
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

        private static void RaSnes9x(Assembly asm, string root, string emu, string games)
        {
            Console.WriteLine("-- RASnes9x: Dir:SRAM and Dir:Savestates as Snes9x resolves them --");
            var exe = Path.Combine(emu, "RASnes9x.exe"); File.WriteAllBytes(exe, Array.Empty<byte>());
            var rom = Path.Combine(games, "Zelda.sfc"); File.WriteAllBytes(rom, new byte[] { 0 });
            var conf = Path.Combine(emu, "snes9x.conf");
            var foldersFor = asm.GetType("LbIntegrations.RaSnes9x.RaSnes9xPlugin", true).GetMethod("FoldersFor", Any);
            (string Save, string State) Now()
            {
                var f = foldersFor.Invoke(null, new object[] { exe, rom });
                return ((string)F(f, "SaveDir"), (string)F(f, "StateDir"));
            }
            (string Save, string State) At(string sram, string states)
            {
                Write(conf, "[Settings\\Win\\Files]\nDir:SRAM = " + sram + "\nDir:Savestates = " + states + "\n");
                return Now();
            }
            var unset = Now();
            Check("no snes9x.conf yet → .\\Saves under its folder (Snes9x's default)", Same(unset.Save, Path.Combine(emu, "Saves")) && Same(unset.State, Path.Combine(emu, "Saves")), unset.Save + " | " + unset.State);
            var rel = At(".\\Battery", ".\\States");
            Check("relative → under its folder", Same(rel.Save, Path.Combine(emu, "Battery")) && Same(rel.State, Path.Combine(emu, "States")), rel.Save + " | " + rel.State);
            var abs = At(Path.Combine(root, "s"), Path.Combine(root, "st"));
            Check("absolute → as it is", Same(abs.Save, Path.Combine(root, "s")) && Same(abs.State, Path.Combine(root, "st")), abs.Save + " | " + abs.State);
            var none = At("", "");
            Check("empty → beside the game", Same(none.Save, games) && Same(none.State, games), none.Save + " | " + none.State);
        }

        private static void SuperZsnes(Assembly asm, string root, string emu, string games)
        {
            Console.WriteLine("-- SUPER ZSNES: the first session, its settings file not written yet --");
            var exe = Path.Combine(emu, "SUPERZSNES.exe"); File.WriteAllBytes(exe, Array.Empty<byte>());
            var rom = Path.Combine(games, "Zelda.sfc"); File.WriteAllBytes(rom, new byte[] { 0 });
            var settings = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesSettings", true);
            var ini = Path.Combine(root, "pack-settings.ini");
            settings.GetField("PathOverride", Any).SetValue(null, ini);
            var foldersFor = asm.GetType("LbIntegrations.SuperZsnes.SuperZsnesPlugin", true).GetMethod("FoldersFor", Any);
            try
            {
                (string Save, string State) At()
                {
                    var f = foldersFor.Invoke(null, new object[] { exe, rom });
                    return ((string)F(f, "SaveDir"), (string)F(f, "StateDir"));
                }
                var bare = At();
                Check("no in-process plugin deployed → beside the ROM (the emulator's default)", Same(bare.Save, games) && Same(bare.State, games), bare.Save + " | " + bare.State);
                File.WriteAllBytes(Path.Combine(emu, "winhttp.dll"), Array.Empty<byte>());
                Directory.CreateDirectory(Path.Combine(emu, "BepInEx", "plugins"));
                File.WriteAllBytes(Path.Combine(emu, "BepInEx", "plugins", "SuperZsnes.BepInEx.dll"), Array.Empty<byte>());
                Write(ini, "# none\n");
                var mod = At();
                Check("deployed, data-folders on (default) → <exe>\\saves and <exe>\\states before the first exit", Same(mod.Save, Path.Combine(emu, "saves")) && Same(mod.State, Path.Combine(emu, "states")), mod.Save + " | " + mod.State);
                Write(ini, "plugin.data-folders=off\n");
                var off = At();
                Check("... plugin.data-folders off → beside the ROM", Same(off.Save, games), off.Save);
                Write(ini, "plugin.bepinex=off\n");
                var nobep = At();
                Check("... the in-process plugin off → beside the ROM", Same(nobep.Save, games), nobep.Save);
            }
            finally { settings.GetField("PathOverride", Any).SetValue(null, null); }
        }

        private static void MelonDs(Assembly asm, string root, string emu, string games)
        {
            Console.WriteLine("-- melonDS: [Instance0] SaveFilePath and SavestatePath as melonDS resolves them --");
            var exe = Path.Combine(emu, "melonDS.exe"); File.WriteAllBytes(exe, Array.Empty<byte>());
            var toml = Path.Combine(emu, "melonDS.toml");
            var resolve = asm.GetType("LbIntegrations.MelonDs.MelonDsPaths", true).GetMethod("Resolve", Any);
            (string Save, string State, object Layout) At(string saves, string states)
            {
                Write(toml, "[Instance0]\nSaveFilePath = \"" + saves + "\"\nSavestatePath = \"" + states + "\"\n");
                var l = resolve.Invoke(null, new object[] { exe });
                return ((string)F(l, "SaveDir"), (string)F(l, "StateDir"), l);
            }
            var rel = At("my saves", "my states");
            Check("relative \"my saves\" → under melonDS's folder", Same(rel.Save, Path.Combine(emu, "my saves")), rel.Save);
            Check("relative \"my states\" → under melonDS's folder", Same(rel.State, Path.Combine(emu, "my states")), rel.State);
            var abs = At(Path.Combine(root, "s").Replace('\\', '/'), Path.Combine(root, "st").Replace('\\', '/'));
            Check("absolute → as it is", Same(abs.Save, Path.Combine(root, "s")) && Same(abs.State, Path.Combine(root, "st")), abs.Save + " | " + abs.State);
            var none = At("", "");
            Check("empty → none configured (beside the rom)", none.Save == null && none.State == null, none.Save + " | " + none.State);
            // A state restored with nothing configured and no rom: the install's savestates\, not its saves\ (review 12/10).
            var restoreDirIn = asm.GetType("LbIntegrations.MelonDs.MelonDsPlugin", true).GetMethod("RestoreDirIn", Any);
            var state = new Unbroken.LaunchBox.Plugins.Data.GameSaveState { GameId = "g" };
            var dir = (string)restoreDirIn.Invoke(null, new object[] { none.Layout, state, null });
            Check("a state with no folder and no rom → savestates\\", Same(dir, Path.Combine(emu, "savestates")), dir);
            var save = new Unbroken.LaunchBox.Plugins.Data.GameSaveGame { GameId = "g" };
            var sdir = (string)restoreDirIn.Invoke(null, new object[] { none.Layout, save, Path.Combine(games, "mk.nds") });
            Check("a save with no folder → beside the rom", Same(sdir, games), sdir);
        }
    }
}
