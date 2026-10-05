// The "RamDisk & VHDX" tab of the NixxMenu: how every plugin of the pack mounts its RAM disks and attaches its
// VHDX (Mehdi, 02/10: one section for all of them). It edits RamDiskOptions - %LOCALAPPDATA%\lb-integrations-
// plugins\ramdisk.ini, read by Shared.RamDisk at every mount.
//
// WHAT IS INSTALLED FIRST, AND THE CHOICES FOLLOW IT. The top of the tab says what this machine has - AIM, ImDisk,
// the helper and its task, Windows' own virtual disk support - and every choice that needs something missing is
// greyed with the reason. A choice saved earlier whose driver has gone since is kept (it comes back with the
// driver) and said: what will really be used is RamDiskOptions.Effective, decided at each mount.

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LbIntegrations.Lbip;
using LbIntegrations.RamDisk;

namespace LbIntegrations.Menus
{
    internal sealed class RamDiskTab : Panel
    {
        public const string Title = "RamDisk & VHDX";

        private readonly RadioButton _auto, _aim, _imdisk, _vm, _awe, _memAuto, _vhdxWindows, _vhdxAim;
        private readonly CheckBox _removable;
        private readonly Label _effective;
        private readonly bool _aimThere, _imdiskThere, _modern;

        public RamDiskTab()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            Padding = new Padding(12);
            var o = RamDiskOptions.Load();

