// The AutoHotkey scripts for puNES - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS ARE puNES's OWN (input.cfg [shortcuts], 0.111's defaults measured 18/10 on a fresh file): F1 saves the state of
// the current slot, F4 loads it, F2 / F3 the slot down / up (12 slots, 0-9, A, B), Alt+Enter full screen, Alt+Q quits.
// The pack's keys (F2 save, F4 load, F6 / F7 slot, F11 full screen, Esc quit) are bound only when free, and on a default
// install every one of them is taken by a puNES default (PunesLaunch, its header) - so these scripts send puNES's. A key
// the user rebinds in puNES's Settings > Input makes them send the old one.
//
// QUITTING closes the window (WinClose = WM_CLOSE): measured 18/10, in a window and in full screen alike, puNES quits at
// once with code 0, no question, after writing the game's .prb and puNES.cfg - as its own close button.

namespace LbIntegrations.Punes
{
    internal static class PunesAhk
    {
        public const string Running =
            "; puNES: F1 saves the state, F4 loads it, F2 / F3 change the slot, Alt+Enter full screen, Alt+Q quits - puNES's own keys\r\n"
            + "; (the pack's F2 / F6 / F7 / F11 / Esc are puNES's defaults for other things). The Exit script closes the window, which\r\n"
            + "; writes the game's save.";

        /// <summary>The window closed, for the exe the entry runs (punes64.exe, punes.exe, a "_p" one).</summary>
        public static string Exit(string exeName) =>
            "; puNES writes the game's save when its window is closed.\r\n"
            + "WinClose, ahk_exe " + (string.IsNullOrWhiteSpace(exeName) ? PunesLaunch.ExecutableName : exeName);

        public const string SaveState =
            "; puNES saves the state of the current slot with F1 (its default key).\r\n"
            + "Send {F1 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 up}";

        public const string LoadState =
            "; puNES loads the state of the current slot with F4 (its default key).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts an earlier build wrote: replaced by the ones above. None yet.</summary>
        public static readonly string[] Superseded = { };
    }
}
