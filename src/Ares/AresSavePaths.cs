// Nothing beside the game (Mehdi, 07/10: "rien ne doit être sauvegardé à côté du jeu, mais dans des répertoires dédiés,
// par plateforme si possible, dans le répertoire de l'émulateur").
//
// ares writes beside the game while Paths/Saves, Paths/Screenshots and Paths/Debugging are empty - its default
// (emulator.cpp locate: no path -> the game's own; pak.cpp saveLocation: no path -> <game><ext>). Each one ares has empty
// is set to a folder of the emulator's - <ares>\Saves\, \Screenshots\, \Debugging\ - and ares then keeps a folder per
// system under it by itself: saves in Saves\<the medium's saveName()>\, states in Saves\<the core's root name>\ (the
// Mega CD's in "Mega Drive"), screenshots in Screenshots\<core>\. A folder the user chose in ares is left as it is.
// Written into settings.bml, not on the line: ares opened by itself writes there too.
//
// WHAT WAS ALREADY WRITTEN moves with it:
//   - the console's own files (Systems\<Pak>.sys\: the Mega CD's backup RAM, the 64DD's clock, the NGP's and WonderSwan's
//     settings - and the plugin's .before-nixx seeds) are copied to where ares now reads them (<Saves><Pak>\<Pak><ext>,
//     measured on 06/10), once, at the change;
//   - a game's saves, states (and their PNG) and own console copies beside it are moved into its folders at its next launch
//     (Migrate). A file already in the new place is never overwritten: the old one then stays where it was, logged.

