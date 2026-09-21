// The AutoHotkey scripts LaunchBox runs from its pause screen, built from the keys Flycast really has.
//
// SHAPE. Copied from LaunchBox's own Emulators.xml, so ours look like theirs and behave the same. For
// RetroArch they ship:
//
//     ; RetroArch saves state with F2 key by default
//     Send {F2 down}
//     Sleep 50
//     Send {F2 up}
//
// Down, pause, up rather than a bare Send: an emulator polling the keyboard can miss a keystroke that
// goes down and up within one frame.
//
// FROM THE TABLE, NEVER FROM A CONSTANT. The scripts quote whatever FlycastHotkeys ended up with,
// including a binding the user had made himself. A script that names a key the emulator does not
// listen to is worse than none - it fails silently, and the user has no way to tell which of the two
// is lying.
//
// AN UNBOUND ACTION GETS A COMMENT AND NOTHING ELSE. Both LaunchBox and LiteBox treat a script whose
// lines are all blank or comments as empty and never launch AutoHotkey for it (LiteBox mirrors
// AutoHotkey.GetIsScriptEmpty - Host/AhkScript.cs:16-17), so the comment costs nothing at runtime and
// says why in the emulator's edit window.
//
// WHERE THEY ARE USED. LaunchBox and BigBox run these from the pause screen. LiteBox stores and edits
// them but does not run them yet - it has no pause screen, and only the "Running" script is launched.
// They are still worth setting: the emulator entry is shared between the two front-ends.

using System.Collections.Generic;

namespace LbIntegrations.Flycast
{
    internal static class FlycastAhk
    {
        /// <summary>AutoHotkey's name for an SDL scancode, for the keys we can bind. Only the ones
        /// that can appear in our table - this is not a general scancode map, and returning null for
        /// anything else is what keeps a wrong script from being written.</summary>
        private static readonly Dictionary<int, string> KeyNames = new Dictionary<int, string>
        {
            [41] = "Escape",
            [58] = "F1", [59] = "F2", [60] = "F3", [61] = "F4",
            [62] = "F5", [63] = "F6", [64] = "F7", [65] = "F8",
            [66] = "F9", [67] = "F10", [68] = "F11", [69] = "F12",
            [43] = "Tab", [44] = "Space", [40] = "Enter",
        };

        public static string SaveState(HotkeyTable table)
            => Script(table, FlycastHotkeys.OptSaveState, "saves state",
                      "Flycast has no save-state key of its own; this plugin binds one on install.");

        public static string LoadState(HotkeyTable table)
            => Script(table, FlycastHotkeys.OptLoadState, "loads state",
                      "Flycast has no load-state key of its own; this plugin binds one on install.");

        public static string Exit(HotkeyTable table)
            => Script(table, FlycastHotkeys.OptEscape, "exits",
                      "Flycast ignores Escape unless it is bound; this plugin binds it on install.");

        private static string Script(HotkeyTable table, string option, string what, string whenMissing)
        {
            var code = table?.KeyFor(option);
            if (code == null || !KeyNames.TryGetValue(code.Value, out var key))
                return "; " + whenMissing + "\r\n"
                     + "; Bind it in Flycast under Settings > Controls, then set this script.";

            return "; Flycast " + what + " with the " + key + " key\r\n"
                 + "Send {" + key + " down}\r\n"
                 + "Sleep 50\r\n"
                 + "Send {" + key + " up}";
        }
    }
}
