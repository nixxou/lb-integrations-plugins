// What a FIRST install of Xenia Canary sets up, so its first start does not stop on "No Profiles Found" (Mehdi, 01/10):
//   - a profile, named after this Windows account (XeniaProfile.GamertagFrom), that Xenia signs into at start - when
//     the folder has none (a folder installed into again keeps its own);
//   - the console set as this Windows is: language, country, time zone, clock (XeniaConsole.FromWindows) - when
//     xconfig.settings is not there yet, which is the case of a new folder: an existing one is the user's.
// Then a notification says what was used and asks whether it is right; "Change..." opens the same panel as the Nixx
// window's Xenia tab. AN UPDATE SETS NOTHING UP: it goes over an install the user has already made his.

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Xenia
{
    internal static class XeniaSetup
    {
        /// <summary>Set up a just-installed Xenia, then say so. Never throws: a failure is a line in the log and the
        /// install stands.</summary>
        public static void AfterFirstInstall(string exe)
        {
            try
            {
                if (XeniaPaths.ForkOf(exe) != XeniaFork.Canary) return;
                var layout = XeniaPaths.Resolve(exe);
                var said = new System.Collections.Generic.List<string>();

                var profiles = XeniaProfile.Find(layout.ContentRoot);
                var signed = XeniaProfile.SignedIn(layout.ConfigFile);
                if (profiles.Count == 0)
                {
                    var tag = XeniaProfile.GamertagFrom();
                    var xuid = XeniaProfile.Create(layout.ContentRoot, tag);
                    XeniaProfile.SignInAtStart(layout.ConfigFile, xuid);
                    said.Add("profile \"" + tag + "\", signed in at start");
                }
                else
                {
                    var profile = profiles.FirstOrDefault(p => p.Xuid == signed);
                    if (profile == null) { profile = profiles[0]; XeniaProfile.SignInAtStart(layout.ConfigFile, profile.Xuid); }
                    said.Add("its profile \"" + profile.Gamertag + "\", signed in at start");
                }

                XeniaConsoleValues console;
                if (!XeniaConsole.Exists(layout.StorageRoot))
                {
                    console = XeniaConsole.FromWindows(XeniaConsole.Read(layout.StorageRoot));
                    XeniaConsole.Write(layout.StorageRoot, console);
                    said.Add("the console as this Windows is: " + console.Describe());
                }
                else
                {
                    console = XeniaConsole.Read(layout.StorageRoot);
                    said.Add("the console as it was: " + console.Describe());
                }

                var message = "Xenia is set up with " + string.Join(", and ", said) + ". Is that right?";
                XeniaNotify.Ask(message, 0, ("Yes", null), ("Change...", () => ShowChange(exe)));
            }
            catch (Exception ex) { Log.Warn("first-install setup", ex); }
        }

        /// <summary>The profile and console of this Xenia in a window of their own - the notification's "Change...".</summary>
        public static void ShowChange(string exe)
        {
            try
            {
                using var form = new Form
                {
                    Text = "Nixx-Xenia - Profile and console",
                    StartPosition = FormStartPosition.CenterScreen,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = true, TopMost = true,
                    Font = new Font("Segoe UI", 9f),
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(8),
                };
                var panel = new XeniaConsolePanel(exe);
                var ok = new Button { Text = "OK", Width = 90 };
                var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
                ok.Click += (_, _) =>
                {
                    var problem = panel.Problem();
                    if (problem != null) { MessageBox.Show(form, problem, form.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    panel.Save();
                    form.DialogResult = DialogResult.OK;
                };
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 0, 0) };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
                stack.Controls.Add(panel);
                stack.Controls.Add(buttons);
                form.Controls.Add(stack);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.ShowDialog(OwnerWindow.Foreground());
            }
            catch (Exception ex) { Log.Warn("the profile and console window", ex); }
        }
    }
}
