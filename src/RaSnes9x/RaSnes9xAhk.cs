// The AutoHotkey scripts for RASnes9x - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are the pack's (Mehdi, 07/10), bound by the plugin when Snes9x has them Unassigned (RaSnes9xLaunch.Hotkeys):
// F2 saves a state in the CURRENT slot, F4 loads it, F6 / F7 change the slot (Snes9x keeps it in CurrentSaveSlot, 0 -
// the .000 file - unless changed). Esc and full screen cannot be bound in Snes9x (RaSnes9xLaunch). The keys are the
// user's to rebind in Input > Customize Hotkeys; a rebound key makes these scripts send the old one.
//
// QUITTING closes the window (WinClose = WM_CLOSE): Snes9x then writes the game's .srm and its snes9x.conf, as when its
// own close button is clicked - measured 06/10. In RetroAchievements' hardcore mode a state cannot be loaded: Snes9x
// asks whether to leave hardcore first.

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xAhk
    {
        public const string Running =
            "; RASnes9x: F2 saves a state in the current slot, F4 loads it, F6 / F7 change the slot (bound by the Nixx plugin).\r\n"
            + "; Alt+Enter is full screen. The Exit script closes the window, which writes the game's save.";

        public const string Exit =
            "; RASnes9x writes the game's .srm when its window is closed.\r\n"
            + "WinClose, ahk_exe RASnes9x.exe";

        public const string SaveState =
            "; RASnes9x saves a state in the current slot with F2 (bound by the Nixx plugin).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; RASnes9x loads the state of the current slot with F4 (bound by the Nixx plugin). Not in hardcore mode.\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts the builds of 06/10 wrote (Snes9x's defaults, Shift+F1 / F1 on slot 0): replaced by the
        /// ones above. The Exit script has not changed.</summary>
        public static readonly string[] Superseded =
        {
            "; RASnes9x: Shift+F1 saves a state in slot 0, F1 loads it (Snes9x's defaults).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.",
            "; RASnes9x saves a state in slot 0 with Shift+F1 (Snes9x's default).\r\n"
            + "Send {Shift down}\r\nSleep 50\r\nSend {F1 down}\r\nSleep 50\r\nSend {F1 up}\r\nSleep 50\r\nSend {Shift up}",
            "; RASnes9x loads the state of slot 0 with F1 (Snes9x's default). Not in hardcore mode.\r\n"
            + "Send {F1 down}\r\nSleep 50\r\nSend {F1 up}",
        };
    }
}