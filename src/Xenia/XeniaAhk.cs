// The AutoHotkey scripts for Xenia, and the honest answer where there is nothing to bind.
//
// WHAT XENIA ACTUALLY HAS, read off its own key handler (src/xenia/app/emulator_window.cc):
//
//     F1   FAQ                F2   build commit        F3   profiler
//     F4   GPU trace frame    F5   clear GPU caches    F6   display config
//     F9   previously played  F11  fullscreen          F12  screenshot
//     Esc  leaves fullscreen - and ONLY that; it does not quit
//
// SAVE STATES DO NOT EXIST in a release build. F7 and F8 call SaveToFile / RestoreFromFile, both
// sit behind `#ifdef DEBUG`, and both write a hard-coded "test.sav" with a TODO beside them. So the
// save-state scripts here are a comment and nothing else: a script that sends F7 would do nothing in
// the build this plugin installs, and a script that silently does nothing is worse than none.
//
// QUITTING IS Alt+F4. Xenia's own File menu spells it out - the item reads "E&xit" with the shortcut
// "Alt+F4" - and Escape is taken by fullscreen. Every front-end's Exit means Escape, so the running
// script remaps it, which is exactly what Unbroken's own Xemu plugin does:
//
//     newEmulator.AutoHotkeyScript = "$Esc::Send, !{F4}";
//
// The $ prefix matters: without it the hotkey would fire on the keystroke the script itself sends
// and loop.

namespace LbIntegrations.Xenia
{
    internal static class XeniaAhk
    {
        /// <summary>Runs alongside the emulator. Makes Escape close Xenia, because Xenia leaves
        /// Escape to fullscreen and a front-end's Exit button sends Escape.</summary>
        public const string Running =
            "; Xenia quits with Alt+F4 (its own File menu says so) and leaves Escape to fullscreen.\r\n"
            + "; Escape is what a frontend's Exit sends, so map it across.\r\n"
            + "$Esc::Send, !{F4}";

        public const string Exit =
            "; Xenia quits with Alt+F4 - see its File menu, \"E&xit  Alt+F4\".\r\n"
            + "Send {Alt down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4}\r\n"
            + "Sleep 50\r\n"
            + "Send {Alt up}";

        /// <summary>No script, and the comment says why. Both hosts treat an all-comment script as
        /// empty and never launch AutoHotkey for it.</summary>
        public const string SaveState =
            "; Xenia has no save states in a release build: F7 and F8 sit behind #ifdef DEBUG and\r\n"
            + "; write a hard-coded test.sav. There is no key to send.";

        public const string LoadState = SaveState;
    }
}
