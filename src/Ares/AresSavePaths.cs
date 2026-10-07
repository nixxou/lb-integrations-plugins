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
                var root = Path.GetDirectoryName(Path.GetFullPath(exe));
                var bml = AresBml.Load(AresLaunch.SettingsOf(exe));
                bool savesWereEmpty = string.IsNullOrWhiteSpace(bml.Get("Paths/Saves"));
                foreach (var (key, folder) in Paths)
                {
                    if (!string.IsNullOrWhiteSpace(bml.Get(key))) continue;
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

        /// <summary>A game's files still beside it, moved into its folders. Never throws.</summary>
        public static void Migrate(string exe, AresSystem system, string rom)
        {
            try
            {
                if (system == null || string.IsNullOrWhiteSpace(rom)) return;
                var full = Path.GetFullPath(rom);
                var played = Path.GetFullPath(AresPlugin.PlayedFile(full, exe) ?? full);
                var folders = AresPlugin.FoldersFor(exe, full, system);
                // Beside the file ares was given (a .7z's unpacked folder), and beside the game itself.
                var besides = new[] { Path.GetDirectoryName(played), Path.GetDirectoryName(full) }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var identity = AresConsoleSession.IdentityOf(full, exe);
                var names = new[] { identity }.Concat(system.Disc ? AresConsoleSession.DiscNames(full, system, exe) : Enumerable.Empty<string>())
                                              .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                int moved = 0;
                void Move(string beside, string file, string target)
                {
                    if (target == null || string.Equals(beside, target, StringComparison.OrdinalIgnoreCase)) return;
                    var from = Path.Combine(beside, file);
                    if (!File.Exists(from) || string.Equals(from, full, StringComparison.OrdinalIgnoreCase) || string.Equals(from, played, StringComparison.OrdinalIgnoreCase)) return;
                    var to = Path.Combine(target, file);
                    if (File.Exists(to)) { Log.Info("saves: " + from + " left where it is - " + to + " is already there"); return; }
                    Directory.CreateDirectory(target);
                    File.Move(from, to);
                    moved++;
                    Log.Info("saves: " + from + " -> " + to);
                }
                foreach (var beside in besides)
                    foreach (var name in names)
                    {
                        foreach (var ext in system.SaveExtensions) Move(beside, name + ext, folders.SaveTarget);
                        foreach (var ext in Enumerable.Range(1, 9).Select(i => ".bs" + i).Concat(new[] { ".bsu", ".blu" }))
                        {
                            Move(beside, name + ext, folders.StateTarget);
                            Move(beside, name + ext + LbipStateThumbs.Suffix, folders.StateTarget);
                        }
                    }
                foreach (var beside in besides)
                    foreach (var cf in system.ConsoleFiles.Where(c => c.PerGame))
                        Move(beside, identity + cf.Ext, folders.SaveTarget);
                if (moved > 0) Log.Info("saves: " + moved + " file(s) of " + identity + " moved out of the game's folder");
            }
            catch (Exception ex) { Log.Warn("saves: could not move the game's files out of its folder", ex); }
        }
    }
}
