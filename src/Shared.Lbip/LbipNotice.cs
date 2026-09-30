// A message put to whoever launched the game, when a launch is refused - the same as FlycastNotice, for every plugin:
// on its own STA thread, above everything (the host, or BigBox, may cover the screen), not waited on - the host's
// launch thread goes on at once. Never a WinForms-typed field, so a host without WindowsDesktop gets a log line.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LbIntegrations.Lbip
{
    internal static class LbipNotice
    {
        /// <summary>For the probe: what would have been said, instead of a window.</summary>
        internal static Action<string, string> Instead;

        public static void Show(string title, string text)
        {
            if (Instead != null) { Instead(title, text); return; }
            try
            {
                var thread = new Thread(() =>
                {
                    try { Box(title, text); }
                    catch (Exception ex) { LbipLog.Warn("no message window (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "launch notice";
                thread.Start();
            }
            catch (Exception ex) { LbipLog.Warn("could not show a message (" + ex.Message + ")"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Box(string title, string text)
        {
            using var owner = new System.Windows.Forms.Form
            {
                TopMost = true, ShowInTaskbar = false, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
                StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(-32000, -32000),
                Size = new System.Drawing.Size(1, 1),
            };
            owner.Show();
            owner.Activate();
            System.Windows.Forms.MessageBox.Show(owner, text, title, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
        }
    }
}
