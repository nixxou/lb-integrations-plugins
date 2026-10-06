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
// THE HOTKEYS: bsnes ships every hotkey UNBOUND (input\hotkeys.cpp reads them from settings.bml, settings.cpp gives
// none), so the pause screens of LaunchBox and BigBox would have nothing to press. The pack's keys (Mehdi, 07/10): F2
// saves the quick state of the current slot, F4 loads it (BsnesAhk sends the same), F6 / F7 the slot down / up, F11
// full screen, Esc quits. Each node is bound only when it is empty or still what an earlier build wrote (F5/F7 for the
// states), and only when no other node - a hotkey or a pad button - already holds that key. A key the user bound is
// never changed.
//
// GAMES: bsnes opens .sfc/.smc, .zip and .7z itself (game.cpp, Program::loadSuperFamicom), so nothing is unpacked.

using System;
using System.Collections.Generic;
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

        /// <summary>The pack's hotkeys (Mehdi, 07/10). Node names: "Hotkey/" + the InputHotkey's name without spaces
        /// (input\hotkeys.cpp). Keys: the index in bsnes's ruby keyboard list (ruby\input\keyboard\rawinput.cpp: Escape 0,
        /// F1..F10 1..10, F11 11) - the same as ares's. Esc cannot be bound from bsnes's own Hotkeys page (Escape there
        /// clears a binding, input.cpp), but a binding read from the file works.</summary>
        internal static readonly (string Node, string Key)[] Hotkeys =
        {
            ("SaveState", SaveKey), ("LoadState", LoadKey), ("DecrementStateSlot", "0x1/0/6"), ("IncrementStateSlot", "0x1/0/7"),
            ("ToggleFullscreen", "0x1/0/11"), ("QuitEmulator", "0x1/0/0"),
        };

        /// <summary>What earlier builds of this plugin wrote, node by node: replaced by the keys above.</summary>
        private static readonly Dictionary<string, string[]> Superseded = new Dictionary<string, string[]>
        {
            ["SaveState"] = new[] { "Keyboard/1/Button/F5", "0x1/0/5" },   // F5, the builds of 06/10
            ["LoadState"] = new[] { "Keyboard/1/Button/F7", "0x1/0/7" },   // F7
        };

        /// <summary>Empty, or still an earlier build's value - bsnes writes a node's assignments joined with ';'.</summary>
        private static bool Ours(string node, string v)
        {
            var set = (v ?? "").Split(';').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
            return set.Count == 0 || (set.Count == 1 && Superseded.TryGetValue(node, out var old) && old.Contains(set[0]));
        }

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

        /// <summary>The game's options written into settings.bml, the pack's hotkeys bound when unbound. Never throws.</summary>
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

                // In the table's order: LoadState leaves F7 before IncrementStateSlot looks for it.
                foreach (var (node, key) in Hotkeys)
                {
                    var path = "Hotkey/" + node;
                    if (!Ours(node, bml.Get(path))) continue;
                    if (bml.BoundElsewhere(key, path)) { Log.Info("settings.bml: " + node + " left unbound - its key " + key + " is already used"); continue; }
                    bml.Set(path, key);
                }

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
