// A compatibility list downloaded whole again, from a game's window (Mehdi, 05/10: on every plugin that downloads a whole
// list - its button in the game's window, by its compatibility state, and the date of the last whole download).
//
// THE ROW: "Whole list downloaded <when>." and its button. THE BUTTON: the plugin's download run off the window's thread,
// under a progress window - its pages when it has some (Cxbx-Reloaded's site, PPSSPP's), else a bar that waits - and
// Cancel. Whatever the plugin does, the rule is its own and the same as ever: a list read only in part, or cancelled,
// never takes the place of the copy.

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LbIntegrations.Lbip
{
    /// <summary>What a download says of itself as it goes, and how it is told to stop.</summary>
    internal sealed class LbipListJob
    {
        public volatile int Done, Total;      // pages; Total 0: one file, how far is not known
        public volatile string Step;          // "Page 3 of 42", "Downloading..."
        public CancellationToken Token;
    }

    internal static class LbipListRefresh
    {
        /// <param name="name">the list, for the progress window ("Cxbx-Reloaded's compatibility list")</param>
        /// <param name="when">"downloaded 5 Oct 2026 14:02", or why not ("never downloaded - ...")</param>
        /// <param name="run">the whole download, on another thread: null when the list was kept, else why not</param>
        /// <param name="done">on the window's thread, once it is over - the state redrawn</param>
        public static Control Row(string name, Func<string> when, Func<LbipListJob, string> run, Action done, int width = 540)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 6, 0, 2) };
            var said = new Label { AutoSize = true, MaximumSize = new Size(width - 10, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 7, 6, 0) };
            var button = new Button { Text = "Download the whole list again", AutoSize = true, Margin = new Padding(3, 2, 3, 0) };
            string Safe(Func<string> f) { try { return f(); } catch (Exception ex) { return "not known (" + ex.Message + ")"; } }
            said.Text = "Whole list " + Safe(when) + ".";
            button.Click += (_, _) =>
            {
                button.Enabled = false;
                string problem = null;
                bool cancelled = false;
                using (var form = new ProgressForm(name))
                {
                    var job = new LbipListJob { Step = "Downloading...", Token = form.Cancel.Token };
                    form.Job = job;
                    var task = Task.Run(() =>
                    {
                        try { return run(job); }
                        catch (Exception ex) { return ex.Message; }
                    });
                    form.Work = task;
                    task.ContinueWith(_ => { try { if (form.IsHandleCreated) form.BeginInvoke(new Action(form.Finish)); } catch { } });
                    form.ShowDialog(row.FindForm());
                    try { problem = task.Result; } catch (Exception ex) { problem = ex.Message; }
                    cancelled = form.Cancel.IsCancellationRequested;
                }
                said.Text = "Whole list " + Safe(when) + "."
                            + (problem == null ? " Downloaded just now." : cancelled ? " Cancelled: the list kept as it was." : " Not downloaded: " + problem + " - the list kept as it was.");
                said.ForeColor = problem == null || cancelled ? SystemColors.GrayText : Color.Firebrick;
                button.Enabled = true;
                try { done?.Invoke(); } catch { }
            };
            row.Controls.Add(said);
            row.Controls.Add(button);
            return row;
        }

        /// <summary>The download's progress, Cancel to stop it - closed by the download's end, never before it.</summary>
        private sealed class ProgressForm : Form
        {
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public LbipListJob Job;
            public Task Work;
            private readonly Label _step;
            private readonly ProgressBar _bar;
            private readonly Button _cancel;
            private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer { Interval = 200 };
            private bool _over;

            public ProgressForm(string name)
            {
                Text = name;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = MaximizeBox = false;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.CenterParent;
                Font = new Font("Segoe UI", 9f);
                ClientSize = new Size(420, 112);
                _step = new Label { AutoSize = false, Location = new Point(12, 12), Size = new Size(396, 20), Text = "Downloading..." };
                _bar = new ProgressBar { Location = new Point(12, 38), Size = new Size(396, 20), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
                _cancel = new Button { Text = "Cancel", Width = 90, Location = new Point(318, 72) };
                _cancel.Click += (_, _) => Stop();
                Controls.AddRange(new Control[] { _step, _bar, _cancel });
                _tick.Tick += (_, _) => Show_();
                // Over before the window was there: closed as soon as it is.
                Shown += (_, _) => { _tick.Start(); if (Work?.IsCompleted == true) Finish(); };
                FormClosing += (_, e) => { if (!_over) { e.Cancel = true; Stop(); } };
            }

            private void Stop()
            {
                Cancel.Cancel();
                _cancel.Enabled = false;
                _step.Text = "Cancelling...";
            }

            private void Show_()
            {
                var j = Job;
                if (j == null || Cancel.IsCancellationRequested) return;
                _step.Text = j.Step ?? "Downloading...";
                int total = j.Total, done = j.Done;
                if (total > 0)
                {
                    if (_bar.Style != ProgressBarStyle.Continuous) _bar.Style = ProgressBarStyle.Continuous;
                    _bar.Maximum = total;
                    _bar.Value = Math.Max(0, Math.Min(total, done));
                }
            }

            public void Finish()
            {
                if (_over) return;
                _over = true;
                _tick.Stop();
                Close();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _tick.Dispose(); Cancel.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
