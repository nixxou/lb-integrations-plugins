// The AutoHotkey scripts for ares - what the pause screens of LaunchBox and BigBox send.
//
// THE STATE KEYS are the ones the plugin binds when ares has none - RetroArch's (AresLaunch.Hotkeys: F2 Save State, F4
// Load State; ares ships every hotkey unbound). They act on the current slot, 1 at every load. A key the user rebinds in
// Settings > Hotkeys makes these scripts send the old one. ares hears them only while its window has the focus.
//
// QUITTING closes the window (WinClose = WM_CLOSE): ares unloads the game, which writes its save and settings.bml.

namespace LbIntegrations.Ares
{
    internal static class AresAhk
    {
        public const string Running =
            "; ares: F2 saves a state, F4 loads it (RetroArch's keys, bound by the Nixx plugin when ares had none).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.";

        public const string Exit =
            "; ares writes the game's save when its window is closed.\r\n"
            + "WinClose, ahk_exe ares.exe";

        public const string SaveState =
            "; ares saves a state with F2 (bound by the Nixx plugin).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; ares loads a state with F4 (bound by the Nixx plugin).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts an earlier build wrote (F5 / F7): replaced by the ones above.</summary>
        public static readonly string[] Superseded =
        {
            "; ares: F5 saves a state, F7 loads it (bound by the Nixx plugin when ares had none).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.",
            "; ares saves a state with F5 (bound by the Nixx plugin).\r\nSend {F5 down}\r\nSleep 50\r\nSend {F5 up}",
            "; ares loads a state with F7 (bound by the Nixx plugin).\r\nSend {F7 down}\r\nSleep 50\r\nSend {F7 up}",
        };
    }
}
