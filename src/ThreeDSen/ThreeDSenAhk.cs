// The AutoHotkey scripts for 3dSen - what the pause screens of LaunchBox and BigBox send, and Esc.
//
// THE KEYS are the ones the plugin binds in input.cfg (ThreeDSenInput, the pack's keys): F2 quick save, F4 quick load -
// 3dSen has ONE quick slot (sav\quick.sav), and its 36 numbered slots only from its own menu, now on F1.
//
// ESC QUITS, as everywhere in the pack (Mehdi 19/10): 3dSen has no quit action - its menu, which had Esc, is moved to F1 in
// input.cfg. In 3dSen's window only (LbipAhk.EscapeScript):
//   - with the pack's in-process plugin in 3dSen's folder (ThreeDSenBepInEx), Esc goes through to it: the plugin asks
//     ("Press Esc again to quit") and quits on the second press, and F6 / F7 pick the slot (tools\3dsen-bepinex);
//   - without it, the running script closes the window on Esc, at once.
// The script looks at the folder of the 3dSen it sees running, so one script serves both. F11 (full screen) has no
// counterpart: 3dSen has no full screen action.
//
// QUITTING closes the window (WinClose = WM_CLOSE): measured clean five times, 3dSen writes rom.json, input.cfg, sav.cfg
// and setting.cfg then exits with 0 (docs\3dsen.md section 3).

namespace LbIntegrations.ThreeDSen
{
    internal static class ThreeDSenAhk
    {
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; 3dSen: F2 save, F4 load, F1 its menu (its 36 save slots, its settings), L fast forward - bound by the Nixx plugin\r\n"
            + "; where 3dSen had its defaults. Escape: with the pack's in-process plugin in 3dSen's folder it goes through to it\r\n"
            + "; (\"press Esc again to quit\"), else it closes 3dSen's window, which writes its files.",
            new[] { "3dSen.exe" }, "NixxEscape()",
            "NixxEscape() {\r\n"
            + "    WinGet, exe, ProcessPath, ahk_exe 3dSen.exe\r\n"
            + "    SplitPath, exe, , dir\r\n"
            + "    if (dir != \"\" && FileExist(dir \"\\winhttp.dll\") && FileExist(dir \"\\BepInEx\\plugins\\" + ThreeDSenBepInEx.PluginFileName + "\"))\r\n"
            + "        Send {Esc}\r\n"
            + "    else\r\n"
            + "        WinClose, ahk_exe 3dSen.exe\r\n"
            + "}");

        /// <summary>The running script of the build before the in-process plugin (Escape always closed the window).</summary>
        private static readonly string RunningWithoutPlugin = LbIntegrations.Lbip.LbipAhk.EscapeScript(
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
            RunningWithoutPlugin,
            "; 3dSen: F2 quick save, F4 quick load (bound by the Nixx plugin when 3dSen had none). Esc opens 3dSen's menu:\r\n"
            + "; its 36 save slots, its settings. The Exit script closes the window, which writes 3dSen's files.",
        };
    }
}
