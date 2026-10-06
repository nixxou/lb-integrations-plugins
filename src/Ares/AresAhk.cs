// The AutoHotkey scripts for ares - what the pause screens of LaunchBox and BigBox send.
//
// THE STATE KEYS are the ones the plugin binds when ares has none (AresLaunch: F5 Save State, F7 Load State - ares ships
// every hotkey unbound). They act on the current slot, 1 at every load. A key the user rebinds in Settings > Hotkeys
// makes these scripts send the old one.
//
// QUITTING closes the window (WinClose = WM_CLOSE): ares unloads the game, which writes its save and settings.bml.

namespace LbIntegrations.Ares
{
    internal static class AresAhk
    {
        public const string Running =
            "; ares: F5 saves a state, F7 loads it (bound by the Nixx plugin when ares had none).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.";

        public const string Exit =
            "; ares writes the game's save when its window is closed.\r\n"
            + "WinClose, ahk_exe ares.exe";

        public const string SaveState =
            "; ares saves a state with F5 (bound by the Nixx plugin).\r\n"
            + "Send {F5 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F5 up}";

        public const string LoadState =
            "; ares loads a state with F7 (bound by the Nixx plugin).\r\n"
            + "Send {F7 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F7 up}";
    }
}
