// The window a pack dialog belongs to: the host's own, never another program's (13/10).
//
// The pack's dialogs (a game's Nixx window, a restore's question, the install choice) took the FOREGROUND window as their
// owner. When that window belongs to another program - a terminal LaunchBox was started from, a probe run beside a
// LiteBox - the dialog is owned by it: shown behind LaunchBox (the "modal in the background" seen 10/10), and, being
// modal, it DISABLES that other program's window until it closes. Measured 13/10: a probe's question owned by LiteBox's
// window left LiteBox disabled - impossible to close - once the probe was stopped. So: the foreground window when it is
// this process's, else this process's main window, else none.

#nullable enable

using System;
using System.Runtime.InteropServices;

namespace LbIntegrations.Compat
{
    internal static class HostWindow
    {
        /// <summary>The owner handle for a dialog of the host's, or IntPtr.Zero.</summary>
        public static IntPtr Handle()
        {
            try
            {
                var fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && GetWindowThreadProcessId(fg, out var pid) != 0 && pid == (uint)Environment.ProcessId) return fg;
            }
            catch { }
            try { return System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
            catch { return IntPtr.Zero; }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
