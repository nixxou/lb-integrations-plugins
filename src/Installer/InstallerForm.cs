// The window: which LaunchBox, what is installed in it, and what the machine offers the optional half.
// Everything it knows how to do is in InstallerCore, RamDiskSetup and VhdxSetup; this only asks and shows.
//
// It tries to answer the folder question itself first, because the common case is the exe dropped
// into the LaunchBox folder and double-clicked. When it cannot, the picker asks for LaunchBox.exe
// rather than for a folder: people know where their LaunchBox.exe is, and "the root, not the one in
// Core" is a sentence that only makes sense once you already know the answer - so Core\LaunchBox.exe
// is accepted and walked up from instead of refused.
//
// LAID OUT BY TABLES, NOT BY HAND. The first version placed every control at a pixel, and a paragraph
// that wrapped to two lines ran over the heading below it; and the plugin names, one line of fixed
// width, lost the seventh off its end. Every card here sizes to what it holds.
//
// ONE STATUS LINE PER PART. "RAM disk not ready" says nothing about what to do; a list with a tick or a
// cross beside the driver, the runtime, the helper and the task says it at a glance - and the same for
// VHDX, and for each plugin against the bytes this exe carries.
//
// The checks are asked OFF THE WINDOW'S THREAD: the scheduled-task lookup runs schtasks over every task
// on the machine, and the plugin check hashes every file; the window says "checking" meanwhile.

namespace NixxIntegrations;

internal sealed class InstallerForm : Form
{
    // ── look ──
    private static readonly Color Back = Color.FromArgb(243, 244, 246);
    private static readonly Color CardBack = Color.White;
    private static readonly Color Border = Color.FromArgb(222, 225, 230);
    private static readonly Color Ink = Color.FromArgb(31, 41, 55);
    private static readonly Color Grey = Color.FromArgb(107, 114, 128);
    private static readonly Color Accent = Color.FromArgb(37, 99, 235);
    private static readonly Color Good = Color.FromArgb(22, 163, 74);
    private static readonly Color Bad = Color.FromArgb(220, 38, 38);
    private static readonly Color Warn = Color.FromArgb(217, 119, 6);
    // TWO COLUMNS, NOT ONE TALL STACK (Mehdi, 02/10: "ca passera pas sur les petites resolutions"): LaunchBox,
    // the plugins on the left, the RAM disk with its two drivers and VHDX on the right - under 700 pixels high,
    // for a 1366x768 screen.
    private const int LeftW = 520, RightW = 600, Gap = 12, Side = 16;
    private const int Width_ = Side + LeftW + Gap + RightW + Side;
    private static int Inner(int cardWidth) => cardWidth - 2 * 14;

    private readonly Font _body = new("Segoe UI", 9f);
    private readonly Font _bold = new("Segoe UI Semibold", 9f);
    private readonly Font _heading = new("Segoe UI Semibold", 11f);
    private readonly Font _symbol = new("Segoe UI Symbol", 10f, FontStyle.Bold);

    private enum Mark { Ok, No, Warn, Info, Wait }

    private string? _root;
    private int _generation;

    // ── the cards' bodies, filled again on every refresh ──
    private readonly TableLayoutPanel _lbRows, _pluginRows, _ramRows, _vhdxRows;
    private readonly Label _lbPath = new() { AutoSize = true };
    private readonly Label _ramTag = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly Label _vhdxTag = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly Button _install, _uninstall, _choose, _ram;

    // THE TWO DRIVERS, side by side (Mehdi, 02/10): ImDisk, the legacy one the RAM disk uses, and the AIM
    // Toolkit, its successor - each checked and installed on its own, one or the other or both. A driver
    // belongs to the machine, so neither needs a LaunchBox to be installed.
    private readonly DriverPanel _legacy, _modern;

    private sealed record Snapshot(Layout? Layout, bool Installed, string? Running,
                                   (string Folder, PluginStatus Status)[] Plugins,
                                   RamDiskState Ram, VhdxState Vhdx, int FreeRamMb);

    public InstallerForm(string? start = null)
    {
        Text = "Nixx integrations for LaunchBox";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Back;
        Font = _body;
        ForeColor = Ink;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

        var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = Padding.Empty, Margin = Padding.Empty };
        page.Controls.Add(Header());

