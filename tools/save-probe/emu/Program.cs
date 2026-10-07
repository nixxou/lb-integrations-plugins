// The save probe's fake emulator: shows how it was started, and writes a save or a state the way an emulator would -
// saves\<rom name><SaveExt>, saves\<rom name><StateExt><slot> - each a new content (role, game, time), so LaunchBox
// sees a change. SaveExt / StateExt come from probe.ini beside the exe (the plugin writes it).

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SaveProbeEmu
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.Run(new ProbeForm(args ?? Array.Empty<string>()));
        }
    }

    internal sealed class ProbeForm : Form
    {
        private readonly string _role, _dir, _rom, _stem;
        private readonly TextBox _log;

        public ProbeForm(string[] args)
        {
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            _dir = Path.GetDirectoryName(exe);
            _role = Path.GetFileNameWithoutExtension(exe).Replace("SaveProbe", "");
            _rom = args.LastOrDefault(a => !a.StartsWith("-"));
            _stem = _rom == null ? "(no game)" : Path.GetFileNameWithoutExtension(_rom);

            Text = "SaveProbe " + _role + " - " + _stem;
            Width = 720; Height = 420; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6) };
            buttons.Controls.Add(Button("Write save", () => Write(Ext("SaveExt", "save"))));
            for (int slot = 1; slot <= 3; slot++) { var s = slot; buttons.Controls.Add(Button("Write state " + s, () => Write(Ext("StateExt", "state") + s))); }
            buttons.Controls.Add(Button("Quit", Close));
            _log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f) };
            Controls.Add(_log);
            Controls.Add(buttons);

            Say("role " + _role + ", started at " + DateTime.Now.ToString("HH:mm:ss"));
            Say("exe  " + exe);
            Say("args " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)));
            Say("game " + (_rom ?? "-") + (_rom != null && !File.Exists(_rom) ? "  (NOT FOUND)" : ""));
            Say("saves go to " + Path.Combine(_dir, "saves"));
        }

        private Button Button(string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 30 };
            b.Click += (_, _) => click();
            return b;
        }

        private string Ext(string key, string kind)
        {
            try
            {
                var ini = Path.Combine(_dir, "probe.ini");
                if (File.Exists(ini))
                    foreach (var l in File.ReadAllLines(ini))
                    {
                        var i = l.IndexOf('=');
                        if (i > 0 && string.Equals(l.Substring(0, i).Trim(), key, StringComparison.OrdinalIgnoreCase)) return l.Substring(i + 1).Trim();
                    }
            }
            catch { }
            return "." + _role.ToLowerInvariant() + kind;
        }

        private void Write(string ext)
        {
            if (_rom == null) { Say("no game: nothing written"); return; }
            var dir = Path.Combine(_dir, "saves");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, _stem + ext);
            File.WriteAllText(path, "SaveProbe " + _role + "\r\ngame " + _stem + "\r\nwritten " + DateTime.Now.ToString("O") + "\r\nnonce " + Guid.NewGuid() + "\r\n", Encoding.UTF8);
            Say(DateTime.Now.ToString("HH:mm:ss") + " wrote " + path);
        }

        private void Say(string line) => _log.AppendText(line + Environment.NewLine);
    }
}
