// Where each value of a per-game options window comes from, shown (Mehdi, 29/09): a coloured bar at the left
// of every setting, a legend saying the colours in the order they win, and a "Reset to defaults" button.
// Shared by every options window of the pack (Shared.Menu: it needs WinForms).
//
// THE ORDER IS THE SAME FOR EVERY EMULATOR: this plugin's > the game's own config in the emulator > the
// emulator's settings.
//   Here        blue    set in this window, for this game - what this plugin gives the emulator
//   GameConfig  amber   not set here: the game's OWN config in the emulator
//                       (PPSSPP's <ID>_ppsspp.ini, Vita3K's Custom Config, Flycast's [<ID>] of emu.cfg)
//   Emulator    grey    neither: the emulator's own settings, or its built-in default
//   HereLoses   red     set here, but the game's own config wins all the same: Flycast, for a game whose id
//                       cannot be named on its command line (a space in it - an arcade board's title)
//   Unused      no bar  a setting not used as things stand (a text set by hand is in use instead)
//
// A bar sits in the setting's own parent, just left of it: it follows it into a hidden tab or group box.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LbIntegrations.Lbip
{
    internal enum OptionLevel { Unused, Emulator, GameConfig, Here, HereLoses }

    internal sealed class OptionMarks
    {
        public static readonly Color HereColor = Color.FromArgb(0, 120, 215);
        public static readonly Color GameConfigColor = Color.FromArgb(222, 150, 0);
        public static readonly Color EmulatorColor = Color.FromArgb(175, 175, 175);
        public static readonly Color HereLosesColor = Color.FromArgb(205, 45, 45);

        public static Color ColorOf(OptionLevel level)
            => level == OptionLevel.Here ? HereColor : level == OptionLevel.GameConfig ? GameConfigColor
             : level == OptionLevel.Emulator ? EmulatorColor : level == OptionLevel.HereLoses ? HereLosesColor : Color.Empty;

        private readonly Dictionary<Control, Panel> _bars = new Dictionary<Control, Panel>();

        /// <summary>Mark a setting's control.</summary>
        public void Set(Control control, OptionLevel level)
        {
            if (control?.Parent == null) return;
            if (!_bars.TryGetValue(control, out var bar))
            {
                if (level == OptionLevel.Unused) return;
                bar = new Panel { Width = 4 };
                control.Parent.Controls.Add(bar);
                _bars[control] = bar;
                void Place()
                {
                    bar.Location = new Point(Math.Max(0, control.Left - 7), control.Top + 1);
                    bar.Height = Math.Max(6, control.Height - 2);
                }
                Place();
                control.LocationChanged += (_, _) => Place();
                control.SizeChanged += (_, _) => Place();
            }
            // Not bar.Visible to decide: WinForms says false for anything in a window not shown yet.
            bool shown = level != OptionLevel.Unused;
            if (shown) bar.BackColor = ColorOf(level);
            bar.Visible = shown;
        }

        /// <summary>The legend: the colours in the order they win, and the red one apart when the window can
        /// show it.</summary>
        public static Control Legend(string emulator, bool gameConfig, bool hereLoses)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = Padding.Empty,
            };
            void Item(Color color, string text)
            {
                row.Controls.Add(new Panel { BackColor = color, Size = new Size(4, 14), Margin = new Padding(0, 3, 4, 0) });
                row.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(0, 2, 6, 0), UseMnemonic = false });
            }
            void Over() => row.Controls.Add(new Label { Text = ">", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 6, 0) });
            Item(HereColor, "Set here");
            Over();
            if (gameConfig) { Item(GameConfigColor, "The game's own " + emulator + " config"); Over(); }
            Item(EmulatorColor, emulator + "'s settings");
            if (hereLoses)
            {
                row.Controls.Add(new Label { Text = "", AutoSize = true, Margin = new Padding(0, 0, 8, 0) });
                Item(HereLosesColor, "Set here, but its config wins");
            }
            return row;
        }

        /// <summary>For an emulator whose game config this plugin swaps whole for a session (Vita3K, PPSSPP - Mehdi, 01/10): a
        /// red line saying what that costs, and a button that PAUSES this plugin's settings for the game - kept, not used at
        /// launch, so what the user changes in the emulator's own game config during the game is kept. Saved on OK, like the
        /// rest of the window. <paramref name="changed"/> is told each new state.</summary>
        public static Panel PauseRow(string emulator, bool paused, Action<bool> changed)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(0, 4, 0, 4) };
            var line = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            var button = new Button { AutoSize = true, Dock = DockStyle.Right };
            new ToolTip().SetToolTip(button, "Paused: this game's settings here are kept but not used at launch - the game runs on its own\n"
                                             + emulator + " config, and what is changed there during the game is kept. Saved on OK.");
            void Show()
            {
                line.ForeColor = paused ? Color.DarkGoldenrod : Color.Firebrick;
                line.Text = paused
                    ? "Paused: the game runs on its own " + emulator + " config - changes made there are kept."
                    : "Changes made in " + emulator + "'s own game config during a game with these settings are not kept.";
                button.Text = paused ? "Resume these settings" : "Pause these settings";
            }
            button.Click += (_, _) => { paused = !paused; Show(); changed(paused); };
            Show();
            row.Controls.Add(line);
            row.Controls.Add(button);
            return row;
        }

        /// <summary>"Reset to defaults": every setting of the window back to its default - nothing is saved
        /// before OK.</summary>
        public static Button ResetButton(Action reset)
        {
            var button = new Button { Text = "Reset to defaults", AutoSize = true };
            new ToolTip().SetToolTip(button, "Every setting of this window back to <Default>, and \"Edit by hand\" off.\nNothing is saved until OK.");
            button.Click += (_, _) => reset();
            return button;
        }

        /// <summary>The bottom of an options window: the legend on its own line, the reset button at the left
        /// of OK and Cancel. <paramref name="extra"/>, when given, goes right of the reset button.</summary>
        public static Panel Bottom(Control legend, Button reset, Button ok, Button cancel, Control extra = null)
        {
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 74 };
            bottom.Controls.Add(legend);
            bottom.Controls.Add(reset);
            if (extra != null) bottom.Controls.Add(extra);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            bottom.Layout += (_, _) => PlaceBottom(bottom, legend, reset, ok, cancel, extra, 12);
            return bottom;
        }

        /// <summary>The bottom's layout, for a window that builds its own: the buttons on the last line, the
        /// legend just above them - and the buttons in front, so a legend taller than it looks (a scaled
        /// display) can never cover one.</summary>
        public static void PlaceBottom(Control bottom, Control legend, Control reset, Control ok, Control cancel, Control extra, int margin)
        {
            cancel.Location = new Point(bottom.ClientSize.Width - margin - cancel.Width, bottom.ClientSize.Height - margin - cancel.Height);
            ok.Location = new Point(cancel.Left - 8 - ok.Width, cancel.Top);
            reset.Location = new Point(margin, cancel.Top + (cancel.Height - reset.Height) / 2);
            if (extra != null) extra.Location = new Point(reset.Right + 8, cancel.Top + (cancel.Height - extra.Height) / 2);
            legend.Location = new Point(margin, Math.Max(0, cancel.Top - legend.Height - 8));
            legend.SendToBack();
            foreach (var c in new[] { reset, extra, ok, cancel }) c?.BringToFront();
        }
    }
}
