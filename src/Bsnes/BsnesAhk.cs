// The AutoHotkey scripts for bsnes - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are the ones the plugin binds when bsnes has none - the pack's (BsnesLaunch.Hotkeys, Mehdi, 07/10: F2
// Save State, F4 Load State, F6 / F7 the slot, F11 full screen, Esc quit - bsnes ships them unbound). They act on the
// current quick slot, Quick/Slot 1 at every start. A key the user rebinds in Settings > Hotkeys makes these scripts send
// the old one.
//
// QUITTING closes the window (WinClose = WM_CLOSE): bsnes unloads the game, which writes its .srm, then writes
// settings.bml - as its own close button.

namespace LbIntegrations.Bsnes
{
    internal static class BsnesAhk
    {
        public const string Running =
            "; bsnes: F2 saves the quick state, F4 loads it, F6 / F7 change the slot, F11 full screen, Esc quits\r\n"
            + "; (bound by the Nixx plugin when bsnes had none). The Exit script closes the window, which writes the game's save.";

        public const string Exit =
            "; bsnes writes the game's .srm when its window is closed.\r\n"
            + "WinClose, ahk_exe bsnes.exe";

        public const string SaveState =
            "; bsnes saves the quick state with F2 (bound by the Nixx plugin).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; bsnes loads the quick state with F4 (bound by the Nixx plugin).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts an earlier build wrote (F5 / F7, then the F2 / F4 note of 06/10): replaced by the ones above.</summary>
        public static readonly string[] Superseded =
        {
            "; bsnes: F2 saves the quick state, F4 loads it (RetroArch's keys, bound by the Nixx plugin when bsnes had none).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.",
            "; bsnes: F5 saves the quick state, F7 loads it (bound by the Nixx plugin when bsnes had none).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.",
            "; bsnes saves the quick state with F5 (bound by the Nixx plugin).\r\nSend {F5 down}\r\nSleep 50\r\nSend {F5 up}",
            "; bsnes loads the quick state with F7 (bound by the Nixx plugin).\r\nSend {F7 down}\r\nSleep 50\r\nSend {F7 up}",
        };
    }
}
