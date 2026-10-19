// The game's state in Ymir's compatibility list, shown as it starts (Mehdi 20/10: "au lancement").
//
// IN LITEBOX the launch's steps are drawn on its loading screen (LbipLaunchStatus): the state is that step's text - nothing of
// ours on top of the host's screen. IN LAUNCHBOX / BIGBOX: a small strip at the top of the screen, its colour the state's,
// for a few seconds - ON ITS OWN STA THREAD, NEVER ACTIVATED (WS_EX_NOACTIVATE, ShowWithoutActivation): Ymir gets the
// keyboard as it comes up, the strip is only seen; topmost, pushed back to the top while it is up (the host's startup
// screen covers everything). Never waited on: the launch goes on at once.
//
// The Nixx page chooses when (YmirSettings CompatAtLaunch): always, only when the game may not play (not Perfect or
// Playable), never.

using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ymir
{
    internal static class YmirCompatToast
    {
        private const int ShownMs = 6000;

        /// <summary>For the probe: what would have been shown, instead of a window.</summary>
        internal static Action<string, string> Instead;

        /// <summary>The state of the game being launched, as the Nixx page says. Never throws, never blocks.</summary>
        public static void ForLaunch(string title, YmirCompatResult result)
        {
            try
            {
                var when = YmirSettings.Value(YmirSettings.CompatAtLaunch);
                if (when == "never" || result == null) return;
                if (when == "problems" && result.State != null && !YmirCompat.MayNotPlay(result.State)) return;
                if (when == "problems" && !result.Found) return;
                var line = YmirCompat.Line(result);
                if (Instead != null) { Instead(title, line); return; }
                if (LbipLaunchStatus.Listening) { LbipLaunchStatus.Report("Ymir's compatibility list: " + line); return; }
                var color = result.State != null ? YmirCompat.ColorOf(result.State) : Color.FromArgb(0x60, 0x60, 0x60);
                var notes = result.Exact?.Notes;
                var thread = new Thread(() =>
                {
                    try { Run(title, line, notes, color); }
                    catch (Exception ex) { Log.Info("no compatibility strip (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "Ymir compatibility";
                thread.Start();
            }
            catch (Exception ex) { Log.Warn("compatibility at launch", ex); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run(string title, string line, string notes, Color color)
        {
            using var form = new Strip { Text = "Ymir compatibility" };
            form.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            form.ShowInTaskbar = false;
            form.TopMost = true;
            form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            form.BackColor = Color.FromArgb(0x20, 0x20, 0x24);
            form.Font = new Font("Segoe UI", 10f);
            var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(760, screen.Width - 40);
            var bar = new System.Windows.Forms.Panel { BackColor = color, Bounds = new Rectangle(0, 0, 8, 10) };
            var head = new System.Windows.Forms.Label
            {
                Text = title, ForeColor = Color.FromArgb(0xC8, 0xC8, 0xC8), AutoSize = false, UseMnemonic = false,
                Bounds = new Rectangle(20, 8, width - 32, 20), Font = new Font("Segoe UI", 9f),
            };
            var text = new System.Windows.Forms.Label
            {
                Text = line, ForeColor = Color.White, AutoSize = false, UseMnemonic = false,
                Bounds = new Rectangle(20, 28, width - 32, 22), Font = new Font("Segoe UI Semibold", 11f),
            };
            form.Controls.Add(bar);
            form.Controls.Add(head);
            form.Controls.Add(text);
            int height = 58;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                var n = notes.Length > 220 ? notes.Substring(0, 217) + "..." : notes;
                form.Controls.Add(new System.Windows.Forms.Label
                {
                    Text = n, ForeColor = Color.FromArgb(0xB0, 0xB0, 0xB0), AutoSize = false, UseMnemonic = false,
                    Bounds = new Rectangle(20, 54, width - 32, 36), Font = new Font("Segoe UI", 9f),
                });
                height = 94;
            }
            bar.Height = height;
            form.Bounds = new Rectangle(screen.Left + (screen.Width - width) / 2, screen.Top + 24, width, height);

            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            var shown = System.Diagnostics.Stopwatch.StartNew();
            timer.Tick += (s, e) =>
            {
                if (shown.ElapsedMilliseconds >= ShownMs) { timer.Stop(); form.Close(); return; }
                try { SetWindowPos(form.Handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate); } catch { }
            };
            form.Shown += (s, e) => timer.Start();
            System.Windows.Forms.Application.Run(form);
        }

        private sealed class Strip : System.Windows.Forms.Form
        {
            protected override bool ShowWithoutActivation => true;

            protected override System.Windows.Forms.CreateParams CreateParams
            {
                get
                {
                    var p = base.CreateParams;
                    p.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost;
                    return p;
                }
            }
        }

        private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080, WsExTopmost = 0x00000008;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
