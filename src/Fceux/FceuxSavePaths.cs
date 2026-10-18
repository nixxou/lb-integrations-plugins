// Nothing beside the game, and every platform's own folders (Mehdi, 07/10: "rien ne doit être sauvegardé à côté du jeu,
// mais dans des répertoires dédiés, par plateforme si possible, dans le répertoire de l'émulateur").
//
// FCEUX NEVER WRITES BESIDE THE GAME (measured 18/10: the ROM folders held only the ROMs after every session; read at the tag,
// file.cpp FCEU_MakeFName): an EMPTY folder setting is a folder of its own install - sav\, fcs\, snaps\, cheats\ beside the
// exe. But those are global, one for all its systems. So at each launch the four settings are pointed at the GAME's
// platform (FceuxSystems.FolderOf):
//
//     "odnonvol"  battery saves <rom>.sav, the FDS disk save <rom>.fds   <FCEUX>\Saves\<platform>
//     "odstates"  states <rom>.fc0-.fc9 and their backups                  <FCEUX>\Saves\<platform>\States
//     "odsnaps"   screenshots <rom>-<n>.png                                <FCEUX>\Saves\<platform>\Screenshots
//     "odcheats"  cheats <rom>.cht                                         <FCEUX>\Saves\<platform>\Cheats
//
// FCEUX takes them AS WRITTEN (file.cpp:474 FCEUI_SetDirOverride; a relative one would follow the process's current
// folder), so they are absolute; it puts PSS between folder and name itself (no trailing backslash), and it CREATES a
// missing folder (measured 18/10, with spaces in the path: the .sav, a state and a screenshot each landed there).
//
// WHOSE SETTING IS IT. Empty (FCEUX's own folders) or one the pack set - under this install's Saves\, or the pack's shape
// (<a folder named as the install>\Saves\...) gone, an install moved or a portable LaunchBox on another drive letter - is
// the pack's, set to the game's platform at every launch. Anything else is a folder the user chose in FCEUX, kept for every
// platform.
//
// WHAT WAS ALREADY WRITTEN: a game's files in FCEUX's own folders (sav\, fcs\, snaps\, cheats\ - only FCEUX writes there,
// by those names) are MOVED into its platform's folders at its launch, never over a file there (LbipBesideGame.Bring).
// Beside the game nothing is FCEUX's (it never wrote there): a .sav there is another emulator's and stays. A game whose
// platform changed keeps its files in the old platform's folder: which game of that name they belong to cannot be told.
//
// THE NAME of a game's files is FileBase: the file name without its extension - for an archive, the ROM INSIDE it
// (measured, docs\fceux.md §8); an archive with several ROMs makes FCEUX ask which, and its files are not moved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;

namespace LbIntegrations.Fceux
{
    internal static class FceuxSavePaths
    {
        public const string Root = "Saves";

        /// <summary>The four settings, FCEUX's own folder for each when empty, and the pack's under the platform's.</summary>
        internal static readonly (string Key, string Default, string Sub)[] Settings =
        {
            (FceuxCfg.SaveDir, "sav", ""),
            (FceuxCfg.StateDir, "fcs", "States"),
            (FceuxCfg.SnapDir, "snaps", "Screenshots"),
            (FceuxCfg.CheatDir, "cheats", "Cheats"),
        };

