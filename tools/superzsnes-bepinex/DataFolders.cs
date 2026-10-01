// What the emulator writes for a game, kept in ITS OWN FOLDER rather than beside the ROM (Mehdi, 01/10):
//
//   srmPath       "{exec}/saves"    <rom>.srm, the cartridge's battery save
//   gameSavePath  "{exec}/states"   <rom>.data.szsnes\ - the states (.szst0..., .szst-last), history, bookmarks
//   chtPath       "{exec}/cheats"   <rom>.cht
//
// MEASURED 01/10: the emulator expands {exec} to its own folder, creates the three folders itself, writes the
// SRAM to saves\<rom>.srm and a state saved with F2 to states\<rom>.data.szsnes\<rom>.szst0 - and READS the SRAM
// from there too, so a save left beside the ROM is not seen any more.
//
// WRITTEN FOR GOOD, the one exception to "nothing of ours lands in the user's file": set on the settings object
// right after LoadMainMenuSave and BEFORE the overrides take their originals, they are the user's values from
// then on and the emulator saves them like any other. Only an EMPTY field is set - empty is the emulator's
// "beside the ROM"; a folder the user chose is theirs. In this plugin rather than in LaunchBox's: the settings
// file does not exist before a first launch, and the emulator opened on its own runs this too.
//
// WHAT WAS BESIDE THE ROM MOVES, for the ROM on the command line, before the emulator loads it: its .srm, its
// .data.szsnes\ folder (file by file into one that exists already), its .cht. Only into OUR folder (a field the
// user set elsewhere is not followed), never over a file that is already there (that one wins; the log says
// so). --nixx-data-folders=off turns all of it off.

using System;
using System.IO;
using System.Linq;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal static class DataFolders
    {
        private static readonly (string Field, string Folder)[] Fields =
        {
            ("srmPath", "saves"), ("gameSavePath", "states"), ("chtPath", "cheats"),
        };

        private static readonly string[] RomExtensions = { ".smc", ".sfc", ".zip", ".swc", ".ufo" };

        private static bool _moved;

        internal static void Apply(MainMenuManager.MainMenuSettings settings)
        {
            if (!Plugin.Opt.DataFolders || settings == null) return;
            var type = settings.GetType();
            foreach (var (field, folder) in Fields)
            {
                try
                {
                    var prop = type.GetProperty(field);
                    if (prop == null || prop.PropertyType != typeof(string)) { Plugin.Logger.LogWarning("data folders: no field " + field + " in this build"); continue; }
                    var current = (string)prop.GetValue(settings);
                    if (!string.IsNullOrWhiteSpace(current)) continue;
                    prop.SetValue(settings, "{exec}/" + folder);
                    Plugin.Logger.LogInfo("data folders: " + field + " empty (beside the ROM) -> {exec}/" + folder + ", kept in the emulator's settings");
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("data folders: " + field + " - " + ex.Message); }
            }
            if (_moved) return;
            _moved = true;
            try { MoveFromBesideTheRom(settings); }
            catch (Exception ex) { Plugin.Logger.LogWarning("data folders: moving what was beside the ROM - " + ex.Message); }
        }

        private static void MoveFromBesideTheRom(MainMenuManager.MainMenuSettings settings)
        {
            var rom = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal)
                          && RomExtensions.Any(e => a.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
            if (rom == null) return;
            rom = Path.GetFullPath(rom);
            var romDir = Path.GetDirectoryName(rom);
            var name = Path.GetFileNameWithoutExtension(rom);
            if (string.IsNullOrEmpty(romDir) || !File.Exists(rom)) return;

            string Ours(string field, string folder)
            {
                var v = (string)settings.GetType().GetProperty(field)?.GetValue(settings);
                return string.Equals((v ?? "").Replace('\\', '/').TrimEnd('/'), "{exec}/" + folder, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath ?? AppContext.BaseDirectory)), folder) : null;
            }

            var saves = Ours("srmPath", "saves");
            if (saves != null) MoveFile(Path.Combine(romDir, name + ".srm"), Path.Combine(saves, name + ".srm"));
            var cheats = Ours("chtPath", "cheats");
            if (cheats != null) MoveFile(Path.Combine(romDir, name + ".cht"), Path.Combine(cheats, name + ".cht"));
            var states = Ours("gameSavePath", "states");
            bool flat = false;
            try { flat = settings.noDirectoryForSaves; } catch { }
            if (states != null && !flat) MoveFolder(Path.Combine(romDir, name + ".data.szsnes"), Path.Combine(states, name + ".data.szsnes"));
        }

        private static void MoveFile(string from, string to)
        {
            if (!File.Exists(from) || string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(to)) { Plugin.Logger.LogInfo("data folders: " + Path.GetFileName(from) + " beside the ROM left there - " + to + " is already there and wins"); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Move(from, to);
            Plugin.Logger.LogInfo("data folders: moved " + from + " -> " + to);
        }

        private static void MoveFolder(string from, string to)
        {
            if (!Directory.Exists(from) || string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
            int moved = 0, kept = 0;
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, Path.GetRelativePath(from, file));
                if (File.Exists(target)) { kept++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Move(file, target);
                moved++;
            }
            // The folder goes when nothing is left in it.
            try { if (!Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Any()) Directory.Delete(from, true); } catch { }
            Plugin.Logger.LogInfo("data folders: " + Path.GetFileName(from) + " beside the ROM -> " + to + ": " + moved + " file(s) moved"
                                  + (kept > 0 ? ", " + kept + " left there (the same name is already in " + to + " and wins)" : ""));
        }
    }
}
