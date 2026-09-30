// The AutoHotkey scripts for SUPER ZSNES, and the honest answer where nothing is known.
//
// WHAT IS MEASURED. The emulator's scene carries a "Keyboard Shortcuts" configuration page and the
// entries "Save State", "Load State", "Fast Forward", "Rewind", "Screenshot" and a "Select Save State
// Slot" dialog; its Input page says "Press the key or button to set this input entry." So the keys
// exist and are the user's to bind. WHAT IS NOT: their defaults. The Unity input asset in the build
// binds only the UI's Move action (WASD and the arrows), and the emulator reads the rest through
// its own ZInputSystem, whose defaults are in code this pack cannot read. A script that sends a key
// nobody measured would fail in silence, which is worse than no script.
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
        /// <summary>Runs alongside the emulator. Nothing to remap: Escape is the emulator's own menu
        /// key and is left to it. Both hosts treat an all-comment script as empty.</summary>
        public const string Running =
            "; SUPER ZSNES: Escape opens the emulator's own menu (save states, cheats, enhancements),\r\n"
            + "; so it is deliberately not remapped to quit. Use the Exit script (Alt+F4) to close it.";

        public const string Exit =
            "; SUPER ZSNES is a Unity application: Alt+F4 closes it from the window manager.\r\n"
            + "Send {Alt down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4}\r\n"
            + "Sleep 50\r\n"
            + "Send {Alt up}";

        /// <summary>No script, and the comment says why.</summary>
        public const string SaveState =
            "; SUPER ZSNES has save-state keys, but they are bound by the user in Config > Input and\r\n"
            + "; their defaults are not readable from the build (closed source, IL2CPP). Nothing is sent.";

        public const string LoadState = SaveState;
    }
}
