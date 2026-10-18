// FCEUX's hotkeys set to the pack's (Mehdi, 18/10 - the keys puNES got, RetroArch's for the rest).
//
// THE KEYS (the Qt build's fceux.cfg, "SDL.Hotkeys.<name> = <key>", one key per action in Qt's text form: "Alt+Return",
// "Shift+F10", "Ctrl+Q" - drivers\Qt\config.cpp at the fceux-2.7.0 tag; measured 18/10 in the lab, runqt2): the pack's
//
//     SaveState F2, LoadState F4, SelectStatePrev / SelectStateNext F6 / F7, FullScreen F11, Quit Esc,
//     Pause P, Turbo L (held - drivers\Qt\input.cpp:1018, getState), Screenshot F8, VolumeMute F9,
//     SoftReset H, HardReset Shift+Esc
//
// FCEUX 2.7.0's defaults on those actions: I, P, none, none, Alt+Return, Ctrl+Q, Pause, Tab, F12, none, Ctrl+R,
// Ctrl+Shift+R - and LoadState2 / 4 / 6 / 7 / 8 / 9 sit on F2 / F4 / F6 / F7 / F8 / F9 (load that slot directly): given
// up, or a key would do two things. No PAL / NTSC hotkey exists in the Qt build; there is no fast-forward toggle (Turbo is
// held), so RetroArch's Space has no counterpart.
//
// MEASURED 18/10 (runqt2, qfceux.exe 2.7.0, Zelda II): with these lines, F2 wrote the slot's .fc0, F8 a screenshot, Esc
// quit (exit code 0) and wrote the .sav; FCEUX kept every line as written after its exit (Esc and Shift+Esc too).
//
// An action is set only when it holds FCEUX's default, nothing, or the pack's key already: one the user changed in FCEUX's
// Hotkey Config is the user's. A pack key another action of the user's holds leaves that action as it is - and an action
// that keeps a key another action of this pass takes is taken off it, never two actions on a key. The pads are left alone
// (their default keys are F, D, S, Return and the arrows - drivers\Qt\input.cpp DefaultGamePad - none of the pack's).

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal static class FceuxHotkeys
    {
        /// <summary>The pack's keys on FCEUX's actions, with FCEUX 2.7.0's default for each ("" = none). A null key: none.</summary>
        internal static readonly (string Name, string Key, string Default)[] Table =
        {
            ("SaveState", "F2", "I"), ("LoadState", "F4", "P"),
            ("SelectStatePrev", "F6", ""), ("SelectStateNext", "F7", ""),
            ("FullScreen", "F11", "Alt+Return"), ("Quit", "Esc", "Ctrl+Q"),
            ("Pause", "P", "Pause"), ("Turbo", "L", "Tab"), ("Screenshot", "F8", "F12"), ("VolumeMute", "F9", ""),
            ("SoftReset", "H", "Ctrl+R"), ("HardReset", "Shift+Esc", "Ctrl+Shift+R"),
            // FCEUX's own on the pack's keys, given up.
            ("LoadState2", null, "F2"), ("LoadState4", null, "F4"), ("LoadState6", null, "F6"),
            ("LoadState7", null, "F7"), ("LoadState8", null, "F8"), ("LoadState9", null, "F9"),
        };

        private static bool Same(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>An action this plugin may set: nothing on it (or missing), FCEUX's default, or the pack's key already.</summary>
        private static bool IsOurs(string now, string key, string dflt) => string.IsNullOrWhiteSpace(now) || Same(now, dflt) || (key != null && Same(now, key));

        /// <summary>The pack's keys written into <paramref name="cfg"/> (in memory - the caller saves). Returns the lines for the log.</summary>
        public static List<string> Bind(FceuxCfg cfg)
        {
            var said = new List<string>();
            var set = new List<string>();
            var left = new List<string>();
            var ours = new HashSet<string>(Table.Where(h => IsOurs(cfg.Get(FceuxCfg.Hotkey + h.Name), h.Key, h.Default)).Select(h => h.Name), StringComparer.Ordinal);
            foreach (var (name, key, dflt) in Table)
            {
                var full = FceuxCfg.Hotkey + name;
                var now = cfg.Get(full);
                if (!ours.Contains(name)) { left.Add(name + " (the user's " + now + ")"); continue; }
                var value = key ?? "";
                if (key != null)
                {
                    var holder = Holder(cfg, key, name, ours);
                    if (holder != null)
                    {
                        // Its old key goes to another action of this pass: taken off, never on two.
                        bool wanted = !string.IsNullOrWhiteSpace(now) && Table.Any(h => h.Key != null && h.Name != name && ours.Contains(h.Name) && Same(now, h.Key));
                        if (!wanted) { left.Add(name + " (" + key + " is " + holder + "'s)"); continue; }
                        value = "";
                    }
                }
                if (now == null || !Same(now, value)) { cfg.Set(full, value); set.Add(name + " = " + (value.Length == 0 ? "none" : value)); }
            }
            if (set.Count > 0) said.Add("fceux.cfg keys: " + string.Join(", ", set));
            if (left.Count > 0) said.Add("fceux.cfg keys left as they are: " + string.Join(", ", left));
            return said;
        }

        /// <summary>The action of the user's that holds <paramref name="key"/> - not one this pass gives a key; null: free.</summary>
        internal static string Holder(FceuxCfg cfg, string key, string except, ICollection<string> moving)
        {
            foreach (var (full, value) in cfg.Entries(FceuxCfg.Hotkey))
            {
                var name = full.Substring(FceuxCfg.Hotkey.Length);
                if (name == except || moving.Contains(name)) continue;
                if (Same(value, key)) return name;
            }
            return null;
        }

        /// <summary>The key FCEUX has on an action now, else <paramref name="dflt"/>.</summary>
        public static string KeyOf(string exe, string name, string dflt)
        {
            try
            {
                var v = FceuxCfg.Load(FceuxCfg.Of(exe)).Get(FceuxCfg.Hotkey + name);
                return string.IsNullOrWhiteSpace(v) ? dflt : v.Trim();
            }
            catch { return dflt; }
        }
    }
}
