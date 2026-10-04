// The AutoHotkey scripts LaunchBox and Big Box run for xemu.
//
// QUITTING. xemu's own shortcut is Ctrl+Q (ui/xui/menubar.cc, ActionShutdown); Unbroken's plugin maps Escape to Alt+F4, which
// closes xemu's SDL window - what it has shipped with. Kept: the window's close is xemu's exit, its disk flushed (to be
// measured against Ctrl+Q). Escape in xemu's window only, the script ending once xemu has gone - LbipAhk.

namespace LbIntegrations.Xemu
{
    internal static class XemuAhk
    {
        public static readonly string Running = LbIntegrations.Lbip.LbipAhk.EscapeScript(
            "; xemu binds no quit key to Escape; Escape is what a frontend's Exit sends, so it closes xemu's window (Alt+F4).",
            new[] { XemuPaths.Exe }, "Send, !{F4}");

        public const string Exit =
            "; xemu quits when its window is closed - Alt+F4.\r\n"
            + "Send {Alt down}\r\n"
            + "Sleep 50\r\n"
            + "Send {F4}\r\n"
            + "Sleep 50\r\n"
            + "Send {Alt up}";
    }
}
