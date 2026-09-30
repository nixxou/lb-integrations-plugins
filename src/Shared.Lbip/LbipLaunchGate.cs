// One launch at a time, per plugin (Mehdi, 30/09): while a game this plugin launched is still on - being prepared,
// running, or its end being put right - a new launch is refused. Refused SILENTLY in the first 5 seconds of the one
// on (a double click), with a message after.
//
// THIS FOLDER IS COMPILED INTO EACH PLUGIN, so each has its own gate: a Flycast game does not hold a PPSSPP launch.
//
// WHEN A LAUNCH IS OVER: its emulator (the executable launched, by its path) has come and gone - or never came within
// 60 s (LaunchBox did not start it after all) - then the plugin's own end is done: EVERY HOLD of the launch let go (a
// watcher that puts the session right takes one - HoldOpen - and lets it go when it is done; adversarial review of
// 30/09: a gate that opened on a timer let a second launch start while the first one's watcher was still waiting, and
// that watcher then put the second one's settings back before its emulator had read them), and Idle (30 s at most),
// then 1 s more. A launch the plugin refused (WasSuccess false) holds nothing. And no launch goes into an emulator already
// open outside one of ours (its executable running): refused, with a message.
//
// NEVER A LOCK HELD ACROSS ANYTHING: the state is two fields under a lock taken for a few instructions; the watch runs
// on a pool thread; the message on a thread of its own (LbipNotice) - the host's launch thread only reads a flag.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Lbip
{
    internal static class LbipLaunchGate
    {
        private const int SilentSeconds = 5, AppearSeconds = 60, IdleSeconds = 30;

        private static readonly object Gate = new object();
        private static DateTime? _since;          // when the launch on started; null: none
        private static string _what;
        private static int _holds;                // watchers of the launch on, not done yet
        private static int _generation;           // which launch: a hold of an older one lets go of nothing

        /// <summary>The longest a hold keeps the gate shut: a watcher that never ends must not lock launches for ever.</summary>
        private const int HoldCapMinutes = 15;

        /// <summary>Keep the gate shut until the returned hold is disposed - for a watcher of the launch being prepared, to
        /// take before the prepare returns. Dispose it once (more is harmless).</summary>
        public static IDisposable HoldOpen()
        {
            lock (Gate) { _holds++; return new Hold(_generation); }
        }

        private sealed class Hold : IDisposable
        {
            private readonly int _of;
            private int _done;
            public Hold(int of) { _of = of; }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 1) return;
                lock (Gate) if (_of == _generation && _holds > 0) _holds--;
            }
        }

        /// <summary>For the probe: no gate at all (its launches start no emulator).</summary>
        internal static bool Off;

        /// <summary>The whole of a launch through the gate: refused while one is on, else <paramref name="prepare"/>,
        /// then held until it is over when it was accepted. <paramref name="idle"/>: true once the plugin's own end of
        /// a session is done (null: nothing to wait for). Never throws.</summary>
        public static PrepareForLaunchResponse Run(string pluginName, PrepareForLaunchArgs args,
                                                   Func<PrepareForLaunchArgs, PrepareForLaunchResponse> prepare, Func<bool> idle = null)
        {
            if (Off) return prepare(args);
            string title = null, exe = null;
            try { title = args?.GameBeingLaunched?.Title; } catch { }
            try { exe = LbipImportWatch.Full(args?.EmulatorBeingLaunched?.ApplicationPath); } catch { }

            DateTime? on; string what;
            lock (Gate)
            {
                on = _since; what = _what;
                if (on == null) { _since = DateTime.UtcNow; _what = title; _generation++; _holds = 0; }
            }
            if (on != null)
            {
                var age = DateTime.UtcNow - on.Value;
                if (age.TotalSeconds < SilentSeconds)
                    LbipLog.Info("launch of \"" + title + "\" refused: \"" + what + "\" was launched " + age.TotalSeconds.ToString("0.0") + " s ago (a double click) - silently");
                else
                {
                    LbipLog.Info("launch of \"" + title + "\" refused: \"" + what + "\" is still on (" + (int)age.TotalSeconds + " s)");
                    LbipNotice.Show(pluginName, "\"" + what + "\" is still running in " + pluginName.Replace("Nixx-", "")
                                    + ".\n\nClose it first - the next game can be launched a second after it has quit.");
                }
                return new PrepareForLaunchResponse(success: false);
            }

            // THE EMULATOR ALREADY OPEN, not by a launch of ours (Mehdi, 30/09: Vita3K opened on a game's settings, the
            // emulator started by hand): a game launched into it would share its files mid-use. Said every time - a
            // double click is caught above, so this one is not.
            if (Running(exe))
            {
                Release("the emulator was already open");
                var emulator = pluginName.Replace("Nixx-", "");
                LbipLog.Info("launch of \"" + title + "\" refused: " + Path.GetFileName(exe) + " is already running");
                LbipNotice.Show(pluginName, emulator + " is already open (" + Path.GetFileName(exe) + ").\n\n"
                                + "Close it first, then launch the game again.");
                return new PrepareForLaunchResponse(success: false);
            }

            PrepareForLaunchResponse response = null;
            try { response = prepare(args); return response; }
            finally
            {
                bool accepted = false;
                try { accepted = response != null && response.WasSuccess; } catch { }
                if (!accepted) { lock (Gate) _holds = 0; Release("the launch was not made"); }
                else Watch(exe, idle);
            }
        }

        private static void Release(string why)
        {
            lock (Gate) { _since = null; _what = null; }
            LbipLog.Info("launch gate open again (" + why + ")");
        }

        private static void Watch(string exe, Func<bool> idle)
        {
            Task.Run(() =>
            {
                string why = "over";
                try
                {
                    var armed = Stopwatch.StartNew();
                    bool came = false;
                    while (armed.Elapsed.TotalSeconds < AppearSeconds)
                    {
                        if (Running(exe)) { came = true; break; }
                        Thread.Sleep(500);
                    }
                    if (!came) why = "the emulator never came";
                    while (Running(exe)) Thread.Sleep(500);
                    // The plugin's watchers of this launch, done.
                    var holding = Stopwatch.StartNew();
                    while (holding.Elapsed.TotalMinutes < HoldCapMinutes) { int h; lock (Gate) h = _holds; if (h == 0) break; Thread.Sleep(250); }
                    lock (Gate) { if (_holds > 0) { LbipLog.Warn("launch gate: a watcher of the last launch still holds it after " + HoldCapMinutes + " min - opened anyway"); _holds = 0; } }
                    var waited = Stopwatch.StartNew();
                    if (idle != null)
                        while (waited.Elapsed.TotalSeconds < IdleSeconds && !Safe(idle)) Thread.Sleep(250);
                    if (came) Thread.Sleep(1000);
                }
                catch (Exception ex) { why = "the watch failed: " + ex.Message; }
                finally { Release(why); }
            });
        }

        private static bool Safe(Func<bool> f) { try { return f(); } catch { return true; } }

        /// <summary>Is the executable at <paramref name="exe"/> running - by its name, and by its path when it can be read.</summary>
        private static bool Running(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                    using (p)
                    {
                        string path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        if (path == null || string.Equals(Path.GetFullPath(path), exe, StringComparison.OrdinalIgnoreCase)) return true;
                    }
            }
            catch { }
            return false;
        }
    }
}
