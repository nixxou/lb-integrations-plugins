// The "Xbox Live (Insignia)" card of the xemu page of the Nixx window (XemuLive): what the Live console is, and the four
// things to do with it - import a console disk with Microsoft's dashboard, import that console's EEPROM, run Insignia's
// Setup Assistant on it (downloaded from Insignia's release), start it on its dashboard (DNS, account). A game plays on it
// with its own option, in its window (Console & disc: "Play online (Insignia)").

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
        public static Control Create(string exe)
        {
            var box = XemuGameForm.Group("Xbox Live (Insignia)");
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 20) };
            var status = new Label { AutoSize = true, MaximumSize = new Size(540, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) };
            void Show()
            {
                if (exe == null) { status.Text = "No xemu of this plugin in the library yet."; return; }
                var hdd = XemuLive.Hdd(exe);
                if (hdd == null) { status.Text = "No Live console yet: import a console disk that has Microsoft's dashboard."; return; }
                var d = XemuLive.Inspect(hdd);
                status.Text = "Live console: " + Path.GetFileName(hdd) + " (" + d.Format + ", " + (new FileInfo(hdd).Length >> 20) + " MB, "
                            + (d.DashboardTitle ?? "no dashboard") + (d.DashboardVersion != null ? " " + d.DashboardVersion : "") + ")"
                            + "\nIts EEPROM: " + (File.Exists(Path.Combine(XemuLive.Dir(exe), "eeprom.bin")) ? "live\\eeprom.bin, kept as it is" : "made at its first start, with your console's identity")
                            + "\nInsignia's Setup Assistant: " + (File.Exists(XemuLive.SetupIso(exe)) ? "downloaded" : "downloaded when you run it");
            }
            Show();
            panel.Controls.Add(status);

            var row1 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            var row2 = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            Button Make(string text, string tip, FlowLayoutPanel row)
            {
                var b = new Button { Text = text, AutoSize = true, Enabled = exe != null, Margin = new Padding(row.Controls.Count == 0 ? 0 : 8, 3, 3, 3) };
                LbipHint.Attach(b, tip);
                row.Controls.Add(b);
                return b;
            }
            var importDisk = Make("Import a console disk...", "A qcow2, or a raw image of a real Xbox's disk, that has Microsoft's dashboard (5960). It is COPIED into "
                                  + "the emulator's live folder; your file is never written. A Live console already there is put aside, never deleted.", row1);
            var importEeprom = Make("Import its EEPROM...", "The eeprom.bin of the console the disk came from, kept as it is - for a disk locked to a real Xbox. "
                                    + "Without it, the Live console gets an EEPROM made once with your console's identity, and keeps it.", row1);
            var setup = Make("Run Insignia setup", "Starts the Live console with Insignia's Setup Assistant (Insignia-Setup.iso, downloaded from "
                             + "github.com/" + XemuLive.SetupRepo + " the first time): Troubleshoot checks it, Register Xbox registers it.", row2);
            var dash = Make("Start the Live console", "Starts it on its dashboard, network on: Xbox Live there to make your account (the code from "
                            + "insignia.live, the same e-mail, the test card 4111 1111 1111 1111); Settings > Network for the DNS " + XemuLive.Dns + ".", row2);
            panel.Controls.Add(row1);
            panel.Controls.Add(row2);
            panel.Controls.Add(LbipHint.Note("Then tick \"Play online (Insignia)\" in a game's window: it plays on this console, online.",
                "Insignia (insignia.live) is Xbox Live brought back. Once: import a disk with Microsoft's dashboard, run Insignia setup "
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
                        if (why != null) MessageBox.Show(form, why, "Nixx-Xemu - Xbox Live", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        else if (done != null) MessageBox.Show(form, done, "Nixx-Xemu - Xbox Live", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    try { if (box.IsHandleCreated) box.BeginInvoke(new Action(Ui)); } catch { }
                });
            }

            importDisk.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Title = "A console disk with Microsoft's dashboard", Filter = "Xbox console disks (*.qcow2;*.img;*.bin)|*.qcow2;*.img;*.bin|All files|*.*" };
                if (dlg.ShowDialog(box.FindForm()) != DialogResult.OK) return;
                var d = XemuLive.Inspect(dlg.FileName);
                if (d.Problem != null) { MessageBox.Show(box.FindForm(), Path.GetFileName(dlg.FileName) + ": " + d.Problem + ".", "Nixx-Xemu - Xbox Live", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (XemuLive.Hdd(exe) != null && MessageBox.Show(box.FindForm(), "There is a Live console already - its registration and account would be put aside (kept in the live folder).\n\nImport this disk instead?",
                        "Nixx-Xemu - Xbox Live", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var path = dlg.FileName;
                Run(importDisk, () => XemuLive.ImportDisk(exe, path), "The Live console is in place: " + (d.DashboardTitle ?? "dashboard") + (d.DashboardVersion != null ? " " + d.DashboardVersion : "") + ".");
            };
            importEeprom.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Title = "The EEPROM of the console the disk came from", Filter = "Xbox EEPROM (*.bin)|*.bin|All files|*.*" };
                if (dlg.ShowDialog(box.FindForm()) != DialogResult.OK) return;
                var path = dlg.FileName;
                Run(importEeprom, () => XemuLive.ImportEeprom(exe, path), "The Live console will use this EEPROM, as it is.");
            };
            setup.Click += (_, _) => Run(setup, () => XemuLive.Boot(exe, setup: true), null);
            dash.Click += (_, _) => Run(dash, () => XemuLive.Boot(exe, setup: false), null);
            return box;
        }
    }
}