            // The LaunchBox root, for the helper's version: this runs inside <root>\Core\LaunchBox.exe.
            if (RamDiskHost.Root() == null)
                try { var core = Path.GetDirectoryName(Environment.ProcessPath); RamDiskHost.UseRoot(Path.GetDirectoryName(core)); } catch { }
            _aimThere = RamDrive.IsAimInstalled();
            _imdiskThere = RamDrive.IsImDiskInstalled();
            var helper = RamDrive.HelperVersion;
            var task = RamDrive.InstalledTaskName();
            _modern = helper != null && helper >= RamDrive.BackendProtocol && task != null;
            bool nativeVhdx = File.Exists(Path.Combine(Environment.SystemDirectory, "virtdisk.dll"))
                              && File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "vhdmp.sys"));
            string imdiskVersion = Version(Path.Combine(Environment.SystemDirectory, "drivers", "imdisk.sys"));
            string aimVersion = Version(Path.Combine(Environment.SystemDirectory, "drivers", "phdskmnt.sys"));

            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            Label Line(string text, bool grey = true) => new Label
            {
                Text = text, AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 2, 0, 6),
                ForeColor = grey ? SystemColors.GrayText : SystemColors.ControlText,
            };
            GroupBox Group(string title, params Control[] rows)
            {
                var box = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0, 0, 0, 10), MinimumSize = new Size(660, 0) };
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(10, 20), Margin = Padding.Empty };
                flow.Controls.AddRange(rows);
                box.Controls.Add(flow);
                return box;
            }
            Control Status(bool ok, string name, string detail)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 1, 0, 1) };
                row.Controls.Add(new Label { Text = ok ? "✔" : "✖", ForeColor = ok ? Color.ForestGreen : Color.Firebrick, AutoSize = true, Font = new Font("Segoe UI Symbol", 9f, FontStyle.Bold), Margin = new Padding(0, 0, 4, 0) });
                row.Controls.Add(new Label { Text = name, AutoSize = true, Font = new Font(Font, FontStyle.Bold), MinimumSize = new Size(190, 0), Margin = Padding.Empty });
                row.Controls.Add(new Label { Text = detail, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(440, 0), Margin = Padding.Empty });
                return row;
            }

            // ── what this machine has ──
            stack.Controls.Add(Group("Installed on this machine",
                Status(_aimThere, "Arsenal Image Mounter", _aimThere ? "driver " + aimVersion + ", AIM Toolkit in " + RamDrive.AimFolder : "not installed - NixxIntegrations.exe installs it"),
                Status(_imdiskThere, "ImDisk", _imdiskThere ? "driver " + imdiskVersion : "not installed - NixxIntegrations.exe installs it"),
                Status(helper != null && task != null, "RAM disk helper", helper == null ? "not installed - run NixxIntegrations.exe"
                       : "version " + helper + (task != null ? ", its elevated task registered" : ", but its elevated task is not registered")
                         + (helper < RamDrive.BackendProtocol ? " - " + RamDrive.BackendProtocol + " is needed for AIM and the options below" : "")),
                Status(nativeVhdx, "Windows virtual disks", nativeVhdx ? "virtdisk.dll and vhdmp.sys - VHDX without any driver of ours" : "missing")));

            // ── RAM disk ──
            // Short on the page, the whole of it on hover (Mehdi, 05/10).
            _auto = new RadioButton { Text = "Automatic (recommended): AIM if installed, else ImDisk", AutoSize = true, Checked = o.Backend == "auto" };
            LbipHint.Attach(_auto, "Automatic: Arsenal Image Mounter when it is installed, ImDisk otherwise (recommended)");
            _aim = new RadioButton { Text = "Arsenal Image Mounter" + (_aimThere ? "" : "  (not installed)"), AutoSize = true, Checked = o.Backend == "aim" };
            _imdisk = new RadioButton { Text = "ImDisk" + (_imdiskThere ? "" : "  (not installed)"), AutoSize = true, Checked = o.Backend == "imdisk" };
            var driver = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty };
            driver.Controls.AddRange(new Control[] { _auto, _aim, _imdisk });
            _effective = Line("", false);
            _effective.ForeColor = Color.DarkGoldenrod;

            _removable = new CheckBox { Text = "Removable media - so it unmounts cleanly", AutoSize = true, Checked = o.Removable };
            LbipHint.Attach(_removable, "Removable media - indexers (Everything, Windows Search) leave it alone, so it unmounts cleanly");

            _memAuto = new RadioButton { Text = "Automatic: virtual memory through ImDisk, physical memory through AIM", AutoSize = true, Checked = o.AutoMemory };
            _vm = new RadioButton { Text = "Virtual memory", AutoSize = true, Checked = !o.AutoMemory && !o.Awe };
            _awe = new RadioButton { Text = "Physical memory (AWE) - never paged out", AutoSize = true, Checked = !o.AutoMemory && o.Awe };
            LbipHint.Attach(_awe, "Physical memory (AWE) - never paged out; through ImDisk it needs its AWEAlloc driver");
            var memory = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty };
            memory.Controls.AddRange(new Control[] { _memAuto, _vm, _awe });

            stack.Controls.Add(Group("RAM disk - melonDS, Vita3K and Xenia play a session on it when it fits",
                Line("Driver", false), driver, _effective,
                Line("Disk", false), _removable,
                Line("Memory", false), memory));

            // ── VHDX ──
            _vhdxWindows = new RadioButton { Text = "Windows' own", AutoSize = true, Checked = o.Vhdx != "aim", Enabled = nativeVhdx };
            _vhdxAim = new RadioButton { Text = "Arsenal Image Mounter" + (_aimThere ? " - can also attach it as removable media" : "  (not installed)"), AutoSize = true, Checked = o.Vhdx == "aim" };
            const string vhdxFull = "Creating a VHDX, or a differencing one over it, is always Windows' own; this is how it is attached.";
            LbipHint.Attach(_vhdxWindows, vhdxFull, _vhdxAim);
            stack.Controls.Add(Group("VHDX - Vita3K's VHDX mode, and disk images",
                _vhdxWindows, _vhdxAim,
                LbipHint.Note("Only how a VHDX is attached: Windows always creates it.", vhdxFull, 640, new Padding(0, 2, 0, 6))));

            Controls.Add(stack);

            foreach (var r in new[] { _auto, _aim, _imdisk }) r.CheckedChanged += (_, _) => Follow();
            Follow();
        }

        /// <summary>Grey what the chosen driver cannot do, and say when a saved choice will not be what is used.</summary>
        private void Follow()
        {
            string chosen = _aim.Checked ? "aim" : _imdisk.Checked ? "imdisk" : "auto";
            var e = new RamDiskOptions { Backend = chosen, Vhdx = _vhdxAim.Checked ? "aim" : "windows" }
                    .Effective(_aimThere && _modern, _imdiskThere, out var notes);
            bool aimUsed = e.Backend == "aim";
            _awe.Enabled = aimUsed || _awe.Checked;
            _vhdxAim.Enabled = (_aimThere && _modern) || _vhdxAim.Checked;
            _effective.Text = e.Backend == null ? "No RAM disk driver is installed: the plugins play on the disk."
                            : "Used now: " + (aimUsed ? "Arsenal Image Mounter" : "ImDisk") + (notes.Length > 0 ? " - " + notes + "." : ".");
        }

        private static string Version(string file)
        {
            try { var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(file); return v.FileMajorPart + "." + v.FileMinorPart + "." + v.FileBuildPart; }
            catch { return "?"; }
        }

        /// <summary>Null when saved, or why not.</summary>
        public string Save()
        {
            var o = new RamDiskOptions
            {
                Backend = _aim.Checked ? "aim" : _imdisk.Checked ? "imdisk" : "auto",
                Removable = _removable.Checked,
                Awe = _awe.Checked,
                AutoMemory = _memAuto.Checked,
                Vhdx = _vhdxAim.Checked ? "aim" : "windows",
            };
            try { o.Save(); return null; }
            catch (Exception ex) { return "The settings could not be saved: " + ex.Message; }
        }
    }
}
