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
        /// <summary>Optional (05/10): string[] Emulators() - "<title>\t<path>" - and string OpenEmulator(string path).</summary>
        public MethodInfo EmulatorsMethod, OpenMethod;

        public string Title { get { try { return TitleProperty.GetValue(null) as string ?? Name; } catch { return Name; } } }
        public Control Create() => (Control)CreateMethod.Invoke(null, null);
        public string Save(Control page) => (string)SaveMethod.Invoke(null, new object[] { page });
        public string[] Emulators() { try { return EmulatorsMethod?.Invoke(null, null) as string[] ?? new string[0]; } catch { return new string[0]; } }
        public string Open(string path) => (string)OpenMethod.Invoke(null, new object[] { path });
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
                var emulators = type.GetMethod("Emulators", flags, null, Type.EmptyTypes, null);
                var open = type.GetMethod("OpenEmulator", flags, null, new[] { typeof(string) }, null);
                if (emulators?.ReturnType != typeof(string[]) || open?.ReturnType != typeof(string)) { emulators = null; open = null; }
                return new SettingsProvider { Name = name, Assembly = asm, TitleProperty = title, CreateMethod = create, SaveMethod = save, EmulatorsMethod = emulators, OpenMethod = open };
            }
            catch (Exception ex) { RelayLog.Warn("looking at " + asm.FullName, ex); return null; }
        }
    }

    internal sealed class NixxSettingsForm : Form
    {
        // ONE WINDOW, PAGES ON THE LEFT (Mehdi, 05/10: "au lieu d'onglets une barre de pages sur la gauche", dark): LiteBox's own
        // options shell (LbApiHost\Host\Options\OptionsWindow.cs) - a list at the left, the page at the right under its title,
        // Cancel / Apply / OK below - in its colours (DarkTheme), so these pages can go into LiteBox one day as they are.
        // Every page is built when the window opens, as the tabs were: OK and Apply save them all, whichever was looked at.
        private sealed class Entry
        {
            public string Title;
            public Control View;            // what the page bar shows at the right; null for a heading
            public Func<string> Save;       // null: nothing to save
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly ListBox _nav;
        private readonly Panel _host;
        private readonly Label _title;
        private RamDiskTab _ramDisk;
        private IdentityPanel _identity;

        private static int S(Control c, int px) => (int)Math.Round(px * (c.DeviceDpi / 96f));

        public NixxSettingsForm(List<SettingsProvider> providers)
        {
            Text = "Nixx Integration Plugins Configuration";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(720, 480);

            Heading("PACK");
            Add("General", GeneralPage(providers), null);
            Add(IdentityPanel.Title, IdentityPage(), () => _identity?.Save());
            Add(RamDiskTab.Title, RamDiskPage(), () => _ramDisk?.Save());
            Heading("EMULATORS");
            foreach (var provider in providers) PluginPage(provider);

            // ── the page bar ──
            _nav = new ListBox
            {
                Dock = DockStyle.Left, Width = S(this, 190), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = S(this, 30),
                BorderStyle = BorderStyle.None, BackColor = DarkTheme.Side, ForeColor = DarkTheme.Text, Font = new Font("Segoe UI", 10f), IntegralHeight = false, Tag = DarkTheme.Own,
            };
            foreach (var e in _entries) _nav.Items.Add(e);
            _nav.DrawItem += DrawEntry;
            // More pages than it can show (Mehdi, 05/10): it scrolls - its scroll bar dark too.
            DarkTheme.NativeDark(_nav);
            int last = 0;
            _nav.SelectedIndexChanged += (_, _) =>
            {
                var i = _nav.SelectedIndex;
                if (i < 0) return;
                if (_entries[i].View == null) { _nav.SelectedIndex = last; return; }   // a heading is not a page
                last = i;
                Show(_entries[i]);
            };

            // ── the page ──
            var right = new Panel { Dock = DockStyle.Fill, BackColor = DarkTheme.Back, Padding = new Padding(S(this, 18), S(this, 12), S(this, 18), S(this, 4)) };
            _title = new Label { Dock = DockStyle.Top, AutoSize = false, Height = S(this, 34), Font = new Font("Segoe UI Semibold", 14f), ForeColor = DarkTheme.Text, BackColor = DarkTheme.Back, UseMnemonic = false, Tag = DarkTheme.Own };
            _host = new Panel { Dock = DockStyle.Fill, BackColor = DarkTheme.Back };
            right.Controls.Add(_host);
            right.Controls.Add(_title);

            // ── below: LiteBox's footer ──
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = S(this, 50), BackColor = DarkTheme.Side };
            Button Action(string text, Color back)
            {
                var b = new Button { Text = text, Width = S(this, 96), Height = S(this, 30), FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White,
                                     Font = new Font("Segoe UI", 9f, FontStyle.Bold), Anchor = AnchorStyles.Right | AnchorStyles.Bottom, UseVisualStyleBackColor = false };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
                return b;
            }
            var cancel = Action("Cancel", DarkTheme.ButtonBack);
            var apply = Action("Apply", DarkTheme.Accent);
            var ok = Action("OK", DarkTheme.Ok);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Click += (_, _) => { if (SaveAll()) { DialogResult = DialogResult.OK; Close(); } };
            apply.Click += (_, _) => SaveAll();
            bottom.Controls.AddRange(new Control[] { cancel, apply, ok });
            bottom.Layout += (_, _) =>
            {
                int y = (bottom.ClientSize.Height - ok.Height) / 2, gap = S(this, 8);
                ok.Location = new Point(bottom.ClientSize.Width - S(this, 16) - ok.Width, y);
                apply.Location = new Point(ok.Left - gap - apply.Width, y);
                cancel.Location = new Point(apply.Left - gap - cancel.Width, y);
            };
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(right);
            Controls.Add(_nav);
            Controls.Add(bottom);
            DarkTheme.Apply(this);

            _nav.SelectedIndex = _entries.FindIndex(e => e.View != null);
        }

        private void Heading(string text) => _entries.Add(new Entry { Title = text });

        private void Add(string title, Control view, Func<string> save)
        {
            view.Dock = DockStyle.Fill;
            view.Visible = false;
            _entries.Add(new Entry { Title = title, View = view, Save = save });
        }

        private void Show(Entry e)
        {
            _title.Text = e.Title;
            _host.SuspendLayout();
            foreach (Control c in _host.Controls) c.Visible = false;
            if (e.View.Parent != _host) _host.Controls.Add(e.View);
            e.View.Visible = true;
            _host.ResumeLayout();
        }

        /// <summary>A row of the page bar: LiteBox's - the whole row in the accent when chosen; a heading small, dim, not chosen.</summary>
        private void DrawEntry(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var entry = _entries[e.Index];
            var g = e.Graphics;
            bool chosen = (e.State & DrawItemState.Selected) != 0 && entry.View != null;
            using (var b = new SolidBrush(chosen ? DarkTheme.Accent : DarkTheme.Side)) g.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + S(this, 12), e.Bounds.Y, e.Bounds.Width - S(this, 16), e.Bounds.Height);
            if (entry.View == null)
            {
                using var small = new Font("Segoe UI", 8f, FontStyle.Bold);
                TextRenderer.DrawText(g, entry.Title, small, new Rectangle(r.X, r.Y + S(this, 8), r.Width, r.Height - S(this, 8)), DarkTheme.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            TextRenderer.DrawText(g, entry.Title, _nav.Font, r, chosen ? Color.White : DarkTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>Every page's Save, in order - EACH ONE, whatever the others say (Mehdi, 04/10: one page refusing,
        /// "Xenia is running", no longer leaves the pages after it unsaved). When any refused: one message listing
        /// them, the first one's page shown, the window left open.</summary>
        private bool SaveAll()
        {
            var refused = new List<(Entry Entry, string Problem)>();
            foreach (var e in _entries)
            {
                if (e.Save == null) continue;
                string problem;
                try { problem = e.Save(); }
                catch (Exception ex) { problem = (ex.InnerException ?? ex).Message; RelayLog.Warn(e.Title + ".Save", ex); }
                if (problem != null) refused.Add((e, problem));
            }
            if (refused.Count == 0) return true;

            _nav.SelectedIndex = _entries.IndexOf(refused[0].Entry);
            var text = refused.Count == 1
                ? refused[0].Problem
                : "These pages were not saved:\n\n" + string.Join("\n\n", refused.Select(r => r.Entry.Title + ": " + r.Problem))
                  + "\n\nEverything else was saved.";
            MessageBox.Show(this, text, refused.Count == 1 ? refused[0].Entry.Title : "Some settings were not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private static Control Failed(string text) => new Label { Dock = DockStyle.Fill, Padding = new Padding(12), ForeColor = Color.Firebrick, Text = text };

        /// <summary>The pack's one console identity, for every plugin - see IdentityPanel.</summary>
        private Control IdentityPage()
        {
            try
            {
                var scroll = new Panel { AutoScroll = true, Padding = new Padding(0, 4, 8, 8) };
                _identity = new IdentityPanel(null, _ => IdentityApply.Show(this));
                scroll.Controls.Add(_identity);
                return scroll;
            }
            catch (Exception ex) { RelayLog.Warn("the identity page", ex); return Failed("Your console could not be shown:\n\n" + ex.Message); }
        }

        /// <summary>One RAM disk section for every plugin of the pack - see RamDiskTab.</summary>
        private Control RamDiskPage()
        {
            try { return _ramDisk = new RamDiskTab(); }
            catch (Exception ex) { RelayLog.Warn("the RAM disk page", ex); return Failed("The RAM disk settings could not be shown:\n\n" + ex.Message); }
        }

        private void PluginPage(SettingsProvider provider)
        {
            var view = new Panel();
            try
            {
                var page = provider.Create();
                page.Dock = DockStyle.Fill;
                view.Controls.Add(page);
                // Its emulators opened without a game, as LaunchBox's "Open emulator" opens them (Mehdi, 05/10).
                if (provider.OpenMethod != null && OpenBar(provider) is Control bar) { view.Controls.Add(bar); page.BringToFront(); }
                Add(provider.Title, view, () => provider.Save(page));
            }
            catch (Exception ex)
            {
                RelayLog.Warn(provider.Name + ".CreatePage", ex);
                view.Controls.Add(Failed(provider.Title + " could not build its settings page:\n\n" + (ex.InnerException ?? ex).Message));
                Add(provider.Title, view, null);       // a page that could not be built has nothing to save
            }
        }
        /// <summary>One button per emulator of the plugin LaunchBox has: opened as its "Open emulator" menu opens it, with what the
        /// plugin does around it (Shared.Lbip\LbipOpenEmulator). Null when there is none.</summary>
        private Control OpenBar(SettingsProvider provider)
        {
            var emulators = provider.Emulators();
            if (emulators.Length == 0) return null;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(8, 4, 8, 4) };
            foreach (var line in emulators)
            {
                var cut = line.IndexOf('\t');
                if (cut <= 0) continue;
                string title = line.Substring(0, cut), path = line.Substring(cut + 1);
                var button = new Button { Text = "Open " + title, AutoSize = true };
                new ToolTip().SetToolTip(button, path + "\n\nOpened without a game, as LaunchBox's \"Open emulator\" opens it.");
                button.Click += (_, _) =>
                {
                    string problem;
                    try { problem = provider.Open(path); }
                    catch (Exception ex) { problem = (ex.InnerException ?? ex).Message; RelayLog.Warn(provider.Name + ".OpenEmulator", ex); }
                    if (problem != null) MessageBox.Show(this, title + " could not be opened: " + problem, provider.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                bar.Controls.Add(button);
            }
            return bar.Controls.Count == 0 ? null : bar;
        }

        /// <summary>The pack at a glance: its guides, the plugins loaded and their versions, where its logs are.</summary>
        private static Control GeneralPage(List<SettingsProvider> providers)
        {
            var scroll = new Panel { AutoScroll = true };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(0, 0), Margin = Padding.Empty };
            scroll.Controls.Add(stack);
            GroupBox Card(string title)
            {
                var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(620, 0), Padding = new Padding(12, 8, 12, 10), Margin = new Padding(0, 0, 0, 12) };
                stack.Controls.Add(g);
                return g;
            }

            var guides = Card("User guides");
            guides.Controls.Add(HelpButtons());

            var loaded = Card("Plugins loaded");
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Location = new Point(12, 24), Margin = Padding.Empty };
            int row = 0;
            void Line(string name, string version, string where)
            {
                table.Controls.Add(new Label { Text = name, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 3, 16, 3) }, 0, row);
                table.Controls.Add(new Label { Text = version, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 16, 3) }, 1, row);
                var path = new Label { Text = where, AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 3, 0, 3) };
                table.Controls.Add(path, 2, row);
                row++;
            }
            if (providers.Count == 0) Line("none", "", "");
            foreach (var p in providers) Line(p.Title, p.Assembly.GetName().Version?.ToString() ?? "", p.Assembly.Location);
            loaded.Controls.Add(table);

            var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins");
            string host = "?";
            try { host = Path.GetFileName(Environment.ProcessPath) + " " + FileVersionInfo.GetVersionInfo(Environment.ProcessPath).FileVersion; } catch { }
            var about = Card("This LaunchBox");
            var aboutFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Location = new Point(12, 24), Margin = Padding.Empty };
            aboutFlow.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6),
                                               Text = "Host: " + host + "\nMenu relay: " + typeof(NixxSettingsMenu).Assembly.Location + "\nGame menus: "
                                                      + (Providers.All() is var menus && menus.Count > 0 ? string.Join(", ", menus.Select(m => m.Name)) : "none loaded")
                                                      + "\nLogs: " + logs });
            var open = new Button { Text = "Open the logs folder", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
            open.Click += (_, _) =>
            {
                try { Directory.CreateDirectory(logs); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + logs + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { RelayLog.Warn("open the logs folder", ex); }
            };
            aboutFlow.Controls.Add(open);
            about.Controls.Add(aboutFlow);
            return scroll;
        }
        /// <summary>One button per plugin that carries a user guide - an embedded resource named "help.html" (Mehdi, 01/10).
        /// Found among the loaded assemblies like the rest of this relay; the page is written to the logs folder's help\ and
        /// opened in the browser. A plugin without one simply has no button.</summary>
        private static Control HelpButtons()
        {
            var row = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(600, 0), WrapContents = true, Location = new Point(12, 24), Margin = Padding.Empty };
            foreach (var (name, asm) in HelpProviders())
            {
                var button = new Button { Text = name, AutoSize = true, Margin = new Padding(0, 2, 6, 2) };
                button.Click += (_, _) => OpenHelp(name, asm);
                row.Controls.Add(button);
            }
            if (row.Controls.Count == 0) row.Controls.Add(new Label { Text = "No plugin with a guide is loaded.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) });
            return row;
        }

        private static List<(string Name, Assembly Assembly)> HelpProviders()
        {
            var found = new List<(string, Assembly)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.IsDynamic || !asm.GetManifestResourceNames().Contains("help.html")) continue;
                    found.Add((TitleOf(asm) ?? asm.GetName().Name, asm));
                }
                catch { }
            }
            return found.OrderBy(f => f.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The page's own &lt;title&gt;, up to its " - ": "Xenia - Nixx plugin guide" -> "Xenia".</summary>
        private static string TitleOf(Assembly asm)
        {
            try
            {
                using var s = asm.GetManifestResourceStream("help.html");
                using var r = new StreamReader(s);
                var html = r.ReadToEnd();
                int a = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase), b = html.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);
                if (a < 0 || b < a) return null;
                var t = System.Net.WebUtility.HtmlDecode(html.Substring(a + 7, b - a - 7)).Trim();
                int dash = t.IndexOf(" - ", StringComparison.Ordinal);
                return dash > 0 ? t.Substring(0, dash) : t;
            }
            catch { return null; }
        }

        private static void OpenHelp(string name, Assembly asm)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins", "help");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, asm.GetName().Name + ".html");
                using (var s = asm.GetManifestResourceStream("help.html"))
                using (var f = File.Create(path)) s.CopyTo(f);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                RelayLog.Info("opened the guide of " + name + " -> " + path);
            }
            catch (Exception ex) { RelayLog.Warn("the guide of " + name, ex); }
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
