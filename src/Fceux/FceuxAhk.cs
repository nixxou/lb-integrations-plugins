// The AutoHotkey scripts for FCEUX - what the pause screens of LaunchBox and BigBox send.
//
// THE QT BUILD HAS NO MENU COMMAND TO POST (its menus are Qt actions, no WM_COMMAND id): save and load are the pack's KEYS,
// F2 and F4, bound in fceux.cfg for the player (FceuxHotkeys) and sent to FCEUX's window once it is in front. Qt reads the
// ordinary key messages - MEASURED 18/10 (runqt2, keybd_event to the focused window): F2 wrote the slot's .fc0, F8 a
// screenshot, Esc quit. A key the user rebound in FCEUX's Hotkey Config is the user's: the script then does what the user
// put on F2 / F4, as the keyboard would.
//
// QUITTING closes the window (WinClose = WM_CLOSE): FCEUX writes the game's .sav and its fceux.cfg - measured 18/10, exit
// code 0. A modal error box (missing BIOS, bad file) blocks it: the plugin refuses those launches before they start.
//
// THE EXECUTABLE'S NAME is the entry's own (qfceux.exe): the scripts are made for it. The scripts the plugin wrote for the
// classic build (fceux64.exe / fceux.exe, its menu commands posted) are replaced when found as they were written.

using System;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal static class FceuxAhk
    {
        private static string Activate(string exeName) => "WinActivate, ahk_exe " + exeName + "\r\nWinWaitActive, ahk_exe " + exeName + ",, 2\r\n";

        public static string Running(string exeName) =>
            "; FCEUX: F2 saves a state in the current slot, F4 loads it, F6 / F7 change the slot, Esc quits\r\n"
            + "; (bound by the Nixx plugin where FCEUX had nothing else on them). The pause screen's Save / Load send F2 / F4;\r\n"
            + "; the Exit script closes the window, which writes the game's save.";

        public static string Exit(string exeName) =>
            "; FCEUX writes the game's .sav when its window is closed.\r\n"
            + "WinClose, ahk_exe " + exeName;

        public static string SaveState(string exeName) =>
            "; FCEUX: F2 saves a state in the current slot (the pack's key, bound in fceux.cfg).\r\n"
            + Activate(exeName) + "Send, {F2}";

        public static string LoadState(string exeName) =>
            "; FCEUX: F4 loads the current slot's state (the pack's key, bound in fceux.cfg).\r\n"
            + Activate(exeName) + "Send, {F4}";

        // ── what the plugin wrote for the classic build ──

        private const int MenuSaveState = 40344, MenuLoadState = 40343;
        private static readonly string[] Classic = { "fceux64.exe", "fceux.exe" };

        private static string ClassicWindow(string exeName) => "ahk_class FCEUXWindowClass ahk_exe " + exeName;

        /// <summary>Running, Save, Load as the classic build's plugin wrote them for <paramref name="exeName"/>.</summary>
        private static string[] ClassicScripts(string exeName) => new[]
        {
            "; FCEUX: F2 saves a state in the current slot, F4 loads it, F6 / F7 change the slot, Esc quits\r\n"
            + "; (bound by the Nixx plugin where FCEUX had nothing else on them). The pause screen's Save / Load use FCEUX's own\r\n"
            + "; menu commands; the Exit script closes the window, which writes the game's save.",
            "; FCEUX's File > Savestate > Save State: the current slot (posted to its window, no key needed).\r\n"
            + "PostMessage, 0x111, " + MenuSaveState + ", 0,, " + ClassicWindow(exeName),
            "; FCEUX's File > Savestate > Load State: the current slot (posted to its window, no key needed).\r\n"
            + "PostMessage, 0x111, " + MenuLoadState + ", 0,, " + ClassicWindow(exeName),
        };

        /// <summary>The scripts made for another executable name, or for the classic build: replaced.</summary>
        public static string[] Superseded(string exeName)
            => FceuxLaunch.ExecutableNames.Where(n => !string.Equals(n, exeName, StringComparison.OrdinalIgnoreCase))
                                          .SelectMany(n => new[] { Running(n), Exit(n), SaveState(n), LoadState(n) })
                                          .Concat(Classic.SelectMany(n => ClassicScripts(n).Append(Exit(n))))
                                          .Concat(ClassicScripts(exeName))
                                          .Distinct().ToArray();
    }
}
