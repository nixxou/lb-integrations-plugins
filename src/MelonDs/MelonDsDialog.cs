// The windows this plugin shows, and the reason it shows any at all.
//
// THE LAUNCHBOX SDK HAS NO MESSAGE API. The vendored assembly holds no ShowMessage, no dialog, no
// notification; the only host-UI handles it exposes are view models nothing here touches. So a
// plugin that needs to say something at launch time brings its own window or says nothing - and
// there are three things that cannot be left unsaid:
//
//   a game cannot start        which file is missing, and where it goes
//   a dump has no console      that one has to be built, on a copy, before any save exists
//   melonDS is open            the one menu item to click, since it cannot be clicked for them
//   melonDS has just closed    whether that setup worked
//   a save's console is gone   which file to go and find, or start again and keep the old one
//
// ON ITS OWN STA THREAD, ALWAYS. WinForms needs a single-threaded apartment and the thread the host
// calls PrepareEmulatorForLaunch on is not documented to be one. Finding out would mean depending on
// the answer; making our own thread costs a dozen lines and depends on nothing.
//
// THE try/catch IS OUTSIDE THE FRAME THAT NAMES WinForms TYPES. An assembly is resolved when a
// method using it is ENTERED, so a catch inside that method is too late to catch its own failure to
// load - measured, it killed the process. A host without WindowsDesktop gets a log line instead.
//
// Suppressed and the no-melonds-dialog marker both silence every window. Anything that can only be
// decided by asking somebody must therefore check Available FIRST and do nothing when it is false:
// setting a console up behind the user's back would be worse than not setting it up.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsDialog
    {
        /// <summary>Beside the log, like every other switch here.</summary>
        private const string KillSwitch = "no-melonds-dialog";

        /// <summary>Set by the probe, which drives this plugin through the same code a launch does
        /// and must never open a window while doing it. A marker file would work but would mean a
        /// test harness writing into the user's real log folder to keep itself quiet.</summary>
        public static bool Suppressed;

        /// <summary>Can anything be asked? Check this before starting something that only makes
        /// sense with somebody watching.</summary>
        public static bool Available
        {
            get { try { return !Suppressed && !Log.Disabled(KillSwitch); } catch { return false; } }
        }

        /// <summary>How long to wait for somebody to read it. A launch that hangs forever because a
        /// window opened off-screen would be a worse failure than the one being reported.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

        /// <summary>Show a window and answer with the index of the button pressed, or -1 when there
        /// was no window or nobody pressed anything. The FIRST button is the default.</summary>
        public static int Ask(string title, string body, string[] buttons)
        {
            if (!Available) return -1;
            if (buttons == null || buttons.Length == 0) buttons = new[] { "Close" };

            int chosen = -1;
            try
            {
                var thread = new Thread(() =>
                {
                    try { chosen = Run(title, body, buttons); }
                    catch (Exception ex)
                    {
                        Log.Verbose("no window could be shown (" + ex.GetType().Name + ")");
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                if (!thread.Join(Patience))
                    Log.Verbose("a window was left open; carrying on without an answer");
            }
            catch (Exception ex) { Log.Verbose("could not show a window - " + ex.Message); }
            return chosen;
        }

        private static int Run(string title, string body, string[] buttons)
        {
            int chosen = -1;

            using var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = true,
                ClientSize = new Size(620, 360),
                TopMost = true,
            };

            var text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Text = body,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Bounds = new Rectangle(16, 16, 588, 286),
                TabStop = false,
            };
            form.Controls.Add(text);

            // Right to left, so the first button - the default - ends up leftmost of the group and
            // the last one sits in the corner where a Close usually is.
            int right = 604;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int index = i;
                int width = Math.Max(110, 12 + TextRenderer.MeasureText(buttons[i], form.Font).Width);
                var button = new Button
                {
                    Text = buttons[i],
                    Bounds = new Rectangle(right - width, 314, width, 30),
                };
                button.Click += (s, e) => { chosen = index; form.Close(); };
                form.Controls.Add(button);
                if (i == 0) form.AcceptButton = button;
                if (i == buttons.Length - 1) form.CancelButton = button;
                right -= width + 8;
            }

            form.ShowDialog();
            return chosen;
        }

        // ── a window that says "hold on", and can be told not to ────────────

        /// <summary>A window shown WHILE something is happening, rather than to ask a question.
        ///
        /// EVERY OTHER WINDOW HERE BLOCKS UNTIL SOMEBODY ANSWERS, which is right for a question and
        /// useless for a wait: the thing being waited for has to keep being checked. So this one is
        /// opened, watched through <see cref="Cancelled"/>, and closed from the outside when the wait
        /// ends - or by the user, which is what Cancel means.
        ///
        /// It exists because a three-minute wait with nothing on screen is indistinguishable from a
        /// hang. Ten seconds of silence is fine; three minutes of it is a bug report.</summary>
        internal sealed class Waiting : IDisposable
        {
            private Form _form;
            private volatile bool _cancelled;

            /// <summary>Has the user asked to stop waiting?</summary>
            public bool Cancelled => _cancelled;

            internal Waiting(string title, string body)
            {
                var ready = new ManualResetEventSlim(false);
                var thread = new Thread(() =>
                {
                    try { Run(title, body, ready); }
                    catch (Exception ex) { Log.Verbose("no waiting window (" + ex.GetType().Name + ")"); }
                    finally { ready.Set(); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                ready.Wait(TimeSpan.FromSeconds(5));
            }

            private void Run(string title, string body, ManualResetEventSlim ready)
            {
                using var form = new Form
                {
                    Text = title,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    MinimizeBox = false,
                    MaximizeBox = false,
                    ControlBox = false,          // closing it means cancelling, and that has a button
                    ShowInTaskbar = true,
                    ClientSize = new Size(620, 200),
                    TopMost = true,
                };

                form.Controls.Add(new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    Text = body,
                    BorderStyle = BorderStyle.None,
                    BackColor = SystemColors.Control,
                    Bounds = new Rectangle(16, 16, 588, 126),
                    TabStop = false,
                });

                var cancel = new Button
                {
                    Text = "Stop waiting",
                    Bounds = new Rectangle(474, 154, 130, 30),
                };
                cancel.Click += (s, e) => { _cancelled = true; form.Close(); };
                form.Controls.Add(cancel);
                form.CancelButton = cancel;

                form.Shown += (s, e) => ready.Set();
                _form = form;
                Application.Run(form);
                _form = null;
            }

            /// <summary>Close it from the thread that was waiting. Safe to call when it never opened
            /// and safe to call twice.</summary>
            public void Dispose()
            {
                try
                {
                    var form = _form;
                    if (form == null || form.IsDisposed) return;
                    if (form.IsHandleCreated) form.BeginInvoke((Action)(() => { try { form.Close(); } catch { } }));
                }
                catch (Exception ex) { Log.Verbose("could not close a waiting window - " + ex.Message); }
            }
        }

        /// <summary>Open a "hold on" window, or answer null when windows are off - in which case the
        /// caller waits in silence, which is the same thing it did before there were windows.</summary>
        public static Waiting Wait(string title, string body)
        {
            try { return Available ? new Waiting(title, body) : null; }
            catch (Exception ex) { Log.Verbose("could not show a waiting window - " + ex.Message); return null; }
        }

        // ── the three things that cannot be left unsaid ──────────────────────

        /// <summary>A game cannot start: what is missing and where it goes.</summary>
        public static void MissingFiles(string gameName, string folder, List<string> missing,
                                        string wantedRegions, string extra)
        {
            try
            {
                if (!Available) return;

                var lines = new List<string>
                {
                    (gameName ?? "This game") + " needs files melonDS cannot generate.",
                    "",
                };

                if (!string.IsNullOrWhiteSpace(wantedRegions))
                {
                    lines.Add("A DSi NAND dump for " + wantedRegions + ".");
                    lines.Add("");
                    lines.Add("A DSi NAND is region locked - the system menu that launches an installed");
                    lines.Add("title refuses titles from another region. Any file name will do: the");
                    lines.Add("region is read from inside the dump, not from what it is called.");
                    lines.Add("");
                }

                if (missing != null && missing.Count > 0)
                {
                    lines.Add(missing.Count == 1 ? "This file is missing:" : "These files are missing:");
                    foreach (var name in missing) lines.Add("    " + name);
                    lines.Add("");
                }

                lines.Add("They go in:");
                lines.Add("    " + (folder ?? "(the folder beside melonDS)"));
                if (!string.IsNullOrWhiteSpace(extra)) { lines.Add(""); lines.Add(extra); }

                var answer = Ask("melonDS - a game cannot start",
                                 string.Join(Environment.NewLine, lines),
                                 new[] { "Open the folder", "Close" });
                if (answer == 0) OpenFolder(folder);
            }
            catch (Exception ex) { Log.Verbose("could not show the missing-files window - " + ex.Message); }
        }

        /// <summary>Open a folder in Explorer. UseShellExecute is what makes a directory path open
        /// the file browser rather than be treated as a program to run.</summary>
        public static void OpenFolder(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex) { Log.Verbose("could not open " + folder + " - " + ex.Message); }
        }
    }
}
