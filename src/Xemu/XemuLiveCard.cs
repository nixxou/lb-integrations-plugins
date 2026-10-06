// The "Xbox Live (Insignia)" card of the xemu page of the Nixx window (XemuLive): the console disk and the EEPROM chosen - each
// a button with the file chosen beside it (Mehdi, 06/10), the EEPROM optional - then running Insignia's Setup Assistant on the
// Live console and starting it on its dashboard. A game plays on it with its own option, in its window (Console & disc:
// "Play online (Insignia)").

using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Xemu
{
    internal static class XemuLiveCard
    {
        private const string Caption = "Nixx-Xemu - Xbox Live";

        private static string Size(long bytes) => bytes < 1 << 20 ? (bytes >> 10) + " KB" : (bytes >> 20) + " MB";

        public static Control Create(string exe)
        {
            var box = XemuGameForm.Group("Xbox Live (Insignia)");
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 20) };

            FlowLayoutPanel Row() => new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            Label Name(string text) => new Label { Text = text, Width = 130, Height = 26, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Margin = new Padding(0, 3, 4, 0) };
            Label PathLabel() => new Label { AutoSize = true, MaximumSize = new Size(330, 0), UseMnemonic = false, Margin = new Padding(8, 8, 0, 0) };
            Button Make(string text, string tip)
            {
                var b = new Button { Text = text, AutoSize = true, Enabled = exe != null, Margin = new Padding(0, 3, 3, 3) };
                LbipHint.Attach(b, tip);
                return b;
            }

            // ── the console disk: a button, the file chosen beside it ──
            var diskRow = Row();
            var chooseDisk = Make("Choose...", "A qcow2, or a raw image of a real Xbox's disk, that has Microsoft's dashboard (5960). It is COPIED once into the "
                                  + "emulator's live folder and kept clean; the Live console works in a differential image over that copy. Your file is never "
                                  + "written. Choosing another disk puts the Live console there before aside - never deleted.");
            var diskPath = PathLabel();
            diskRow.Controls.AddRange(new Control[] { Name("Console disk"), chooseDisk, diskPath });

            // ── its EEPROM, optional: a button, the file chosen beside it ──
            var eepromRow = Row();
            var chooseEeprom = Make("Choose...", "Optional. The eeprom.bin of the console the disk came from - for a disk locked to a real Xbox. It is COPIED and the "
                                    + "Live console works on the copy. Without one, the Live console gets an EEPROM made once with your console's identity "
                                    + "(the pack's seed: serial, MAC, keys), and keeps it - the console Insignia registers.");
            var eepromPath = PathLabel();
            var clearEeprom = Make("Use my console's", "Back to an EEPROM made with your console's identity, at the Live console's next start. The copy of the chosen "
                                   + "EEPROM is put aside, not deleted. Insignia sees another console then: register it again (Run Insignia setup).");
            clearEeprom.Margin = new Padding(8, 3, 3, 3);
            eepromRow.Controls.AddRange(new Control[] { Name("EEPROM (optional)"), chooseEeprom, eepromPath, clearEeprom });

            // ── what to do with it ──
            var actions = Row();
            actions.Margin = new Padding(0, 8, 0, 2);
            var setup = Make("Run Insignia setup", "Starts the Live console with Insignia's Setup Assistant (Insignia-Setup.iso, downloaded from github.com/"
                             + XemuLive.SetupRepo + " the first time): Troubleshoot checks it, Register Xbox registers it.");
            var dash = Make("Start the Live console", "Starts it on its dashboard, network on: Xbox Live there to make your account (the code from insignia.live, "
                            + "the same e-mail, the test card 4111 1111 1111 1111); Settings > Network for the DNS " + XemuLive.Dns + " if the setup asks.");
            dash.Margin = new Padding(8, 3, 3, 3);
            actions.Controls.AddRange(new Control[] { setup, dash });

            var status = new Label { AutoSize = true, MaximumSize = new Size(540, 0), UseMnemonic = false, Margin = new Padding(0, 4, 0, 2) };

            void Show()
            {
                if (exe == null) { diskPath.Text = "no xemu of this plugin in the library yet"; eepromPath.Text = ""; status.Text = ""; clearEeprom.Visible = false; return; }
                var base_ = XemuLive.Base(exe) ?? (XemuLive.Hdd(exe) != null ? XemuLive.Base(exe) : null);
                var source = XemuLive.Source(exe, "disk");
                diskPath.Text = base_ == null ? "none yet - a disk with Microsoft's dashboard" : source ?? Path.GetFileName(base_);
                var eepromSource = XemuLive.Source(exe, "eeprom");
                bool copied = XemuLive.Dir(exe) is string d && File.Exists(Path.Combine(d, "eeprom.bin")) && eepromSource != null;
                eepromPath.Text = copied ? eepromSource : "none - one made with your console's identity";
                clearEeprom.Visible = copied;
                if (base_ == null) { status.Text = ""; setup.Enabled = dash.Enabled = false; return; }
                setup.Enabled = dash.Enabled = true;
                var hdd = XemuLive.Hdd(exe);
                var info = hdd != null ? XemuLive.Inspect(hdd) : null;
                status.Text = (info?.DashboardTitle ?? "dashboard not read") + (info?.DashboardVersion != null ? " " + info.DashboardVersion : "")
                            + " - the console's changes in live.qcow2 (" + (hdd != null ? Size(new FileInfo(hdd).Length) : "not made") + "), over a clean copy"
                            + "\nInsignia's Setup Assistant: " + (File.Exists(XemuLive.SetupIso(exe)) ? "downloaded" : "downloaded when you run it");
            }
            Show();

            panel.Controls.Add(diskRow);
            panel.Controls.Add(eepromRow);
            panel.Controls.Add(actions);
            panel.Controls.Add(status);
            // Insignia's list of games (XemuInsignia): only those get "Play online" and "Run with Insignia".
            XemuInsignia.RefreshSoon();
            var list = LbipListRefresh.Row("Insignia's list of games",
                () => (XemuInsignia.Downloaded() is DateTime d ? "downloaded " + d.ToString("g") : "the plugin's copy of 06/10") + " - " + XemuInsignia.All.Count + " games played online",
                XemuInsignia.FetchWhole, null, 540);
            list.Margin = new Padding(0, 6, 0, 2);
            panel.Controls.Add(list);
            panel.Controls.Add(LbipHint.Note("Then tick \"Play online (Insignia)\" in a game's window: it plays on this console, online.",
                "Insignia (insignia.live) is Xbox Live brought back. Once: choose a disk with Microsoft's dashboard, run Insignia setup "
                + "(Register Xbox), ask insignia.live for a code and make your account on the Live console's dashboard; the DNS must be "
                + XemuLive.Dns + " in its network settings. A game in Live mode plays on that one console - its updates and your account "
                + "there - with xemu's network on; no save or savestate of it is handled then.", 540, new Padding(0, 6, 0, 2)));
            box.Controls.Add(panel);

            void Run(Button b, Func<string> work, string done)
            {
                b.Enabled = false;
                var form = box.FindForm();
                Task.Run(work).ContinueWith(t =>
                {
                    void Ui()
                    {
                        b.Enabled = true;
                        Show();
                        var why = t.IsFaulted ? t.Exception?.GetBaseException().Message : t.Result;
                        if (why != null) MessageBox.Show(form, why, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        else if (done != null) MessageBox.Show(form, done, Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    try { if (box.IsHandleCreated) box.BeginInvoke(new Action(Ui)); } catch { }
                });
            }

            chooseDisk.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Title = "A console disk with Microsoft's dashboard", Filter = "Xbox console disks (*.qcow2;*.img;*.bin)|*.qcow2;*.img;*.bin|All files|*.*" };
                if (dlg.ShowDialog(box.FindForm()) != DialogResult.OK) return;
                var d = XemuLive.Inspect(dlg.FileName);
                if (d.Problem != null) { MessageBox.Show(box.FindForm(), Path.GetFileName(dlg.FileName) + ": " + d.Problem + ".", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (XemuLive.Base(exe) != null && MessageBox.Show(box.FindForm(), "There is a Live console already - its registration and account would be put aside (kept in the live folder).\n\nUse this disk instead?",
                        Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var path = dlg.FileName;
                Run(chooseDisk, () => XemuLive.ImportDisk(exe, path), null);
            };
            chooseEeprom.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Title = "The EEPROM of the console the disk came from (optional)", Filter = "Xbox EEPROM (*.bin)|*.bin|All files|*.*" };
                if (dlg.ShowDialog(box.FindForm()) != DialogResult.OK) return;
                var path = dlg.FileName;
                Run(chooseEeprom, () => XemuLive.ImportEeprom(exe, path), null);
            };
            clearEeprom.Click += (_, _) => Run(clearEeprom, () => XemuLive.ClearEeprom(exe), null);
            setup.Click += (_, _) => Run(setup, () => XemuLive.Boot(exe, setup: true), null);
            dash.Click += (_, _) => Run(dash, () => XemuLive.Boot(exe, setup: false), null);
            return box;
        }
    }
}
