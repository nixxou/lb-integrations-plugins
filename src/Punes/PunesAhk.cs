// The AutoHotkey scripts for puNES - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS ARE THE PACK'S, set in puNES's input.cfg by the plugin before each launch (PunesLaunch, its header - Mehdi
// 18/10): F2 saves the state of the current slot, F4 loads it, F6 / F7 the slot down / up (12 slots, 0-9, A, B), F11 full
// screen, Esc quits, Shift+Esc hard reset, F10 / Shift+F10 PAL / NTSC - and RetroArch's P pause, Space fast forward (held:
// L), F8 screenshot, F9 mute, H soft reset. An action the user rebinds in puNES's Settings >
// Input keeps the user's key, and these scripts then send the pack's.
//
// QUITTING closes the window (WinClose = WM_CLOSE): measured 18/10, in a window and in full screen alike, puNES quits at
// once with code 0, no question, after writing the game's .prb and puNES.cfg - as its own close button.

namespace LbIntegrations.Punes
{
    internal static class PunesAhk
    {
        public const string Running =
            "; puNES: F2 saves the state, F4 loads it, F6 / F7 change the slot, F11 full screen, Esc quits - the pack's keys, set in\r\n"
            + "; puNES's input.cfg by the plugin. The Exit script closes the window, which writes the game's save.";

        /// <summary>The window closed, for the exe the entry runs (punes64.exe, punes.exe, a "_p" one).</summary>
        public static string Exit(string exeName) =>
            "; puNES writes the game's save when its window is closed.\r\n"
            + "WinClose, ahk_exe " + (string.IsNullOrWhiteSpace(exeName) ? PunesLaunch.ExecutableName : exeName);

        public const string SaveState =
            "; puNES saves the state of the current slot with F2 (the pack's key, set in its input.cfg).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; puNES loads the state of the current slot with F4 (the pack's key, set in its input.cfg).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts an earlier build wrote: replaced by the ones above - puNES's own keys (F1 save), before 18/10.</summary>
        public static readonly string[] Superseded =
        {
            "; puNES: F1 saves the state, F4 loads it, F2 / F3 change the slot, Alt+Enter full screen, Alt+Q quits - puNES's own keys\r\n"
            + "; (the pack's F2 / F6 / F7 / F11 / Esc are puNES's defaults for other things). The Exit script closes the window, which\r\n"
            + "; writes the game's save.",
            "; puNES saves the state of the current slot with F1 (its default key).\r\n"
            + "Send {F1 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 up}",
            "; puNES loads the state of the current slot with F4 (its default key).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}",
        };
    }
}
