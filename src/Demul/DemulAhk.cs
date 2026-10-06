// The AutoHotkey scripts LaunchBox and Big Box run for Demul - the pack's keys (Mehdi, 07/10: F2 save state, F4 load
// state, F6 / F7 previous / next slot, Esc quit, F11 full screen).
//
// QUITTING IS FILE > EXIT. Demul has no quit key: its exit is the menu (File > Exit, id 40080 in demul.exe's MENUEX,
// read off demul_251220.7z) or closing the window (Alt+F4: "fixed to Alt+F4", LaunchBox's forum, topic 54822). The
// same forum measured the difference: Process, Close loses the nvram, Alt+F4 writes it (topic 42437: a Cannon Spike's
// settings kept). So the
// script picks File > Exit on the Demul window that has the menu (WinMenuSelectItem, ErrorLevel 0 when one did), and
// only when none answers - a menu bar taken away in full screen - closes the window, which is Alt+F4's WM_CLOSE.
//
// THE STATE KEYS ARE READ, NOT ASSUMED: the script takes Demul's own key for the action from padDemul.ini beside the
// running demul.exe ([GLOBAL0] SAVESTATE / LOADSTATE - F2 / F4 once DemulHotkeys has bound them) and sends that scan
// code (a DirectInput code is the key's scan code; 0x80 and up are the E0-prefixed ones, AutoHotkey's sc1xx). A key
// the user rebinds is followed; one left unbound (0) or on a pad button (a code above 255) sends nothing, rather than
// F4, which on a Demul the plugin could not bind is still the arcade TEST button. IniRead reads the file as Demul
// writes it (UTF-8 with a BOM, ASCII keys). padDemul.dll reads the keyboard through DirectInput, in the foreground
// only: the window is activated first, and the key is held 100 ms - several frames.
//
// FULL SCREEN is Alt+Enter in Demul (the DXGI switch of its DX11 video plugins; what LaunchBox's forum users press,
// topic 77876): F11 sends it, in Demul's window only. F2 / F4 / F6 / F7 need no script - padDemul.ini has them.
//
// The $ prefix keeps a hotkey from firing on a keystroke the script itself sends. AutoHotkey v1, as LaunchBox runs it.

namespace LbIntegrations.Demul
{
    internal static class DemulAhk
    {
        public const string ExecutableName = "demul.exe";

        private const string Quit =
            "DemulQuit() {\r\n"
            + "    ; File > Exit, Demul's own way out - on whichever of its windows has the menu.\r\n"
            + "    WinGet, ids, List, ahk_exe demul.exe\r\n"
            + "    Loop, %ids% {\r\n"
            + "        id := ids%A_Index%\r\n"
            + "        WinMenuSelectItem, ahk_id %id%,, File, Exit\r\n"
            + "        if (!ErrorLevel)\r\n"
            + "            return\r\n"
            + "    }\r\n"
            + "    ; No menu answered: close the window, as Alt+F4 does (which writes the nvram too).\r\n"
            + "    WinClose, ahk_exe demul.exe\r\n"
            + "}";

        private const string Key =
            "DemulKey(action) {\r\n"
            + "    ; Demul's own key for the action, from padDemul.ini beside the running demul.exe.\r\n"
            + "    WinGet, exe, ProcessPath, ahk_exe demul.exe\r\n"
            + "    if (exe = \"\")\r\n"
            + "        return\r\n"
            + "    SplitPath, exe,, dir\r\n"
            + "    IniRead, code, %dir%\\padDemul.ini, GLOBAL0, %action%, 0\r\n"
            + "    code := Trim(code)\r\n"
            + "    if code is not integer\r\n"
            + "        return\r\n"
            + "    ; 0 = unbound, above 255 = a pad button: nothing to send.\r\n"
            + "    code += 0\r\n"
            + "    if (code <= 0 || code > 255)\r\n"
            + "        return\r\n"
            + "    sc := Format(\"sc{:03X}\", code < 128 ? code : code + 128)\r\n"
            + "    WinActivate, ahk_exe demul.exe\r\n"
            + "    Sleep, 50\r\n"
            + "    Send, {%sc% down}\r\n"
            + "    Sleep, 100\r\n"
            + "    Send, {%sc% up}\r\n"
            + "}";

        /// <summary>In Demul's window only, and ending by itself once Demul has gone - see LbipAhk. Escape quits (what a
        /// frontend's Exit sends) and F11 toggles full screen; the second hotkey rides in the same #If block.</summary>
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; Demul has no quit key (its exit is File > Exit, or Alt+F4); Escape is what a frontend's Exit sends, so it quits\r\n"
            + "; through File > Exit (the nvram is written). F11 toggles full screen (Demul's Alt+Enter).\r\n"
            + "; F2 / F4 save / load a state, F6 / F7 change the slot: padDemul.ini's own keys, bound by the Nixx plugin.",
            new[] { ExecutableName }, "DemulQuit()\r\n$F11::Send, !{Enter}", Quit);

        public const string Exit =
            "; Demul quits through File > Exit, which writes the nvram - or closes its window (Alt+F4) when no menu answers.\r\n"
            + "DemulQuit()\r\n"
            + Quit;

        public const string SaveState =
            "; Demul saves a state to its current slot with its SAVESTATE key (F2, bound by the Nixx plugin) - read from padDemul.ini.\r\n"
            + "DemulKey(\"SAVESTATE\")\r\n"
            + Key;

        public const string LoadState =
            "; Demul loads the state of its current slot with its LOADSTATE key (F4, bound by the Nixx plugin) - read from padDemul.ini.\r\n"
            + "DemulKey(\"LOADSTATE\")\r\n"
            + Key;

        /// <summary>What this plugin wrote before: none - no earlier build set any script.</summary>
        public static readonly string[] Previous = new string[0];
    }
}
