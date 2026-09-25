// A folder and a handful of buttons. Everything it knows how to do is in InstallerCore and
// RamDiskSetup; this only asks which LaunchBox, and shows what came back.
//
// It tries to answer the folder question itself first, because the common case is the exe dropped
// into the LaunchBox folder and double-clicked. When it cannot, the picker asks for LaunchBox.exe
// rather than for a folder: people know where their LaunchBox.exe is, and "the root, not the one in
// Core" is a sentence that only makes sense once you already know the answer - so Core\LaunchBox.exe
// is accepted and walked up from instead of refused.

namespace NixxIntegrations;

internal sealed class InstallerForm : Form
{
    private string? _root;

    private readonly Label _rootLabel  = new() { AutoSize = false, Location = new Point(16, 46), Size = new Size(520, 20) };
    private readonly Label _stateLabel = new() { AutoSize = false, Location = new Point(16, 68), Size = new Size(520, 20) };
    private readonly Button _install   = new() { Text = "Install / Update",   Location = new Point(16, 104),  Width = 160, Height = 34 };
    private readonly Button _uninstall = new() { Text = "Uninstall",          Location = new Point(186, 104), Width = 160, Height = 34 };
    private readonly Button _choose    = new() { Text = "Choose LaunchBox…",  Location = new Point(376, 104), Width = 160, Height = 34 };

    // THE OPTIONAL HALF. Nothing in the pack needs a RAM disk today - Vita3K will - and every part
    // of it is missing on a clean machine, so it lives below the line with its own state and its own
    // button rather than mixed into the sentence above.
    private readonly Label _ramState = new() { AutoSize = false, Location = new Point(16, 186), Size = new Size(520, 36) };
    private readonly Button _ram     = new() { Text = "Enable RAM disk", Location = new Point(16, 228),  Width = 160, Height = 34 };
    private readonly Button _imdisk  = new() { Text = "Get ImDisk…",     Location = new Point(186, 228), Width = 160, Height = 34 };

    public InstallerForm()
    {
        Text = "Nixx integrations for LaunchBox";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(552, 316);

        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(520, 28),
            Text = string.Join("   ", Payload.Folders),
            Font = new Font(Font, FontStyle.Bold),
        });
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(16, 150),
            Size = new Size(520, 30),
            ForeColor = SystemColors.GrayText,
            Text = "Installs the plugin folders only. Your emulators, saves and NAND dumps are never "
                 + "touched, and no emulator entry is created or changed.",
        });
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(16, 166),
            Size = new Size(520, 20),
            Font = new Font(Font, FontStyle.Bold),
            Text = "Optional — RAM disk",
        });
        Controls.Add(new Label
        {
            AutoSize = false,
            Location = new Point(16, 270),
            Size = new Size(520, 36),
            ForeColor = SystemColors.GrayText,
            Text = "Shared with LiteBox: the same helper, in the same folder, run by the same "
                 + "scheduled task. Enabling it asks for administrator rights once, never again.",
        });

        _install.Click   += (_, _) => Run(InstallerCore.Install,   "Install");
        _uninstall.Click += (_, _) => Run(InstallerCore.Uninstall, "Uninstall");
        _choose.Click    += (_, _) => Choose();
        // Same signature as the two above, so it wires the same way. That is what the Run contract
        // is for.
        _ram.Click       += (_, _) => Run(RamDiskSetup.Enable, "RAM disk");
        _imdisk.Click    += (_, _) => Open("https://sourceforge.net/projects/imdisk-toolkit/");

        Controls.AddRange(new Control[] { _rootLabel, _stateLabel, _install, _uninstall, _choose,
                                          _ramState, _ram, _imdisk });

        // The exe dropped at the LaunchBox root, or inside Core, are both ordinary ways to run this.
        var here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (InstallerCore.LooksLikeRoot(here)) SetRoot(here);
        else if (InstallerCore.LooksLikeRoot(Path.GetDirectoryName(here))) SetRoot(Path.GetDirectoryName(here)!);
        else Refresh_();
    }

    private void SetRoot(string root) { _root = root; Refresh_(); }

    private void Refresh_()
    {
        if (_root == null)
        {
            _rootLabel.Text = "No LaunchBox found - choose one.";
            _stateLabel.Text = "";
            _install.Enabled = false;
            _uninstall.Enabled = false;
            _ramState.Text = "";
            _ram.Enabled = false;
            return;
        }

        var l = InstallerCore.Resolve(_root);
        var installed = InstallerCore.IsInstalled(l);
        var where = l.PluginsRoot.Substring(l.Root.Length).TrimStart(Path.DirectorySeparatorChar);

        _rootLabel.Text = _root + (l.LbMajor > 0 ? "   (LaunchBox " + l.LbMajor + ")" : "   (version unknown)");
        _stateLabel.Text = (installed ? "Installed" : "Not installed") + " - plugins go into " + where + "\\";
        _install.Enabled = true;
        _uninstall.Enabled = installed;

        var running = InstallerCore.RunningHost();
        if (running != null) _stateLabel.Text += "   |   " + running + " is running - close it first";

        // Asked every refresh rather than cached: LiteBox may have registered the task since this
        // window opened, and the answer is three file checks and one schtasks query.
        var ram = RamDiskSetup.Look(l);
        _ramState.Text = RamDiskSetup.Describe(ram);
        // Nothing to do when it is already shared and working; everything else is worth a try, even
        // a missing driver, because the button then says exactly what to go and get.
        _ram.Enabled = !ram.Ready;
    }

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
        SetRoot(root);
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
