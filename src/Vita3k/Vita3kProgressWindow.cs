// The window that says a game is being installed, while it is.
//
// WHY THE PLUGIN BRINGS ITS OWN. Installing a game happens in PrepareEmulatorForLaunch, and
// PrepareForLaunchArgs carries no progress channel - unlike installing an emulator, where the host
// passes ReportProgressAction and shows it in its own window. A 4 GB game takes seconds to copy,
// decrypt and fingerprint; with nothing on screen that is indistinguishable from a hang.
//
// IT APPEARS ONLY FOR A WAIT WORTH SHOWING. Nothing is drawn for the first ShowAfterMs: relaunching
// the game already on the console, or a small homebrew, is over before that and flashes nothing.
//
// Built on the pattern of Shared.Dsi's waiting window, for the same reasons:
//   - ON ITS OWN STA THREAD. WinForms needs a single-threaded apartment, and the thread the host calls
//     PrepareEmulatorForLaunch on is not documented to be one.
//   - THE try/catch IS OUTSIDE THE FRAME THAT NAMES WinForms TYPES. An assembly is resolved when a
//     method using it is entered, so a catch inside that method is too late to catch its own failure
//     to load. A host without WindowsDesktop gets a log line and no window, never a crash.
//   - TOPMOST, AND KEPT THERE. The host's startup screen may already be covering everything, and at
//     the end of a game the host brings itself back to the front AFTER this window has opened - a
//     TopMost set once lost that race and the closing window was never seen (measured 26/09: 4.8 s
//     on screen, behind LaunchBox). So it is activated when shown and pushed back to the top of the
//     topmost band on every tick, without taking the focus again.
//
// Report is called from the installing thread; the window reads the latest value on a timer. No
// BeginInvoke per byte, and no way for a slow UI to slow the install down.

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kProgressWindow : IDisposable
    {
        private const int ShowAfterMs = 700;

        private readonly string _title;
        private volatile string _step = "";
        private double _fraction = -1;        // < 0: not known, the bar runs as a marquee
        private volatile bool _closed;

        // No field of a WinForms type, on purpose: it would make loading THIS class load WinForms,
        // before the try in Open could catch a host that has none. Only Run names those types.

        /// <summary>Open one, or answer null - a failure to show a window must never stop a game.</summary>
        public static Vita3kProgressWindow Open(string title)
        {
            try { return new Vita3kProgressWindow(title); }
            catch (Exception ex) { Log.Warn("no progress window (" + ex.GetType().Name + ": " + ex.Message + ")"); return null; }
        }

        private Vita3kProgressWindow(string title)
        {
            _title = title;
            var thread = new Thread(() =>
            {
                try
                {
                    // The delay, spent where it costs nothing: on this thread, before any window.
                    for (int waited = 0; waited < ShowAfterMs && !_closed; waited += 50) Thread.Sleep(50);
                    if (!_closed) Run();
                }
                catch (Exception ex) { Log.Warn("no progress window (" + ex.GetType().Name + ": " + ex.Message + ")"); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "Vita3K progress";
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

            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            int ticks = 0;
            timer.Tick += (s, e) =>
            {
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
                KeepOnTop(form.Handle);
                form.Activate();
                SetForegroundWindow(form.Handle);
                timer.Start();
            };

            Application.Run(form);
            timer.Dispose();
        }

        /// <summary>Back to the top of the topmost band, without activating - the focus is taken once,
        /// when shown, and never fought over afterwards.</summary>
        /// <summary>Set while a question (Vita3kQuestion) is on screen: pushed back on top every tick,
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
