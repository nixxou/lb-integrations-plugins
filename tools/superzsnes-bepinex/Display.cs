// Full screen on the PRIMARY display, whichever number Unity gives it.
//
// Unity's -monitor N is an index into its own enumeration, and its Screenmanager registry keys
// remember the last display used; neither says "the primary one". Windows does: the primary
// monitor is the one holding the virtual desktop's origin, MonitorFromPoint((0,0)) returns it and
// GetMonitorInfo gives its rectangle. And Unity goes full-screen-window on whichever display the
// window is on when asked. So, over the first frames of the game:
//
//   1. find this process's Unity window (class "UnityWndClass"),
//   2. if it is not on the primary display: make it a small window, move it there with
//      SetWindowPos, one frame apart so the engine sees each state,
//   3. ask Screen.SetResolution(<primary width>, <primary height>, FullScreenWindow).
//
// FullScreenWindow rather than ExclusiveFullScreen: the emulator asks for exclusive itself and
// Unity's log answers "Failed to change display to ExclusiveFullscreen...reverting to
// FullscreenWindow" on this machine; the borderless window is what actually works, and it is what
// a frontend wants anyway (instant alt-tab, overlays visible).
//
// Driven by --nixx-display=primary, ticked from the per-frame hook; a few frames of a window
// appearing in the wrong place are the price, and it gives up after 300 frames with a log line
// rather than fighting the engine forever.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace LbIntegrations.SuperZsnes.Mod
{
    internal static class PrimaryDisplay
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private const uint MONITOR_DEFAULTTOPRIMARY = 1;
        private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

        private static int _stage;          // 0 look, 1 windowed asked, 2 moved, 3 done, -1 given up
        private static int _frames;
        private static IntPtr _window;
        private static RECT _primary;

        /// <summary>Called every frame from the menu manager's Update. Idempotent once done.</summary>
        public static void Tick()
        {
            if (_stage == 3 || _stage == -1) return;
            if (++_frames > 300) { _stage = -1; Plugin.Logger.LogWarning("primary display: no Unity window found in 300 frames, giving up"); return; }

            try
            {
                if (_stage == 0)
                {
                    if (_window == IntPtr.Zero) _window = FindUnityWindow();
                    if (_window == IntPtr.Zero) return;

                    var mon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (mon == IntPtr.Zero || !GetMonitorInfoW(mon, ref info)) { _stage = -1; Plugin.Logger.LogWarning("primary display: GetMonitorInfo failed"); return; }
                    _primary = info.rcMonitor;

                    GetWindowRect(_window, out var r);
                    int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                    bool onPrimary = cx >= _primary.Left && cx < _primary.Right && cy >= _primary.Top && cy < _primary.Bottom;
                    Plugin.Logger.LogInfo("primary display: " + Rect(_primary) + "; window at " + Rect(r) + (onPrimary ? " (on it)" : " (elsewhere)")
                                          + "; mode " + Screen.fullScreenMode);

                    if (onPrimary)
                    {
                        if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
                            Screen.SetResolution(_primary.Right - _primary.Left, _primary.Bottom - _primary.Top, FullScreenMode.FullScreenWindow);
                        _stage = 3;
                        Plugin.Logger.LogInfo("primary display: full screen window, " + (_primary.Right - _primary.Left) + "x" + (_primary.Bottom - _primary.Top));
                        return;
                    }

                    // Elsewhere: a small window first, so the move is a plain move.
                    Screen.SetResolution(640, 480, FullScreenMode.Windowed);
                    _stage = 1;
                    return;
                }

                if (_stage == 1)
                {
                    SetWindowPos(_window, IntPtr.Zero, _primary.Left + 40, _primary.Top + 40, 640, 480, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                    _stage = 2;
                    return;
                }

                if (_stage == 2)
                {
                    Screen.SetResolution(_primary.Right - _primary.Left, _primary.Bottom - _primary.Top, FullScreenMode.FullScreenWindow);
                    _stage = 3;
                    Plugin.Logger.LogInfo("primary display: moved, full screen window " + (_primary.Right - _primary.Left) + "x" + (_primary.Bottom - _primary.Top));
                }
            }
            catch (Exception ex)
            {
                _stage = -1;
                Plugin.Logger.LogWarning("primary display: " + ex);
            }
        }

        private static IntPtr FindUnityWindow()
        {
            uint pid = (uint)Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var name = new StringBuilder(64);
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var owner);
                if (owner != pid || !IsWindowVisible(h)) return true;
                name.Clear();
                GetClassNameW(h, name, name.Capacity);
                if (name.ToString() == "UnityWndClass") { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static string Rect(RECT r) => r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
    }
}
