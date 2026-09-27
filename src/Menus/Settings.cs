// "Nixx Integration Plugins Configuration..." in LaunchBox's Tools menu: one window for the whole pack,
// a General tab of its own and one tab per plugin - the content of which the PLUGIN builds.
//
// Here for the same reason as the game menus (Menus.cs): a plugin in Local\Plugins never has a menu of
// its own asked on LaunchBox 14. This DLL, in the classic Plugins\ root, shows the entry and hosts the
// window; each plugin fills its own tab.
//
// THE CONTRACT IS A NAME, as for the game menus. A pack plugin whose assembly is <X> exposes
//
//     public static class LbIntegrations.<X>.Settings
//     {
//         public static string Title { get; }        // its tab's name
//         public static Control CreatePage();        // its tab's content - a WinForms control it builds
//         public static string Save(Control page);   // OK / Apply: null when saved, or why not
//     }
//
// Control is WinForms' own type, the same on both sides: nothing of the pack is shared. A plugin that
// fails to build its page gets a tab saying so, the others are untouched; one that refuses to save
// keeps the window open, on its tab, with its reason.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Menus
{
    public sealed class NixxSettingsMenu : ISystemMenuItemPlugin
    {
        public string Caption => "Nixx Integration Plugins Configuration...";
        public Image IconImage => _icon ??= SystemIcons.Application.ToBitmap();
        private static Image _icon;
        public bool ShowInLaunchBox => true;
        public bool ShowInBigBox => false;
        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected()
        {
            try
            {
                using var form = new NixxSettingsForm(SettingsProviders.All());
                form.ShowDialog(Owner.Foreground());
            }
            catch (Exception ex) { RelayLog.Warn("the configuration window", ex); }
        }
    }

    /// <summary>One plugin's Settings class, bound once.</summary>
    internal sealed class SettingsProvider
    {
        public string Name;
        public Assembly Assembly;
        public PropertyInfo TitleProperty;
        public MethodInfo CreateMethod, SaveMethod;

        public string Title { get { try { return TitleProperty.GetValue(null) as string ?? Name; } catch { return Name; } } }
        public Control Create() => (Control)CreateMethod.Invoke(null, null);
        public string Save(Control page) => (string)SaveMethod.Invoke(null, new object[] { page });
    }

    internal static class SettingsProviders
    {
        private static readonly Dictionary<Assembly, SettingsProvider> Seen = new Dictionary<Assembly, SettingsProvider>();

        public static List<SettingsProvider> All()
        {
            var found = new List<SettingsProvider>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                SettingsProvider provider;
                lock (Seen)
                    if (!Seen.TryGetValue(asm, out provider)) Seen[asm] = provider = Bind(asm);
                if (provider != null) found.Add(provider);
            }
            return found.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static SettingsProvider Bind(Assembly asm)
        {
            try
            {
                if (asm.IsDynamic) return null;
                var name = asm.GetName().Name;
                if (string.IsNullOrEmpty(name)) return null;
                var type = asm.GetType("LbIntegrations." + name + ".Settings", throwOnError: false);
                if (type == null) return null;

                var flags = BindingFlags.Public | BindingFlags.Static;
                var title = type.GetProperty("Title", flags);
                var create = type.GetMethod("CreatePage", flags, null, Type.EmptyTypes, null);
                var save = type.GetMethod("Save", flags, null, new[] { typeof(Control) }, null);
                if (title?.PropertyType != typeof(string) || create == null || !typeof(Control).IsAssignableFrom(create.ReturnType)
                    || save == null || save.ReturnType != typeof(string))
                {
                    RelayLog.Warn(type.FullName + " is there but not as the relay expects it (Title, Control CreatePage(), string Save(Control))");
                    return null;
                }
                RelayLog.Info("relaying the settings of " + name);
                return new SettingsProvider { Name = name, Assembly = asm, TitleProperty = title, CreateMethod = create, SaveMethod = save };
            }
            catch (Exception ex) { RelayLog.Warn("looking at " + asm.FullName, ex); return null; }
        }
    }

    internal sealed class NixxSettingsForm : Form
    {
        private readonly List<(SettingsProvider Provider, TabPage Tab, Control Page)> _plugins = new List<(SettingsProvider, TabPage, Control)>();
        private readonly TabControl _tabs;

        public NixxSettingsForm(List<SettingsProvider> providers)
        {
            Text = "Nixx Integration Plugins Configuration";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(700, 500);
            MinimumSize = new Size(560, 400);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(GeneralTab(providers));
            foreach (var provider in providers) _tabs.TabPages.Add(PluginTab(provider));

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
            var ok = new Button { Text = "OK", Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            var apply = new Button { Text = "Apply", Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
            ok.Click += (_, _) => { if (SaveAll()) { DialogResult = DialogResult.OK; Close(); } };
            apply.Click += (_, _) => SaveAll();
            bottom.Controls.AddRange(new Control[] { ok, cancel, apply });
            bottom.Layout += (_, _) =>
            {
                apply.Location = new Point(bottom.ClientSize.Width - 12 - apply.Width, 10);
                cancel.Location = new Point(apply.Left - 8 - cancel.Width, 10);
                ok.Location = new Point(cancel.Left - 8 - ok.Width, 10);
            };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(_tabs);
            Controls.Add(bottom);
        }

        /// <summary>Every plugin's Save, in tab order. The first that refuses stops it: its tab is shown,
        /// with its reason, and nothing after it is saved.</summary>
        private bool SaveAll()
        {
            foreach (var (provider, tab, page) in _plugins)
            {
                if (page == null) continue;   // its page could not be built: nothing to save
                string problem;
                try { problem = provider.Save(page); }
                catch (Exception ex) { problem = (ex.InnerException ?? ex).Message; RelayLog.Warn(provider.Name + ".Save", ex); }
                if (problem == null) continue;
                _tabs.SelectedTab = tab;
                MessageBox.Show(this, problem, provider.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private TabPage PluginTab(SettingsProvider provider)
        {
            var tab = new TabPage(provider.Title) { UseVisualStyleBackColor = true };
            Control page = null;
            try
            {
                page = provider.Create();
                page.Dock = DockStyle.Fill;
                tab.Controls.Add(page);
            }
            catch (Exception ex)
            {
                RelayLog.Warn(provider.Name + ".CreatePage", ex);
                tab.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill, Padding = new Padding(12), ForeColor = Color.Firebrick,
                    Text = provider.Title + " could not build its settings page:\n\n" + (ex.InnerException ?? ex).Message,
                });
                page = null;
            }
            _plugins.Add((provider, tab, page));
            return tab;
        }

        private static TabPage GeneralTab(List<SettingsProvider> providers)
        {
            var tab = new TabPage("General") { UseVisualStyleBackColor = true, Padding = new Padding(12) };

            var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins");
            string host = "?";
            try { host = Path.GetFileName(Environment.ProcessPath) + " " + FileVersionInfo.GetVersionInfo(Environment.ProcessPath).FileVersion; } catch { }

            var lines = new List<string>
            {
                "Nixx integration plugins",
                "",
                "Host: " + host,
                "Menu relay: " + typeof(NixxSettingsMenu).Assembly.Location,
                "",
                "Plugins with settings:",
            };
            if (providers.Count == 0) lines.Add("    none loaded");
            foreach (var p in providers)
                lines.Add("    " + p.Title + "  " + p.Assembly.GetName().Version + "  -  " + p.Assembly.Location);
            lines.Add("");
            lines.Add("Plugins with game menus:");
            var menus = Providers.All();
            if (menus.Count == 0) lines.Add("    none loaded");
            foreach (var m in menus) lines.Add("    " + m.Name);
            lines.Add("");
            lines.Add("Logs: " + logs);

            var text = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, Text = string.Join(Environment.NewLine, lines),
            };
            var open = new Button { Text = "Open the logs folder", AutoSize = true, Dock = DockStyle.Bottom };
            open.Click += (_, _) =>
            {
                try { Directory.CreateDirectory(logs); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + logs + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { RelayLog.Warn("open the logs folder", ex); }
            };
            tab.Controls.Add(text);
            tab.Controls.Add(open);
            return tab;
        }
    }

    /// <summary>The host's active window, so a dialog is modal to it and comes up in front.</summary>
    internal sealed class Owner : IWin32Window
    {
        private Owner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }

        public static IWin32Window Foreground()
        {
            var handle = GetForegroundWindow();
            return handle == IntPtr.Zero ? null : new Owner(handle);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