        /// <summary>&lt;FCEUX&gt;\Saves\&lt;platform&gt;[\sub].</summary>
        public static string PackDir(string exe, string platform, string sub)
        {
            var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), Root, FceuxSystems.FolderOf(platform));
            return string.IsNullOrEmpty(sub) ? dir : Path.Combine(dir, sub);
        }

        /// <summary>A value of the pack's: empty, under this install's Saves\, or the pack's shape somewhere that is gone.</summary>
        internal static bool IsPacks(string value, string exeDir)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            try
            {
                var v = value.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
                if (!Path.IsPathRooted(v)) return false;
                var full = Path.GetFullPath(v);
                var saves = Path.Combine(Path.GetFullPath(exeDir).TrimEnd('\\'), Root);
                if (string.Equals(full, saves, StringComparison.OrdinalIgnoreCase) || full.StartsWith(saves + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                if (Directory.Exists(full)) return false;
                // Stale: ...\<install's folder name>\Saves\... - the shape the pack writes.
                var install = Path.GetFileName(Path.GetFullPath(exeDir).TrimEnd('\\'));
                var parts = full.Split('\\');
                for (int i = 0; i + 1 < parts.Length; i++)
                    if (string.Equals(parts[i], install, StringComparison.OrdinalIgnoreCase) && string.Equals(parts[i + 1], Root, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>The folders a game of <paramref name="platform"/> gets, written into <paramref name="cfg"/> (in memory -
        /// the caller saves). The settings that were EMPTY before - FCEUX was writing into its own folders - are returned,
        /// for the log; the files of a game in those folders are moved at its launch (Migrate), not here.</summary>
        public static HashSet<string> Apply(FceuxCfg cfg, string exe, string platform)
        {
            var wasEmpty = new HashSet<string>(StringComparer.Ordinal);
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            foreach (var (key, _, sub) in Settings)
            {
                var v = cfg.Get(key);
                if (!IsPacks(v, exeDir)) continue;                      // the user's folder, for every platform
                var dir = PackDir(exe, platform, sub);
                if (!FceuxCfg.Representable(dir)) { Log.Warn(key + " left as it is: " + dir + " has characters FCEUX cannot take (outside the ANSI code page)"); continue; }
                if (string.IsNullOrWhiteSpace(v)) wasEmpty.Add(key);
                if (!string.Equals((v ?? "").Trim(), dir, StringComparison.Ordinal)) cfg.Set(key, dir);
            }
            return wasEmpty;
        }

        /// <summary>At install, before any game: every pack folder for the NES. Never throws.</summary>
        public static void Ensure(string exe, string platform = null)
        {
            try
            {
                var cfg = FceuxCfg.Load(FceuxCfg.Of(exe));
                Apply(cfg, exe, platform ?? FceuxSystems.NesPlatform);
                bool changed = cfg.Changed;
                cfg.SaveIfChanged();
                if (changed) Log.Info("fceux.cfg: saves, states, screenshots and cheats in " + PackDir(exe, platform ?? FceuxSystems.NesPlatform, ""));
            }
            catch (Exception ex) { Log.Warn("could not set FCEUX's folders - it keeps its own", ex); }
        }

        /// <summary>Where the game's files of one setting are now: the setting's folder (relative ones from the exe's folder),
        /// else FCEUX's own.</summary>
        internal static string DirOf(FceuxCfg cfg, string exe, string key)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            var v = cfg.Get(key);
            if (!string.IsNullOrWhiteSpace(v)) return LbIntegrations.Saves.ExpectedDir.OfConfig(v, exeDir);
            return Path.Combine(exeDir, Settings.First(s => s.Key == key).Default);
        }

        /// <summary>The name FCEUX gives a game's files (FileBase): the file's name without its extension, for an archive
        /// the one ROM inside it - null when that cannot be told.</summary>
        public static string NameOf(string rom)
        {
            if (string.IsNullOrWhiteSpace(rom)) return null;
            if (Archives.IsArchive(rom))
            {
                var inside = Archives.RomInside(rom);
                return inside == null ? null : Path.GetFileNameWithoutExtension(inside);
            }
            return Path.GetFileNameWithoutExtension(rom);
        }

        /// <summary>The game's files in FCEUX's own folders brought into its platform's (see the header). Never throws.</summary>
        public static int Migrate(string exe, string rom, FceuxCfg cfg)
        {
            int brought = 0;
            try
            {
                var name = NameOf(rom);
                if (string.IsNullOrEmpty(name)) return 0;
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
                void Say(string m) => Log.Info(m);
                foreach (var (key, dflt, _) in Settings)
                {
                    // A folder the user chose: FCEUX reads nothing from its own any more, and the user's choice is not ours to fill.
                    if (!IsPacks(cfg.Get(key), exeDir) || string.IsNullOrWhiteSpace(cfg.Get(key))) continue;
                    var from = Path.Combine(exeDir, dflt);
                    var to = DirOf(cfg, exe, key);
                    if (to == null || !Directory.Exists(from) || string.Equals(Path.GetFullPath(from).TrimEnd('\\'), Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var file in FilesOf(from, name, key))
                        if (LbipBesideGame.Bring(file, to, copy: false, Say)) brought++;
                }
                if (brought > 0) Log.Info("saves: " + brought + " file(s) of " + name + " moved out of FCEUX's own folders into its platform's");
            }
            catch (Exception ex) { Log.Warn("saves: could not move the game's files into its platform's folders", ex); }
            return brought;
        }

        /// <summary>A game's files in one of FCEUX's folders, by the names FCEUX gives them (file.cpp FCEU_MakeFName, state.cpp).</summary>
        internal static IEnumerable<string> FilesOf(string dir, string name, string key)
        {
            if (!Directory.Exists(dir)) yield break;
            bool Named(string file, string pattern) => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(file),
                "^" + System.Text.RegularExpressions.Regex.Escape(name) + pattern + "$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            string pattern = key == FceuxCfg.SaveDir ? @"\.(sav|fds)"
                           : key == FceuxCfg.StateDir ? @"(-bak|\.bak)?\.fc\d(\.png)?|-resume\.fcs|-autosave\d+\.fcs"
                           : key == FceuxCfg.SnapDir ? @"-\d+\.png"
                           : @"\.cht";
            foreach (var file in Directory.EnumerateFiles(dir))
                if (Named(file, pattern)) yield return file;
        }
    }
}
