// The game loaded once RetroAchievements is logged in - not from the command line (measured 13/10, Mehdi).
//
// RASnes9x starts its login WITHOUT waiting for it (RetroAchievements.cpp: RA_AttemptLogin(false), in RA_Init) and loads
// the ROM of its command line right after (wsnes9x.cpp, WinMain: RA_Init, then LoadROM(rom_filename)). The login takes
// about 80 ms; a ROM loaded before it ends gets "Cannot load achievements - You must be logged in to load achievements.
// Please reload the game after logging in." (RACache\RALog.txt: "logged in successfully", then no ResolveHash, the game
// unloaded). It held 4 times out of 4 on 13/10, under LiteBox and LaunchBox alike; on 06/10 the ROM came 3 ms after.
//
// So when RetroAchievements will log in (its toolset beside the exe, a token in its prefs), the launch leaves the ROM out
// of the command line and puts it first in Snes9x's recent games ([Settings\Win\Files] Rom:RecentGame1); a watcher waits
// for the window and for the login's end in RALog.txt (success or failure, 5 s at most), then sends the menu command of
// recent game 1 (WM_COMMAND 0xFF00, wsnes9x.cpp) - the same LoadROM, logged in. Without RetroAchievements nothing changes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xLateLoad
    {
        private const string Files = "Settings\\Win\\Files";
        private const int RecentCount = 32;          // MAX_RECENT_GAMES_LIST_SIZE (wconfig.cpp)
        private const int RecentGame1 = 0xFF00;      // WM_COMMAND id of the first recent game (wsnes9x.cpp)
        private const int WM_COMMAND = 0x0111;

        /// <summary>Will RetroAchievements log in when this Snes9x starts? Its toolset beside the exe and a token in its prefs.</summary>
        public static bool Wanted(string exe)
        {
            try { return RaSnes9xCheevos.HasToolset(exe) && RaSnes9xCheevos.Read(exe).HasToken; }
            catch { return false; }
        }

        /// <summary><paramref name="rom"/> first in Snes9x's recent games (the others after it, once each).</summary>
        public static void PutFirst(string exe, string rom)
        {
            var conf = RaSnes9xConf.Load(RaSnes9xConf.Of(exe));
            var list = new List<string> { rom };
            for (int i = 1; i <= RecentCount; i++)
            {
                var v = conf.Get(Files, "Rom:RecentGame" + i);
                if (!string.IsNullOrWhiteSpace(v) && !list.Any(p => string.Equals(p, v.Trim(), StringComparison.OrdinalIgnoreCase))) list.Add(v.Trim());
            }
            for (int i = 1; i <= RecentCount; i++)
                if (i <= list.Count || conf.Get(Files, "Rom:RecentGame" + i) != null) conf.Set(Files, "Rom:RecentGame" + i, i <= list.Count ? list[i - 1] : "");
            conf.SaveIfChanged();
        }

        /// <summary>What is left on an empty line: an empty (or blank) new line is read by the hosts as "unchanged" and the ROM
        /// comes back (LiteBox's EmuPlugins; seen 13/10 under LaunchBox: with a single space the game still loaded at once, from the line). Snes9x
        /// ignores an option it does not know (wconfig.cpp, S9xParseArg: no else), so this one does nothing.</summary>
        internal const string Placeholder = "-lbip-late-load";

        /// <summary>The host's line without the ROM - <see cref="Placeholder"/> when nothing else is left; null when the ROM is
        /// not found on it.</summary>
        public static string WithoutRom(string line, params string[] roms)
        {
            line = line ?? "";
            foreach (var rom in roms.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var form in new[] { "\"" + rom + "\"", rom })
                {
                    int at = line.LastIndexOf(form, StringComparison.OrdinalIgnoreCase);
                    if (at >= 0) { var left = line.Remove(at, form.Length).Trim(); return left.Length == 0 ? Placeholder : left; }
                }
            return null;   // not found: the caller keeps the ROM on the line
        }

        /// <summary>From now on: wait for <paramref name="exe"/>'s window and the end of its login, then load recent game 1.</summary>
        public static void Watch(string exe, string rom)
        {
            var full = Path.GetFullPath(exe);
            var log = Path.Combine(Path.GetDirectoryName(full), "RACache", "RALog.txt");
            long from = 0;
            try { from = File.Exists(log) ? new FileInfo(log).Length : 0; } catch { }
            var started = DateTime.Now.AddSeconds(-2);
            var t = new Thread(() =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    IntPtr hwnd = IntPtr.Zero;
                    int pid = 0;
                    // The window: the process the host is about to start, from this exe.
                    while (sw.Elapsed < TimeSpan.FromSeconds(30) && hwnd == IntPtr.Zero)
                    {
                        Thread.Sleep(50);
                        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)))
                            using (p)
                                try
                                {
                                    if (!string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase) || p.StartTime < started) continue;
                                    p.Refresh();
                                    if (p.MainWindowHandle != IntPtr.Zero) { hwnd = p.MainWindowHandle; pid = p.Id; }
                                }
                                catch { }
                    }
                    if (hwnd == IntPtr.Zero) { Log.Warn("late load: no RASnes9x window within 30 s - " + Path.GetFileName(rom) + " not loaded"); return; }
                    // The login's end in RALog.txt, written by this run (after what the file held at the launch).
                    string how = "timed out";
                    var login = Stopwatch.StartNew();
                    while (login.Elapsed < TimeSpan.FromSeconds(5))   // a login takes < 0.1 s; offline, the game waits no longer (Mehdi, 13/10)
                    {
                        var lines = Tail(log, from).Split('\n');
                        if (lines.Any(l => l.IndexOf("logged in successfully", StringComparison.OrdinalIgnoreCase) >= 0)) { how = "logged in"; break; }
                        // The server's answer that is not a success (a token refused, offline): no point waiting.
                        if (lines.Any(l => l.IndexOf("<< login2 response", StringComparison.Ordinal) >= 0 && l.IndexOf("\"Success\":true", StringComparison.Ordinal) < 0)) { how = "login refused"; break; }
                        Thread.Sleep(50);
                    }
                    PostMessage(hwnd, WM_COMMAND, (IntPtr)RecentGame1, IntPtr.Zero);
                    Log.Info("late load: " + Path.GetFileName(rom) + " loaded after the login (" + how + ", " + (int)sw.Elapsed.TotalMilliseconds + " ms after the launch, pid " + pid + ")");
                }
                catch (Exception ex) { Log.Warn("late load failed - " + Path.GetFileName(rom) + " may not be loaded", ex); }
            }) { IsBackground = true, Name = "RASnes9x late load" };
            t.Start();
        }

        /// <summary>What was added to <paramref name="path"/> since <paramref name="from"/> (all of it when it shrank).</summary>
        internal static string Tail(string path, long from)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(fs.Length < from ? 0 : from, SeekOrigin.Begin);
                using var r = new StreamReader(fs);
                return r.ReadToEnd();
            }
            catch { return ""; }
        }

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
