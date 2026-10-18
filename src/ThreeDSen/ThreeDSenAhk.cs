// The AutoHotkey scripts for 3dSen - what the pause screens of LaunchBox and BigBox send, and Esc.
//
// THE KEYS are the ones the plugin binds in input.cfg (ThreeDSenInput, the pack's keys): F2 quick save, F4 quick load -
// 3dSen has ONE quick slot (sav\quick.sav), and its 36 numbered slots only from its own menu, now on F1.
//
// ESC QUITS, as everywhere in the pack (Mehdi 19/10): 3dSen has no quit action, so the running script closes its window on
// Esc, in 3dSen's window only (LbipAhk.EscapeScript) - its menu, which had Esc, is moved to F1 in input.cfg. The pack's
// F6 / F7 (slot) and F11 (full screen) have no counterpart: F5-F10 turn the camera, 3dSen has no full screen action.
//
// QUITTING closes the window (WinClose = WM_CLOSE): measured clean five times, 3dSen writes rom.json, input.cfg, sav.cfg
// and setting.cfg then exits with 0 (docs\3dsen.md section 3).

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenAhk
    {
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; 3dSen: F2 quick save, F4 quick load, F1 its menu (its 36 save slots, its settings), L fast forward - bound by the\r\n"
            + "; Nixx plugin where 3dSen had its defaults. 3dSen has no quit key: Escape closes its window, which writes its files.",
            new[] { "3dSen.exe" }, "WinClose, ahk_exe 3dSen.exe");

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

        /// <summary>Scripts an earlier build wrote, replaced by the ones above when found word for word.</summary>
        public static readonly string[] Superseded =
        {
            "; 3dSen: F2 quick save, F4 quick load (bound by the Nixx plugin when 3dSen had none). Esc opens 3dSen's menu:\r\n"
            + "; its 36 save slots, its settings. The Exit script closes the window, which writes 3dSen's files.",
        };
    }
}
