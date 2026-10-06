// The AutoHotkey scripts LaunchBox and Big Box run for Vita3K: the one that runs alongside the emulator,
// and the one their pause screen's Exit sends. No save and load state scripts: Vita3K has no save
// states at all (none in its source, measured 28/09), and a script for a key that does nothing would
// only pretend otherwise.
//
// FULL SCREEN IS ALREADY F11, the pack's key (Mehdi, 07/10): keyboard-gui-fullscreen in config.yml,
// F11 by default (config.h; ctrl_keyboard_filter.cpp toggles on it). Nothing is bound: a config.yml
// that says otherwise was changed by the user. No slot keys either - no save states.
//
// QUITTING IS CLOSING THE WINDOW. Vita3K binds no quit key, and a frontend's Exit sends Escape - so
// Escape, and the Exit script, close Vita3K's window, which is what its own close button does: the
// game is stopped, and the plugin's watcher ends a Vita3K still there five seconds after its log says
// the game closed (Vita3kGameClosed). The confirmation box ("An app is still running...") is turned
// off in the emulator's own settings (Vita3kConfig.QuietExitConfirm), or this close would stop at it.
//
// The $ prefix keeps the hotkey from firing on a keystroke the script itself sends.

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kAhk
    {
        /// <summary>In Vita3K's window only, and ending by itself once Vita3K has gone (Mehdi, 04/10) - see LbipAhk.</summary>
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; Vita3K binds no quit key; Escape is what a frontend's Exit sends, so it closes Vita3K's window.",
            Vita3kPaths.ExecutableNames, "WinClose, ahk_exe Vita3K.exe");

        /// <summary>What this plugin wrote before: a field still holding one of these is brought up to date.</summary>
        public static readonly string[] PreviousRunning =
        {
            "; Vita3K binds no quit key; Escape is what a frontend's Exit sends, so it closes Vita3K's window.\r\n"
            + "$Esc::WinClose, ahk_exe Vita3K.exe",
        };

        public const string Exit =
            "; Vita3K quits by closing its window - the game stops, and the plugin ends a Vita3K still there 5 s later.\r\n"
            + "WinClose, ahk_exe Vita3K.exe";
    }
}
