// The AutoHotkey scripts for Mesen - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are the pack's (Mehdi 07/10), bound by the plugin in Mesen's settings.json when the action had none
// (MesenLaunch.BindPackKeys): F2 SaveState, F4 LoadState (the current slot, slot 1 at every start), F6 / F7 the slot down /
// up, Esc Exit; F11 is Mesen's own full screen. A key the user rebinds in Mesen makes these scripts send the old one. Mesen
// hears keys only while its window has the focus (Preferences.AllowBackgroundInput false by default), as these scripts send.
//
// QUITTING closes the window (WinClose = WM_CLOSE): Mesen's main window closes the game, which writes its battery file and its
// RecentGames entry, then settings.json - measured 18/10: gone in 0.1-0.2 s, exit code 0 (once 16.7 s).

namespace LbIntegrations.Mesen
{
    internal static class MesenAhk
    {
        public const string Running =
            "; Mesen: F2 saves a state, F4 loads it, F6 / F7 change the slot, F11 full screen, Esc quits\r\n"
            + "; (bound by the Nixx plugin where Mesen had none). The Exit script closes the window, which writes the game's save.";

        public const string Exit =
            "; Mesen writes the game's save when its window is closed.\r\n"
            + "WinClose, ahk_exe Mesen.exe";

        public const string SaveState =
            "; Mesen saves a state with F2 (bound by the Nixx plugin).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; Mesen loads a state with F4 (bound by the Nixx plugin).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts an earlier build wrote: none yet (keep this list when a script changes).</summary>
        public static readonly string[] Superseded = { };
    }
}
