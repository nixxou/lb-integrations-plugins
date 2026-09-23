// The one window this plugin shows: a DSiWare title cannot run, here is what is missing, here is
// the folder it goes in.
//
// WHY A WINDOW AT ALL, when everything else here talks through a log file. Because the failure is
// silent otherwise. melonDS refuses to start, LaunchBox reports nothing useful, and the reason -
// "you own five NAND dumps and this game needs the sixth" - is not something anyone will guess. The
// log says it, and the log is not where somebody looks when a game does not launch.
//
// WHY IT IS OURS. The LaunchBox SDK has no message API of any kind: the vendored assembly holds no
// ShowMessage, no dialog, no notification, and the only host-UI handles it exposes are view models
// nothing in this repository touches. So a plugin that wants to say something at launch time brings
// its own window or says nothing.
//
// ON ITS OWN STA THREAD, ALWAYS. WinForms needs a single-threaded apartment, and the thread the host
// calls PrepareEmulatorForLaunch on is not documented to be one. Finding out would mean depending on
// the answer; making our own thread costs a dozen lines and depends on nothing.
//
// IT NEVER STOPS A LAUNCH. PrepareEmulatorForLaunch cannot cancel one, and this does not pretend to:
// the window is shown, it is dismissed, and melonDS then starts and fails with its own message. What
// this adds is the sentence melonDS cannot say, because melonDS does not know about a bios folder.
//
// Every path is guarded and every failure falls back one step - window, then just opening the folder,
// then just the log. A plugin that threw while explaining a problem would be worse than the problem.
// The no-dsi-dialog marker beside the log turns it off entirely.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LbIntegrations.MelonDs
{
    internal static class MelonDsMissingFiles
    {
        /// <summary>Beside the log, like every other switch here. Present, nothing is ever shown
        /// and the log carries the whole story on its own. Named for the plugin rather than for
        /// DSi, because a DS game with external BIOS turned on and no BIOS to find is the same
        /// failure and deserves the same sentence.</summary>
        private const string KillSwitch = "no-melonds-dialog";

        /// <summary>Set by the probe, which drives this plugin through the same code a launch does
        /// and must never open a window while doing it. A marker file would work but would mean a
        /// test harness writing into the user's real log folder to keep itself quiet.</summary>
        public static bool Suppressed;

        /// <summary>How long to wait for somebody to read it. A launch that hangs forever because a
        /// window opened off-screen would be a worse failure than the one being reported.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

        /// <summary>Say that a title cannot run, and why. <paramref name="missing"/> is the list of
        /// file names; <paramref name="wantedRegions"/> is the prose for the NAND, or null when the
        /// NAND is not the problem.</summary>
        public static void Show(string gameName, string folder, List<string> missing,
                                string wantedRegions, string extra)
        {
            try
            {
                if (Suppressed || Log.Disabled(KillSwitch)) return;

                var body = Compose(gameName, folder, missing, wantedRegions, extra);

                // THE try/catch HAS TO BE OUT HERE, in a frame that names no WinForms type. An
                // assembly is resolved when a method that uses it is ENTERED, so a catch inside Run
                // is too late to catch Run's own failure to load - measured: it killed the process
                // instead. A host that does not carry WindowsDesktop gets the folder and a log line.
                var thread = new Thread(() =>
                {
                    try { Run(gameName, folder, body); }
                    catch (Exception ex)
                    {
                        Log.Verbose("no window could be shown (" + ex.GetType().Name
                                    + "); opening the folder instead");
                        OpenFolder(folder);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                if (!thread.Join(Patience))
                    Log.Verbose("the missing-files window was left open; carrying on without it");
            }
            catch (Exception ex)
            {
                Log.Verbose("could not show the missing-files window - " + ex.Message);
                OpenFolder(folder);
            }
        }

        private static string Compose(string gameName, string folder, List<string> missing,
                                      string wantedRegions, string extra)
        {
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
            lines.Add("    " + (folder ?? "(the bios folder beside melonDS)"));
            if (!string.IsNullOrWhiteSpace(extra)) { lines.Add(""); lines.Add(extra); }

            return string.Join(Environment.NewLine, lines);
        }

        private static void Run(string gameName, string folder, string body)
        {
            try
            {
                using var form = new Form
                {
                    Text = "melonDS - a game cannot start",
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    MinimizeBox = false,
                    MaximizeBox = false,
                    ShowInTaskbar = true,
                    ClientSize = new Size(560, 300),
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
                    Bounds = new Rectangle(16, 16, 528, 224),
                    TabStop = false,
                };

                var open = new Button
                {
                    Text = "Open the folder",
                    Bounds = new Rectangle(296, 254, 130, 30),
                    // A folder that is not there yet is the usual case for somebody who has never
                    // put anything in it, so it is made rather than reported as missing.
                    Enabled = !string.IsNullOrWhiteSpace(folder),
                };
                open.Click += (s, e) => { OpenFolder(folder); form.Close(); };

                var close = new Button
                {
                    Text = "Close",
                    Bounds = new Rectangle(434, 254, 110, 30),
                    DialogResult = DialogResult.Cancel,
                };

                form.Controls.Add(text);
                form.Controls.Add(open);
                form.Controls.Add(close);
                form.AcceptButton = open;
                form.CancelButton = close;

                form.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Verbose("the missing-files window failed - " + ex.Message + "; opening the folder");
                OpenFolder(folder);
            }
        }

        /// <summary>Open the folder in Explorer. UseShellExecute is what makes a directory path open
        /// the file browser rather than be treated as a program to run.</summary>
        private static void OpenFolder(string folder)
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