using System;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ares
{
    internal static class AresSavePaths
    {
        private static readonly (string Key, string Folder)[] Paths =
        {
            ("Paths/Saves", "Saves"),
            ("Paths/Screenshots", "Screenshots"),
            ("Paths/Debugging", "Debugging"),
        };

        /// <summary>ares's folders set to the emulator's own, where ares has none. Never throws.</summary>
        public static void Ensure(string exe)
        {
            try
            {
                if (string.IsNullOrEmpty(exe)) return;
                // A session left unfinished is finished first: its plan names the console files where they were.
                AresConsoleSession.Recover();
                // The .cues of the install's Discs folder whose image is gone (a hard link would keep it on the disk).
                AresLaunch.SweepDiscs(exe);
                var root = Path.GetDirectoryName(Path.GetFullPath(exe));
                var bml = AresBml.Load(AresLaunch.SettingsOf(exe));
                bool savesWereEmpty = string.IsNullOrWhiteSpace(bml.Get("Paths/Saves"));
                foreach (var (key, folder) in Paths)
                {
                    var v = bml.Get(key);
                    // A folder the pack set that no longer points into this install (moved, another drive letter): set again.
                    bool stale = LbipBesideGame.IsStalePackPath(v, folder, root);
                    if (!string.IsNullOrWhiteSpace(v) && !stale) continue;
                    // Present and empty: ares has been writing beside the games with it - noted FIRST (LbipBesideGame), and
                    // without the moment the setting is left: every other game's save would be stranded beside it.
                    if (v != null && !stale && !LbipBesideGame.Note(root, key)) { Log.Warn(key + " left empty - the moment ares stopped writing beside the games could not be noted"); continue; }
                    var dir = Path.Combine(root, folder);
                    Directory.CreateDirectory(dir);
                    // ares glues the system's name straight on (locate: {path, system, "/"}): the trailing slash is needed.
                    bml.Set(key, dir.Replace('\\', '/').TrimEnd('/') + "/");
                }
                if (!bml.Changed) return;
                bml.SaveIfChanged();
                Log.Info("settings.bml: saves, screenshots and debugging files in the emulator's folder (" + root + ")");
                if (savesWereEmpty) MoveConsoleFiles(exe);
            }
            catch (Exception ex) { Log.Warn("could not set ares's folders - it writes beside the game as before", ex); }
        }

        /// <summary>Every system's console files, from Systems\ to the new place - copied, the old ones kept.</summary>
        private static void MoveConsoleFiles(string exe)
        {
            foreach (var cf in AresSystems.All.SelectMany(s => s.ConsoleFiles).GroupBy(c => c.Pak + "|" + c.File).Select(g => g.First()))
            {
                try
                {
                    var from = AresPlugin.ConsolePath(exe, cf, "");
                    var to = AresPlugin.ConsolePath(exe, cf);
                    if (from == null || to == null || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var suffix in new[] { "", AresConsoleSession.BeforeSuffix })
                    {
                        if (!File.Exists(from + suffix) || File.Exists(to + suffix)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(from + suffix, to + suffix);
                        Log.Info("console memory: " + from + suffix + " -> " + to + suffix);
                    }
                }
                catch (Exception ex) { Log.Warn("console memory: could not copy " + cf.Pak + " " + cf.File, ex); }
            }
        }

        /// <summary>A game's files still beside it brought into its folders, by the pack's rule (docs\saves-lot9.md,
        /// LbipBesideGame): ares's own states (.bs1-.bs9, .bsu, .blu and their pictures) MOVED at every launch; the saves and the
        /// per-game console copies - extensions other emulators write too (.ram, .sav, .rtc...) - COPIED when last written
        /// before the moment Paths/Saves was set on an ares that had been writing beside the games (a setting made before
        /// 13/10 has no moment noted: those stay); never a file of a ROM extension of the system (a ZX Spectrum's .wav is a
        /// tape as well as a save). <paramref name="except"/>: the file a restore reads from, never touched. Never throws.</summary>
        public static void Migrate(string exe, AresSystem system, string rom, string except = null)
        {
            try
            {
                if (system == null || string.IsNullOrWhiteSpace(rom)) return;
                var full = Path.GetFullPath(rom);
                var played = Path.GetFullPath(AresPlugin.PlayedFile(full, exe) ?? full);
                var folders = AresPlugin.FoldersFor(exe, full, system);
                // The game's own folder, and the plugin's own folders ares may have been given a file in - a .7z's Unpacked\
                // folder, a disc's Discs\ folder: what ares wrote there is ares's, whatever its extension (moved, no moment needed).
                var gameDir = Path.GetDirectoryName(full);
                var ownDirs = new[] { Path.GetDirectoryName(played), AresLaunch.DiscDir(exe, full) }
                              .Where(d => d != null && !string.Equals(d, gameDir, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var identity = AresConsoleSession.IdentityOf(full, exe);
                var names = new[] { identity }.Concat(system.Disc ? AresConsoleSession.DiscNames(full, system, exe) : Enumerable.Empty<string>())
                                              .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var root = Path.GetDirectoryName(Path.GetFullPath(exe));
                var savesSince = LbipBesideGame.Since(root, "Paths/Saves");
                int brought = 0;
                void Say(string m) => Log.Info(m);
                void Own(string dir, string file, string target)
                {
                    var from = Path.Combine(dir, file);
                    if (string.Equals(from, full, StringComparison.OrdinalIgnoreCase) || string.Equals(from, played, StringComparison.OrdinalIgnoreCase)) return;
                    // Never INTO the game's folder: a target there means ares's folder is empty (its moment could not be noted) -
                    // ares writes beside what it loaded (Discs\, Unpacked\) and reads it there; taken to the game it is lost to it.
                    if (string.IsNullOrWhiteSpace(target) || string.Equals(Path.GetFullPath(target).TrimEnd('\\'), gameDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                    if (LbipBesideGame.Bring(from, target, copy: false, Say, except)) brought++;
                }
                void Generic(string file, string target)
                {
                    var from = Path.Combine(gameDir, file);
                    // Never a ROM of this system (a .wav is a ZX Spectrum's tape as well as its save), never the game itself.
                    if (system.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()) || string.Equals(from, full, StringComparison.OrdinalIgnoreCase)) return;
                    if (LbipBesideGame.WrittenBefore(from, savesSince) && LbipBesideGame.Bring(from, target, copy: true, Say, except, root)) brought++;
                }
                var states = Enumerable.Range(1, 9).Select(i => ".bs" + i).Concat(new[] { ".bsu", ".blu" }).ToList();
                foreach (var name in names)
                {
                    foreach (var ext in states)
                        foreach (var dir in ownDirs.Append(gameDir))
                        {
                            Own(dir, name + ext, folders.StateTarget);
                            Own(dir, name + ext + LbipStateThumbs.Suffix, folders.StateTarget);
                        }
                    foreach (var ext in system.SaveExtensions)
                    {
                        foreach (var dir in ownDirs) Own(dir, name + ext, folders.SaveTarget);
                        Generic(name + ext, folders.SaveTarget);
                    }
                }
                foreach (var cf in system.ConsoleFiles.Where(c => c.PerGame))
                {
                    foreach (var dir in ownDirs) Own(dir, identity + cf.Ext, folders.SaveTarget);
                    Generic(identity + cf.Ext, folders.SaveTarget);
                }
                if (brought > 0) Log.Info("saves: " + brought + " file(s) of " + identity + " brought out of the game's folder");
            }
            catch (Exception ex) { Log.Warn("saves: could not move the game's files out of its folder", ex); }
        }
    }
}
