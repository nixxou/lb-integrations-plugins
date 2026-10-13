// Nothing beside the game (Mehdi, 07/10; lot 9, 13/10): bsnes's saves, states, cheats and screenshots in folders of its
// own install, never in the games folder.
//
// bsnes writes beside the game while a Path node is empty (target-bsnes/program/paths.cpp, Program::path: the node's
// folder when it is set, else the game's; then the game's name and the extension - so the node is a PREFIX and needs its
// trailing slash). Each node bsnes has empty is set to a folder of the install: <bsnes>\Saves\, \States\, \Cheats\,
// \Screenshots\. A folder the user chose is left as it is - except one the pack set that no longer points into this install
// (the install moved, a portable LaunchBox on another drive letter: LbipBesideGame.IsStalePackPath). Written into
// settings.bml before bsnes starts: bsnes reads it then, and writes back what it holds when it quits.
//
// WHAT WAS ALREADY WRITTEN beside the game, by the pack's rule (docs\saves-lot9.md, LbipBesideGame):
//   - bsnes's own files - the states' <name>.bsz and its <name>.bsz.png - MOVED at every launch: no other emulator writes them;
//   - the generic ones - <name>.srm, .rtc, .psr, .cht, also written by SUPER ZSNES, Snes9x and RetroArch under the same
//     name - COPIED when last written before the moment the node was set on a bsnes that had been running with it empty:
//     until then bsnes read that file as the game's own; another emulator may still read it beside the game. One written
//     after is somebody else's, and stays.
// Never over a file already there.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;

namespace LbIntegrations.Bsnes
{
    internal static class BsnesSavePaths
    {
        internal static readonly (string Node, string Folder)[] Nodes =
        {
            ("Path/Saves", "Saves"),
            ("Path/States", "States"),
            ("Path/Cheats", "Cheats"),
            ("Path/Screenshots", "Screenshots"),
        };

        /// <summary>bsnes's folders set to the install's own, where bsnes has none. The nodes set now are returned. Never throws.</summary>
        public static HashSet<string> Ensure(string exe)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrEmpty(exe)) return set;
                var root = Path.GetDirectoryName(Path.GetFullPath(exe));
                var bml = BsnesBml.Load(BsnesBml.Of(exe));
                foreach (var (node, folder) in Nodes)
                {
                    var v = bml.Get(node);
                    bool stale = LbipBesideGame.IsStalePackPath(v, folder, root);
                    if (!string.IsNullOrWhiteSpace(v) && !stale) continue;
                    // Present and empty: bsnes wrote it so - it has been writing beside the games. Noted FIRST: without the
                    // moment, every other game's save would be stranded beside it once the node is set.
                    if (v != null && !stale && !LbipBesideGame.Note(root, node)) { Log.Warn(node + " left empty - the moment bsnes stopped writing beside the games could not be noted"); continue; }
                    var dir = Path.Combine(root, folder);
                    Directory.CreateDirectory(dir);
                    bml.Set(node, dir.Replace('\\', '/').TrimEnd('/') + "/");
                    set.Add(node);
                    if (stale) Log.Info(node + " pointed to " + v + ", no longer this install - set to " + dir);
                }
                if (!bml.Changed) return set;
                bml.SaveIfChanged();
                Log.Info("settings.bml: " + string.Join(", ", set) + " in the emulator's folder (" + root + ")");
            }
            catch (Exception ex) { Log.Warn("could not set bsnes's folders - it writes beside the game as before", ex); set.Clear(); }
            return set;
        }

        /// <summary>The game's files still beside it brought into the install's folders, by the rule of the header.
        /// <paramref name="except"/>: the file a restore reads from, never touched. Never throws.</summary>
        public static int Migrate(string exe, string rom, string except = null)
        {
            int brought = 0;
            try
            {
                if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(rom)) return 0;
                var full = Path.GetFullPath(rom);
                var beside = Path.GetDirectoryName(full);
                var name = BsnesPlugin.NameOf(full);
                if (name.Length == 0) return 0;
                var root = Path.GetDirectoryName(Path.GetFullPath(exe));
                var folders = BsnesPlugin.FoldersFor(exe, full);
                string cheats = null;
                try { cheats = LbIntegrations.Saves.ExpectedDir.OfConfig(BsnesBml.Load(BsnesBml.Of(exe)).Get("Path/Cheats"), root); } catch { }
                void Say(string m) => Log.Info(m);
                // bsnes's own: always, moved.
                foreach (var ext in new[] { ".bsz", ".bsz" + LbipStateThumbs.Suffix })
                    if (LbipBesideGame.Bring(Path.Combine(beside, name + ext), folders.StateDir, copy: false, Say, except)) brought++;
                // The generic ones: those bsnes was reading beside the game until it stopped - copied.
                var saves = LbipBesideGame.Since(root, "Path/Saves");
                foreach (var ext in new[] { ".srm", ".rtc", ".psr" })
                    if (LbipBesideGame.WrittenBefore(Path.Combine(beside, name + ext), saves)
                        && LbipBesideGame.Bring(Path.Combine(beside, name + ext), folders.SaveDir, copy: true, Say, except, root)) brought++;
                if (LbipBesideGame.WrittenBefore(Path.Combine(beside, name + ".cht"), LbipBesideGame.Since(root, "Path/Cheats"))
                    && LbipBesideGame.Bring(Path.Combine(beside, name + ".cht"), cheats, copy: true, Say, except, root)) brought++;
                if (brought > 0) Log.Info("saves: " + brought + " file(s) of " + name + " brought out of the game's folder");
            }
            catch (Exception ex) { Log.Warn("saves: could not bring the game's files out of its folder", ex); }
            return brought;
        }
    }
}
