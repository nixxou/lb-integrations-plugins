// What a launch writes: the game's options and the pack's free hotkeys into snes9x.conf, and a .7z game unpacked for Snes9x.
//
// THE OPTIONS (RaSnes9xSettings.All) go into [Display\Win] before every launch - Snes9x reads the file when it starts
// and writes it back whole when it quits (RaSnes9xConf), so a key the plugin manages is the plugin's: changed in
// Snes9x's own menus, it comes back to the Nixx window's value at the next launch. Every other key - the pads, the
// hotkeys, the sound, the folders - is the user's: only an UNASSIGNED hotkey is ever bound, below, and only an EMPTY
// save or state folder (beside the game) is set to ".\Saves" (ForceSaveFolders), and with RetroAchievements on the game
// is put first in the recent games, loaded from there once logged in (RaSnes9xLateLoad).
//
// THE HOTKEYS, the pack's keys (Mehdi, 07/10) where Snes9x lets them be bound ([Controls\Win\Hotkeys], "Key:<name> =
// F2" + "Mods:<name> = none", wconfig.cpp ADD/ADDN): F2 SlotSave, F4 SlotLoad (the CURRENT slot, kept in
// [Settings\Win] CurrentSaveSlot), F6 SlotMinus, F7 SlotPlus - all four Unassigned in Snes9x (wsnes9x.cpp, CustomKeys).
// Each is bound only while it is Unassigned and no pad button or other hotkey of the user's holds the key. Snes9x fires
// EVERY hotkey matching a key (wsnes9x.cpp, the hotkey ifs of WM_KEYDOWN are independent), and its defaults put "load
// slot 1 / 3 / 5 / 6" on F2 / F4 / F6 / F7: such a LoadSlotN still on its default is set Unassigned when its key is
// taken - else F2 would save AND load. Shift+F1..F10 (save slot N) and F1, F3, F5, F8..F10 stay.
// NOT BINDABLE: Esc is never a hotkey (wsnes9x.cpp: "if(!(wParam == 0 || wParam == VK_ESCAPE))" - Esc shows or hides
// the menu bar); full screen has no hotkey, only the Alt+Enter accelerator (rsrc\snes9x.rc) - F11 is Snes9x's "load
// from a dialog" and stays so. Quitting is the window's close (the Exit script), as QuitS9X would do (WM_CLOSE).
//
//   Fullscreen  Fullscreen:Enabled, and Fullscreen:EmulateFullscreen = TRUE (a borderless window over the screen)
//   Aspect      Stretch:Enabled = TRUE; Stretch:MaintainAspectRatio; Stretch:AspectRatioBaseWidth 299 (4:3) or 256 (8:7)
//   Integer     Stretch:IntegerScaling        Smooth  Stretch:BilinearFilter
//   VSync       Vsync                         Output  OutputMethod 1 Direct3D, 2 OpenGL, 0 DirectDraw
//
// A .7z GAME: Snes9x opens .zip and .jma itself (memmap.cpp), not .7z. Its ROM is unpacked to
// %TEMP%\lbip-rasnes9x\<archive>\ UNDER THE ARCHIVE'S NAME (Lylat.7z holding "Star Fox (USA).sfc" runs as Lylat.sfc),
// so its .srm and its states carry the same name a zip would give them, and a game moved from .7z to .zip keeps them.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xLaunch
    {
        public const string ExecutableName = "RASnes9x.exe";
        private const string Display = "Display\\Win";

        /// <summary>The extensions of a SNES ROM inside an archive.</summary>
        internal static readonly string[] RomExtensions = { ".sfc", ".smc", ".swc", ".fig", ".bs", ".st", ".bin" };

        public static bool IsRaSnes9xExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>A RASnes9x running from that folder: an edit of snes9x.conf now would be lost when it quits.</summary>
        public static bool Running(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)))
                    using (p)
                        try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        /// <summary>The game's options written into snes9x.conf. Never throws: a launch with the file as it was beats none.</summary>
        public static void PrepareConf(string exe, string gameId, string rom)
        {
            try
            {
                var conf = RaSnes9xConf.Load(RaSnes9xConf.Of(exe));
                ForceSaveFolders(conf, exe);
                bool full = RaSnes9xSettings.BoolFor(gameId, "Fullscreen");
                conf.SetBool(Display, "Fullscreen:Enabled", full);
                if (full) conf.SetBool(Display, "Fullscreen:EmulateFullscreen", true);

                var aspect = RaSnes9xSettings.For(gameId, "Aspect");
                conf.SetBool(Display, "Stretch:Enabled", true);
                conf.SetBool(Display, "Stretch:MaintainAspectRatio", aspect != "stretch");
                if (aspect != "stretch") conf.Set(Display, "Stretch:AspectRatioBaseWidth", aspect == "8:7" ? "256" : "299");
                conf.SetBool(Display, "Stretch:IntegerScaling", RaSnes9xSettings.BoolFor(gameId, "IntegerScaling"));
                conf.SetBool(Display, "Stretch:BilinearFilter", RaSnes9xSettings.BoolFor(gameId, "Smooth"));
                conf.SetBool(Display, "Vsync", RaSnes9xSettings.BoolFor(gameId, "VSync"));
                var output = RaSnes9xSettings.For(gameId, "Output");
                conf.Set(Display, "OutputMethod", output == "opengl" ? "2" : output == "ddraw" ? "0" : "1");
                BindHotkeys(conf);

                bool changed = conf.Changed;
                conf.SaveIfChanged();
                // The game's files beside it brought in only now that Snes9x will read the folders from its file (review 13/10:
                // moved before a save that failed, they were out of Snes9x's sight while it kept writing beside the game).
                BringBesideTheGame(conf, exe, rom);
                Log.Info("snes9x.conf: " + (changed ? "written" : "already as wanted") + " - " + (full ? "full screen" : "windowed") + ", " + aspect
                         + (RaSnes9xSettings.BoolFor(gameId, "IntegerScaling") ? ", integer" : "") + ", " + output);
            }
            catch (Exception ex) { Log.Warn("snes9x.conf could not be written - the game starts with it as it was", ex); }
        }

        private const string Files = "Settings\\Win\\Files";

        /// <summary>The folder the pack gives an EMPTY Dir:SRAM / Dir:Savestates (Mehdi, 12/10): Snes9x's own default.</summary>
        internal const string ForcedDir = ".\\Saves";

        /// <summary>Snes9x's folders the pack gives a value when they are EMPTY (Snes9x's "beside the ROM"; for a .7z the %TEMP%
        /// folder it is unpacked to, which the next unpacking empties - RomFor): the saves and states (Mehdi, 12/10), and since
        /// lot 9 (13/10) the screenshots, movies, SPC dumps and cheats too - Snes9x's own defaults. A folder set is the user's.</summary>
        internal static readonly (string Key, string Folder)[] Folders =
        {
            ("Dir:SRAM", ForcedDir), ("Dir:Savestates", ForcedDir), ("Dir:Screenshots", ".\\Screenshots"),
            ("Dir:Movies", ".\\Movies"), ("Dir:SPCs", ".\\SPCs"), ("Dir:Cheats", ".\\Cheats"),
        };

        /// <summary>Every EMPTY folder of <see cref="Folders"/> set to its default - present and empty in snes9x.conf means
        /// Snes9x has been writing beside the games with it: the moment is noted FIRST (LbipBesideGame), and without it the
        /// setting is left as it is (every other game's save would be stranded beside it).</summary>
        private static void ForceSaveFolders(RaSnes9xConf conf, string exe)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            foreach (var (key, folder) in Folders)
            {
                var v = conf.Get(Files, key);
                if (v == null || !string.IsNullOrWhiteSpace(v)) continue;     // missing = Snes9x's default already; set = the user's
                if (!LbIntegrations.Lbip.LbipBesideGame.Note(exeDir, key)) { Log.Warn("snes9x.conf: " + key + " left empty - the moment Snes9x stopped writing beside the games could not be noted"); continue; }
                conf.Set(Files, key, folder);
                Log.Info("snes9x.conf: " + key + " was empty (beside the game) - set to " + folder);
            }
        }

        /// <summary>What is still beside the game brought into Snes9x's folders, by the pack's rule (docs\saves-lot9.md): Snes9x's
        /// own files MOVED at every launch - the states .000-.009 with their pictures and the undo .oops, the movies .smv; the
        /// generic ones - the save .srm and its clock .rtc, the cheats .cht, also written under that name by bsnes, SUPER ZSNES
        /// and RetroArch - COPIED when last written before the moment their folder was set (LbipBesideGame): Snes9x read them as
        /// the game's own until then, another emulator may still read them there. A folder set before 13/10 has no moment noted,
        /// and its generic files beside the games stay. Never over a file already in the folder.</summary>
        private static void BringBesideTheGame(RaSnes9xConf conf, string exe, string rom)
        {
            if (string.IsNullOrWhiteSpace(rom)) return;
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            string DirOf(string key)
            {
                var v = conf.Get(Files, key);
                // Present and EMPTY: Snes9x still writes beside the game (its moment could not be noted, the folder left so) -
                // nothing is taken from there, or each session's files would be carried where Snes9x does not look (review 13/10).
                if (v != null && string.IsNullOrWhiteSpace(v)) return null;
                var folder = Folders.First(f => f.Key == key).Folder;
                return LbIntegrations.Saves.ExpectedDir.OfConfig(v ?? folder, exeDir);
            }
            try
            {
                var name = Path.GetFileNameWithoutExtension(rom);
                foreach (var from in BesideTheGame(rom).Where(Directory.Exists))
                    foreach (var file in Directory.GetFiles(from, name + ".*"))
                    {
                        var ext = file.Substring(Path.Combine(from, name).Length);   // ".srm", ".000", ".000.png"
                        string key = null; bool copy = false;
                        // The %TEMP% unpack folder is the plugin's own: what Snes9x left there is Snes9x's - moved, whatever its date.
                        bool unpacked = string.Equals(from.TrimEnd('\\'), UnpackDir(Path.GetFullPath(rom)).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                        bool Before(string setting) => unpacked || LbIntegrations.Lbip.LbipBesideGame.WrittenBefore(file, LbIntegrations.Lbip.LbipBesideGame.Since(exeDir, setting));
                        // Snes9x's ten slots are .000-.009 - not any three digits (a split archive's Game.012 is no state).
                        if (System.Text.RegularExpressions.Regex.IsMatch(ext, @"^\.(00\d|oops)(\.png)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) key = "Dir:Savestates";
                        else if (string.Equals(ext, ".smv", StringComparison.OrdinalIgnoreCase)) key = "Dir:Movies";
                        else if ((string.Equals(ext, ".srm", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".rtc", StringComparison.OrdinalIgnoreCase))
                                 && Before("Dir:SRAM")) { key = "Dir:SRAM"; copy = true; }
                        else if (string.Equals(ext, ".cht", StringComparison.OrdinalIgnoreCase)
                                 && Before("Dir:Cheats")) { key = "Dir:Cheats"; copy = true; }
                        if (key == null) continue;
                        if (unpacked) copy = false;
                        LbIntegrations.Lbip.LbipBesideGame.Bring(file, DirOf(key), copy, m => Log.Info(m), installDir: exeDir);
                    }
            }
            catch (Exception ex) { Log.Warn("the files beside the game could not all be brought to Snes9x's folders", ex); }
        }
        /// <summary>Where an empty folder setting had Snes9x write: beside the ROM, and for a .7z the folder it is unpacked to.</summary>
        private static IEnumerable<string> BesideTheGame(string rom)
        {
            var full = Path.GetFullPath(rom);
            yield return Path.GetDirectoryName(full);
            if (string.Equals(Path.GetExtension(full), ".7z", StringComparison.OrdinalIgnoreCase)) yield return UnpackDir(full);
        }

        /// <summary>The folder a .7z is unpacked to: one per archive PATH, two archives of the same name never share it.</summary>
        internal static string UnpackDir(string rom)
        {
            string hash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                hash = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(Path.GetTempPath(), "lbip-rasnes9x", Path.GetFileNameWithoutExtension(rom) + "-" + hash);
        }

        private const string HotkeySection = "Controls\\Win\\Hotkeys";

        /// <summary>The pack's keys, Snes9x's hotkey names (Mehdi, 07/10).</summary>
        internal static readonly (string Name, string Key)[] Hotkeys =
        {
            ("SlotSave", "F2"), ("SlotLoad", "F4"), ("SlotMinus", "F6"), ("SlotPlus", "F7"),
        };

        /// <summary>Snes9x's own defaults on those keys (wsnes9x.cpp, CustomKeys: Load[1], [3], [5], [6] = VK_F2, F4, F6, F7
        /// with no modifier): given up when still so, since Snes9x would fire both.</summary>
        private static readonly Dictionary<string, string> DefaultOnKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["F2"] = "LoadSlot1", ["F4"] = "LoadSlot3", ["F6"] = "LoadSlot5", ["F7"] = "LoadSlot6",
        };

        private static bool NoMods(string mods) => string.IsNullOrWhiteSpace(mods) || string.Equals(mods.Trim(), "none", StringComparison.OrdinalIgnoreCase);

        /// <summary>The pack's hotkeys bound where Snes9x has them Unassigned (a missing line is Snes9x's default:
        /// Unassigned for these four), and never over a key the user put on something else.</summary>
        private static void BindHotkeys(RaSnes9xConf conf)
        {
            var entries = conf.Entries(HotkeySection);
            var pads = conf.Entries("Controls\\Win");
            string Value(string key) => entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
            foreach (var (name, key) in Hotkeys)
            {
                var now = Value("Key:" + name);
                if (!string.IsNullOrWhiteSpace(now) && !string.Equals(now, "Unassigned", StringComparison.OrdinalIgnoreCase)) continue;

                DefaultOnKey.TryGetValue(key, out var dflt);
                var dfltKey = Value("Key:" + dflt);
                bool dfltStill = dfltKey == null || (string.Equals(dfltKey, key, StringComparison.OrdinalIgnoreCase) && NoMods(Value("Mods:" + dflt)));
                // Anyone else on the key with no modifier: a hotkey ("Key:X = F2", "Mods:X = none") or a pad button.
                var taken = entries.Where(e => e.Key.StartsWith("Key:", StringComparison.OrdinalIgnoreCase) && string.Equals(e.Value, key, StringComparison.OrdinalIgnoreCase)
                                               && NoMods(Value("Mods:" + e.Key.Substring(4))))
                                   .Select(e => e.Key.Substring(4))
                                   .Where(n => !(dfltStill && string.Equals(n, dflt, StringComparison.OrdinalIgnoreCase)))
                                   .Concat(pads.Where(e => string.Equals(e.Value, key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Key))
                                   .FirstOrDefault();
                if (taken != null) { Log.Info("snes9x.conf: " + name + " left Unassigned - " + key + " is already " + taken + "'s"); continue; }

                conf.Set(HotkeySection, "Key:" + name, key);
                conf.Set(HotkeySection, "Mods:" + name, "none");
                if (dflt != null && dfltStill)
                {
                    conf.Set(HotkeySection, "Key:" + dflt, "Unassigned");
                    conf.Set(HotkeySection, "Mods:" + dflt, "none");
                    Log.Info("snes9x.conf: " + key + " bound to " + name + " - " + dflt + " (Snes9x's default on " + key + ") set Unassigned");
                }
                else Log.Info("snes9x.conf: " + key + " bound to " + name);
                entries = conf.Entries(HotkeySection);
            }
        }

        /// <summary>For a .7z: the ROM inside, unpacked under the archive's name, or null with <paramref name="problem"/>
        /// said. Any other file comes back as it is.</summary>
        public static string RomFor(string rom, out string problem)
        {
            problem = null;
            if (!string.Equals(Path.GetExtension(rom ?? ""), ".7z", StringComparison.OrdinalIgnoreCase)) return rom;
            try
            {
                using var archive = ArchiveFactory.Open(rom);
                var roms = archive.Entries.Where(e => !e.IsDirectory && RomExtensions.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant())).ToList();
                var name = Path.GetFileNameWithoutExtension(rom);
                var pick = roms.Count == 1 ? roms[0]
                         : roms.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Key), name, StringComparison.OrdinalIgnoreCase));
                if (pick == null)
                {
                    problem = roms.Count == 0 ? "there is no Super Nintendo ROM in " + Path.GetFileName(rom)
                            : Path.GetFileName(rom) + " holds " + roms.Count + " ROMs, and none is named after the archive - Snes9x cannot tell which to play. "
                              + "Keep one ROM per archive, or name the one to play \"" + name + Path.GetExtension(roms[0].Key) + "\".";
                    return null;
                }
                var dir = UnpackDir(rom);
                var target = Path.Combine(dir, name + Path.GetExtension(pick.Key).ToLowerInvariant());
                if (File.Exists(target) && new FileInfo(target).Length == pick.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom))
                    return target;
                Directory.CreateDirectory(dir);
                // The ROMs unpacked before only: anything else there is Snes9x's (review 13/10: a save left in the folder went with them).
                foreach (var old in Directory.GetFiles(dir).Where(f => RomExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))) try { File.Delete(old); } catch { }
                using (var src = pick.OpenEntryStream())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(dst);
                Log.Info("unpacked " + pick.Key + " from " + Path.GetFileName(rom) + " -> " + target);
                return target;
            }
            catch (Exception ex)
            {
                Log.Warn("could not unpack " + rom, ex);
                problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message;
                return null;
            }
        }

        /// <summary>The host's line with its game path swapped for <paramref name="newRom"/>.</summary>
        public static string SwapRom(string line, string oldRom, string newRom)
        {
            line = line ?? "";
            foreach (var form in new[] { "\"" + oldRom + "\"", oldRom })
            {
                int at = line.LastIndexOf(form, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) return line.Substring(0, at) + "\"" + newRom + "\"" + line.Substring(at + form.Length);
            }
            // Spelt otherwise by the host (relative, forward slashes): the last quoted token, else the last token - never a
            // second game beside the first.
            var m = System.Text.RegularExpressions.Regex.Match(line, "\"[^\"]*\"\\s*$");
            if (m.Success) return line.Substring(0, m.Index) + "\"" + newRom + "\"";
            var cut = line.TrimEnd().LastIndexOf(' ');
            return cut < 0 ? "\"" + newRom + "\"" : line.Substring(0, cut) + " \"" + newRom + "\"";
        }
    }
}
