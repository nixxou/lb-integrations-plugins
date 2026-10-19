// The AutoHotkey scripts for Kronos - what runs beside it, and what the pause screens of LaunchBox and BigBox send.
//
// THE KEYS are the pack's (docs\kronos.md §5). Kronos has no "save / load the current slot": only an action per slot, its
// digit shortcuts dead (measured 19/10), so the plugin binds Save<n> to Ctrl+Shift+F<n> and Load<n> to Shift+F<n> (slot 0 on
// F10, KronosLaunch.BindPackKeys) and this script, in Kronos's window only and ending with it (LbipAhk.EscapeScript), does:
//   F2 / F4      save / load a state in the slot it counts (1 at the start) - Kronos's own F2 (pause) and F4 (vertical
//                sync) never see the keys;
//   F6 / F7      the slot down / up, said on the screen;
//   F11          full screen: Kronos's Alt+Enter;
//   PageDown /   the next / previous disc (RetroArch's keys): the discs of the game are in lbip-discs.txt beside the exe
//   PageUp       (KronosLaunch.WriteDiscs - the disc started on the first line); the script opens Kronos's Eject/Load ISO
//                (Ctrl+I: its Windows file dialog "Select your iso/cue/bin/zip/chd file" - measured 19/10), writes the next
//                one's path in its file name box and presses Enter: the disc is changed without a reset (ui\UIYabause.cpp
//                runActions), as on the console. IN FULL SCREEN Ctrl+I opens nothing (measured 19/10: Qt's hidden menu bar):
//                the script leaves full screen (Alt+Enter) for the dialog and goes back to it after;
//   Esc          quit - Kronos's own Quit (Ctrl+Q), then WM_CLOSE 3 s later should it still be there. MEASURED 19/10: in full
//                screen a bare WM_CLOSE crashes Kronos as it quits (0xC0000005 in Qt5Gui.dll, three times out of three);
//                Ctrl+Q, or Alt+Enter then WM_CLOSE, quit with code 0 - its settings written, its backup RAM flushed.
// A slot key the user rebinds in Kronos makes these scripts send the old one.

namespace LbIntegrations.Kronos
{
    internal static class KronosAhk
    {
        private const string Exe = KronosLaunch.ExecutableName;

