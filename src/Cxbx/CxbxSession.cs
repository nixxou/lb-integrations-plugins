// A game's session, watched from the launch to its end.
//
// FULL SCREEN IN A WINDOW. Cxbx-Reloaded has two: [video] FullScreen, exclusive (Direct3D's own, which locks up the
// screen on some machines), and its "faux" full screen - a borderless, maximized window - which it only enters on
// Alt+Enter (Direct3D9.cpp, WM_SYSKEYDOWN VK_RETURN -> ToggleFauxFullscreen). There is no setting for it. So once the
// game's window shows - class "CxbxRender", in a cxbxr-ldr process - this sends it that Alt+Enter. Nothing is sent when
// [video] FullScreen is on: the exclusive mode is then the user's choice, and ToggleFauxFullscreen does nothing anyway.
//
// THE END IS WHEN NO LOADER HAS BEEN SEEN FOR A WHILE, not when the first one exits: a game that reboots (a dashboard,
// a second disc, many menus) makes Cxbx-Reloaded start a NEW cxbxr-ldr and the first one quit. Watched by name, with a
// grace, the session survives that. Then: the RAM disk released, the save packed (CxbxSaves). The launch gate stays
// shut until this is done (LbipLaunchGate.HoldOpen).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxSession
    {
        private const int AppearSeconds = 60, GoneSeconds = 6;

        public static void Watch(string exe, string titleId, bool borderless, bool closeGui = false)
        {
            var hold = LbipLaunchGate.HoldOpen();
            Task.Run(() =>
            {
                using var held = hold;
                try
                {
                    var armed = Stopwatch.StartNew();
                    while (!CxbxPaths.LoaderRunning() && armed.Elapsed.TotalSeconds < AppearSeconds) Thread.Sleep(500);
                    if (!CxbxPaths.LoaderRunning()) { Log.Info("session: Cxbx-Reloaded never started"); return; }
                    Log.Info("session: Cxbx-Reloaded is running" + (borderless ? " - full screen in a window once the game shows" : ""));

                    var handled = new HashSet<IntPtr>();
                    var seen = new HashSet<int>();
                    var lastSeen = Stopwatch.StartNew();
                    while (lastSeen.Elapsed.TotalSeconds < GoneSeconds)
                    {
                        var pids = LoaderPids();
                        if (pids.Count > 0)
                        {
                            lastSeen.Restart();
                            foreach (var pid in pids) seen.Add(pid);
                            if (borderless) Borderless(pids, handled);
                        }
                        Thread.Sleep(400);
                    }
                    Log.Info("session: over" + (seen.Count > 1 ? " (" + seen.Count + " loaders - the game restarted itself " + (seen.Count - 1) + " time(s))" : ""));

                    // THE GUI STAYS OPEN when its game stops (measured 03/10: Alt+F4 in the game, the loader gone, cxbx.exe still
                    // there) - and LaunchBox waits on cxbx.exe. Closed here, as its own close button would: never killed.
                    if (closeGui) CloseGui(exe);
                }
                catch (Exception ex) { Log.Warn("session watch", ex); }
                finally
                {
                    CxbxRamSession.Release("its game is over");
                    CxbxOptions.Restore(exe, "its game is over");
                    try { if (titleId != null) CxbxSaves.Sync(exe, titleId, LbIntegrations.Xbox.XboxSyncMode.SessionEnd); } catch (Exception ex) { Log.Warn("save capture", ex); }
                }
            });
        }

        private static void CloseGui(string exe)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(CxbxPaths.Gui)))
                    using (p)
                    {
                        string path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (path != null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) continue;
                        if (!p.CloseMainWindow()) { Log.Info("session: the GUI has no window to close - left open"); continue; }
                        if (p.WaitForExit(15000)) Log.Info("session: the game is over - the GUI closed, back to the frontend");
                        else Log.Info("session: the GUI was asked to close and is still there (a question on screen?) - left to the user");
                    }
            }
            catch (Exception ex) { Log.Warn("session: closing the GUI", ex); }
        }

        private static List<int> LoaderPids()
        {
            var found = new List<int>();
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(CxbxPaths.Loader)))
                    using (p) found.Add(p.Id);
            }
            catch { }
            return found;
        }

        /// <summary>Each new game window of these processes, once visible: Alt+Enter - in the GUI it takes the game out of the GUI's window, full screen; a second one puts it back, menus within reach.</summary>
        private static void Borderless(List<int> pids, HashSet<IntPtr> handled)
        {
            // The game's window is top-level when the loader runs alone, and a CHILD of the GUI's window when the GUI
            // launched it (measured 03/10: style WS_CHILD, its parent the GUI's) - so the children are looked at too.
            var candidates = new List<IntPtr>();
            EnumWindows((top, _) =>
            {
                candidates.Add(top);
                EnumChildWindows(top, (child, __) => { candidates.Add(child); return true; }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
            foreach (var hwnd in candidates)
            {
                try
                {
                    if (handled.Contains(hwnd) || !IsWindowVisible(hwnd)) continue;
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (!pids.Contains((int)pid)) continue;
                    var cls = new StringBuilder(64);
                    GetClassName(hwnd, cls, cls.Capacity);
                    if (cls.ToString() != "CxbxRender") continue;
                    handled.Add(hwnd);
                    // A moment for the device to be made: the toggle restyles the window under it.
                    Thread.Sleep(700);
                    PostMessage(hwnd, WmSysKeyDown, (IntPtr)VkReturn, (IntPtr)0x20000001);   // bit 29: Alt held
                    Log.Info("session: Alt+Enter sent to the game's window - full screen in a window");
                }
                catch { }
            }
        }

        /// <summary>Is [video] FullScreen on in the settings this install reads - the exclusive mode, then left alone.</summary>
        public static bool ExclusiveFullScreen(string exe)
        {
            try
            {
                var ini = CxbxPaths.SettingsOf(exe);
                if (ini == null) return false;
                return CxbxPaths.ReadIni(ini).TryGetValue("video|FullScreen", out var v)
                       && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");
            }
            catch { return false; }
        }

        private const uint WmSysKeyDown = 0x0104;
        private const int VkReturn = 0x0D;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
