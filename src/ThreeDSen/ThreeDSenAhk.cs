// The AutoHotkey scripts for 3dSen - what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are the ones the plugin binds in input.cfg when 3dSen has none (ThreeDSenInput, the pack's keys): F2 quick
// save, F4 quick load - 3dSen has ONE quick slot (sav\quick.sav), and its 36 numbered slots only from its own menu.
// The pack's F6 / F7 (slot), F11 (full screen) and Esc (quit) are not 3dSen's: F5-F8 turn the camera, Esc opens its
// menu (docs\3dsen.md section 7, question Q3) - so no key is mapped for them, and Esc keeps opening 3dSen's menu.
//
// QUITTING closes the window (WinClose = WM_CLOSE): measured clean five times, 3dSen writes rom.json, input.cfg, sav.cfg
// and setting.cfg then exits with 0 (docs\3dsen.md section 3).

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenAhk
    {
        public const string Running =
            "; 3dSen: F2 quick save, F4 quick load (bound by the Nixx plugin when 3dSen had none). Esc opens 3dSen's menu:\r\n"
            + "; its 36 save slots, its settings. The Exit script closes the window, which writes 3dSen's files.";

        public const string Exit =
            "; 3dSen writes its files when its window is closed.\r\n"
            + "WinClose, ahk_exe 3dSen.exe";

        public const string SaveState =
            "; 3dSen's quick save is F2 (bound by the Nixx plugin).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; 3dSen's quick load is F4 (bound by the Nixx plugin).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>Scripts an earlier build wrote, replaced by the ones above when found word for word. None yet.</summary>
        public static readonly string[] Superseded = { };
    }
}