        var columns = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(Side, 12, Side, 6), Margin = Padding.Empty };
        FlowLayoutPanel Column(int right) => new()
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, right, 0), Padding = Padding.Empty,
        };
        var left = Column(Gap);
        var right = Column(0);
        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        page.Controls.Add(columns);

        // LaunchBox
        _choose = MakeButton("Choose LaunchBox…", primary: false);
        _choose.Click += (_, _) => Choose();
        _lbPath.Font = _bold;
        _lbPath.MaximumSize = new Size(Inner(LeftW) - 160, 0);
        var lbTop = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
        lbTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lbTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lbTop.Controls.Add(_lbPath, 0, 0);
        lbTop.Controls.Add(_choose, 1, 0);
        _lbRows = Rows(LeftW);
        left.Controls.Add(Card("LaunchBox", null, LeftW, lbTop, _lbRows));

        // Plugins
        _install = MakeButton("Install", primary: true);
        _uninstall = MakeButton("Uninstall", primary: false);
        _install.Click += (_, _) => Run(InstallerCore.Install, "Install");
        _uninstall.Click += (_, _) => Run(InstallerCore.Uninstall, "Uninstall");
        _pluginRows = new TableLayoutPanel { AutoSize = true, ColumnCount = 6, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
        left.Controls.Add(Card("Plugins", null, LeftW, _pluginRows,
            Buttons(_install, _uninstall),
            Note(LeftW, "Installs the plugin folders only. Your emulators, games, saves and NAND dumps are never touched, "
               + "and no emulator entry is created or changed.")));

        // RAM disk
        _ram = MakeButton("Set up", primary: false);
        _ram.Click += (_, _) =>
        {
            // The helper and the task, once ImDisk is there. The drivers have their own buttons, below.
            if (!RamDiskSetup.DriverInstalled()) { Refresh_(); return; }
            Run(RamDiskSetup.Enable, "RAM disk");
        };
        _ramRows = Rows(RightW);

        int half = (Inner(RightW) - 10) / 2;
        _legacy = new DriverPanel(this, "ImDisk", half,
            "A RAM disk driver, also used by LiteBox. Installs the ImDisk " + (ImDiskSetup.Bundled()?.ToString(3) ?? "")
            + " driver alone (LTR Data), not the ImDisk Toolkit, with its control panel.");
        _legacy.Action.Click += (_, _) => RunMachine(() => ImDiskSetup.SetUpWithPrompt(_root == null ? null : InstallerCore.Resolve(_root), RamDriver.ImDisk), "ImDisk");
        _legacy.Second.Text = "Control panel";
        _legacy.Second.Click += (_, _) =>
        {
            try { ImDiskSetup.OpenControlPanel(); }
            catch (Exception ex) { MessageBox.Show(this, "Could not open imdisk.cpl: " + ex.Message, "ImDisk", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        _modern = new DriverPanel(this, "AIM Toolkit", half,
            "Arsenal Image Mounter, a RAM disk and disk image driver, with its tools: mounts images, VHD, VHDX, VMDK... from "
            + "Explorer's right-click menu, and RamDiskUI. Installed whole, build " + AimSetup.Build + ".");
        _modern.Action.Click += (_, _) => RunMachine(() => ImDiskSetup.SetUpWithPrompt(null, RamDriver.Aim), "AIM Toolkit");
        _modern.Second.Text = "RamDiskUI";
        _modern.Second.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AimSetup.RamDiskUI()!) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "Could not open RamDiskUI: " + ex.Message, "AIM Toolkit", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        var drivers = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        drivers.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, half + 10));
        drivers.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, half));
        drivers.Controls.Add(_legacy, 0, 0);
        drivers.Controls.Add(_modern, 1, 0);
        right.Controls.Add(Card("RAM disk", _ramTag, RightW, drivers, _ramRows, Buttons(_ram),
            Note(RightW, "Optional. melonDS, Vita3K and Xenia play a session in memory instead of on your disk when it fits. "
               + "Shared with LiteBox (same helper, same scheduled task); setting it up asks for administrator rights once.")));

        // VHDX
        _vhdxRows = Rows(RightW);
        right.Controls.Add(Card("VHDX", _vhdxTag, RightW, _vhdxRows,
            Note(RightW, "Optional. Vita3K's VHDX mode (in its settings) installs each game once into a virtual disk over the "
               + "pristine console, and plays each session on a differencing disk. Hyper-V is not needed.")));

        Controls.Add(page);

        // The exe dropped at the LaunchBox root, or inside Core, are both ordinary ways to run this.
        var here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (start != null) _root = start;
        else if (InstallerCore.LooksLikeRoot(here)) _root = here;
        else if (InstallerCore.LooksLikeRoot(Path.GetDirectoryName(here))) _root = Path.GetDirectoryName(here);
        Refresh_();
    }

    // ── building blocks ──────────────────────────────────────────────────────

    private Control Header()
    {
        var version = Application.ProductVersion;
        int plus = version.IndexOf('+');
        if (plus > 0) version = version.Substring(0, plus);
        var head = new Panel { BackColor = Color.FromArgb(30, 41, 59), Size = new Size(Width_, 64), Margin = Padding.Empty };
        head.Controls.Add(new Label
        {
            Text = "Nixx integrations", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 15f),
            AutoSize = true, Location = new Point(16, 8), BackColor = Color.Transparent,
        });
        head.Controls.Add(new Label
        {
            Text = "Emulator plugins for LaunchBox  ·  " + version, ForeColor = Color.FromArgb(203, 213, 225),
            AutoSize = true, Location = new Point(18, 38), BackColor = Color.Transparent,
        });
        return head;
    }

    /// <summary>A white box with a heading, sized to what it holds.</summary>
    private Control Card(string title, Label? tag, int width, params Control[] parts)
    {
        var card = new Panel { BackColor = CardBack, AutoSize = true, Padding = new Padding(14, 10, 14, 12), Margin = new Padding(0, 0, 0, 10) };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        var inner = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Location = new Point(14, 10), Margin = Padding.Empty };
        int innerWidth = Inner(width);
        inner.MinimumSize = new Size(innerWidth, 0);
        inner.MaximumSize = new Size(innerWidth, 0);
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var heading = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        heading.Controls.Add(new Label { Text = title, Font = _heading, AutoSize = true, Margin = new Padding(0, 0, 6, 0) });
        if (tag != null) heading.Controls.Add(tag);
        inner.Controls.Add(heading);
        foreach (var p in parts) inner.Controls.Add(p);
        card.Controls.Add(inner);
        return card;
    }

    /// <summary>A status list: mark, name, what it says.</summary>
    private static TableLayoutPanel Rows(int cardWidth)
    {
        // The detail column's width rides in Tag, for Row.
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Tag = Inner(cardWidth) - 24 - 130 - 8 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    private void Row(TableLayoutPanel t, Mark mark, string name, string detail)
    {
        int r = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(MarkLabel(mark), 0, r);
        t.Controls.Add(new Label { Text = name, AutoSize = true, Font = _bold, Margin = new Padding(0, 3, 6, 3) }, 1, r);
        t.Controls.Add(new Label
        {
            Text = detail, AutoSize = true, UseMnemonic = false, ForeColor = mark == Mark.Ok || mark == Mark.Info ? Grey : Ink,
            MaximumSize = new Size(t.Tag is int w ? w : 300, 0), Margin = new Padding(0, 3, 0, 3),
        }, 2, r);
    }

    private Label MarkLabel(Mark mark) => new()
    {
        AutoSize = true, Font = _symbol, Margin = new Padding(0, 1, 0, 1),
        Text = mark switch { Mark.Ok => "✔", Mark.No => "✖", Mark.Warn => "!", Mark.Info => "•", _ => "…" },
        ForeColor = mark switch { Mark.Ok => Good, Mark.No => Bad, Mark.Warn => Warn, _ => Grey },
    };

    /// <summary>One driver: its name and tag, what it is, its state, and its buttons.</summary>
    private sealed class DriverPanel : Panel
    {
        public readonly Button Action, Second;
        private readonly Label _mark, _state;
        private readonly InstallerForm _form;

        public readonly Label Tag;

        public DriverPanel(InstallerForm form, string title, int width, string what)
        {
            _form = form;
            BackColor = Color.FromArgb(249, 250, 251);
            Width = width;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(width, 0);
            MaximumSize = new Size(width, 0);
            Margin = Padding.Empty;
            Padding = new Padding(10, 8, 10, 10);
            Paint += (_, e) => { using var pen = new Pen(Border); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };

            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(10, 8), Margin = Padding.Empty };
            var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            head.Controls.Add(new Label { Text = title, Font = form._heading, AutoSize = true, Margin = new Padding(0, 0, 6, 0) });
            // "recommended", shown where the window recommends this one - set by Show.
            Tag = new Label { Text = "recommended", ForeColor = Color.White, BackColor = Accent, AutoSize = true, Padding = new Padding(4, 1, 4, 1), Margin = new Padding(0, 4, 0, 0), Visible = false };
            head.Controls.Add(Tag);
            stack.Controls.Add(head);
            var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            _mark = form.MarkLabel(Mark.Wait);
            _state = new Label { AutoSize = true, Font = form._bold, Margin = new Padding(2, 3, 0, 0), MaximumSize = new Size(width - 50, 0) };
            line.Controls.Add(_mark);
            line.Controls.Add(_state);
            stack.Controls.Add(line);
            stack.Controls.Add(new Label { Text = what, AutoSize = true, ForeColor = Grey, MaximumSize = new Size(width - 20, 0), Margin = new Padding(0, 0, 0, 8) });
            Action = form.MakeButton("Install", primary: true);
            Second = form.MakeButton("", primary: false);
            Action.MinimumSize = Second.MinimumSize = new Size(0, 30);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            buttons.Controls.Add(Action);
            buttons.Controls.Add(Second);
            stack.Controls.Add(buttons);
            Controls.Add(stack);
        }

        /// <summary>The state, the install button (null hides it), the second button shown or not.</summary>
        public void Set(Mark mark, string state, string? action, bool second)
        {
            var fresh = _form.MarkLabel(mark);
            _mark.Text = fresh.Text;
            _mark.ForeColor = fresh.ForeColor;
            fresh.Dispose();
            _state.Text = state;
            Action.Visible = action != null;
            if (action != null) Action.Text = action;
            Second.Visible = second;
        }
    }

    private Label Note(int cardWidth, string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Grey, MaximumSize = new Size(Inner(cardWidth), 0), Margin = new Padding(0, 6, 0, 0),
    };

    private static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        f.Controls.AddRange(buttons);
        return f;
    }

    private Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(120, 32), Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat, Font = _bold, Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.EnabledChanged += (_, _) =>
        {
            b.BackColor = !b.Enabled ? Color.FromArgb(229, 231, 235) : primary ? Accent : Color.White;
            b.FlatAppearance.BorderColor = !b.Enabled ? Border : primary ? Accent : Border;
        };
        return b;
    }

    // ── what is shown ────────────────────────────────────────────────────────

    /// <summary>Asked again on every change: LiteBox may have registered the task, or somebody installed
    /// ImDisk, since the window opened. The answer comes back on the window's thread.</summary>
    private void Refresh_()
    {
        int generation = ++_generation;
        var root = _root;
        ShowChecking(root);
        Task.Run(() =>
        {
            Snapshot s;
            try { s = Take(root); }
            catch (Exception ex)
            {
                s = new Snapshot(null, false, null, Array.Empty<(string, PluginStatus)>(),
                                 new RamDiskState(false, false, false, ex.Message, false, null, null, null), VhdxSetup.Look(), 0);
            }
            try { BeginInvoke(() => { if (generation == _generation) Show(s); }); } catch { }
        });
    }

    private static Snapshot Take(string? root)
    {
        var l = root == null ? null : InstallerCore.Resolve(root);
        var plugins = l == null ? Array.Empty<(string, PluginStatus)>()
                    : Payload.Folders.Append(Payload.Menus).Select(f => (f, InstallerCore.StatusOf(l, f))).ToArray();
        return new Snapshot(l, l != null && InstallerCore.IsInstalled(l), InstallerCore.RunningHost(), plugins,
                            RamDiskSetup.Look(l), VhdxSetup.Look(), LbIntegrations.RamDisk.RamDrive.GetFreeRamMb());
    }

    private void ShowChecking(string? root)
    {
        SuspendLayout();
        _lbPath.Text = root ?? "No LaunchBox found - choose one.";
        Clear(_lbRows); Clear(_pluginRows); Clear(_ramRows); Clear(_vhdxRows);
        Row(_lbRows, Mark.Wait, "Checking", "…");
        _install.Enabled = _uninstall.Enabled = false;
        _ram.Visible = false;
        _legacy.Set(Mark.Wait, "Checking…", null, false);
        _modern.Set(Mark.Wait, "Checking…", null, false);
        SetTag(_ramTag, Mark.Wait, "checking…");
        SetTag(_vhdxTag, Mark.Wait, "checking…");
        ResumeLayout(true);
    }

    private static void Clear(TableLayoutPanel t)
    {
        foreach (Control c in t.Controls.Cast<Control>().ToArray()) c.Dispose();
        t.Controls.Clear();
        t.RowStyles.Clear();
        t.RowCount = 0;
    }

    private void Show(Snapshot s)
    {
        SuspendLayout();
        Clear(_lbRows); Clear(_pluginRows); Clear(_ramRows); Clear(_vhdxRows);
        var l = s.Layout;

        // ── LaunchBox ──
        if (l == null)
        {
            _lbPath.Text = "No LaunchBox found";
            Row(_lbRows, Mark.No, "Installation", "Not found next to this file - click Choose LaunchBox and pick its LaunchBox.exe.");
        }
        else
        {
            _lbPath.Text = l.Root;
            var where = l.PluginsRoot.Substring(l.Root.Length).TrimStart(Path.DirectorySeparatorChar) + "\\";
            Row(_lbRows, l.LbMajor > 0 ? Mark.Ok : Mark.Warn, l.LbMajor > 0 ? "LaunchBox " + l.LbMajor : "Version unknown",
                "Plugins go into " + where + (l.LbMajor > 0 ? "" : " - guessed from what is there."));
            if (s.Running != null) Row(_lbRows, Mark.Warn, s.Running + " is running", "Close it before installing, updating or uninstalling.");
        }

        // ── plugins ──
        if (l == null)
        {
            _pluginRows.ColumnCount = 1;
            _pluginRows.Controls.Add(new Label { Text = "Choose a LaunchBox to see what is installed in it.", ForeColor = Grey, AutoSize = true, Margin = new Padding(0, 3, 0, 3) });
        }
        else
        {
            _pluginRows.ColumnCount = 6;
            _pluginRows.ColumnStyles.Clear();
            for (int i = 0; i < 2; i++)
            {
                _pluginRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
                _pluginRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
                _pluginRows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, i == 0 ? 110 : 100));
            }
            for (int i = 0; i < s.Plugins.Length; i++)
            {
                var (folder, status) = s.Plugins[i];
                int row = i / 2, col = (i % 2) * 3;
                if (_pluginRows.RowCount <= row) { _pluginRows.RowCount = row + 1; _pluginRows.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
                var mark = status switch { PluginStatus.UpToDate => Mark.Ok, PluginStatus.Missing => Mark.No, _ => Mark.Warn };
                var name = new Label { Text = folder.Replace("Nixx-", ""), AutoSize = true, Font = _bold, Margin = new Padding(0, 3, 4, 0) };
                new ToolTip().SetToolTip(name, folder + " - " + What(folder));
                var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                stack.Controls.Add(name);
                stack.Controls.Add(new Label { Text = What(folder), AutoSize = true, ForeColor = Grey, Margin = new Padding(0, 0, 4, 3) });
                _pluginRows.Controls.Add(MarkLabel(mark), col, row);
                _pluginRows.Controls.Add(stack, col + 1, row);
                _pluginRows.Controls.Add(new Label
                {
                    Text = status switch
                    {
                        PluginStatus.UpToDate => "up to date",
                        PluginStatus.Outdated => "update available",
                        PluginStatus.Elsewhere => "in the old folder",
                        _ => "not installed",
                    },
                    AutoSize = true, ForeColor = mark == Mark.Ok ? Grey : mark == Mark.No ? Ink : Warn, Margin = new Padding(0, 3, 0, 0),
                }, col + 2, row);
            }
        }
        bool any = s.Plugins.Any(p => p.Status != PluginStatus.Missing);
        bool behind = s.Plugins.Any(p => p.Status != PluginStatus.UpToDate);
        _install.Text = !any ? "Install" : behind ? "Update" : "Reinstall";
        _install.Enabled = l != null;
        _uninstall.Enabled = l != null && s.Installed;

        // ── RAM disk ──
        var r = s.Ram;
        // LBIP_PREVIEW_NO_DRIVER=1: the window as a machine without ImDisk sees it - for looking at the choice, nothing else.
        if (Environment.GetEnvironmentVariable("LBIP_PREVIEW_NO_DRIVER") == "1") r = r with { Driver = false, Task = null };
        var imdisk = ImDiskSetup.InstalledVersion();
        var aim = AimSetup.DriverVersion();
        bool aimToolkit = AimSetup.ToolkitInstalled();
        bool imdiskThere = LbIntegrations.RamDisk.RamDrive.IsImDiskInstalled();
        _legacy.Set(imdiskThere ? Mark.Ok : Mark.No,
                    imdiskThere ? "Installed" + (imdisk != null ? ", version " + imdisk.ToString(3) : "") : "Not installed",
                    imdiskThere ? null : l != null ? "Install and set up" : "Install",
                    ImDiskSetup.CanOpenControlPanel);
        _modern.Set(aimToolkit ? Mark.Ok : aim != null ? Mark.Warn : Mark.No,
                    aimToolkit ? "Installed" + (aim != null ? ", driver " + aim.ToString(3) : "")
                    : aim != null ? "Driver " + aim.ToString(3) + " without the Toolkit" : "Not installed",
                    aimToolkit ? null : l != null ? "Install and set up" : "Install",
                    AimSetup.RamDiskUI() != null);
        Row(_ramRows, r.Runtime ? Mark.Ok : Mark.No, ".NET runtime", r.Runtime ? ".NET 9 or newer is present." : "Missing: the helper needs the .NET 9 Desktop Runtime or newer.");
        if (!r.Known)
        {
            Row(_ramRows, Mark.Info, "Helper", "Choose a LaunchBox to check it.");
            Row(_ramRows, Mark.Info, "Elevated task", "Choose a LaunchBox to check it.");
        }
        else
        {
            Row(_ramRows, !r.Helper ? Mark.No : r.HelperOld ? Mark.Warn : Mark.Ok, "Helper",
                !r.Helper ? "Not deployed yet."
                : r.HelperOld ? "Version " + r.HelperVersion + " - older than " + r.BundledVersion + ", which Set up puts in its place."
                : "Version " + (r.HelperVersion?.ToString() ?? "unknown") + ", in ThirdParty\\RomExtractor\\ramdisk\\.");
            Row(_ramRows, r.Task != null ? Mark.Ok : Mark.No, "Elevated task", r.Task != null ? "Registered (" + r.Task + ") - no prompt when a disk is mounted." : "Not registered: Set up asks for administrator rights once.");
        }
        var free = s.FreeRamMb > 0 ? "  ·  " + (s.FreeRamMb / 1024.0).ToString("0.0") + " GB of memory free" : "";
        SetTag(_ramTag, !r.Known ? Mark.Info : r.Ready ? Mark.Ok : Mark.Warn, (!r.Known ? "choose a LaunchBox" : r.Ready ? "ready, through " + (LbIntegrations.RamDisk.RamDrive.ActiveBackend() == "aim" ? "AIM" : "ImDisk") : "not set up") + free);
        if (r.Ready)
        {
            _ram.Visible = false;
        }
        else
        {
            // ONE BUTTON, SAYING THE ONE NEXT THING TO DO - LiteBox's row does the same.
            // The helper and the task: only once ImDisk is there - before that, its panel's Install does all three.
            _ram.Visible = r.Known && r.Driver;
            _ram.Enabled = l != null && r.Runtime;
            _ram.Text = r.Helper && r.HelperOld && r.Task != null ? "Update the helper" : "Set up the helper and task";
        }

        // ── VHDX ──
        var v = s.Vhdx;
        // LBIP_PREVIEW_NO_VHDX=1: the window as a machine without Windows' virtual disk support sees it - for looking only.
        if (Environment.GetEnvironmentVariable("LBIP_PREVIEW_NO_VHDX") == "1") v = v with { Api = false };
        // ImDisk is the recommendation; the AIM Toolkit only where Windows cannot attach a VHDX, since it can.
        _legacy.Tag.Visible = true;
        _modern.Tag.Visible = !v.Ready;
        var lacks = new List<string>();
        if (!v.Windows8) lacks.Add("Windows 8 or later");
        if (!v.Api) lacks.Add("virtdisk.dll");
        if (!v.Driver) lacks.Add("vhdmp.sys");
        Row(_vhdxRows, v.Ready ? Mark.Ok : Mark.No, "Windows support",
            v.Ready ? "Native virtual disks, differencing disks included (virtdisk.dll, vhdmp.sys)." : "Missing: " + string.Join(", ", lacks) + ".");
        if (r.Known)
        {
            bool helperOk = r.HelperVersion != null && r.HelperVersion >= LbIntegrations.RamDisk.RamDrive.VhdxProtocol && r.Task != null;
            Row(_vhdxRows, helperOk ? Mark.Ok : Mark.Warn, "Attach without prompt",
                helperOk ? "The RAM disk helper " + r.HelperVersion + " and its task can attach a disk."
                         : "Needs the RAM disk helper " + LbIntegrations.RamDisk.RamDrive.VhdxProtocol.ToString(2) + " or newer and its task - set up the RAM disk above.");
        }
        if (!v.Ready)
            Row(_vhdxRows, Mark.Info, "AIM Toolkit", aimToolkit
                ? "Installed here: it can attach VHDX instead - choose it in the NixxMenu's RamDisk & VHDX tab."
                : "An equivalent can be obtained with the AIM Toolkit, installable above.");
        SetTag(_vhdxTag, v.Ready ? Mark.Ok : Mark.No, v.Ready ? "supported" : "not supported");

        ResumeLayout(true);
    }

    private void SetTag(Label tag, Mark mark, string text)
    {
        tag.Text = (mark == Mark.Ok ? "✔ " : mark == Mark.No ? "✖ " : mark == Mark.Warn ? "! " : "") + text;
        tag.ForeColor = mark switch { Mark.Ok => Good, Mark.No => Bad, Mark.Warn => Warn, _ => Grey };
    }

    private static string What(string folder) => folder switch
    {
        Payload.Flycast => "Dreamcast, Naomi",
        Payload.MelonDs => "DS, DSi",
        Payload.NoGba => "DS, DSi",
        Payload.Ppsspp => "PSP",
        Payload.SuperZsnes => "Super Nintendo",
        Payload.Vita3k => "PS Vita",
        Payload.Xenia => "Xbox 360",
        Payload.Menus => "right-click menus",
        _ => "",
    };

    // ── what it does ─────────────────────────────────────────────────────────

    private void Choose()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select LaunchBox.exe",
            Filter = "LaunchBox.exe|LaunchBox.exe|BigBox.exe|BigBox.exe|Executable|*.exe",
            CheckFileExists = true,
        };
        if (_root != null) { try { dialog.InitialDirectory = _root; } catch { } }
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var root = InstallerCore.RootFromExe(dialog.FileName);
        if (!InstallerCore.LooksLikeRoot(root))
        {
            MessageBox.Show(this,
                "That does not look like a LaunchBox installation: there is no Core folder with "
                + "LaunchBox.exe or BigBox.exe in it.\n\nPick the LaunchBox.exe of the installation "
                + "you want the plugins in.",
                "Not a LaunchBox folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _root = root;
        Refresh_();
    }

    /// <summary>Open a link in whatever the user browses with. Wrapped because ShellExecute throws
    /// when no browser is registered, and a dead button is better than a crashed installer.</summary>
    private void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open " + url + "\n\n" + ex.Message,
                            "No browser", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>A driver install: a property of the machine, run with or without a LaunchBox.</summary>
    private void RunMachine(Func<(bool ok, string message)> action, string what)
    {
        Enabled = false;
        Cursor = Cursors.WaitCursor;
        bool ok;
        string message;
        try { (ok, message) = action(); }
        finally { Cursor = Cursors.Default; Enabled = true; }
        MessageBox.Show(this, message, what + (ok ? " - done" : " - not done"),
                        MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        Refresh_();
    }

    private void Run(Func<Layout, (bool ok, string message)> action, string what)
    {
        if (_root == null) return;

        Enabled = false;
        Cursor = Cursors.WaitCursor;
        bool ok;
        string message;
        try { (ok, message) = action(InstallerCore.Resolve(_root)); }
        finally { Cursor = Cursors.Default; Enabled = true; }

        MessageBox.Show(this, message, what + (ok ? " - done" : " - not done"),
                        MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        Refresh_();
    }
}
