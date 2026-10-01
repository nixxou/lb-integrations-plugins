// The window that says what the plugin is doing while it takes a while - here, the reading of a folder's Xbox 360 files
// (XeniaScan), the first time a big one is looked at. Vita3K's progress window (src\Vita3k\Vita3kProgressWindow.cs), the
// same rules: nothing for the first ShowAfterMs, its own STA thread, no WinForms-typed field, topmost and kept there.

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaProgressWindow : IDisposable
    {
        private const int ShowAfterMs = 700;

        private readonly string _title;
        private volatile string _step = "";
        private double _fraction = -1;        // < 0: not known, the bar runs as a marquee
        private volatile bool _closed;

        // No field of a WinForms type, on purpose: it would make loading THIS class load WinForms,
        // before the try in Open could catch a host that has none. Only Run names those types.

        /// <summary>How long a window opened with always stays up, at least.</summary>
        private const int MinimumShownMs = 1500;

        private readonly bool _modal;

        /// <summary>Open one, or answer null - a failure to show a window must never stop a game.
        /// <paramref name="always"/>: shown at once, for at least MinimumShownMs, modal to the host - the
        /// closing window (see the header).</summary>
        public static XeniaProgressWindow Open(string title, bool always = false)
        {
            try { return new XeniaProgressWindow(title, always); }
            catch (Exception ex) { Log.Warn("no progress window (" + ex.GetType().Name + ": " + ex.Message + ")"); return null; }
        }

        private XeniaProgressWindow(string title, bool always)
        {
            _title = title;
            _modal = always;
            var thread = new Thread(() =>
            {
                try
                {
                    // The delay, spent where it costs nothing: on this thread, before any window.
                    if (!always)
                        for (int waited = 0; waited < ShowAfterMs && !_closed; waited += 50) Thread.Sleep(50);
                    if (always || !_closed) Run();
                }
                catch (Exception ex) { Log.Warn("no progress window (" + ex.GetType().Name + ": " + ex.Message + ")"); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Xenia progress";
            thread.Start();
        }

        /// <summary>What is happening now, and how far along it is - 0..1, or null when that is not
        /// known. Safe from any thread, cheap enough to call per file.</summary>
        public void Report(string step, double? fraction)
        {
            if (step != null) _step = step;
            Volatile.Write(ref _fraction, fraction.HasValue ? Math.Max(0, Math.Min(1, fraction.Value)) : -1);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Run()
        {
            using var form = new Form
            {
                Text = _title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ControlBox = false,          // nothing here can be cancelled halfway without harm
                ShowInTaskbar = true,
                ClientSize = new Size(480, 96),
                TopMost = true,
            };

            var label = new Label
            {
                AutoEllipsis = true,
                Bounds = new Rectangle(16, 16, 448, 22),
                Text = _step,
            };
            var bar = new ProgressBar
            {
                Bounds = new Rectangle(16, 48, 448, 24),
                Minimum = 0,
                Maximum = 1000,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
            };
            form.Controls.Add(label);
            form.Controls.Add(bar);

            // MODAL TO THE HOST: owned by its main window, which is disabled while this is up.
            IntPtr host = IntPtr.Zero;
            if (_modal)
            {
                try { host = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
                if (host != IntPtr.Zero) form.StartPosition = FormStartPosition.Manual;
            }
            var shown = System.Diagnostics.Stopwatch.StartNew();

            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            int ticks = 0;
            timer.Tick += (s, e) =>
            {
                // Done: a full bar, and kept up until it has been seen.
                if (_closed && _modal && shown.ElapsedMilliseconds < MinimumShownMs)
                {
                    if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                    bar.Value = bar.Maximum;
                    return;
                }
                if (_closed) { timer.Stop(); form.Close(); return; }
                if (++ticks % 3 == 0) KeepOnTop(form.Handle);

                var step = _step;
                if (label.Text != step) label.Text = step;

                var f = Volatile.Read(ref _fraction);
                if (f < 0)
                {
                    if (bar.Style != ProgressBarStyle.Marquee) bar.Style = ProgressBarStyle.Marquee;
                }
                else
                {
                    if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                    bar.Value = (int)(f * 1000);
                }
            };
            form.Shown += (s, e) =>
            {
                if (host != IntPtr.Zero)
                {
                    try
                    {
                        SetWindowLongPtr(form.Handle, GwlpHwndParent, host);
                        if (GetWindowRect(host, out var r))
                            form.Location = new Point(r.Left + (r.Right - r.Left - form.Width) / 2, r.Top + (r.Bottom - r.Top - form.Height) / 2);
                        EnableWindow(host, false);
                    }
                    catch { }
                }
                KeepOnTop(form.Handle);
                form.Activate();
                SetForegroundWindow(form.Handle);
                timer.Start();
            };

            try { Application.Run(form); }
            finally
            {
                // Never leave the host disabled, whatever happened above.
                if (host != IntPtr.Zero) try { EnableWindow(host, true); } catch { }
                timer.Dispose();
            }
        }

        private static readonly int GwlpHwndParent = -8;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

        /// <summary>Back to the top of the topmost band, without activating - the focus is taken once,
        /// when shown, and never fought over afterwards.</summary>
        /// <summary>Set while a question is on screen: pushed back on top every tick,
        /// this window would cover the very box somebody has to answer.</summary>
        public static volatile bool Suspended;

        private static void KeepOnTop(IntPtr handle)
        {
            if (Suspended) return;
            try { SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow); }
            catch { }
        }

        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10, SwpShowWindow = 0x40;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>Close it. Safe when it never opened, safe twice, safe from any thread: the window
        /// notices on its next tick.</summary>
        public void Dispose() => _closed = true;
    }
}
