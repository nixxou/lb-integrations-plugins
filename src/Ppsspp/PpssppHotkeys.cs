// Giving PPSSPP the save-state keys it ships without, and describing them to the host.
//
// PPSSPP HAS save states, unlike Xenia, and it binds no key to them. Measured on a real install:
// memstick\PSP\SYSTEM\controls.ini holds the COMPLETE mapping - every pad button, Fast-forward,
// Pause, Rewind - and carries no line at all for "Save State" or "Load State".
//
// THE FILE IS ADDITIVE, which makes this far safer than the same job on Flycast. Flycast's mapping
// file REPLACES its defaults, so writing one means reproducing all nineteen of them or the user
// loses his controls. PPSSPP's controls.ini already spells every binding out, so a missing line is
// a missing binding and nothing else: we add the four that are absent and touch nothing else.
//
// THE CODES ARE ANDROID KEYCODES, on every platform, written as "<device>-<keycode>" with device 1
// for the keyboard. Verified against the defaults in that same file rather than assumed:
//
//     Fast-forward = 1-61    KEYCODE_TAB = 61
//     Pause        = 1-111   KEYCODE_ESCAPE = 111
//     Start        = 1-62    KEYCODE_SPACE = 62
//     Cross        = 1-51    KEYCODE_X = 51
//
// so the function keys run F1 = 131 through F12 = 142.
//
// F2 AND F4 are ours to match RetroArch, which is what LaunchBox's own AutoHotkey scripts already
// assume, and what this repository's Flycast plugin uses.
//
// THE MENU MOVES TO F1 AND ESCAPE QUITS, which takes one move and one addition.
//
// PPSSPP binds Pause to Escape, and Escape is what every frontend's Exit sends. The menu therefore
// goes to F1, which nothing else uses, and the dedicated "Exit App" action takes Escape. Only the
// KEYBOARD part of Pause moves; its pad bindings stay.
//
// NOT --escape-exit, and its help text is why this was got wrong once. It reads "Escape key exits
// the application", but Core/CmdLine.cpp assigns it to bPauseExitsEmulator, and NativeApp.cpp acts
// on it like this:
//
//     if (g_Config.bPauseExitsEmulator) {
//       if (pspKeys contains VIRTKEY_PAUSE) { System_ExitApp(); return true; }
//     }
//
// It intercepts whatever key is bound to PAUSE - so with the menu on F1 it made F1 quit and left
// Escape doing nothing. The flag cannot coexist with a menu key at all.
//
// VIRTKEY_EXIT_APP is the real one, and it may ask for confirmation: GetConfirmExitMessage returns a
// prompt when the network is up, or after AskForExitConfirmationAfterSeconds of unsaved progress -
// 300 by default. That is PPSSPP protecting the user's progress, not something to work around here.
//
// Both changes happen only while the defaults are untouched: Pause must still hold exactly Escape,
// and the key we want must be free. A user who moved either has already decided.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Ppsspp
{
    /// <summary>What controls.ini says about the four actions we care about, after Ensure ran.</summary>
    internal sealed class PpssppHotkeyTable
    {
        /// <summary>Entry name ("Save State") to keyboard keycode, for the keyboard binding only.</summary>
        public readonly Dictionary<string, int> Keys = new Dictionary<string, int>(StringComparer.Ordinal);

        public string Path;
        public bool Written;
        public string Skipped;

        public int? KeyFor(string entry) => Keys.TryGetValue(entry, out var k) ? k : (int?)null;
    }

    internal static class PpssppHotkeys
    {
        public const string Section = "ControlMapping";

        public const string Pause = "Pause";
        public const string ExitApp = "Exit App";
        public const string SaveState = "Save State";
        public const string LoadState = "Load State";
        public const string PreviousSlot = "Previous Slot";
        public const string NextSlot = "Next Slot";

        /// <summary>Device 1 is the keyboard; the rest of a value's comma-separated parts are pads.</summary>
        private const int KeyboardDevice = 1;

        /// <summary>The menu key, and the one binding this plugin MOVES rather than adds.
        ///
        /// PPSSPP binds Pause to Escape. Escape is also what a frontend's Exit sends, and PPSSPP has
        /// a --escape-exit flag that makes it quit on that key - so leaving Pause there means the two
        /// fight over one key. The menu goes to F1, which nothing else uses, and Escape is left to
        /// quitting. The pad bindings on Pause are untouched: only the keyboard part moves.</summary>
        private const int PauseFromKey = 111;    // KEYCODE_ESCAPE
        private const int PauseToKey = 131;      // KEYCODE_F1

        /// <summary>Ours, with the Android keycode each F-key has.</summary>
        private static readonly (string Entry, int Code, string Key)[] OurKeys =
        {
            (SaveState, 132, "F2"),
            (LoadState, 134, "F4"),
            (PreviousSlot, 136, "F6"),
            (NextSlot, 137, "F7"),
        };

        /// <summary>Add the missing save-state bindings to controls.ini and report what it says.
        ///
        /// A file that does not exist is left alone: PPSSPP writes it on first run, and creating one
        /// ourselves would mean inventing the whole mapping, which is the Flycast trap this file does
        /// not have to fall into. <paramref name="mayEditExisting"/> false only reads.</summary>
        public static PpssppHotkeyTable Ensure(PpssppLayout layout, bool mayEditExisting)
        {
            var table = new PpssppHotkeyTable();
            try
            {
                var path = ControlsPath(layout);
                if (path == null) { table.Skipped = "no PPSSPP install to write into"; return table; }
                table.Path = path;

                if (!File.Exists(path))
                {
                    // PPSSPP has not run yet. Writing a mapping now would be guessing at every
                    // binding; it writes the complete file itself on first launch.
                    table.Skipped = "controls.ini does not exist yet - PPSSPP writes it on first run";
                    return table;
                }

                var wanted = OurKeys.Select(k => k.Entry).ToArray();
                var current = PpssppIni.Read(path, Section, wanted);
                Fill(table, current);

                if (!mayEditExisting) { table.Skipped = "read only"; return table; }

                // A key already used by ANY entry is not ours to take.
                var taken = TakenKeyboardCodes(path);
                var add = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var added = new List<string>();
                foreach (var (entry, code, key) in OurKeys)
                {
                    if (current.TryGetValue(entry, out var existing) && !string.IsNullOrWhiteSpace(existing))
                        continue;                                   // his binding wins
                    if (taken.Contains(code)) continue;             // and so does his key
                    add[entry] = KeyboardDevice + "-" + code;
                    table.Keys[entry] = code;
                    added.Add(key + " -> " + entry);
                }

                // THE ONE MOVE. Everything else here only fills a blank; this takes Escape away from
                // the menu so it can quit. Done only when Pause still holds exactly the default
                // Escape and F1 is free - a user who moved either has decided already.
                var pause = PpssppIni.Read(path, Section, Pause);
                if (pause.TryGetValue(Pause, out var pauseValue)
                    && KeyboardCodeOf(pauseValue) == PauseFromKey
                    && !taken.Contains(PauseToKey))
                {
                    add[Pause] = Rebind(pauseValue, PauseFromKey, PauseToKey);
                    table.Keys[Pause] = PauseToKey;
                    taken.Remove(PauseFromKey);          // Escape is free from this moment
                    taken.Add(PauseToKey);
                    added.Add("F1 -> " + Pause);
                }

                // And Escape, once free, quits - through the action PPSSPP has for it.
                var exit = PpssppIni.Read(path, Section, ExitApp);
                var exitValue = exit.TryGetValue(ExitApp, out var e) ? e : null;
                if (string.IsNullOrWhiteSpace(exitValue) && !taken.Contains(PauseFromKey))
                {
                    add[ExitApp] = KeyboardDevice + "-" + PauseFromKey;
                    table.Keys[ExitApp] = PauseFromKey;
                    added.Add("Escape -> " + ExitApp);
                }

                if (add.Count == 0) { table.Skipped = "every shortcut was already bound"; return table; }

                var error = PpssppIni.Write(path, Section, add);
                if (error != null) { table.Skipped = error; return table; }

                table.Written = true;
                Log.Info("added to " + path + ": " + string.Join(", ", added));
                return table;
            }
            catch (Exception ex)
            {
                Log.Warn("could not set up the save-state shortcuts", ex);
                table.Skipped = ex.GetType().Name + ": " + ex.Message;
                return table;
            }
        }

        private static string ControlsPath(PpssppLayout layout)
        {
            try
            {
                var system = layout?.SystemDir;
                return string.IsNullOrWhiteSpace(system) ? null : Path.Combine(system, "controls.ini");
            }
            catch { return null; }
        }

        private static void Fill(PpssppHotkeyTable table, IDictionary<string, string> current)
        {
            foreach (var pair in current)
            {
                var code = KeyboardCodeOf(pair.Value);
                if (code != null) table.Keys[pair.Key] = code.Value;
            }
        }

        /// <summary>The same value with one keyboard code swapped, every other part kept in place -
        /// the pad bindings on the same action are none of our business.</summary>
        private static string Rebind(string value, int from, int to)
        {
            var parts = (value ?? "").Split(',');
            for (var i = 0; i < parts.Length; i++)
                if (parts[i].Trim() == KeyboardDevice + "-" + from)
                    parts[i] = KeyboardDevice + "-" + to;
            return string.Join(",", parts.Select(x => x.Trim()).Where(x => x.Length > 0));
        }

        /// <summary>The keyboard code in a value like "1-132,20-4036": the first part whose device is
        /// the keyboard. A pad binding on the same action is none of our business.</summary>
        private static int? KeyboardCodeOf(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            foreach (var part in value.Split(','))
            {
                var bits = part.Trim().Split('-');
                if (bits.Length != 2) continue;
                if (!int.TryParse(bits[0], out var device) || device != KeyboardDevice) continue;
                if (int.TryParse(bits[1], out var code)) return code;
            }
            return null;
        }

        /// <summary>Every keyboard code the file already uses, whatever the action.</summary>
        private static HashSet<int> TakenKeyboardCodes(string path)
        {
            var taken = new HashSet<int>();
            try
            {
                var inSection = false;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("["))
                    {
                        inSection = line.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inSection || line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                    var eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    foreach (var part in line.Substring(eq + 1).Split(','))
                    {
                        var bits = part.Trim().Split('-');
                        if (bits.Length == 2 && int.TryParse(bits[0], out var d) && d == KeyboardDevice
                            && int.TryParse(bits[1], out var c))
                            taken.Add(c);
                    }
                }
            }
            catch { }
            return taken;
        }
    }
}
