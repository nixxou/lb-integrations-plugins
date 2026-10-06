// What a launch writes: the game's options into settings.bml, the state hotkeys when there are none, and --fullscreen.
//
// THE OPTIONS (BsnesSettings.All) go in before every launch - bsnes reads settings.bml when it starts and writes it back
// whole when it quits (BsnesBml), so a node the plugin manages is the plugin's: changed in bsnes's own menus, it comes
// back to the Nixx window's value at the next launch. Every other node - pads, hotkeys, paths, audio - is the user's.
//
//   Aspect   Video/AspectCorrection         Output  Video/Output (Scale, Center, Stretch)
//   Smooth   Video/Blur                     VSync   Video/Blocking
//   Mode7    Emulator/Hack/PPU/Mode7/Scale (1 = native)
//   Screen   --fullscreen on the command line (bsnes.cpp: program.startFullScreen) - nothing kept in the file
//
// THE STATE HOTKEYS: bsnes ships Hotkey/SaveState and Hotkey/LoadState UNBOUND, so the pause screens of LaunchBox and
// BigBox would have nothing to press. When - and only when - one is empty, it is bound: F5 saves the quick state of the
// current slot, F7 loads it (BsnesAhk sends the same). A key the user bound is never changed.
//
// GAMES: bsnes opens .sfc/.smc, .zip and .7z itself (game.cpp, Program::loadSuperFamicom), so nothing is unpacked.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LbIntegrations.Bsnes
{
    internal static class BsnesLaunch
    {
        public const string ExecutableName = "bsnes.exe";
        public const string SaveKey = "Keyboard/1/Button/F5";
        public const string LoadKey = "Keyboard/1/Button/F7";

        public static bool IsBsnesExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>A bsnes running from that folder: an edit of settings.bml now would be lost when it quits.</summary>
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

        /// <summary>The game's options written into settings.bml, the state keys bound when unbound. Never throws.</summary>
        public static void PrepareSettings(string exe, string gameId)
        {
            try
            {
                var bml = BsnesBml.Load(BsnesBml.Of(exe));
                bml.SetBool("Video/AspectCorrection", BsnesSettings.For(gameId, "Aspect") == "4:3");
                bml.Set("Video/Output", BsnesSettings.For(gameId, "Output"));
                bml.SetBool("Video/Blur", BsnesSettings.BoolFor(gameId, "Smooth"));
                bml.SetBool("Video/Blocking", BsnesSettings.BoolFor(gameId, "VSync"));
                var mode7 = BsnesSettings.For(gameId, "Mode7");
                bml.Set("Emulator/Hack/PPU/Mode7/Scale", mode7);
                if (mode7 != "1") bml.SetBool("Emulator/Hack/PPU/Fast", true);   // HD Mode 7 is the fast PPU's

                if (string.IsNullOrWhiteSpace(bml.Get("Hotkey/SaveState"))) bml.Set("Hotkey/SaveState", SaveKey);
                if (string.IsNullOrWhiteSpace(bml.Get("Hotkey/LoadState"))) bml.Set("Hotkey/LoadState", LoadKey);

                bool changed = bml.Changed;
                bml.SaveIfChanged();
                Log.Info("settings.bml: " + (changed ? "written" : "already as wanted") + " - " + BsnesSettings.For(gameId, "Aspect") + ", "
                         + BsnesSettings.For(gameId, "Output") + (mode7 != "1" ? ", HD Mode 7 " + mode7 + "x" : ""));
            }
            catch (Exception ex) { Log.Warn("settings.bml could not be written - the game starts with it as it was", ex); }
        }

        /// <summary>The host's line with --fullscreen added when wanted (and never twice), or taken out when not.</summary>
        public static string CommandLine(string current, bool fullscreen)
        {
            var line = (current ?? "").Trim();
            bool has = line.Split(' ').Any(a => a.Trim('"') == "--fullscreen");
            if (fullscreen && !has) return ("--fullscreen " + line).Trim();
            if (!fullscreen && has) return string.Join(" ", line.Split(' ').Where(a => a.Trim('"') != "--fullscreen")).Trim();
            return line;
        }
    }
}
