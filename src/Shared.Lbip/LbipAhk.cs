// The running AutoHotkey script of an emulator that quits on another key than Escape: Escape mapped across, and only
// there (Mehdi, 04/10).
//
// MEASURED 04/10: an AutoHotkey that LaunchBox had started for melonDS the evening before was still running the next
// day - the LaunchBox that started it was gone, so nobody stopped it - and its "$Esc::Send, ^q" turned EVERY Escape on
// the machine into Ctrl+Q: SUPER ZSNES's Escape "did nothing". Two guards, so it cannot happen again:
//   - the hotkey holds only in the emulator's own window (#If): anywhere else Escape stays Escape;
//   - the script ends by itself once the emulator has gone - three checks two seconds apart, 6 s, so a Cxbx-Reloaded
//     game that reboots (a new loader, a moment with none) is not taken for the end. A script that never sees the
//     emulator at all (a launch still being prepared, then one that never came) ends after 30 minutes.
// The timer is started from a static initializer: AutoHotkey runs those when the script loads, wherever the function
// sits, so it starts even when the host puts hotkeys of its own before ours (the auto-execute section ends at the
// first one). AutoHotkey v1, as LaunchBox runs it. The closing #If leaves whatever the host appends unrestricted.

using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Lbip
{
    internal static class LbipAhk
    {
        /// <summary>The running script: <paramref name="comment"/> (its "; " lines), then Escape doing
        /// <paramref name="action"/> in the window of one of <paramref name="exes"/> only, then
        /// <paramref name="functions"/> (what the action calls), then the watch that ends the script.</summary>
        public static string EscapeScript(string comment, string[] exes, string action, string functions = null)
        {
            string AnyOf(string call) => string.Join(" || ", exes.Select(e => call + "(\"ahk_exe " + e + "\")"));
            string Running() => string.Join("\r\n", exes.Select(e => "    Process, Exist, " + e + "\r\n    if (ErrorLevel)\r\n        return true"));
            return comment.TrimEnd() + "\r\n"
                + "; Only in its own window, and the script ends by itself 6 s after the emulator has gone: one left behind\r\n"
                + "; by a host that did not stop it would otherwise take Escape from every program.\r\n"
                + "#If NixxEmulatorActive()\r\n"
                + "$Esc::" + action + "\r\n"
                + "#If\r\n"
                + (string.IsNullOrWhiteSpace(functions) ? "" : functions.TrimEnd() + "\r\n")
                + "NixxEmulatorActive() {\r\n"
                + "    return " + AnyOf("WinActive") + "\r\n"
                + "}\r\n"
                + "NixxEmulatorRunning() {\r\n"
                + Running() + "\r\n"
                + "    return false\r\n"
                + "}\r\n"
                + "NixxWatchStart() {\r\n"
                + "    static started := NixxWatchStart()\r\n"
                + "    SetTimer, NixxWatch, 2000\r\n"
                + "}\r\n"
                + "NixxWatch() {\r\n"
                + "    global NixxSeen, NixxGone, NixxWaited\r\n"
                + "    if (NixxEmulatorRunning()) {\r\n"
                + "        NixxSeen := 1, NixxGone := 0\r\n"
                + "        return\r\n"
                + "    }\r\n"
                + "    if (NixxSeen) {\r\n"
                + "        NixxGone += 1\r\n"
                + "        if (NixxGone >= 3)\r\n"
                + "            ExitApp\r\n"
                + "    } else {\r\n"
                + "        NixxWaited += 1\r\n"
                + "        if (NixxWaited >= 900)\r\n"
                + "            ExitApp\r\n"
                + "    }\r\n"
                + "}";
        }

        /// <summary>To be written: blank, or an older script of this pack's (line endings aside) - never the current one
        /// again, never anyone else's.</summary>
        public static bool Ours(string field, string current, IEnumerable<string> previous)
        {
            if (string.IsNullOrWhiteSpace(field)) return true;
            string Flat(string s) => (s ?? "").Replace("\r\n", "\n").Trim();
            var f = Flat(field);
            return f != Flat(current) && previous.Any(p => Flat(p) == f);
        }
    }
}
