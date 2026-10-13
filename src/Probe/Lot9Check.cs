// Lot 9, nothing beside the game: the folders a plugin sets when its emulator has none, and the files still beside a game moved
// by the pack's rule (docs\saves-lot9.md) - the emulator's own extensions at every launch, the generic ones only at the launch
// where the folder was found empty. Temp folders only, no emulator run.
//
//   Probe <Bsnes.dll> --lot9-bsnes
//   Probe <MelonDs.dll> --lot9-melonds

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Probe
{
    internal static class Lot9Check
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static int _bad;

        private static void Check(string what, bool ok, string detail = null)
        {
            if (!ok) _bad++;
            Console.WriteLine("  [" + (ok ? "ok" : "FAIL") + "] " + what + (detail != null ? " - " + detail : ""));
        }

        private static string Temp(string tag)
        {
            var d = Path.Combine(Path.GetTempPath(), "lbip-lot9-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static bool Done(string root)
        {
            try { Directory.Delete(root, true); } catch { }
            Console.WriteLine(_bad == 0 ? "  all good" : "  " + _bad + " FAILED");
            return _bad == 0;
        }

        public static bool Bsnes(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- bsnes: empty Path nodes set, the game's files moved --");
            var root = Temp("bsnes");
            try
            {
                var emu = Path.Combine(root, "bsnes"); var games = Path.Combine(root, "games");
                Directory.CreateDirectory(emu); Directory.CreateDirectory(games);
                var exe = Path.Combine(emu, "bsnes.exe"); File.WriteAllText(exe, "");
                var bml = Path.Combine(emu, "settings.bml");
                File.WriteAllText(bml, "Path\n  Games\n  Patches\n  Saves\n  Cheats\n  States\n  Screenshots: C:/mine/\n");
                var rom = Path.Combine(games, "Zelda.sfc");
                foreach (var f in new[] { "Zelda.sfc", "Zelda.srm", "Zelda.rtc", "Zelda.bsz", "Zelda.bsz.png", "Zelda.cht", "Other.srm" })
                    File.WriteAllText(Path.Combine(games, f), f);
                var t = asm.GetType("LbIntegrations.Bsnes.BsnesSavePaths", true);
                var empty = (HashSet<string>)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
                System.Threading.Thread.Sleep(30);
                var text = File.ReadAllText(bml);
                Check("the three empty nodes found and set, the user's screenshots folder kept",
                      empty.SetEquals(new[] { "Path/Saves", "Path/Cheats", "Path/States" }) && text.Contains("Screenshots: C:/mine/"), string.Join(", ", empty));
                Check("set absolute, with bsnes's trailing slash", text.Contains("  Saves: " + emu.Replace('\\', '/') + "/Saves/") && text.Contains("  States: " + emu.Replace('\\', '/') + "/States/"), text.Replace("\n", " | "));
                Check("LF kept, no BOM", !text.Contains("\r") && File.ReadAllBytes(bml)[0] != 0xEF);
                int moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { exe, rom, null });
                Check("the states (and picture) moved, the save, its clock and the cheats COPIED (another emulator may read them there)", moved == 5 && !File.Exists(Path.Combine(games, "Zelda.bsz")) && File.Exists(Path.Combine(games, "Zelda.srm"))
                      && File.Exists(Path.Combine(emu, "States", "Zelda.bsz")) && File.Exists(Path.Combine(emu, "States", "Zelda.bsz.png"))
                      && File.Exists(Path.Combine(emu, "Saves", "Zelda.srm")) && File.Exists(Path.Combine(emu, "Saves", "Zelda.rtc"))
                      && File.Exists(Path.Combine(emu, "Cheats", "Zelda.cht")), moved + " moved");
                Check("the ROM and another game's save stay", File.Exists(rom) && File.Exists(Path.Combine(games, "Other.srm")));

                // A later launch: nothing empty any more - bsnes's own .bsz moves, a generic .srm beside the game stays.
                var stamp = File.GetLastWriteTimeUtc(bml);
                empty = (HashSet<string>)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
                Check("a later launch: nothing set, settings.bml not rewritten", empty.Count == 0 && File.GetLastWriteTimeUtc(bml) == stamp);
                File.Delete(Path.Combine(games, "Zelda.srm")); File.WriteAllText(Path.Combine(games, "Zelda.srm"), "somebody's");
                File.WriteAllText(Path.Combine(games, "Zelda.bsz"), "again");
                File.WriteAllText(Path.Combine(emu, "States", "Zelda.bsz.png"), "kept");
                File.WriteAllText(Path.Combine(games, "Zelda.bsz.png"), "old picture");
                moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { exe, rom, null });
                Check("a later launch: the .srm beside the game left (its owner unknown)", File.ReadAllText(Path.Combine(games, "Zelda.srm")) == "somebody's");
                Check("a later launch: a .bsz already in States kept, the one beside the game left", File.ReadAllText(Path.Combine(emu, "States", "Zelda.bsz")) == "Zelda.bsz" && File.Exists(Path.Combine(games, "Zelda.bsz")));
                Check("never over a file already there (the picture)", File.ReadAllText(Path.Combine(emu, "States", "Zelda.bsz.png")) == "kept" && File.Exists(Path.Combine(games, "Zelda.bsz.png")));
                // ANOTHER game launched later (review 13/10): its .srm, written while bsnes wrote beside the games, moves too.
                var mario = Path.Combine(games, "Mario.sfc"); File.WriteAllText(mario, "rom");
                File.WriteAllText(Path.Combine(games, "Mario.srm"), "mario's");
                File.SetLastWriteTimeUtc(Path.Combine(games, "Mario.srm"), DateTime.UtcNow.AddDays(-30));
                moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { exe, mario, null });
                Check("another game, later: its .srm from before the folder was set copied", moved == 1 && File.ReadAllText(Path.Combine(emu, "Saves", "Mario.srm")) == "mario's");
                Check("the moment kept beside the install", File.Exists(Path.Combine(emu, "lbip-beside-game.ini")) && File.ReadAllText(Path.Combine(emu, "lbip-beside-game.ini")).Contains("Path/Saves="));
                // The copy made ONCE (review 13/10): bsnes's save deleted to start over, the old one beside the game never comes back.
                File.Delete(Path.Combine(emu, "Saves", "Mario.srm"));
                moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { exe, mario, null });
                Check("a copy never made twice (bsnes's own deleted: the old one does not come back)", moved == 0 && !File.Exists(Path.Combine(emu, "Saves", "Mario.srm")), moved + " brought");

                // A FRESH install (no settings.bml: bsnes never ran): the nodes set, no moment noted - a .srm beside a game is another's.
                var fresh = Path.Combine(root, "fresh"); Directory.CreateDirectory(fresh);
                var fexe = Path.Combine(fresh, "bsnes.exe"); File.WriteAllText(fexe, "");
                File.WriteAllText(Path.Combine(games, "Kirby.sfc"), "rom"); File.WriteAllText(Path.Combine(games, "Kirby.srm"), "snes9x's");
                File.SetLastWriteTimeUtc(Path.Combine(games, "Kirby.srm"), DateTime.UtcNow.AddDays(-30));
                t.GetMethod("Ensure", Any).Invoke(null, new object[] { fexe });
                t.GetMethod("Migrate", Any).Invoke(null, new object[] { fexe, Path.Combine(games, "Kirby.sfc"), null });
                Check("a fresh install: nothing noted, another emulator's .srm left beside the game", !File.Exists(Path.Combine(fresh, "lbip-beside-game.ini")) && File.Exists(Path.Combine(games, "Kirby.srm")) && !File.Exists(Path.Combine(fresh, "Saves", "Kirby.srm")));
                // A restore reading a file beside the game (LiteBox's Replace from another group's live save): never touched.
                File.WriteAllText(Path.Combine(games, "Luigi.sfc"), "rom"); File.WriteAllText(Path.Combine(games, "Luigi.bsz"), "src");
                t.GetMethod("Migrate", Any).Invoke(null, new object[] { exe, Path.Combine(games, "Luigi.sfc"), Path.Combine(games, "Luigi.bsz") });
                Check("a restore's own source never moved", File.Exists(Path.Combine(games, "Luigi.bsz")) && !File.Exists(Path.Combine(emu, "States", "Luigi.bsz")));
                // An install moved / another drive letter: a pack folder pointing nowhere is set again, no moment noted.
                File.WriteAllText(bml, "Path\n  Saves: Q:/gone/bsnes/Saves/\n  States: " + emu.Replace('\\', '/') + "/States/\n");
                var again = (HashSet<string>)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
                Check("a stale pack path set again to this install", again.SetEquals(new[] { "Path/Saves", "Path/Cheats", "Path/Screenshots" }) && File.ReadAllText(bml).Contains("Saves: " + emu.Replace('\\', '/') + "/Saves/"), string.Join(", ", again));
                // The user's own folder on a drive not mounted now (review 13/10): not the pack's shape, never replaced.
                File.WriteAllText(bml, "Path\n  Saves: Q:/Sync/Saves/\n  States: " + emu.Replace('\\', '/') + "/States/\n  Cheats: C:/c/\n  Screenshots: C:/s/\n");
                again = (HashSet<string>)t.GetMethod("Ensure", Any).Invoke(null, new object[] { exe });
                Check("a user's folder on an absent drive kept", again.Count == 0 && File.ReadAllText(bml).Contains("Saves: Q:/Sync/Saves/"), string.Join(", ", again));
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).Message); }
            return Done(root);
        }

        public static bool MelonDs(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine("-- melonDS: empty path keys of every window set, the game's files moved --");
            var root = Temp("melonds");
            try
            {
                var emu = Path.Combine(root, "melonDS"); var games = Path.Combine(root, "games");
                Directory.CreateDirectory(emu); Directory.CreateDirectory(games);
                var exe = Path.Combine(emu, "melonDS.exe"); File.WriteAllText(exe, "");
                var toml = Path.Combine(emu, "melonDS.toml");
                File.WriteAllText(toml, "[Instance0]\nSaveFilePath = \"\"\nSavestatePath = \"\"\n\n[Instance1]\nSaveFilePath = \"C:\\\\mine\"\n");
                var rom = Path.Combine(games, "Mario.nds");
                File.WriteAllBytes(rom, new byte[0x200]);
                foreach (var f in new[] { "Mario.sav", "Mario.ml1", "Mario.ml1.png", "Mario.ml8", "Mario.mch", "Other.sav" })
                    File.WriteAllText(Path.Combine(games, f), f);
                var layout = asm.GetType("LbIntegrations.MelonDs.MelonDsPaths", true).GetMethod("Resolve", Any).Invoke(null, new object[] { exe });
                var t = asm.GetType("LbIntegrations.MelonDs.MelonDsSavePaths", true);
                var empty = (HashSet<string>)t.GetMethod("Ensure", Any).Invoke(null, new object[] { layout });
                var text = File.ReadAllText(toml);
                Check("[Instance0]: the three keys found empty and set", empty.SetEquals(new[] { "SaveFilePath", "SavestatePath", "CheatFilePath" }), string.Join(", ", empty));
                Check("[Instance1]: its own folder kept, its empty ones set", text.Contains("C:\\\\mine") && System.Text.RegularExpressions.Regex.Matches(text, "SavestatePath").Count == 2, text.Replace("\n", " | "));
                var stateDir = (string)layout.GetType().GetProperty("StateDir")?.GetValue(layout) ?? (string)layout.GetType().GetField("StateDir")?.GetValue(layout);
                Check("the layout follows (states in savestates\\)", string.Equals(stateDir, Path.Combine(emu, "savestates"), StringComparison.OrdinalIgnoreCase), stateDir);
                int moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { layout, rom, null });
                Check("states (and picture) and cheats moved, the save copied", moved == 5 && File.Exists(Path.Combine(games, "Mario.sav"))
                      && File.Exists(Path.Combine(emu, "savestates", "Mario.ml1")) && File.Exists(Path.Combine(emu, "savestates", "Mario.ml1.png"))
                      && File.Exists(Path.Combine(emu, "savestates", "Mario.ml8")) && File.Exists(Path.Combine(emu, "cheats", "Mario.mch"))
                      && File.Exists(Path.Combine(emu, "saves", "Mario.sav")), moved + " moved");
                Check("the ROM and another game's save stay", File.Exists(rom) && File.Exists(Path.Combine(games, "Other.sav")));
                // A later launch: a .sav beside the game is somebody's - left; melonDS's own .ml2 moves.
                layout = asm.GetType("LbIntegrations.MelonDs.MelonDsPaths", true).GetMethod("Resolve", Any).Invoke(null, new object[] { exe });
                empty = (HashSet<string>)t.GetMethod("Ensure", Any).Invoke(null, new object[] { layout });
                System.Threading.Thread.Sleep(30);
                File.Delete(Path.Combine(games, "Mario.sav")); File.WriteAllText(Path.Combine(games, "Mario.sav"), "somebody's"); File.WriteAllText(Path.Combine(games, "Mario.ml2"), "s2");
                moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { layout, rom, null });
                Check("a later launch: nothing set, the .sav written since left, the .ml2 moved", empty.Count == 0 && moved == 1 && File.Exists(Path.Combine(games, "Mario.sav"))
                      && File.Exists(Path.Combine(emu, "savestates", "Mario.ml2")), moved + " moved");
                // Another game later: its .sav from before the folder was set moves (review 13/10).
                var zelda = Path.Combine(games, "Zelda.nds"); File.WriteAllBytes(zelda, new byte[0x200]);
                File.WriteAllText(Path.Combine(games, "Zelda.sav"), "zelda's"); File.SetLastWriteTimeUtc(Path.Combine(games, "Zelda.sav"), DateTime.UtcNow.AddDays(-30));
                moved = (int)t.GetMethod("Migrate", Any).Invoke(null, new object[] { layout, zelda, null });
                Check("another game, later: its .sav from before copied", moved == 1 && File.ReadAllText(Path.Combine(emu, "saves", "Zelda.sav")) == "zelda's");
            }
            catch (Exception ex) { Check("ran", false, (ex.InnerException ?? ex).Message); }
            return Done(root);
        }
    }
}
