// Two buttons and a folder. Everything it knows how to do is in InstallerCore; this only asks which
// LaunchBox, and shows what came back.
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

    public InstallerForm()
    {
        Text = "Nixx integrations for LaunchBox";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(552, 190);

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

        _install.Click   += (_, _) => Run(InstallerCore.Install,   "Install");
        _uninstall.Click += (_, _) => Run(InstallerCore.Uninstall, "Uninstall");
        _choose.Click    += (_, _) => Choose();

        Controls.AddRange(new Control[] { _rootLabel, _stateLabel, _install, _uninstall, _choose });

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
