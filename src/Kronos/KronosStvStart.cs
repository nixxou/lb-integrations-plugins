// Starting an ST-V game in Kronos 2.7.5 - which cannot do it from its command line or its settings alone (measured 19/10):
//
// THE GAME LIST IS ONLY MADE BY THE SETTINGS WINDOW. Kronos loads a set from the list of games it found in the ROM folder
// (stv.c availableGames), and in 2.7.5 that list is filled only when its settings window shows the ST-V cartridge
// (UISettings -> STVGetRomList): the call meant for the start (UIYabause::threadInitialized) is never made - the fork fixed it
// after 2.7.5. Started with -a, Kronos says "Cannot initialize STV emulation" twice and stays on an empty window.
//
// SO, ONCE KRONOS'S WINDOW IS THERE, AS A USER WOULD - with no key typed and no click, nothing that needs the window in front
// or touches the keyboard (the user may be typing elsewhere):
//   1. its toolbar's "Select Cartridge" invoked through UI Automation (the settings window opens on the cartridge tab, the
//      folder of kronos.ini's Cartridge\Path\STVRomgame read, the list made);
//   2. that window's OK by an Enter POSTED to it (its default button; its controls are not exposed to UI Automation);
//   3. "Run" invoked; then, for a game in full screen, its toolbar's "Fullscreen" toggled (a -f at the start would put the
//      settings window over a full-screen picture).
// Each step waits for what it needs, a few seconds at most; a step that fails is said in the log and the rest left to the user
// (the help: Ctrl+A, OK, Run). The settings window's OK writes Kronos's whole settings, as it always does - its own extra keys
// (STVGameName, STV\Region...) stay, the session's are put back after.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace LbIntegrations.Kronos
{
    internal static class KronosStvStart
    {
        private const int AppearSeconds = 60;

        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);

        /// <summary>The steps above, in the background once Kronos at <paramref name="exe"/> shows its window. Never throws.</summary>
        public static void Begin(string exe, string set, bool fullscreen)
        {
            Task.Run(() =>
            {
                try { Drive(exe, set, fullscreen); }
                catch (Exception ex) { Log.Warn("st-v start: " + ex.Message + " - start the game in Kronos: Ctrl+A, OK, Run", ex); }
            });
        }

        internal static void Drive(string exe, string set, bool fullscreen)
        {
            var clock = Stopwatch.StartNew();
            int pid = 0;
            AutomationElement main = null;
            // Its main window, by the process of that exe.
            while (clock.Elapsed.TotalSeconds < AppearSeconds && main == null)
            {
                Thread.Sleep(400);
                pid = PidOf(exe);
                if (pid == 0) continue;
                main = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
                if (main != null && Button(main, "Select Cartridge") == null) main = null;   // still being built
            }
            if (main == null) { Log.Warn("st-v start: Kronos's window never came - start the game in Kronos: Ctrl+A, OK, Run"); return; }

            // 1. Select Cartridge: a modal window - invoked aside, its return waits for that window.
            var select = Button(main, "Select Cartridge");
            Task.Run(() => { try { Invoke(select); } catch { } });
            var dialog = WaitWindow(pid, "Settings", 15);
            if (dialog == IntPtr.Zero) { Log.Warn("st-v start: the settings window did not open - start the game in Kronos: Ctrl+A, OK, Run"); return; }
            Thread.Sleep(1200);   // the list made (the folder's zips read)

            // 2. OK: Enter posted to the window - its default button.
            PostMessage(dialog, 0x0100, (IntPtr)0x0D, (IntPtr)0x001C0001);   // WM_KEYDOWN VK_RETURN
            PostMessage(dialog, 0x0101, (IntPtr)0x0D, unchecked((IntPtr)0xC01C0001));   // WM_KEYUP
            var gone = Stopwatch.StartNew();
            while (FindWindow(pid, "Settings") != IntPtr.Zero && gone.Elapsed.TotalSeconds < 10) Thread.Sleep(200);
            if (FindWindow(pid, "Settings") != IntPtr.Zero) { Log.Warn("st-v start: the settings window stayed open - press OK, then Run"); return; }
            Thread.Sleep(500);

            // 3. Run, then the full screen asked for.
            Invoke(Button(main, "Run"));
            Log.Info("st-v start: " + set + " - its list made in the settings window, started (" + clock.Elapsed.TotalSeconds.ToString("0.0") + " s)");
            if (fullscreen)
            {
                Thread.Sleep(800);
                var full = Button(main, "Fullscreen");
                if (full != null && full.TryGetCurrentPattern(TogglePattern.Pattern, out var t) && ((TogglePattern)t).Current.ToggleState != ToggleState.On)
                    ((TogglePattern)t).Toggle();
            }
        }

        private static AutomationElement Button(AutomationElement main, string name)
            => main.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));

        private static void Invoke(AutomationElement e)
        {
            if (e == null) throw new InvalidOperationException("a button of Kronos's toolbar is not there");
            ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }

        private static int PidOf(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(KronosLaunch.ExecutableName)))
                    using (p)
                        try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return p.Id; } catch { }
            }
            catch { }
            return 0;
        }

        private static IntPtr WaitWindow(int pid, string title, int seconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var h = FindWindow(pid, title);
                if (h != IntPtr.Zero) return h;
                Thread.Sleep(200);
            }
            return IntPtr.Zero;
        }

        /// <summary>A visible top-level window of that process with exactly that title.</summary>
        private static IntPtr FindWindow(int pid, string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var p);
                if (p != (uint)pid || !IsWindowVisible(h)) return true;
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, sb.Capacity);
                if (sb.ToString() == title) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
