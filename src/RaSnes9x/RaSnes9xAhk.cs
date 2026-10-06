// The AutoHotkey scripts for RASnes9x - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are Snes9x's defaults, read off the snes9x.conf RASnes9x 1.2 writes at its first start (06/10):
// Key:SaveSlot0 = F1 with Mods:SaveSlot0 = Shift, Key:LoadSlot0 = F1 with no modifier - slot 0, the .000 file. They are
// the user's to rebind in Input > Customize Hotkeys; a rebound key makes these scripts send the old one.
//
// QUITTING closes the window (WinClose = WM_CLOSE): Snes9x then writes the game's .srm and its snes9x.conf, as when its
// own close button is clicked - measured 06/10. In RetroAchievements' hardcore mode a state cannot be loaded: Snes9x
// asks whether to leave hardcore first.

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xAhk
    {
        public const string Running =
            "; RASnes9x: Shift+F1 saves a state in slot 0, F1 loads it (Snes9x's defaults).\r\n"
            + "; Nothing to remap. The Exit script closes the window, which writes the game's save.";

        public const string Exit =
            "; RASnes9x writes the game's .srm when its window is closed.\r\n"
            + "WinClose, ahk_exe RASnes9x.exe";

        public const string SaveState =
            "; RASnes9x saves a state in slot 0 with Shift+F1 (Snes9x's default).\r\n"
            + "Send {Shift down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 up}\r\n"
            + "Sleep 50\r\n"
            + "Send {Shift up}";

        public const string LoadState =
            "; RASnes9x loads the state of slot 0 with F1 (Snes9x's default). Not in hardcore mode.\r\n"
            + "Send {F1 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 up}";
    }
}
