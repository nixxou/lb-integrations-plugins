// A message put to whoever launched the game, when a launch is refused - the SDK's PrepareForLaunchResponse
// carries no text, so a refusal without this is a launch that silently does nothing.
//
// Built as the Vita3K plugin's Vita3kQuestion, for the same reasons: on its own STA thread (the thread the
// host prepares a launch on is not documented to be one), above everything (the host, or BigBox, may cover
// the screen), and never with a WinForms-typed field, so a host without WindowsDesktop gets a log line,
// never a crash.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.Flycast
{
    internal static class FlycastNotice
    {
        /// <summary>Say it, and wait for OK. Never throws.</summary>
        public static void Show(string title, string text)
        {
            try
            {
                var thread = new Thread(() =>
                {
                    try { Box(title, text); }
                    catch (Exception ex) { Log.Warn("no message window (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "Flycast notice";
                thread.Start();
                thread.Join();
            }
            catch (Exception ex) { Log.Warn("could not show a message (" + ex.Message + ")"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Box(string title, string text)
        {
            // A topmost owner, off screen: MessageBox has no TopMost of its own.
            using var owner = new Form
            {
                TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-32000, -32000),
                Size = new System.Drawing.Size(1, 1),
            };
            owner.Show();
            owner.Activate();
            MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
