// --launch-gate: one launch at a time (src\Shared.Lbip\LbipLaunchGate.cs), in the plugin under test, with a REAL process
// standing in for the emulator (ping, a few seconds long). Nothing is written; the message a refusal would show is caught.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Probe
{
    internal static class LaunchGateCheck
    {
        private static int _bad;

        private static bool Check(string what, bool ok, string detail = null)
        {
            Console.WriteLine("    " + (ok ? "ok   " : "FAIL ") + what + (!ok && detail != null ? "\n          got: " + detail : ""));
            if (!ok) _bad++;
            return ok;
        }

        public static bool Run(Assembly asm)
        {
            _bad = 0;
            Console.WriteLine();
            Console.WriteLine("-- one launch at a time  [starts ping.exe as the emulator] --");
            var gate = asm.GetType("LbIntegrations.Lbip.LbipLaunchGate", throwOnError: true);
            var notice = asm.GetType("LbIntegrations.Lbip.LbipNotice", throwOnError: true);
            gate.GetField("Off", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            var said = new List<string>();
            notice.GetField("Instead", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, new Action<string, string>((t, x) => { lock (said) said.Add(x); }));
            var run = gate.GetMethod("Run", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
            var emu = new StubEmulator { Title = "stand-in", ApplicationPath = ping };
            PrepareForLaunchArgs Args(string title) => new PrepareForLaunchArgs(emu, StubGame.Create(title, title, "C:\\game.bin"), "", null, null);
            int prepared = 0;
            Func<PrepareForLaunchArgs, PrepareForLaunchResponse> accept = _ => { prepared++; return new PrepareForLaunchResponse(true, null); };
            Func<PrepareForLaunchArgs, PrepareForLaunchResponse> refuse = _ => { prepared++; return new PrepareForLaunchResponse(false, null); };
            bool Go(string title, Func<PrepareForLaunchArgs, PrepareForLaunchResponse> prepare)
                => ((PrepareForLaunchResponse)run.Invoke(null, new object[] { "Nixx-Test", Args(title), prepare, null })).WasSuccess;
            // Open = the plugin is asked (a refused prepare holds nothing, so asking changes nothing).
            bool Open() { int before = prepared; Go("probe-open", refuse); lock (said) said.Clear(); return prepared > before; }
            bool WaitOpen(int seconds)
            {
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds) { if (Open()) return true; Thread.Sleep(200); }
                return false;
            }

            // 1. A launch the plugin refuses holds nothing.
            prepared = 0;
            Check("a launch the plugin refused: nothing held, the next one goes", !Go("A", refuse) && Go("B", refuse) == false && prepared == 2);

            // 1b. The emulator already open outside a launch of ours: refused, said, the plugin not asked, nothing held after.
            using (var open = Process.Start(new ProcessStartInfo(ping, "-n 3 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false }))
            {
                Thread.Sleep(300);
                prepared = 0;
                Check("the emulator already open: refused, said, the plugin not asked", !Go("Game 0", accept) && prepared == 0 && said.Count == 1 && said[0].Contains("already open"), string.Join(" | ", said));
                lock (said) said.Clear();
                open.WaitForExit();
            }
            Check("  ...and once it is closed, nothing held", Open());
            // 2. An accepted launch, its emulator running: the next is refused - silently at first, then said.
            Check("a launch accepted", Go("Game 1", accept));
            using (var p = Process.Start(new ProcessStartInfo(ping, "-n 9 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false }))
            {
                prepared = 0;
                Check("a second click at once: refused, silently, the plugin not even asked", !Go("Game 2", accept) && said.Count == 0 && prepared == 0);
                Thread.Sleep(5500);
                Check("after 5 s, the emulator still running: refused, and said", !Go("Game 3", accept) && said.Count == 1 && said[0].Contains("Game 1"), string.Join(" | ", said));
                var quit = Stopwatch.StartNew();
                p.WaitForExit();
                Check("while it runs, still refused", true);
                // 3. Open again a second after it has quit - not before.
                Thread.Sleep(300);
                Check("300 ms after the emulator has quit: still held (the second after)", !Open());
                Check("open again, about a second after it quit", WaitOpen(5) && quit.Elapsed.TotalSeconds >= 1.0, quit.Elapsed.TotalSeconds.ToString("0.0") + " s");
            }

            // 3b. A watcher's hold (adversarial review, 30/09): the gate stays shut until the plugin's own end is done,
            //     the emulator long gone - then opens a second after.
            var holdOpen = gate.GetMethod("HoldOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            IDisposable hold = null;
            Func<PrepareForLaunchArgs, PrepareForLaunchResponse> acceptHeld = _ => { prepared++; hold = (IDisposable)holdOpen.Invoke(null, null); return new PrepareForLaunchResponse(true, null); };
            Check("a launch whose watcher holds the gate", Go("Game H", acceptHeld) && hold != null);
            using (var p = Process.Start(new ProcessStartInfo(ping, "-n 2 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })) p.WaitForExit();
            Thread.Sleep(2500);
            Check("the emulator gone 2.5 s, the watcher not done: still shut", !Open());
            var let = Stopwatch.StartNew();
            hold.Dispose();
            Check("the watcher done: open again, a second after", WaitOpen(5) && let.Elapsed.TotalSeconds >= 0.9, let.Elapsed.TotalSeconds.ToString("0.0") + " s");

            // 4. An emulator that never comes: held for its 60 s, not for ever - not waited here, only told apart.
            Check("an accepted launch whose emulator never comes is held (released after 60 s)", Go("Game 5", accept) && !Open());

            gate.GetField("Off", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
            notice.GetField("Instead", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            Console.WriteLine(_bad == 0 ? "\n  OK - one launch at a time, a double click refused without a word" : "\n  " + _bad + " FAILED");
            return _bad == 0;
        }
    }
}
