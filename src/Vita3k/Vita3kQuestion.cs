// A yes/no question put to whoever launched the game - the one place the plugin asks anything.
//
// Built like Vita3kProgressWindow, for the same reasons: on its own STA thread (the thread the host
// prepares a launch on is not documented to be one), topmost (the host, or BigBox, may be covering
// everything), and never with a WinForms-typed field, so a host without WindowsDesktop gets the
// default answer and a log line, never a crash. While it is up, the progress window stops pushing
// itself back on top - see Vita3kProgressWindow.Suspended - or the two would fight over the screen.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kQuestion
    {
        /// <summary>Ask, and wait for the answer. <paramref name="fallback"/> when no window can be
        /// shown at all.</summary>
        public static bool Ask(string title, string text, bool fallback)
        {
            bool answer = fallback;
            try
            {
                Vita3kProgressWindow.Suspended = true;
                var thread = new Thread(() =>
                {
                    try { answer = Show(title, text); }
                    catch (Exception ex) { Log.Warn("no question window (" + ex.GetType().Name + ": " + ex.Message + ") - answering " + (fallback ? "yes" : "no")); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "Vita3K question";
                thread.Start();
                thread.Join();
            }
            catch (Exception ex) { Log.Warn("could not ask (" + ex.Message + ") - answering " + (fallback ? "yes" : "no")); }
            finally { Vita3kProgressWindow.Suspended = false; }
            return answer;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Show(string title, string text)
        {
            // A topmost owner, off screen: MessageBox has no TopMost of its own, and a box owned by a
            // topmost window stays above the host.
            using var owner = new Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000),
                Size = new System.Drawing.Size(1, 1),
            };
            owner.Show();
            owner.Activate();
            return MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button1) == DialogResult.Yes;
        }
    }
}
