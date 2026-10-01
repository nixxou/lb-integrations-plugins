// The Xenia tab of the pack's configuration window (Nixx-Menus' Tools entry "Nixx Integration Plugins
// Configuration..."): the profile and the console of the Xenia in the library (XeniaConsolePanel), then the options
// passed to every game (XeniaOptionRows) - a game's own are in its right-click menu, Nixx-Xenia : Options....
//
// THE CONTRACT IS A NAME (src\Menus\Settings.cs has it): a public static class LbIntegrations.<assembly name>.Settings
// with Title, Control CreatePage(), string Save(Control).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins;

namespace LbIntegrations.Xenia
{
    /// <summary>What Nixx-Menus calls, by name. Public, its signatures fixed.</summary>
    public static class Settings
    {
        public static string Title => "Xenia";

        public static Control CreatePage() => new XeniaSettingsPage();

        public static string Save(Control page)
        {
            if (!(page is XeniaSettingsPage ours)) return "this is not the Xenia page";
            try { return ours.Save(); }
            catch (Exception ex) { Log.Warn("settings page save", ex); return "the settings could not be written: " + ex.Message; }
        }
    }

    internal sealed class XeniaSettingsPage : UserControl
    {
        private readonly XeniaConsolePanel _console;
        private readonly XeniaOptionRows _rows;
        private readonly TextBox _line;

        public XeniaSettingsPage()
        {
            var exe = XeniaLibrary.Executables().FirstOrDefault(e => XeniaPaths.ForkOf(e) == XeniaFork.Canary);
            var top = new Label
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 8, 12, 0), ForeColor = SystemColors.GrayText,
                Text = "The profile and the console of Xenia, then the options passed to every game at launch. A game's own options, "
                       + "and its compatibility, are in its right-click menu: Nixx-Xenia : Options...",
            };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
            var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            scroll.Controls.Add(stack);

            if (exe != null)
            {
                stack.Controls.Add(new Label { Text = "Xenia: " + exe, AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 0, 0, 4) });
                _console = new XeniaConsolePanel(exe);
                stack.Controls.Add(_console);
            }
            else
                stack.Controls.Add(new Label
                {
                    AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(6, 4, 0, 10),
                    Text = "No Xenia Canary in the library yet: its profile and console settings show here once it is installed (Add Emulator).",
                });

            var own = exe != null ? XeniaOptions.Own(XeniaPaths.Resolve(exe).ConfigFile) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            stack.Controls.Add(new Label { Text = "Options for every game", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(4, 8, 0, 2) });
            _rows = new XeniaOptionRows(XeniaSettings.Read(), o => "Xenia's own: " + o.LabelOf(own.TryGetValue(o.Key, out var v) ? v : o.Default));
            stack.Controls.Add(_rows);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 104, Padding = new Padding(12, 4, 12, 8) };
            _line = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f), BackColor = SystemColors.Window };
            var file = new TextBox { Dock = DockStyle.Bottom, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
                                     ForeColor = SystemColors.GrayText, Text = "Settings file: " + XeniaSettings.SettingsPath, TabStop = false };
            bottom.Controls.Add(_line);
            bottom.Controls.Add(XeniaOptionRows.Legend());
            bottom.Controls.Add(file);

            Controls.Add(scroll);
            Controls.Add(top);
            Controls.Add(bottom);
            _rows.Changed += ShowLine;
            ShowLine();
        }

        private void ShowLine()
        {
            var flags = XeniaSettings.Flags(_rows.Values());
            _line.Text = flags.Count == 0 ? "(nothing added to the command line for every game)" : string.Join(" ", flags);
        }

        public string Save()
        {
            var problem = _rows.Problem() ?? _console?.Problem();
            if (problem != null) return problem;
            _console?.Save();
            XeniaSettings.WriteAll(_rows.Values());
            return null;
        }
    }

    /// <summary>The Xenia executables the library points at, resolved - canary first.</summary>
    internal static class XeniaLibrary
    {
#pragma warning disable CS0649
        /// <summary>For the probe, which has no library: the one executable to show. Set by reflection.</summary>
        internal static string ExeOverride;
#pragma warning restore CS0649

        public static List<string> Executables()
        {
            var found = new List<string>();
            if (ExeOverride != null) { found.Add(ExeOverride); return found; }
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return found;
                foreach (var emu in dm.GetAllEmulators())
                {
                    string path;
                    try { path = emu.ApplicationPath; } catch { continue; }
                    if (!XeniaPaths.IsXeniaExecutable(path)) continue;
                    var full = XeniaPlugin.ResolveFullPathForUi(path);
                    if (full != null && File.Exists(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
                }
            }
            catch (Exception ex) { Log.Warn("listing the emulators", ex); }
            return found.OrderBy(e => XeniaPaths.ForkOf(e) == XeniaFork.Canary ? 0 : 1).ToList();
        }
    }
}
