// A thumbnail beside every save state (Mehdi, 07/10: "à côté des savestates courantes, quand c'est possible, un fichier png
// du même nom avec la thumbnail" - PNG).
//
// THE FILE: <state file>.png - "Zelda.bs1" -> "Zelda.bs1.png", "ULUS10041_1.00_0.ppst" -> "ULUS10041_1.00_0.ppst.png". The
// state's whole name, not its stem: an emulator that keeps its slots as extensions (.bs1 .. .bs9) would otherwise give
// every slot the same picture. An emulator that writes its own picture of the state (PPSSPP's .jpg) gets it as PNG under
// the same name (FromImage).
//
// THE PICTURE: taken when the state file is written - the moment the user pressed the key, the game's frame on screen.
// A session watches the emulator's state folders (Watch) for as long as the emulator runs; at a state written, the
// emulator's main window (its client area) is captured: from the screen when it is the foreground window (what is shown, OpenGL, Vulkan or
// Direct3D alike), else PrintWindow. A picture that comes out all black (an OpenGL window in the background gives that to PrintWindow - measured on ares) is not kept - a
// state without a thumbnail rather than a wrong one. Scaled to 480 pixels wide at most.
//
// A save manager's job: GetCompanionSaveFiles names the PNG of a state (Companion), so LaunchBox's backups carry it and a
// restore puts it back; a state deleted takes its PNG with it (Remove).

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Lbip
{
    internal static class LbipStateThumbs
    {
        public const string Suffix = ".png";
        private const int MaxWidth = 480;

        /// <summary>The thumbnail of a state file.</summary>
        public static string PathOf(string statePath) => string.IsNullOrEmpty(statePath) ? null : statePath + Suffix;

        /// <summary>Is this one of our thumbnails (never a state itself).</summary>
        public static bool IsThumb(string path) => path != null && (path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) || path.EndsWith(Suffix + ".part", StringComparison.OrdinalIgnoreCase));

        /// <summary>For GetCompanionSaveFiles: the state's thumbnail when there is one.</summary>
        public static string[] Companion(string statePath)
        {
            try { var p = PathOf(statePath); return p != null && File.Exists(p) ? new[] { p } : new string[0]; }
            catch { return new string[0]; }
        }

        /// <summary>A state deleted: its thumbnail too. Never throws.</summary>
        public static void Remove(string statePath)
        {
            try { var p = PathOf(statePath); if (p != null && File.Exists(p)) File.Delete(p); } catch { }
        }

        /// <summary>A picture the emulator wrote of a state itself (a .jpg beside it) kept as the state's PNG. Never throws.</summary>
        public static bool FromImage(string imagePath, string statePath)
        {
            try
            {
                if (!File.Exists(imagePath)) return false;
                using (var src = LoadUnlocked(imagePath))
                using (var small = Scale(src))
                    Save(small, PathOf(statePath));
                return true;
            }
            catch (Exception ex) { Debug.WriteLine("thumbnail from " + imagePath + ": " + ex.Message); return false; }
        }

        /// <summary>A picture the plugin decoded from the state itself (Mesen's .mss carries its frame) kept as the state's PNG,
        /// scaled as the others. The caller keeps <paramref name="picture"/>. Never throws.</summary>
        public static bool FromBitmap(Image picture, string statePath)
        {
            try
            {
                if (picture == null) return false;
                using (var small = Scale(picture))
                    Save(small, PathOf(statePath));
                return true;
            }
            catch (Exception ex) { Debug.WriteLine("thumbnail of " + statePath + ": " + ex.Message); return false; }
        }

        // ── the watch ────────────────────────────────────────────────────────

        /// <summary>Watches <paramref name="dirs"/> while an emulator of <paramref name="exe"/> runs (a minute to appear, it
        /// ends 2 s after the last one is gone): each file <paramref name="isState"/> accepts that is written gets its
        /// thumbnail. <paramref name="log"/> is told what happened. Never throws.</summary>
        public static void Watch(string exe, IEnumerable<string> dirs, Func<string, bool> isState, Action<string> log = null)
        {
            try
            {
                var folders = (dirs ?? Enumerable.Empty<string>()).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (exe == null || folders.Count == 0 || isState == null) return;
                foreach (var d in folders) try { Directory.CreateDirectory(d); } catch { }
                var pending = new ConcurrentDictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
                var watchers = new List<FileSystemWatcher>();
                void Seen(string path)
                {
                    try
                    {
                        if (IsThumb(path) || !isState(path)) return;
                        // The frame now - before the state is even finished: the moment the key was pressed.
                        if (!pending.ContainsKey(path))
                        {
                            var shot = CaptureEmulator(exe);
                            if (shot != null && !pending.TryAdd(path, shot)) shot.Dispose();
                            if (shot == null) log?.Invoke("thumbnail: no picture of the emulator for " + Path.GetFileName(path));
                        }
                        // Written once the state file has stopped changing.
                        Task.Run(async () =>
                        {
                            long last = -1;
                            for (int i = 0; i < 40; i++)
                            {
                                await Task.Delay(250).ConfigureAwait(false);
                                long len = -2;
                                try { len = new FileInfo(path).Length; } catch { }
                                if (len == last && len > 0) break;
                                last = len;
                            }
                            if (pending.TryRemove(path, out var bmp))
                                using (bmp)
                                {
                                    try { using (var small = Scale(bmp)) Save(small, PathOf(path)); log?.Invoke("thumbnail: " + Path.GetFileName(PathOf(path))); }
                                    catch (Exception ex) { log?.Invoke("thumbnail: not written for " + Path.GetFileName(path) + " - " + ex.Message); }
                                }
                        });
                    }
                    catch (Exception ex) { log?.Invoke("thumbnail: " + ex.Message); }
                }
                foreach (var d in folders)
                {
                    try
                    {
                        var w = new FileSystemWatcher(d) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
                        w.Created += (_, e) => Seen(e.FullPath);
                        w.Changed += (_, e) => Seen(e.FullPath);
                        w.Renamed += (_, e) => Seen(e.FullPath);
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                    }
                    catch (Exception ex) { log?.Invoke("thumbnail: cannot watch " + d + " - " + ex.Message); }
                }
                if (watchers.Count == 0) return;
                Task.Run(() =>
                {
                    try
                    {
                        var armed = Stopwatch.StartNew();
                        while (!Running(exe) && armed.Elapsed.TotalSeconds < 60) Thread.Sleep(500);
                        var lastSeen = Stopwatch.StartNew();
                        while (lastSeen.Elapsed.TotalSeconds < 2) { if (Running(exe)) lastSeen.Restart(); Thread.Sleep(500); }
                        Thread.Sleep(3000);   // a state written as the emulator closed is still finished
                    }
                    catch { }
                    finally { foreach (var w in watchers) try { w.Dispose(); } catch { } }
                });
            }
            catch (Exception ex) { log?.Invoke("thumbnail watch: " + ex.Message); }
        }

        private static IEnumerable<Process> Of(string exe)
        {
            string full;
            try { full = Path.GetFullPath(exe); } catch { yield break; }
            Process[] all;
            try { all = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(full)); } catch { yield break; }
            foreach (var p in all)
            {
                bool ours;
                try { ours = string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase); } catch { ours = true; }
                if (ours) yield return p; else p.Dispose();
            }
        }

        private static bool Running(string exe)
        {
            var any = false;
            foreach (var p in Of(exe)) using (p) any = true;
            return any;
        }

        // ── the picture ──────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        /// <summary>The emulator's window as it is now, or null.</summary>
        public static Bitmap CaptureEmulator(string exe)
        {
            try
            {
                var fg = GetForegroundWindow();
                GetWindowThreadProcessId(fg, out var fgPid);
                IntPtr main = IntPtr.Zero;
                foreach (var p in Of(exe))
                    using (p)
                    {
                        if (fg != IntPtr.Zero && p.Id == fgPid) { main = fg; break; }
                        if (main == IntPtr.Zero) main = p.MainWindowHandle;
                    }
                if (main == IntPtr.Zero || IsIconic(main)) return null;
                bool foreground = main == fg;
                var shot = foreground ? FromScreen(main) : null;
                if (shot != null && !Black(shot)) return shot;
                shot?.Dispose();
                shot = FromPrintWindow(main);
                if (shot != null && !Black(shot)) return shot;
                shot?.Dispose();
                return null;
            }
            catch { return null; }
        }

        /// <summary>The window's client area on screen: the game, without the title bar and the menu.</summary>
        private static Rectangle Bounds(IntPtr hwnd)
        {
            GetClientRect(hwnd, out var c);
            var origin = new POINT();
            ClientToScreen(hwnd, ref origin);
            return new Rectangle(origin.X, origin.Y, c.Right - c.Left, c.Bottom - c.Top);
        }

        private static Bitmap FromScreen(IntPtr hwnd)
        {
            var b = Bounds(hwnd);
            if (b.Width < 16 || b.Height < 16) return null;
            var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        private static Bitmap FromPrintWindow(IntPtr hwnd)
        {
            var b = Bounds(hwnd);
            int w = b.Width, h = b.Height;
            if (w < 16 || h < 16) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var dc = g.GetHdc();
                try { PrintWindow(hwnd, dc, 1 | 2 /* PW_CLIENTONLY | PW_RENDERFULLCONTENT */); } finally { g.ReleaseHdc(dc); }
            }
            return bmp;
        }

        /// <summary>Every sampled pixel black: nothing was drawn (a dark scene of a game still has something lit -
        /// Zelda's intro, measured, is 99 % black).</summary>
        private static bool Black(Bitmap bmp)
        {
            for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 100))
                for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 100))
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.R + c.G + c.B >= 24) return false;
                }
            return true;
        }

        private static Bitmap Scale(Image src)
        {
            int w = Math.Min(MaxWidth, src.Width), h = Math.Max(1, src.Height * w / Math.Max(1, src.Width));
            var dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return dst;
        }

        private static void Save(Image img, string path)
        {
            var tmp = path + ".part";
            img.Save(tmp, ImageFormat.Png);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static Image LoadUnlocked(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes)) return new Bitmap(ms);
        }
    }
}
