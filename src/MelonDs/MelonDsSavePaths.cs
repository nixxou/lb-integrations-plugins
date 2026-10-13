// Nothing beside the game (Mehdi, 07/10; lot 9, 13/10): melonDS's saves, states and cheats in folders of its install.
//
// melonDS writes beside the ROM while a path key of its window is empty (EmuInstance::getAssetPath: an empty
// SaveFilePath / SavestatePath / CheatFilePath means "the ROM's directory"), and each window has its own keys
// ([Instance0], [Instance1]... - a second window opened from melonDS has its own table). Every one of those keys found empty
// is set to the install's own folder - saves\, savestates\, cheats\ - at every launch and before a restore, not only at an
// install of ours as before 13/10: Mehdi's rule is about every melonDS the pack drives. A folder the user CHOSE is left as
// it is (except one the pack set that no longer points into this install: LbipBesideGame.IsStalePackPath); an empty one is
// not a choice, it is melonDS's default, which is exactly what the rule forbids. The paths are written ABSOLUTE
// (getAssetPath hands the value to Windows as it is: a relative one would follow the working directory of whoever starts
// melonDS).
//
// WHAT WAS ALREADY WRITTEN beside the game, by the pack's rule (docs\saves-lot9.md, LbipBesideGame):
//   - melonDS's own extensions - the states .ml1 to .ml8 with their thumbnails .mlN.png, the cheats .mch - MOVED at every launch;
//   - the generic .sav - other DS emulators write that name too - COPIED when last written before the moment [Instance0]
//     SaveFilePath was set on a melonDS that had been running with it empty: until then melonDS read that file as the game's
//     own. One written after is somebody else's, and stays.
// Never over a file already there; never for a DSiWare title (its save lives in the NAND, Shared.Dsi).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Dsi;
using LbIntegrations.Lbip;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsSavePaths
    {
        public const string KeyCheatFilePath = "CheatFilePath";
        public const string CheatDirName = "cheats";

        /// <summary>Every window's empty save, state and cheat keys set to the install's folders. Returns the keys of
        /// [Instance0] set now; the layout's SaveDir / StateDir follow. Never throws.</summary>
        public static HashSet<string> Ensure(MelonDsLayout layout)
        {
            var setNow = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (layout?.ConfigFile == null || string.IsNullOrEmpty(layout.InstallDir)) return setNow;
                var folders = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MelonDsPaths.KeySaveFilePath] = layout.DefaultSaveDir,
                    [MelonDsPaths.KeySavestatePath] = layout.DefaultStateDir,
                    [KeyCheatFilePath] = Path.Combine(layout.InstallDir, CheatDirName),
                };
                // [Instance0] always (melonDS's first window), and every other window's table melonDS already wrote.
                var tables = new List<string> { MelonDsPaths.InstanceTable };
                tables.AddRange(MelonDsToml.Keys(layout.ConfigFile).Keys
                                .Where(t => t.StartsWith("Instance", StringComparison.Ordinal) && t.Length > 8 && t.Substring(8).All(char.IsDigit) && t != MelonDsPaths.InstanceTable)
                                .OrderBy(t => t, StringComparer.Ordinal));
                foreach (var table in tables)
                {
                    // The file held (being written) and its keys unknown: left for the next launch - an "empty" read here would
                    // overwrite a folder the user chose (review 13/10).
                    if (!MelonDsToml.TryRead(layout.ConfigFile, table, out var have, folders.Keys.ToArray())) { Log.Info("[" + table + "] melonDS.toml could not be read now - its folders are checked at the next launch"); continue; }
                    var wanted = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in folders)
                    {
                        string v = null;
                        bool present = have != null && have.TryGetValue(kv.Key, out v);
                        bool stale = LbipBesideGame.IsStalePackPath(v, Path.GetFileName(kv.Value), layout.InstallDir);
                        if (present && !string.IsNullOrWhiteSpace(v) && !stale) continue;
                        // Present and empty in [Instance0]: melonDS wrote it so - it has been writing beside the games. Noted FIRST:
                        // without the moment every other game's save would be stranded beside it once the key is set.
                        if (table == MelonDsPaths.InstanceTable && present && !stale && !LbipBesideGame.Note(layout.InstallDir, kv.Key))
                        { Log.Warn("[" + table + "] " + kv.Key + " left empty - the moment melonDS stopped writing beside the games could not be noted"); continue; }
                        wanted[kv.Key] = MelonDsToml.Text(kv.Value);
                        if (stale) Log.Info("[" + table + "] " + kv.Key + " pointed to " + v + ", no longer this install - set to " + kv.Value);
                    }
                    if (wanted.Count == 0) continue;
                    foreach (var dir in wanted.Keys.Select(k => folders[k])) try { Directory.CreateDirectory(dir); } catch { }
                    var error = MelonDsToml.Write(layout.ConfigFile, table, wanted);
                    if (error != null) { Log.Warn("[" + table + "] save folders not set: " + error); continue; }
                    Log.Info("[" + table + "] " + string.Join(", ", wanted.Keys) + " in the emulator's folder");
                    if (table == MelonDsPaths.InstanceTable) foreach (var k in wanted.Keys) setNow.Add(k);
                }
                if (setNow.Contains(MelonDsPaths.KeySaveFilePath)) layout.SaveDir = layout.DefaultSaveDir;
                if (setNow.Contains(MelonDsPaths.KeySavestatePath)) layout.StateDir = layout.DefaultStateDir;
            }
            catch (Exception ex) { Log.Warn("could not set melonDS's save folders - it writes beside the game as before", ex); setNow.Clear(); }
            return setNow;
        }

        /// <summary>The game's files still beside it brought into the install's folders, by the rule of the header.
        /// <paramref name="except"/>: the file a restore reads from, never touched. Never throws.</summary>
        public static int Migrate(MelonDsLayout layout, string romPath, string except = null)
        {
            int brought = 0;
            try
            {
                if (layout == null || string.IsNullOrWhiteSpace(romPath)) return 0;
                var rom = NdsHeader.Describe(Path.GetFullPath(romPath));
                if (rom.IsDSiWare || string.IsNullOrWhiteSpace(rom.AssetName) || string.IsNullOrWhiteSpace(rom.RomDir)) return 0;
                var beside = rom.RomDir;
                string cheats = null;
                try
                {
                    MelonDsToml.TryRead(layout.ConfigFile, MelonDsPaths.InstanceTable, out var have, KeyCheatFilePath);
                    cheats = LbIntegrations.Saves.ExpectedDir.OfConfig(have != null && have.TryGetValue(KeyCheatFilePath, out var c) ? c : null, layout.InstallDir);
                }
                catch { }
                void Say(string m) => Log.Info(m);
                var name = rom.AssetName;
                for (int slot = 1; slot <= 8; slot++)
                    foreach (var ext in new[] { ".ml" + slot, ".ml" + slot + LbipStateThumbs.Suffix })
                        if (LbipBesideGame.Bring(Path.Combine(beside, name + ext), layout.StateDir, copy: false, Say, except)) brought++;
                if (LbipBesideGame.Bring(Path.Combine(beside, name + ".mch"), cheats, copy: false, Say, except)) brought++;
                var sav = Path.Combine(beside, name + ".sav");
                if (LbipBesideGame.WrittenBefore(sav, LbipBesideGame.Since(layout.InstallDir, MelonDsPaths.KeySaveFilePath))
                    && LbipBesideGame.Bring(sav, layout.SaveDir, copy: true, Say, except, layout.InstallDir)) brought++;
                if (brought > 0) Log.Info("saves: " + brought + " file(s) of " + name + " brought out of the game's folder");
            }
            catch (Exception ex) { Log.Warn("saves: could not bring the game's files out of its folder", ex); }
            return brought;
        }
    }
}
