// The AutoHotkey scripts for no$gba.
//
// READ OFF ITS OWN File MENU, which is the only place these are written down - there is no key list
// in the documentation and the executable is packed, so nothing can be grepped out of it. Opening
// the menu on a running instance gave:
//
//     Load snapshot          F7
//     Write snapshot         F8
//     Reset Cartridge        Num-*
//     Quit                   Esc
//     Cartridge menu         F12
//
// ESCAPE ALREADY QUITS, and that is the pleasant surprise. Every frontend's Exit button sends
// Escape, and melonDS needed a script to translate it into Ctrl+Q. Here the emulator does the right
// thing unprompted, so the running script has nothing to fix - and saying so is more useful than
// leaving the field empty, because an empty field is indistinguishable from a plugin that forgot.
//
// THE SNAPSHOT KEYS ARE NOT AN ENDORSEMENT. F8 creates the SNAP folder and, across several sessions
// including clean exits, left nothing in it. The scripts send what the menu says; whether a file
// appears is the emulator's business, and NoGbaSaves deliberately does not claim to manage what has
// never been seen.
//
// The shape - a comment, then down / pause / up - is Unbroken's, read out of their Emulators.xml. An
// emulator polling the keyboard can miss a keystroke that goes down and up inside one frame.

namespace LbIntegrations.NoGba
{
    internal static class NoGbaAhk
    {
        /// <summary>Runs alongside the emulator. Nothing to translate: Escape is already Quit in
        /// no$gba's File menu, so a remap would send the key the emulator was going to act on
        /// anyway - and the $ guard against a self-triggered loop would be guarding nothing.</summary>
        public const string Running =
            "; no$gba already quits on Escape - it is the Quit accelerator in its File menu.\r\n"
            + "; Nothing to remap, so this script deliberately does nothing.";

        public const string Exit =
            "; no$gba quits on Escape (File > Quit).\r\n"
            + "Send {Esc down}\r\n"
            + "Sleep 50\r\n"
            + "Send {Esc up}";

        public const string SaveState =
            "; no$gba writes a snapshot with F8 (File > Write snapshot).\r\n"
            + "Send {F8 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F8 up}";

        public const string LoadState =
            "; no$gba loads a snapshot with F7 (File > Load snapshot).\r\n"
            + "Send {F7 down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F7 up}";
    }
}
