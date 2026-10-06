// The AutoHotkey scripts LaunchBox and Big Box run for xemu.
//
// QUITTING. xemu's own shortcut is Ctrl+Q (ui/xui/menubar.cc, ActionShutdown); Unbroken's plugin maps Escape to Alt+F4, which
// closes xemu's SDL window - what it has shipped with. Kept: the window's close is xemu's exit, its disk flushed (to be
// measured against Ctrl+Q). Escape in xemu's window only, the script ending once xemu has gone - LbipAhk.
//
// THE PACK'S KEYS (Mehdi, 07/10: F2 save state, F4 load state, F6 / F7 previous / next slot, Esc quit, F11 full screen).
// xemu v0.8.136 lets none of its keys be bound - all are hard-wired, read off its source:
//     F1   main menu                      ui/xui/main.cc        F2   the popup menu (also right-click, the guide button)
//     F5-F8        load the snapshot bound to that key           Shift+F5-F8   save it (over the one of that name)
//     F11  full screen (Ctrl+Alt+F too)   menubar.cc, xemu.c    F12  screenshot           Ctrl+Q quit
// A key's snapshot is a NAME in xemu.toml, [general.snapshots.shortcuts] f5..f8 (config_spec.yml) - unbound, xemu only says
// "F5 is not bound to a snapshot". So the plugin names them "Slot 1".."Slot 4" where the user has none (BindSnapshotSlots),
// and the running script puts the pack's keys on xemu's: F2 sends Shift+F<4+slot>, F4 F<4+slot>, F6 / F7 step the slot
// (1..4, round, 1 at every launch, a tooltip says which). F11 is xemu's already. Taken in xemu's window: F2 (its popup menu
// stays on the right click and the guide button) and F6 / F7 (their slots stay on F2 / F4 once picked, and Shift+F6 / F7
// still save them - the hotkeys hold without a modifier only). xemu reads the keys only with no window of its own open.
// The pause screens' Save / Load State act on F5, "Slot 1": another AutoHotkey's Send is not seen by this script's hotkeys
// (SendLevel 0), so they cannot know the slot picked.

using System;
using System.IO;

namespace LbIntegrations.Xemu
{
    internal static class XemuAhk
    {
        /// <summary>The names xemu's F5..F8 are given when unbound - the snapshots the pack's keys save and load.</summary>
        public static readonly string[] SlotNames = { "Slot 1", "Slot 2", "Slot 3", "Slot 4" };

        private const string ShortcutsTable = "general.snapshots.shortcuts";

        // The pack's keys, after Escape's line and so under its #If (LbipAhk.EscapeScript): in xemu's window only.
        private const string StateKeys =
            "Send, !{F4}\r\n"
            + "$F2::NixxXemuState(true)\r\n"
            + "$F4::NixxXemuState(false)\r\n"
            + "$F6::NixxXemuStep(-1)\r\n"
            + "$F7::NixxXemuStep(1)";

        // Shift held around the key, a pause each side: xemu reads Shift and the F key in its frame (ImGui), not as one chord.
        private const string StateFunctions =
            "NixxXemuState(save) {\r\n"
            + "    global NixxSlot\r\n"
            + "    key := \"F\" . ((NixxSlot ? NixxSlot : 1) + 4)\r\n"
            + "    if (save) {\r\n"
            + "        Send, {Shift down}\r\n"
            + "        Sleep 50\r\n"
            + "    }\r\n"
            + "    Send, {%key% down}\r\n"
            + "    Sleep 50\r\n"
            + "    Send, {%key% up}\r\n"
            + "    if (save) {\r\n"
            + "        Sleep 50\r\n"
            + "        Send, {Shift up}\r\n"
            + "    }\r\n"
            + "}\r\n"
            + "NixxXemuStep(step) {\r\n"
            + "    global NixxSlot\r\n"
            + "    NixxSlot := Mod((NixxSlot ? NixxSlot : 1) - 1 + step + 4, 4) + 1\r\n"
            + "    ToolTip, % \"xemu: slot \" . NixxSlot . \" (F\" . (NixxSlot + 4) . \")\"\r\n"
            + "    SetTimer, NixxXemuTipOff, -1500\r\n"
            + "}\r\n"
            + "NixxXemuTipOff() {\r\n"
            + "    ToolTip\r\n"
            + "}";

        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; xemu binds no quit key to Escape; Escape is what a frontend's Exit sends, so it closes xemu's window (Alt+F4).\r\n"
            + "; The pack's state keys on xemu's own, which can't be rebound: F5-F8 load the snapshot bound to that key,\r\n"
            + "; Shift+F5-F8 save it (\"Slot 1\"-\"Slot 4\", bound by the Nixx plugin). F2 saves the current slot, F4 loads it,\r\n"
            + "; F6 / F7 pick the previous / next one (1 at every launch). F11 is xemu's own full screen.",
            new[] { XemuPaths.Exe }, StateKeys, StateFunctions);

        /// <summary>What this plugin wrote before (04/10 - 07/10: Escape only): a field still holding it is brought up to date.</summary>
        public static readonly string[] PreviousRunning =
        {
            LbIntegrations.Lbip.LbipAhk.EscapeScript(
                "; xemu binds no quit key to Escape; Escape is what a frontend's Exit sends, so it closes xemu's window (Alt+F4).",
                new[] { XemuPaths.Exe }, "Send, !{F4}"),
        };

        public const string Exit =
            "; xemu quits when its window is closed - Alt+F4.\r\n"
            + "Send {Alt down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4}\r\n"
            + "Sleep 50\r\n"
            + "Send {Alt up}";

        public const string SaveState =
            "; xemu saves the snapshot bound to F5 (\"Slot 1\", bound by the Nixx plugin) with Shift+F5.\r\n"
            + "Send {Shift down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F5 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F5 up}\r\n"
            + "Sleep 50\r\n"
            + "Send {Shift up}";

        public const string LoadState =
            "; xemu loads the snapshot bound to F5 (\"Slot 1\", bound by the Nixx plugin) with F5.\r\n"
            + "Send {F5 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F5 up}";

        /// <summary>xemu's F5..F8 given a snapshot each, "Slot 1".."Slot 4", in the user's xemu.toml - each only when it has none
        /// (absent or empty: xemu writes no default, so an unbound key is an absent one). A name the user bound is never
        /// touched. The user's file, not the session's: the table is then the session's to change, and comes back whole
        /// (XemuSessionConfig).</summary>
        public static void BindSnapshotSlots(string toml)
        {
            try
            {
                if (toml == null || !File.Exists(toml)) return;
                var doc = XemuTomlDoc.Load(toml);
                var bound = new System.Collections.Generic.List<string>();
                for (int i = 0; i < SlotNames.Length; i++)
                {
                    var key = "f" + (i + 5);
                    if (!string.IsNullOrEmpty(XemuToml.Text(doc.Get(ShortcutsTable, key)))) continue;
                    doc.Set(ShortcutsTable, key, XemuToml.Literal(SlotNames[i]));
                    bound.Add("F" + (i + 5) + " = " + SlotNames[i]);
                }
                if (bound.Count == 0) return;
                doc.Save(toml);
                Log.Info("xemu.toml: snapshot keys bound - " + string.Join(", ", bound));
            }
            catch (Exception ex) { Log.Warn("xemu.toml: the snapshot keys could not be bound", ex); }
        }
    }
}
