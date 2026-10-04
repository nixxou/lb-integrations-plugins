// The AutoHotkey scripts LaunchBox and Big Box run for Cxbx-Reloaded: the one that runs alongside the emulator, and
// the one their pause screen's Exit sends - Vita3kAhk's shape. No save and load state scripts: Cxbx-Reloaded has no
// save states, and a script for a key that does nothing would only pretend otherwise.
//
// QUITTING IS CLOSING THE WINDOW. Read in its source (Direct3D9.cpp, the game window's WndProc, 03/10): WM_CLOSE is
// what F6 and Alt+F4 send, and it shuts the emulation down (CxbxrShutDown) - the loader exits, the plugin's session
// ends (CxbxSession waits on cxbxr-ldr.exe) and the disc or RAM disk is released. Escape, Cxbx's own, quits only in
// EXCLUSIVE full screen; in the full screen in a window the plugin sets (its faux full screen, Alt+Enter) it only
// goes back to a window. A frontend's Exit sends Escape - so Escape, and the Exit script, close the window.
//
// NOT IN THE MIDDLE OF A SAVE (Mehdi, 03/10): before closing, the script waits until nothing in E:\UDATA - every game's
// saves - has been written for 5 seconds, 30 seconds at most. UDATA is found as the plugin finds it (CxbxPaths.DataDir,
// from Cxbx-Reloaded's Settings.cpp): settings.ini beside the loader = portable unless it says otherwise, else
// %AppData%\Cxbx-Reloaded; [gui] DataStorageToggle 0 = %AppData%\Cxbx-Reloaded, 1 = the loader's folder, 2 =
// DataCustomLocation - written in hex ("0x1"), which AutoHotkey compares as a number.
//
// The $ prefix keeps the hotkey from firing on a keystroke the script itself sends. AutoHotkey v1, as LaunchBox runs it.

namespace LbIntegrations.Cxbx
{
    internal static class CxbxAhk
    {
        private const string Quit =
            "CxbxQuit() {\r\n"
            + "    WinGet, exe, ProcessPath, ahk_exe cxbxr-ldr.exe\r\n"
            + "    if (exe = \"\")\r\n"
            + "        return\r\n"
            + "    SplitPath, exe,, dir\r\n"
            + "    ini := dir . \"\\settings.ini\", toggle := 1\r\n"
            + "    if !FileExist(ini)\r\n"
            + "        ini := A_AppData . \"\\Cxbx-Reloaded\\settings.ini\", toggle := 0\r\n"
            + "    IniRead, toggle, %ini%, gui, DataStorageToggle, %toggle%\r\n"
            + "    data := A_AppData . \"\\Cxbx-Reloaded\"\r\n"
            + "    if (toggle = 1)\r\n"
            + "        data := dir\r\n"
            + "    else if (toggle = 2)\r\n"
            + "        IniRead, data, %ini%, gui, DataCustomLocation, %data%\r\n"
            + "    udata := data . \"\\EmuDisk\\Partition1\\UDATA\"\r\n"
            + "    ; Not in the middle of a save: nothing in UDATA written for 5 s - 30 s at most.\r\n"
            + "    Loop, 60 {\r\n"
            + "        newest := \"\"\r\n"
            + "        Loop, Files, %udata%\\*, FDR\r\n"
            + "            if (A_LoopFileTimeModified > newest)\r\n"
            + "                newest := A_LoopFileTimeModified\r\n"
            + "        if (newest = \"\")\r\n"
            + "            break\r\n"
            + "        age := A_Now\r\n"
            + "        EnvSub, age, %newest%, Seconds\r\n"
            + "        if (age >= 5)\r\n"
            + "            break\r\n"
            + "        Sleep, 500\r\n"
            + "    }\r\n"
            + "    WinClose, ahk_exe cxbxr-ldr.exe\r\n"
            + "}";

        /// <summary>In the game's window only, and ending by itself once the loader has gone (Mehdi, 04/10) - see LbipAhk.</summary>
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; Cxbx-Reloaded's Escape only leaves its full screen; Escape is what a frontend's Exit sends, so it closes the game's\r\n"
            + "; window - once no save has been written for 5 s.",
            new[] { CxbxPaths.Loader }, "CxbxQuit()", Quit);

        public const string Exit =
            "; Cxbx-Reloaded quits by closing its window (what its F6 and Alt+F4 do) - once no save has been written for 5 s.\r\n"
            + "CxbxQuit()\r\n"
            + Quit;

        /// <summary>What this plugin wrote before: a field still holding one of these is ours, brought up to date - a
        /// script anyone else wrote is left alone.</summary>
        public static readonly string[] PreviousRunning =
        {
            "; Cxbx-Reloaded's Escape only leaves its full screen; Escape is what a frontend's Exit sends, so it closes the game's window.\r\n"
            + "$Esc::WinClose, ahk_exe cxbxr-ldr.exe",
            "; Cxbx-Reloaded's Escape only leaves its full screen; Escape is what a frontend's Exit sends, so it closes the game's\r\n"
            + "; window - once no save has been written for 5 s.\r\n"
            + "$Esc::CxbxQuit()\r\n"
            + Quit,
        };

        public static readonly string[] PreviousExit =
        {
            "; Cxbx-Reloaded quits by closing its window (what its F6 and Alt+F4 do): the emulation shuts down.\r\n"
            + "WinClose, ahk_exe cxbxr-ldr.exe",
        };
    }
}
