// The AutoHotkey scripts for melonDS.
//
// CONSTANTS, NOT A FILE READ, and that is the difference with the Flycast and PPSSPP plugins. Those
// two ship without save-state keys, so their plugins write bindings and then quote back whatever the
// mapping file ended up saying. melonDS already has the keys - it just does not keep them anywhere a
// plugin could change. They are Qt menu shortcuts, set in code (src/frontend/qt_sdl/Window.cpp:353-401):
//
//     F1 .. F8              load state, slots 1 to 8
//     Shift+F1 .. Shift+F8  save state, slots 1 to 8
//     F9 / Shift+F9         load / save through a file dialog
//     F12                   undo state load
//     Ctrl+Q                quit (QKeySequence::Quit, which is Ctrl+Q on Windows)
//
// None of those appear in melonDS.toml, and none can be rebound: the configurable hotkeys are the
// HK_* list (EmuInstance.h:36-59), which has no save-state, load-state, slot or quit entry at all.
// So there is nothing to write, nothing to measure, and nothing that can drift - but the shortcuts
// are also nobody's to change, which is why the comments name the file they come from.
//
// SLOT 1, because a host's Save State button is one button and melonDS gives one key per slot. Slot
// 1 is what its own menu lists first, and F1 / Shift+F1 are the pair a user would reach for.
//
// The shape - a comment, then down / pause / up - is Unbroken's, read out of their Emulators.xml. An
// emulator polling the keyboard can miss a keystroke that goes down and up inside one frame.

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsAhk
    {
        /// <summary>Runs alongside the emulator. Makes Escape quit, because melonDS binds Ctrl+Q and
        /// every frontend's Exit button sends Escape.
        ///
        /// The $ prefix matters: without it the hotkey would fire on the keystroke the script itself
        /// sends and loop. Unbroken's own Xemu plugin uses the same trick.</summary>
        public const string Running =
            "; melonDS quits with Ctrl+Q (Qt's standard Quit shortcut, Window.cpp:401).\r\n"
            + "; Escape is what a frontend's Exit sends, so map it across.\r\n"
            + "$Esc::Send, ^q";

        public const string Exit =
            "; melonDS quits with Ctrl+Q - Qt's standard Quit key, bound in Window.cpp:401.\r\n"
            + "Send {Ctrl down}\r\n"
            + "Sleep 50\r\n"
            + "Send {q}\r\n"
            + "Sleep 50\r\n"
            + "Send {Ctrl up}";

        public const string SaveState =
            "; melonDS saves to state slot 1 with Shift+F1 (Window.cpp:356).\r\n"
            + "Send {Shift down}\r\n"
            + "Send {F1 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 up}\r\n"
            + "Send {Shift up}";

        public const string LoadState =
            "; melonDS loads state slot 1 with F1 (Window.cpp:372).\r\n"
            + "Send {F1 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F1 up}";
    }
}
