// The Options window's "Compatibility" tab (Mehdi, 29/09): where the game stands in Vita3K's
// compatibility list, and the three settings Vita3K never sets for a game by itself - and that some
// games need changed:
//   - File Loading Delay (emulator/file-loading-delay, 0-30): "required for some games that load files
//     too quickly compared to real hardware (e.g., Silent Hill)" - Vita3K's own help;
//   - CPU optimizations (cpu/cpu-opt): Dynarmic's; some games crash with them;
//   - NGS audio (audio/enable-ngs - "ngs-enable" in config.yml): the advanced audio library; some games
//     crash, or go silent, with it.
// The same rules as the Graphics tab: a filled box or an untouched "Override default" is the default -
// what the game runs on without this plugin - and only what is not on default is kept for the game.
// The modules (core) are left to the Advanced tab: Vita3K's automatic mode already loads the modules it
// knows to work, for every game (module/src/load_module.cpp, auto_lle_modules).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kCompatFields : Panel
    {
        private Dictionary<string, string> _defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly CheckBox _cpuOpt, _ngs, _delayOverride;
        private readonly TrackBar _delay;
        private readonly Label _delayValue;
        private bool _showing, _enabled = true;

        public const string DelayKey = "emulator/file-loading-delay", CpuKey = "cpu/cpu-opt", NgsKey = "audio/enable-ngs";

        public Vita3kCompatFields()
        {
            Size = new Size(620, 250);

            var loading = new GroupBox { Text = "Loading", Location = new Point(0, 0), Size = new Size(620, 110) };
            loading.Controls.Add(new Label { Text = "File Loading Delay", AutoSize = true, Location = new Point(12, 22) });
            _delayOverride = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(190, 20) };
            _delay = new TrackBar { Minimum = 0, Maximum = 30, SmallChange = 1, LargeChange = 5, TickStyle = TickStyle.None, Location = new Point(8, 44), Width = 292 };
            _delayValue = new Label { AutoSize = true, Location = new Point(12, 84) };
            var help = new Label
            {
                AutoSize = false, Location = new Point(318, 44), Size = new Size(290, 60), ForeColor = SystemColors.GrayText,
                Text = "An artificial delay on file loading, required for some games that load files too quickly compared to real hardware (Silent Hill, for one).",
            };
            loading.Controls.AddRange(new Control[] { _delayOverride, _delay, _delayValue, help });
            loading.Layout += (_, _) => _delayValue.Top = _delay.Bottom + 4;

            var cpu = new GroupBox { Text = "CPU", Location = new Point(0, 118), Size = new Size(620, 60) };
            _cpuOpt = Box("Enable optimizations", new Point(12, 24));
            cpu.Controls.Add(_cpuOpt);
            new ToolTip().SetToolTip(_cpuOpt, "Dynarmic's JIT optimizations. Faster; some games crash with them.");

            var audio = new GroupBox { Text = "Audio", Location = new Point(0, 186), Size = new Size(620, 60) };
            _ngs = Box("Enable NGS support", new Point(12, 24));
            audio.Controls.Add(_ngs);
            new ToolTip().SetToolTip(_ngs, "The advanced audio library NGS. Some games crash, or have no sound, with it.");

            Controls.AddRange(new Control[] { loading, cpu, audio });

            EventHandler refresh = (_, _) => { if (!_showing) Refresh_(); };
            _cpuOpt.CheckStateChanged += refresh;
            _ngs.CheckStateChanged += refresh;
            _delay.ValueChanged += refresh;
            _delayOverride.CheckedChanged += (_, _) =>
            {
                if (_showing) return;
                if (!_delayOverride.Checked) _delay.Value = Ticks(Default(DelayKey));
                Refresh_();
            };
        }

        private static CheckBox Box(string text, Point at) => new CheckBox { Text = text, Tag = text, AutoSize = true, Location = at, ThreeState = true };

        private string Default(string key) => _defaults.TryGetValue(key, out var v) ? v : null;

        private static int Ticks(string v) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(0, Math.Min(30, n)) : 0;

        /// <summary>The defaults (section/attribute) and the game's own over them.</summary>
        public void ShowValues(Dictionary<string, string> defaults, Dictionary<string, string> own)
        {
            _defaults = defaults ?? new Dictionary<string, string>(StringComparer.Ordinal);
            own ??= new Dictionary<string, string>(StringComparer.Ordinal);
            string Own(string k) => own.TryGetValue(k, out var v) ? v : null;
            _showing = true;
            SetBox(_cpuOpt, Own(CpuKey));
            SetBox(_ngs, Own(NgsKey));
            _delayOverride.Checked = Own(DelayKey) != null;
            _delay.Value = Ticks(Own(DelayKey) ?? Default(DelayKey));
            _showing = false;
            Refresh_();
        }

        private static void SetBox(CheckBox box, string v)
            => box.CheckState = v == null ? CheckState.Indeterminate : string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ? CheckState.Checked : CheckState.Unchecked;

        private static string OfBox(CheckBox box) => box.CheckState == CheckState.Indeterminate ? null : box.Checked ? "true" : "false";

        /// <summary>Only what is not on default, as section/attribute -> value.</summary>
        public Dictionary<string, string> Read()
        {
            var v = new Dictionary<string, string>(StringComparer.Ordinal);
            if (OfBox(_cpuOpt) is string c) v[CpuKey] = c;
            if (OfBox(_ngs) is string n) v[NgsKey] = n;
            if (_delayOverride.Checked) v[DelayKey] = _delay.Value.ToString(CultureInfo.InvariantCulture);
            return v;
        }

        public void SetEditable(bool value) { _enabled = value; Refresh_(); }

        private void Refresh_()
        {
            foreach (var (cb, key) in new[] { (_cpuOpt, CpuKey), (_ngs, NgsKey) })
            {
                var d = Default(key);
                cb.Text = cb.CheckState != CheckState.Indeterminate ? (string)cb.Tag
                        : (string)cb.Tag + "   (default: " + (string.IsNullOrEmpty(d) ? "not set" : string.Equals(d, "true", StringComparison.OrdinalIgnoreCase) ? "on" : string.Equals(d, "false", StringComparison.OrdinalIgnoreCase) ? "off" : d) + ")";
                cb.Enabled = _enabled;
            }
            bool isDefault = !_delayOverride.Checked;
            _delayValue.Text = isDefault && !int.TryParse(Default(DelayKey), out _) ? "default: not set"
                             : _delay.Value + (_delay.Value == 0 ? " (no delay)" : "") + (isDefault ? "   - default" : "");
            _delayOverride.Enabled = _enabled;
            _delay.Enabled = _enabled && _delayOverride.Checked;
        }

        internal static string Key(Dictionary<string, string> v)
            => v == null ? "" : string.Join(";", v.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
    }
}
