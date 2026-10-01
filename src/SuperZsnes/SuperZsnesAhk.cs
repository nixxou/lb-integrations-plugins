// The AutoHotkey scripts for SUPER ZSNES.
//
// THE STATE KEYS, MEASURED 01/10 in a fresh 0.310's own settings file: its inputData binds ZInputSystem's
// GameInput SaveState (2) to the Input System's Key 95 = F2, LoadState (3) to Key 97 = F4 and StateSelect (4) to
// Key 96 = F3 - the user's own account of the defaults on 30/09, and F2 was seen writing <rom>.szst0. They are the
// user's to rebind in Config > Input; a rebound key makes these scripts send the old one.
//
// QUITTING IS Alt+F4. It is a Unity windowed application, and Alt+F4 closes one from the window
// manager whatever the game does with its keys. Escape is NOT remapped to it: in SUPER ZSNES, Escape
// leaves the game for the emulator's own menu (its handler is literally named EscapeBackToMenu), and
// that menu is where save states, cheats and the per-game enhancement toggles live - a script that
// turned Escape into Alt+F4 would take that menu away from a keyboard player to save a click.

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesAhk
    {
        /// <summary>Runs alongside the emulator. Nothing to remap: with the pack's in-process plugin, Escape asks
        /// before quitting and F1 opens the emulator's menu. Both hosts treat an all-comment script as empty.</summary>
        public const string Running =
            "; SUPER ZSNES: with the pack's plugin, Escape asks before quitting (twice) and F1 opens the\r\n"
            + "; emulator's own menu. Nothing to remap. The Exit script closes it with Alt+F4.";

        public const string Exit =
            "; SUPER ZSNES is a Unity application: Alt+F4 closes it from the window manager.\r\n"
            + "Send {Alt down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4}\r\n"
            + "Sleep 50\r\n"
            + "Send {Alt up}";

        /// <summary>F2, the default SaveState key; slot 0 unless the slot was changed (F3).</summary>
        public const string SaveState =
            "; SUPER ZSNES saves a state with F2 (Config > Input > Save State, its default).\r\n"
            + "Send {F2 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F2 up}";

        /// <summary>F4, the default LoadState key.</summary>
        public const string LoadState =
            "; SUPER ZSNES loads a state with F4 (Config > Input > Load State, its default).\r\n"
            + "Send {F4 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4 up}";

        /// <summary>What earlier versions wrote in the state fields: replaced like a blank field, the user never
        /// having written it.</summary>
        public static readonly string[] Superseded =
        {
            "; SUPER ZSNES has save-state keys, but they are bound by the user in Config > Input and\r\n"
            + "; their defaults are not readable from the build (closed source, IL2CPP). Nothing is sent.",
            "; SUPER ZSNES: Escape opens the emulator's own menu (save states, cheats, enhancements),\r\n"
            + "; so it is deliberately not remapped to quit. Use the Exit script (Alt+F4) to close it.",
        };
    }
}
