// The AutoHotkey scripts for FCEUX - what the pause screens of LaunchBox and BigBox send.
//
// SAVE AND LOAD ARE FCEUX'S OWN MENU COMMANDS, posted to its main window - not keys. drivers\win\window.cpp (at the tag):
// WM_COMMAND MENU_SAVESTATE (40344) is FCEUI_SaveState(0) and MENU_LOADSTATE (40343) FCEUI_LoadState(0), the CURRENT slot
// (drivers\win\resource.h for the numbers); MENU_NEXTSAVESTATE 40347, MENU_PREVIOUSSAVESTATE 40348. MEASURED 18/10 with
// LaunchBox's own AutoHotkey (ThirdParty\AutoHotkey, v1.1.24.03), the taskbar holding the focus: the scripts below saved
// .fc0, moved to slot 1, saved .fc1, came back and loaded slot 0 (FCEUX's undo .bak.fc0 written), then WinClose quit
// FCEUX (exit code 0). So they need neither the focus nor any key: a key the user rebinds in FCEUX changes nothing here -
// where a sent F2, with the pack's binding refused (the user had put SAVE_STATE elsewhere), would have LOADED slot 2,
// FCEUX's default on F2.
//
// THE KEYS are the pack's all the same, bound in fceux.cfg for the player where FCEUX lets them be (FceuxHotkeys: F2 save,
// F4 load, F6 / F7 the slot, Esc quit) - measured with AutoHotkey's Send 18/10: F7 + F2 wrote .fc1, F6 + F4 loaded slot 0,
// Esc quit and wrote the .sav. (.NET SendKeys reaches nothing: FCEUX reads the keyboard through DirectInput.)
//
// QUITTING closes the window (WinClose = WM_CLOSE to FCEUXWindowClass): FCEUX writes the game's .sav and its fceux.cfg -
// measured, about 0.1 s windowed, 5-6 s from full screen. A modal error box (missing BIOS, bad file) blocks it: the plugin
// refuses those launches before they start.
//
// THE EXECUTABLE'S NAME is the entry's own (fceux64.exe as the plugin installs it, fceux.exe for the 32-bit build): the
// scripts are made for it.

using System;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal static class FceuxAhk
    {
        public const int MenuSaveState = 40344, MenuLoadState = 40343;

        private static string Window(string exeName) => "ahk_class FCEUXWindowClass ahk_exe " + exeName;

        public static string Running(string exeName) =>
            "; FCEUX: F2 saves a state in the current slot, F4 loads it, F6 / F7 change the slot, Esc quits\r\n"
            + "; (bound by the Nixx plugin where FCEUX had nothing else on them). The pause screen's Save / Load use FCEUX's own\r\n"
            + "; menu commands; the Exit script closes the window, which writes the game's save.";

        public static string Exit(string exeName) =>
            "; FCEUX writes the game's .sav when its window is closed.\r\n"
            + "WinClose, ahk_exe " + exeName;

        public static string SaveState(string exeName) =>
            "; FCEUX's File > Savestate > Save State: the current slot (posted to its window, no key needed).\r\n"
            + "PostMessage, 0x111, " + MenuSaveState + ", 0,, " + Window(exeName);

        public static string LoadState(string exeName) =>
            "; FCEUX's File > Savestate > Load State: the current slot (posted to its window, no key needed).\r\n"
            + "PostMessage, 0x111, " + MenuLoadState + ", 0,, " + Window(exeName);

        /// <summary>The scripts made for the other executable name (the entry pointed at the other build): replaced.</summary>
        public static string[] Superseded(string exeName)
            => FceuxLaunch.ExecutableNames.Where(n => !string.Equals(n, exeName, StringComparison.OrdinalIgnoreCase))
                                          .SelectMany(n => new[] { Running(n), Exit(n), SaveState(n), LoadState(n) }).ToArray();
    }
}
