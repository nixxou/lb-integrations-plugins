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
//
// THE CLOSING WINDOW IS ALWAYS SHOWN, AND MODAL (Mehdi, 29/09): the end of a session - the save taken,
// the RAM disk released - is the moment somebody could relaunch or quit the host under it. Opened with
// always, it shows at once rather than after ShowAfterMs, stays up at least MinimumShownMs so it is
// read rather than flashed, ends on a full bar, and holds the host's main window disabled while it is
// up: owned by it (centred on it) and the only thing that answers until it closes.

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

        /// <summary>How long a window opened with always stays up, at least.</summary>
        private const int MinimumShownMs = 1500;

        private readonly bool _modal;

        /// <summary>Where the game stands in Vita3K's compatibility list, shown under the bar - null for none.</summary>
        private readonly VitaCompat _compat;

        /// <summary>Open one, or answer null - a failure to show a window must never stop a game.
        /// <paramref name="always"/>: shown at once, for at least MinimumShownMs, modal to the host - the
        /// closing window (see the header).</summary>
        public static Vita3kProgressWindow Open(string title, bool always = false, VitaCompat compat = null)
        {
            try { return new Vita3kProgressWindow(title, always, compat); }
            catch (Exception ex) { Log.Warn("no progress window (" + ex.GetType().Name + ": " + ex.Message + ")"); return null; }
        }

        private Vita3kProgressWindow(string title, bool always, VitaCompat compat)
        {
            _title = title;
            _modal = always;
            _compat = compat;
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

            // The game's state in Vita3K's own list, under the bar (Mehdi, 29/09) - see Vita3kCompat.
            var compat = Vita3kCompatRow.Build(_compat, 448);
            if (compat != null)
            {
                form.ClientSize = new Size(480, 122);
                compat.Location = new Point(16, 84);
                form.Controls.Add(compat);
            }

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
