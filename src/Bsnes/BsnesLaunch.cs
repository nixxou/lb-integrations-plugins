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
// BigBox would have nothing to press. When - and only when - one is empty (or still an earlier build's F5/F7), it is
// bound to RetroArch's keys: F2 saves the quick state of the current slot, F4 loads it (BsnesAhk sends the same). A key
// the user bound is never changed.
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
        // bsnes binds "0x<device id>/<group>/<input>" (input\input.cpp, InputMapping::bind: token[0].natural() is the
        // device id) - the keyboard is 0x1, group 0, F1..F12 = 1..12, as in ares. What an earlier build of this plugin
        // wrote ("Keyboard/1/Button/F5", which bind() reads as device 0 - nothing) is replaced.
        public const string SaveKey = "0x1/0/2";   // F2, RetroArch's key (Mehdi, 06/10)
        public const string LoadKey = "0x1/0/4";   // F4
        private static readonly string[] Superseded = { "Keyboard/1/Button/F5", "Keyboard/1/Button/F7", "0x1/0/5", "0x1/0/7" };   // F5/F7 of earlier builds

        private static bool Unbound(string v) => string.IsNullOrWhiteSpace(v) || Array.IndexOf(Superseded, v.Trim()) >= 0;

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
                var output = BsnesSettings.For(gameId, "Output");
                bml.Set("Video/Output", output == "center" ? "Center" : output == "stretch" ? "Stretch" : "Scale");
                bml.SetBool("Video/Blur", BsnesSettings.BoolFor(gameId, "Smooth"));
                bml.SetBool("Video/Blocking", BsnesSettings.BoolFor(gameId, "VSync"));
                var mode7 = BsnesSettings.For(gameId, "Mode7");
                bml.Set("Emulator/Hack/PPU/Mode7/Scale", mode7);
                if (mode7 != "1") bml.SetBool("Emulator/Hack/PPU/Fast", true);   // HD Mode 7 is the fast PPU's

                if (Unbound(bml.Get("Hotkey/SaveState"))) bml.Set("Hotkey/SaveState", SaveKey);
                if (Unbound(bml.Get("Hotkey/LoadState"))) bml.Set("Hotkey/LoadState", LoadKey);

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
            // Tokens as Windows cuts them: a game path holding " --fullscreen " inside its quotes is one token, kept.
            var tokens = Tokenize(line);
            bool has = tokens.Contains("--fullscreen");
            if (fullscreen && !has) return ("--fullscreen " + line).Trim();
            if (!fullscreen && has) return string.Join(" ", tokens.Where(a => a != "--fullscreen").Select(Quote));
            return line;
        }

        private static string Quote(string a) => a.StartsWith("--") && !a.Any(char.IsWhiteSpace) ? a : "\"" + a + "\"";

        internal static System.Collections.Generic.List<string> Tokenize(string line)
        {
            var list = new System.Collections.Generic.List<string>();
            var cur = new System.Text.StringBuilder();
            bool quoted = false, any = false;
            foreach (var c in line ?? "")
            {
                if (c == '"') { quoted = !quoted; any = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted) { if (any || cur.Length > 0) list.Add(cur.ToString()); cur.Clear(); any = false; continue; }
                cur.Append(c);
            }
            if (any || cur.Length > 0) list.Add(cur.ToString());
            return list;
        }
    }
}