        private const string Functions =
            "#If NixxEmulatorActive()\r\n"
            + "$F2::KronosState(\"save\")\r\n"
            + "$F4::KronosState(\"load\")\r\n"
            + "$F6::KronosSlot(-1)\r\n"
            + "$F7::KronosSlot(1)\r\n"
            + "$F11::Send !{Enter}\r\n"
            + "$PgDn::KronosDisc(1)\r\n"
            + "$PgUp::KronosDisc(-1)\r\n"
            + "#If\r\n"
            + "KronosState(what) {\r\n"
            + "    global KronosSlotNow\r\n"
            + "    if (KronosSlotNow = \"\")\r\n"
            + "        KronosSlotNow := 1\r\n"
            + "    if (what = \"save\")\r\n"
            + "        Send ^+{F%KronosSlotNow%}\r\n"
            + "    else\r\n"
            + "        Send +{F%KronosSlotNow%}\r\n"
            + "    KronosSay((what = \"save\" ? \"State saved\" : \"State loaded\") \"  -  slot \" KronosSlotNow)\r\n"
            + "}\r\n"
            + "KronosSlot(step) {\r\n"
            + "    global KronosSlotNow\r\n"
            + "    if (KronosSlotNow = \"\")\r\n"
            + "        KronosSlotNow := 1\r\n"
            + "    KronosSlotNow := Mod(KronosSlotNow - 1 + step + 10, 10) + 1\r\n"
            + "    KronosSay(\"State slot \" KronosSlotNow)\r\n"
            + "}\r\n"
            + "KronosDisc(step) {\r\n"
            + "    global KronosDiscNow\r\n"
            + "    WinGet, exe, ProcessPath, ahk_exe " + Exe + "\r\n"
            + "    if (exe = \"\")\r\n"
            + "        return\r\n"
            + "    SplitPath, exe,, dir\r\n"
            + "    FileRead, text, %dir%\\" + KronosLaunch.DiscsFile + "\r\n"
            + "    if ErrorLevel\r\n"
            + "        return\r\n"
            + "    discs := []\r\n"
            + "    Loop, Parse, text, `n, `r\r\n"
            + "        if (A_LoopField != \"\")\r\n"
            + "            discs.Push(A_LoopField)\r\n"
            + "    count := discs.Length() - 1\r\n"
            + "    if (count < 2)\r\n"
            + "        return\r\n"
            + "    if (KronosDiscNow = \"\")\r\n"
            + "        KronosDiscNow := discs[1] + 0\r\n"
            + "    KronosDiscNow := Mod(KronosDiscNow - 1 + step + count, count) + 1\r\n"
            + "    entry := StrSplit(discs[KronosDiscNow + 1], \"`t\")\r\n"
            + "    path := entry[1]\r\n"
            + "    name := entry[2]\r\n"
            + "    WinGet, main, ID, A\r\n"
            + "    WinGetPos, x, y, w, h, ahk_id %main%\r\n"
            + "    full := (w >= A_ScreenWidth && h >= A_ScreenHeight)\r\n"
            + "    if (full) {\r\n"
            + "        Send !{Enter}\r\n"
            + "        Sleep 600\r\n"
            + "    }\r\n"
            + "    Send ^i\r\n"
            + "    WinWait, ahk_class #32770 ahk_exe " + Exe + ",, 5\r\n"
            + "    if ErrorLevel {\r\n"
            + "        if (full)\r\n"
            + "            Send !{Enter}\r\n"
            + "        return\r\n"
            + "    }\r\n"
            + "    ControlSetText, Edit1, %path%\r\n"
            + "    Sleep 150\r\n"
            + "    ControlSend, Edit1, {Enter}\r\n"
            + "    WinWaitClose, ahk_class #32770 ahk_exe " + Exe + ",, 5\r\n"
            + "    if (full) {\r\n"
            + "        WinActivate, ahk_id %main%\r\n"
            + "        Sleep 300\r\n"
            + "        Send !{Enter}\r\n"
            + "    }\r\n"
            + "    if (name = \"\")\r\n"
            + "        SplitPath, path, name\r\n"
            + "    KronosSay(\"Disc \" KronosDiscNow \" / \" count \"  -  \" name)\r\n"
            + "}\r\n"
            + "KronosQuit() {\r\n"
            + "    Send ^q\r\n"
            + "    WinWaitClose, ahk_exe " + Exe + ",, 3\r\n"
            + "    if ErrorLevel\r\n"
            + "        WinClose, ahk_exe " + Exe + "\r\n"
            + "}\r\n"
            + "KronosSay(text) {\r\n"
            + "    Gui, KronosNote:Destroy\r\n"
            + "    Gui, KronosNote:+AlwaysOnTop -Caption +ToolWindow +E0x08000020\r\n"
            + "    Gui, KronosNote:Margin, 18, 10\r\n"
            + "    Gui, KronosNote:Color, 202024\r\n"
            + "    Gui, KronosNote:Font, s13 cFFFFFF, Segoe UI Semibold\r\n"
            + "    Gui, KronosNote:Add, Text,, %text%\r\n"
            + "    Gui, KronosNote:Show, NoActivate xCenter y40 AutoSize\r\n"
            + "    hide := Func(\"KronosNoteHide\")\r\n"
            + "    SetTimer, % hide, -2500\r\n"
            + "}\r\n"
            + "KronosNoteHide() {\r\n"
            + "    Gui, KronosNote:Destroy\r\n"
            + "}";

        private const string Comment =
            "; Kronos: F2 saves a state, F4 loads it, F6 / F7 change the slot, F11 full screen, PageDown / PageUp the disc,\r\n"
            + "; Escape quits - this script, in Kronos's window only (the slot keys are bound by the Nixx plugin in kronos.ini).";

        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(Comment, new[] { Exe }, "KronosQuit()", Functions);

        /// <summary>Ctrl+Q first: a bare WM_CLOSE crashes Kronos in full screen (header).</summary>
        public const string Exit =
            "; Kronos quits cleanly with its own Quit (Ctrl+Q); its window closed 3 s later should it still be there.\r\n"
            + "WinActivate, ahk_exe " + Exe + "\r\n"
            + "Send ^q\r\n"
            + "WinWaitClose, ahk_exe " + Exe + ",, 3\r\n"
            + "if ErrorLevel\r\n"
            + "    WinClose, ahk_exe " + Exe;

        /// <summary>From the pause screens: slot 1 (the script beside Kronos counts its own slot, this one cannot see it).</summary>
        public const string SaveState =
            "; Kronos saves a state in slot 1 with Ctrl+Shift+F1 (bound by the Nixx plugin).\r\n"
            + "Send ^+{F1}";

        public const string LoadState =
            "; Kronos loads the state of slot 1 with Shift+F1 (bound by the Nixx plugin).\r\n"
            + "Send +{F1}";

        /// <summary>The scripts an earlier build wrote (keep this list when a script changes).</summary>
        public static readonly string[] Superseded = { };
    }
}
