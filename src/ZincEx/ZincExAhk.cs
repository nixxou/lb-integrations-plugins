// The AutoHotkey scripts LaunchBox and Big Box run for ZiNc EX - the pack's keys where ZiNc has them (Mehdi, 07/10: Esc
// quit, F11 full screen; F2 / F4 / F6 / F7 are save states, and ZiNc has none).
//
// THE GAME IS ZiNc.exe, not ZiNc EX: ZiNc EX starts it and waits for it (settings.c run_game_cli), so the keys go to
// ZiNc's window. ESCAPE IS ZiNc's OWN QUIT KEY ("ESC - exits the emulator", its readme): the script sends it, held 100 ms
// (the input plugin reads the keyboard each frame), and closes the window only when ZiNc is still there 3 s later.
// FULL SCREEN is Alt+Enter in ZiNc EX's renderers ("Alt+Enter toggles fullscreen", its readme): F11 sends it, in ZiNc's
// window only. No save state: ZiNc 1.1 has none, so those two scripts stay empty.
//
// The $ prefix keeps a hotkey from firing on a keystroke the script itself sends. AutoHotkey v1, as LaunchBox runs it.

namespace LbIntegrations.ZincEx
{
    internal static class ZincExAhk
    {
        public const string GameExecutable = "ZiNc.exe";

        private const string Quit =
            "ZincQuit() {\r\n"
            + "    ; ZiNc's own quit key, then its window closed if it is still there.\r\n"
            + "    WinActivate, ahk_exe ZiNc.exe\r\n"
            + "    Sleep, 50\r\n"
            + "    Send, {Esc down}\r\n"
            + "    Sleep, 100\r\n"
            + "    Send, {Esc up}\r\n"
            + "    WinWaitClose, ahk_exe ZiNc.exe,, 3\r\n"
            + "    if (ErrorLevel)\r\n"
            + "        WinClose, ahk_exe ZiNc.exe\r\n"
            + "}";

        /// <summary>In ZiNc's window only, and ending by itself once ZiNc has gone - see LbipAhk. Escape quits as it always
        /// does in ZiNc (held long enough to be read, the window closed if it was not); F11 toggles full screen.</summary>
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; Escape quits ZiNc (its own key, held long enough to be read). F11 toggles full screen (ZiNc EX's Alt+Enter).\r\n"
            + "; ZiNc 1.1 has no save states.",
            new[] { GameExecutable }, "ZincQuit()\r\n$F11::Send, !{Enter}", Quit);

        public const string Exit =
            "; ZiNc quits on Escape, its own key - or its window is closed when it does not.\r\n"
            + "ZincQuit()\r\n"
            + Quit;

        /// <summary>What this plugin wrote before: none - no earlier build set any script.</summary>
        public static readonly string[] Previous = new string[0];
    }
}
