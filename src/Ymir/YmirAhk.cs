// The AutoHotkey scripts for Ymir - what runs beside it, and what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are the pack's (Mehdi 20/10, docs\ymir.md §12). Ymir's own, bound in Ymir.toml by the plugin (YmirLaunch
// .BindPackKeys): F2 save state, F4 load state, F11 full screen. What Ymir cannot do itself is this script's, in Ymir's
// window only and ending with it (LbipAhk.EscapeScript):
//   Esc          quit - WinClose (WM_CLOSE: Ymir closes cleanly, code 0, and writes its settings - measured 20/10);
//                Ymir refuses Escape in its own bindings.
//   F6 / F7      the slot down / up: Ymir has no "next slot", only "select slot 1..10" on 1..0 (its defaults) - the
//                script counts the slot (1 at the start, as Ymir) and sends that key.
//   PageDown /   the next / previous disc (RetroArch's keys): the discs of the game are in lbip-discs.txt beside the exe
//   PageUp       (YmirLaunch.WriteDiscs - the disc started on the first line); the script opens Ymir's Load Disc (Ctrl+O, a
//                Windows file dialog "Load Sega Saturn disc image"), writes the next one's path in its file name box and
//                presses Enter - measured 20/10: the game ran on with the other disc.
// A key the user rebinds in Ymir makes these scripts send the old one.

namespace LbIntegrations.Ymir
{
    internal static class YmirAhk
    {
        private const string Exe = YmirLaunch.ExecutableName;

        /// <summary>The first build's functions (20/10): kept word for word, so its script is recognised as ours and replaced.</summary>
        private const string FunctionsV1 =
            "#If NixxEmulatorActive()\r\n"
            + "$F6::YmirSlot(-1)\r\n"
            + "$F7::YmirSlot(1)\r\n"
            + "$PgDn::YmirDisc(1)\r\n"
            + "$PgUp::YmirDisc(-1)\r\n"
            + "#If\r\n"
            + "YmirSlot(step) {\r\n"
            + "    global YmirSlotNow\r\n"
            + "    if (YmirSlotNow = \"\")\r\n"
            + "        YmirSlotNow := 1\r\n"
            + "    YmirSlotNow := Mod(YmirSlotNow - 1 + step + 10, 10) + 1\r\n"
            + "    key := Mod(YmirSlotNow, 10)\r\n"
            + "    Send {%key% down}\r\n"
            + "    Sleep 50\r\n"
            + "    Send {%key% up}\r\n"
            + "}\r\n"
            + "YmirDisc(step) {\r\n"
            + "    global YmirDiscNow\r\n"
            + "    WinGet, exe, ProcessPath, ahk_exe " + Exe + "\r\n"
            + "    if (exe = \"\")\r\n"
            + "        return\r\n"
            + "    SplitPath, exe,, dir\r\n"
            + "    FileRead, text, %dir%\\" + YmirLaunch.DiscsFile + "\r\n"
            + "    if ErrorLevel\r\n"
            + "        return\r\n"
            + "    discs := []\r\n"
            + "    Loop, Parse, text, `n, `r\r\n"
            + "        if (A_LoopField != \"\")\r\n"
            + "            discs.Push(A_LoopField)\r\n"
            + "    count := discs.Length() - 1\r\n"
            + "    if (count < 2)\r\n"
            + "        return\r\n"
            + "    if (YmirDiscNow = \"\")\r\n"
            + "        YmirDiscNow := discs[1] + 0\r\n"
            + "    YmirDiscNow := Mod(YmirDiscNow - 1 + step + count, count) + 1\r\n"
            + "    path := discs[YmirDiscNow + 1]\r\n"
            + "    Send ^o\r\n"
            + "    WinWait, ahk_class #32770 ahk_exe " + Exe + ",, 5\r\n"
            + "    if ErrorLevel\r\n"
            + "        return\r\n"
            + "    ControlSetText, Edit1, %path%\r\n"
            + "    Sleep 150\r\n"
            + "    ControlSend, Edit1, {Enter}\r\n"
            + "}";

        /// <summary>The disc changed, said on the screen (Mehdi 20/10: the swap is too quick to be seen): "Disc 2 / 2" and the
        /// file's name, 3 s at the top, a window that never takes the focus nor the clicks (WS_EX_NOACTIVATE | TRANSPARENT).</summary>
        private static readonly string Functions = FunctionsV1.Replace(
            "    ControlSend, Edit1, {Enter}\r\n}",
            "    ControlSend, Edit1, {Enter}\r\n"
            + "    SplitPath, path, name\r\n"
            + "    YmirSay(\"Disc \" YmirDiscNow \" / \" count \"  -  \" name)\r\n"
            + "}\r\n"
            + "YmirSay(text) {\r\n"
            + "    Gui, YmirNote:Destroy\r\n"
            + "    Gui, YmirNote:+AlwaysOnTop -Caption +ToolWindow +E0x08000020\r\n"
            + "    Gui, YmirNote:Margin, 18, 10\r\n"
            + "    Gui, YmirNote:Color, 202024\r\n"
            + "    Gui, YmirNote:Font, s13 cFFFFFF, Segoe UI Semibold\r\n"
            + "    Gui, YmirNote:Add, Text,, %text%\r\n"
            + "    Gui, YmirNote:Show, NoActivate xCenter y40 AutoSize\r\n"
            + "    hide := Func(\"YmirNoteHide\")\r\n"
            + "    SetTimer, % hide, -3000\r\n"
            + "}\r\n"
            + "YmirNoteHide() {\r\n"
            + "    Gui, YmirNote:Destroy\r\n"
            + "}");

        private const string Comment =
            "; Ymir: F2 saves a state, F4 loads it, F11 full screen (bound by the Nixx plugin in Ymir.toml);\r\n"
            + "; F6 / F7 change the slot, PageDown / PageUp the disc, Escape quits - this script, in Ymir's window only.";

        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(Comment, new[] { Exe }, "WinClose, ahk_exe " + Exe, Functions);

        public const string Exit =
            "; Ymir quits cleanly, writing its settings, when its window is closed.\r\n"
            + "WinClose, ahk_exe " + Exe;

        public const string SaveState =
            "; Ymir saves a state with F2.\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        public const string LoadState =
            "; Ymir loads a state with F4 (bound by the Nixx plugin).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>The scripts an earlier build wrote (keep this list when a script changes): the first build's, without the
        /// disc's notice.</summary>
        public static readonly string[] Superseded =
        {
            LbIntegrations.Lbip.LbipAhk.EscapeScript(Comment, new[] { Exe }, "WinClose, ahk_exe " + Exe, FunctionsV1),
        };
    }
}
