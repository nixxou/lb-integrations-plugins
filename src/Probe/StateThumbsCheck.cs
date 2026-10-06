// The thumbnail beside a save state (LbipStateThumbs), measured on a real emulator: the emulator is started on a copy of a
// game, a state file is written in the watched folder while it runs, and the PNG must appear beside it with the game on it.

#nullable disable

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using LbIntegrations.Lbip;

namespace LbIntegrations.Probe
{
    internal static class StateThumbsCheck
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        private struct RC { public int L, T, R, B; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RC r);
        private static bool Has(string[] a, string f) => Array.IndexOf(a, f) >= 0;
        private static void Front(IntPtr h)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);   // Alt down: a background process may then take the foreground
            SetForegroundWindow(h);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
        }
        /// <summary>--state-thumbs &lt;exe&gt; &lt;rom&gt; [args before the rom, ' for "]: true when the PNG came out and is not black.</summary>
        public static bool Run(string exe, string rom, string before)
        {
            var dir = Path.Combine(Path.GetTempPath(), "lbip-thumbs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, Path.GetFileName(rom));
            File.Copy(rom, copy);
            var state = Path.Combine(dir, Path.GetFileNameWithoutExtension(rom) + ".bs1");
            LbipStateThumbs.Watch(exe, new[] { dir }, p => p.EndsWith(".bs1", StringComparison.OrdinalIgnoreCase), m => Console.WriteLine("  " + m));
            var p = Process.Start(new ProcessStartInfo(exe, (before ?? "").Replace('\'', '"') + " \"" + copy + "\"") { UseShellExecute = false });
            try
            {
                Thread.Sleep(8000);
                // As in a real session: the emulator has the focus when its save key is pressed.
                if (Has(Environment.GetCommandLineArgs(), "--front")) Front(p.MainWindowHandle);
                Thread.Sleep(1000);
                p.Refresh();
                GetWindowThreadProcessId(GetForegroundWindow(), out var fgPid);
                Console.WriteLine("  ares pid " + p.Id + " main " + p.MainWindowHandle + " '" + p.MainWindowTitle + "', foreground pid " + fgPid);
                using (var shot = LbipStateThumbs.CaptureEmulator(Path.GetFullPath(p.MainModule.FileName)))
                {
                    GetWindowRect(p.MainWindowHandle, out var wr);
                    using (var raw = new Bitmap(Math.Max(1, wr.R - wr.L), Math.Max(1, wr.B - wr.T)))
                    {
                        using (var g = Graphics.FromImage(raw)) g.CopyFromScreen(wr.L, wr.T, 0, 0, raw.Size);
                        raw.Save(Path.Combine(dir, "raw-screen.png"));
                        Console.WriteLine("  raw " + raw.Width + "x" + raw.Height + " at " + wr.L + "," + wr.T + " -> " + Path.Combine(dir, "raw-screen.png"));
                    }
                    Console.WriteLine("  capture now: " + (shot == null ? "null" : shot.Width + "x" + shot.Height));
                }
                File.WriteAllBytes(state, new byte[4096]);
                Thread.Sleep(3000);
                var png = LbipStateThumbs.PathOf(state);
                if (!File.Exists(png)) { Console.WriteLine("FAIL no " + png); return false; }
                using (var b = new Bitmap(png)) Console.WriteLine("OK " + png + " " + b.Width + "x" + b.Height + " " + new FileInfo(png).Length + " bytes");
                Console.WriteLine("companion: " + string.Join(",", LbipStateThumbs.Companion(state)));
                return true;
            }
            finally { try { if (!p.HasExited) p.Kill(); } catch { } }
        }
    }
}