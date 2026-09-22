// The AutoHotkey scripts LaunchBox and BigBox send from their pause screen.
//
// Shaped like Unbroken's own, read out of their Emulators.xml - a comment naming the key, then
// down, pause, up rather than a bare Send, because an emulator polling the keyboard can miss a
// keystroke that goes down and up inside one frame.
//
// FROM THE FILE, NEVER FROM A CONSTANT. The scripts quote whatever controls.ini ends up saying,
// including a binding the user made himself. A script naming a key the emulator does not listen to
// fails silently, and the user has no way to tell which of the two is lying.
//
// EXIT IS LEFT TO THE COMMAND LINE, and needs no script: --escape-exit makes PPSSPP quit on Escape
// by itself, which is what a frontend's Exit sends. Simulating keystrokes for something the
// emulator already has a flag for would be strictly worse. The menu is on F1 - see PpssppHotkeys.

using System.Collections.Generic;

namespace LbIntegrations.Ppsspp
{
    internal static class PpssppAhk
    {
        /// <summary>AutoHotkey's name for the Android keycodes we can bind. Deliberately only those:
        /// returning null for anything else is what keeps a wrong script from being written.</summary>
        private static readonly Dictionary<int, string> KeyNames = new Dictionary<int, string>
        {
            [131] = "F1", [132] = "F2", [133] = "F3", [134] = "F4",
            [135] = "F5", [136] = "F6", [137] = "F7", [138] = "F8",
            [139] = "F9", [140] = "F10", [141] = "F11", [142] = "F12",
            [61] = "Tab", [62] = "Space", [111] = "Escape", [66] = "Enter",
        };

        public static string SaveState(PpssppHotkeyTable table)
            => Script(table, PpssppHotkeys.SaveState, "saves state");

        public static string LoadState(PpssppHotkeyTable table)
            => Script(table, PpssppHotkeys.LoadState, "loads state");

        private static string Script(PpssppHotkeyTable table, string entry, string what)
        {
            var code = table?.KeyFor(entry);
            if (code == null || !KeyNames.TryGetValue(code.Value, out var key))
                return "; PPSSPP binds no key to \"" + entry + "\" by default, and this plugin could not\r\n"
                     + "; add one. Map it under Settings > Controls > Keyboard, then set this script.";

            return "; PPSSPP " + what + " with the " + key + " key\r\n"
                 + "Send {" + key + " down}\r\n"
                 + "Sleep 50\r\n"
                 + "Send {" + key + " up}";
        }
    }
}
