// Vita3K's Graphics settings - the "Renderer" and "Image Quality" groups of its own window and its FPS
// Hack box (gui-qt settings_dialog.ui / settings_dialog.cpp) - as fields, for the Options window's
// "Graphics" tab (a game's own) and the pack's configuration window (config.yml's).
//
// FOR A GAME, EVERY SETTING CAN BE LEFT ON "DEFAULT" (Mehdi, 29/09) - what the game runs on without us:
// its custom config, else config.yml, else Vita3K's default (Vita3kGameConfig.DefaultsOf):
//   - a box has three states, the FILLED square being "default", and its label then says which:
//     "V-Sync   (default: on)";
//   - a list starts with "<Default : Nearest>";
//   - a slider has an "Override default" box beside it: unticked, it shows the default, greyed.
// Only what is NOT on default is kept for the game. In the configuration window there is no default -
// the values are config.yml's own - so the boxes have two states, no list has the first entry, and the
// sliders have no box.
//
// NOTHING IS GUESSED ABOUT A VALUE (Mehdi, 29/09). An EMPTY one is shown as not set: "<Default>" with no
// name, "(default: not set)", "<Not set>" in the configuration window, where it stays as it is unless
// changed. ONE EXCEPTION, the renderer (Mehdi, 29/09): not set - config.yml can say backend-renderer: "" -
// or not a name we know is VULKAN, as for Vita3K (app_init.cpp, set_backend_renderer: OpenGL when it says
// so, Vulkan otherwise), and Vita3K's own default; so one renderer's options are always the ones shown.
//
// WHAT VITA3K SHOWS, SHOWN THE SAME (settings_dialog.cpp, update_gpu_visibility): V-Sync is OpenGL's
// only, accuracy and asynchronous pipeline compilation Vulkan's only, FSR a Vulkan filter only - by the
// renderer the game will run on, the default one when the list is on default. The GPU and the memory
// mapping are left out: their choices come from asking Vulkan about the graphics card.
//
// A VALUE WE DO NOT KNOW (a filter a newer Vita3K has, say) is shown as it is, and kept.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using LbIntegrations.Lbip;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kGraphicsFields : Panel
    {
        private static readonly string[] Backends = { "OpenGL", "Vulkan" };
        private static readonly string[] Filters = { "Nearest", "Bilinear", "Bicubic", "FXAA", "FSR" };

        /// <summary>A list entry: what is written, and what is shown. A null value is "default".</summary>
        private sealed class Item
        {
            public string Value, Text;
            public override string ToString() => Text;
        }

        private readonly bool _perGame;
        private Dictionary<string, string> _defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _showing;
        private string _filterShown;
        private bool _enabled = true;

        private readonly ComboBox _backend, _filter, _accuracy;
        private readonly CheckBox _surfaceSync, _vsync, _async, _fpsHack, _texCache, _resOverride, _anisoOverride;
        private readonly TrackBar _resolution, _aniso;
        private readonly Label _resValue, _anisoValue;
        private readonly GroupBox _openGl, _vulkan;

        public Vita3kGraphicsFields(bool perGame)
        {
            _perGame = perGame;
            Size = new Size(620, 392);

            // ── Renderer
            var renderer = new GroupBox { Text = "Renderer", Location = new Point(0, 0), Size = new Size(620, 184) };
            renderer.Controls.Add(new Label { Text = "Backend Renderer", AutoSize = true, Location = new Point(12, 22) });
            _backend = Combo(new Point(12, 42), 286);
            renderer.Controls.Add(new Label { Text = "Screen Filter", AutoSize = true, Location = new Point(318, 22) });
            _filter = Combo(new Point(318, 42), 286);
            renderer.Controls.Add(_backend);
            renderer.Controls.Add(_filter);

            renderer.Controls.Add(new Label { Text = "Surface Sync", AutoSize = true, Location = new Point(12, 76) });
            _surfaceSync = Box("Disable Surface Sync", new Point(12, 96));
            renderer.Controls.Add(_surfaceSync);

            _openGl = new GroupBox { Text = "OpenGL Options", Location = new Point(312, 70), Size = new Size(296, 52) };
            _vsync = Box("V-Sync", new Point(10, 22));
            _openGl.Controls.Add(_vsync);
            _vulkan = new GroupBox { Text = "Vulkan Options", Location = new Point(312, 70), Size = new Size(296, 98) };
            _vulkan.Controls.Add(new Label { Text = "Accuracy", AutoSize = true, Location = new Point(10, 24) });
            _accuracy = Combo(new Point(90, 20), 196);
            _vulkan.Controls.Add(_accuracy);
            // Its "(default: ...)" would not fit on one line beside the name: two lines.
            _async = Box("Asynchronous Pipeline Compilation", new Point(10, 50));
            _async.AutoSize = false;
            _async.Size = new Size(280, 40);
            _vulkan.Controls.Add(_async);
            renderer.Controls.Add(_openGl);
            renderer.Controls.Add(_vulkan);

            // ── Image Quality
            var quality = new GroupBox { Text = "Image Quality", Location = new Point(0, 192), Size = new Size(620, 138) };
            quality.Controls.Add(new Label { Text = "Internal Resolution Upscaling", AutoSize = true, Location = new Point(12, 22) });
            _resOverride = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(190, 20), Visible = perGame };
            _resolution = new TrackBar { Minimum = 2, Maximum = 32, SmallChange = 1, LargeChange = 4, TickStyle = TickStyle.None, Location = new Point(8, 44), Width = 292 };
            _resValue = new Label { AutoSize = true, Location = new Point(12, 84) };
            quality.Controls.Add(new Label { Text = "Anisotropic Filtering", AutoSize = true, Location = new Point(318, 22) });
            _anisoOverride = new CheckBox { Text = "Override default", AutoSize = true, Location = new Point(490, 20), Visible = perGame };
            _aniso = new TrackBar { Minimum = 0, Maximum = 4, SmallChange = 1, LargeChange = 1, TickStyle = TickStyle.BottomRight, Location = new Point(314, 44), Width = 292 };
            _anisoValue = new Label { AutoSize = true, Location = new Point(318, 84) };
            quality.Controls.AddRange(new Control[] { _resOverride, _resolution, _resValue, _anisoOverride, _aniso, _anisoValue });
            // UNDER THE SLIDER AS IT IS DRAWN, not at a fixed place: a TrackBar is taller at 125 % and more,
            // and covered the value beneath it (measured 29/09 on Mehdi's screen).
            quality.Layout += (_, _) =>
            {
                _resValue.Top = _resolution.Bottom + 4;
                _anisoValue.Top = _aniso.Bottom + 4;
            };

            // ── Hacks
            var hacks = new GroupBox { Text = "Hacks and textures", Location = new Point(0, 338), Size = new Size(620, 52) };
            _fpsHack = Box("FPS Hack", new Point(12, 22));
            hacks.Controls.Add(_fpsHack);
            _texCache = Box("Texture Cache", new Point(318, 22));
            hacks.Controls.Add(_texCache);
            new ToolTip().SetToolTip(_texCache, "Faster, at the cost of video memory; turning it off mends graphical glitches in some games.");
            new ToolTip().SetToolTip(_fpsHack, "May double the framerate from 30 FPS to 60 FPS in some games, but can cause some games to run twice as fast.");

            Controls.AddRange(new Control[] { renderer, quality, hacks });

            EventHandler refresh = (_, _) => { if (!_showing) Refresh_(); };
            _backend.SelectedIndexChanged += refresh;
            foreach (var cb in new[] { _surfaceSync, _vsync, _async, _fpsHack, _texCache }) cb.CheckStateChanged += refresh;
            _resOverride.CheckedChanged += (_, _) => { if (_showing) return; if (!_resOverride.Checked) _resolution.Value = ResolutionTicks(Default("resolution-multiplier")); Refresh_(); };
            _anisoOverride.CheckedChanged += (_, _) => { if (_showing) return; if (!_anisoOverride.Checked) _aniso.Value = AnisoTicks(Default("anisotropic-filtering")); Refresh_(); };
            _resolution.ValueChanged += refresh;
            _aniso.ValueChanged += refresh;
        }

        private static ComboBox Combo(Point at, int width) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = at, Width = width };

        private CheckBox Box(string text, Point at)
            => new CheckBox { Text = text, Tag = text, AutoSize = true, Location = at, ThreeState = _perGame };

        private string Default(string name) => _defaults.TryGetValue(name, out var v) ? v : null;

        private static bool NotSet(string v) => string.IsNullOrEmpty(v);

        // ── values ───────────────────────────────────────────────────────────

        private static bool IsTrue(string v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>A renderer's name as one of ours when it is one (whatever its case), as it is otherwise.</summary>
        private static string Canonical(string v, string[] known)
            => v == null ? null : known.FirstOrDefault(k => string.Equals(k, v, StringComparison.OrdinalIgnoreCase)) ?? v;

        private static bool TryMultiplier(string v, out float m)
            => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out m) && m > 0;

        private static int ResolutionTicks(string v) => TryMultiplier(v, out var m) ? Math.Max(2, Math.Min(32, (int)Math.Round(m * 4))) : 4;

        private static int AnisoTicks(string v)
        {
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return 0;
            int t = 0;
            while (t < 4 && (1 << (t + 1)) <= n) t++;
            return t;
        }

        /// <summary>Fill a list: for a game "&lt;Default : x&gt;" first ("&lt;Default&gt;" when the default is
        /// not set), then the choices; a current value that is none of them - not set, or unknown - is one
        /// more entry, so it is shown and kept.</summary>
        private void FillCombo(ComboBox box, IEnumerable<string> choices, string defaultValue, string current)
        {
            box.Items.Clear();
            if (_perGame) box.Items.Add(new Item { Value = null, Text = NotSet(defaultValue) ? "<Default>" : "<Default : " + defaultValue + ">" });
            foreach (var c in choices) box.Items.Add(new Item { Value = c, Text = c });
            if (_perGame && current == null) { box.SelectedIndex = 0; return; }
            var at = box.Items.Cast<Item>().ToList().FindIndex(i => i.Value != null && i.Value == current);
            if (at < 0)
            {
                box.Items.Add(new Item { Value = current ?? "", Text = NotSet(current) ? "<Not set>" : current });
                at = box.Items.Count - 1;
            }
            box.SelectedIndex = at;
        }

        /// <summary>The chosen value of a list; null when it is on default.</summary>
        private static string Chosen(ComboBox box) => (box.SelectedItem as Item)?.Value;

        private static void SetBox(CheckBox box, string value, bool perGame)
            => box.CheckState = value == null && perGame ? CheckState.Indeterminate : IsTrue(value) ? CheckState.Checked : CheckState.Unchecked;

        private static string OfBox(CheckBox box)
            => box.CheckState == CheckState.Indeterminate ? null : box.Checked ? "true" : "false";

        /// <summary>Show the defaults (a game's: what it runs on without us; the configuration window's:
        /// config.yml's) and, for a game, its own values over them.</summary>
        public void ShowValues(Dictionary<string, string> defaults, Dictionary<string, string> own)
        {
            _defaults = defaults ?? new Dictionary<string, string>(StringComparer.Ordinal);
            own ??= new Dictionary<string, string>(StringComparer.Ordinal);
            string Own(string name) => _perGame ? (own.TryGetValue(name, out var v) ? v : null) : Default(name);
            _showing = true;

            FillCombo(_backend, Backends, Renderer(Default("backend-renderer")), Own("backend-renderer") == null ? null : Renderer(Own("backend-renderer")));
            string Accuracy(string v) => NotSet(v) ? v : IsTrue(v) ? "High" : "Standard";
            FillCombo(_accuracy, new[] { "Standard", "High" }, Accuracy(Default("high-accuracy")), Accuracy(Own("high-accuracy")));
            SetBox(_surfaceSync, Own("disable-surface-sync"), _perGame);
            SetBox(_vsync, Own("v-sync"), _perGame);
            SetBox(_async, Own("async-pipeline-compilation"), _perGame);
            SetBox(_fpsHack, Own("fps-hack"), _perGame);
            SetBox(_texCache, Own("texture-cache"), _perGame);

            var res = Own("resolution-multiplier");
            _resOverride.Checked = _perGame && res != null;
            _resolution.Value = ResolutionTicks(res ?? Default("resolution-multiplier"));
            var aniso = Own("anisotropic-filtering");
            _anisoOverride.Checked = _perGame && aniso != null;
            _aniso.Value = AnisoTicks(aniso ?? Default("anisotropic-filtering"));

            _filterShown = Own("screen-filter");
            _filter.Items.Clear();   // refilled by Refresh_ from _filterShown
            _showing = false;
            Refresh_();
        }

        /// <summary>For a game, only what is not on default; for the configuration window, every value
        /// shown - the caller writes only what changed.</summary>
        public Dictionary<string, string> Read()
        {
            var v = new Dictionary<string, string>(StringComparer.Ordinal);
            void Put(string name, string value) { if (value != null) v[name] = value; }
            var renderer = EffectiveBackend();
            Put("backend-renderer", Chosen(_backend));
            Put("screen-filter", Chosen(_filter));
            Put("disable-surface-sync", OfBox(_surfaceSync));
            // What the renderer does not use is not written: Vita3K would not show it either.
            if (renderer == "OpenGL") Put("v-sync", OfBox(_vsync));
            else
            {
                var acc = Chosen(_accuracy);
                Put("high-accuracy", acc == null ? null : acc == "High" ? "true" : acc == "Standard" ? "false" : acc);
                Put("async-pipeline-compilation", OfBox(_async));
            }
            if (!_perGame || _resOverride.Checked) Put("resolution-multiplier", (_resolution.Value / 4f).ToString("0.##", CultureInfo.InvariantCulture));
            if (!_perGame || _anisoOverride.Checked) Put("anisotropic-filtering", (1 << _aniso.Value).ToString(CultureInfo.InvariantCulture));
            Put("fps-hack", OfBox(_fpsHack));
            Put("texture-cache", OfBox(_texCache));
            return v;
        }

        /// <summary>A renderer as shown: ours by its name, Vulkan when not set; a name we do not know as it is.</summary>
        private static string Renderer(string v) => NotSet(v) ? "Vulkan" : Canonical(v, Backends);

        /// <summary>The renderer the game will run on: OpenGL when it says so, Vulkan otherwise - see the header.</summary>
        private string EffectiveBackend()
            => (Chosen(_backend) ?? Renderer(Default("backend-renderer"))) == "OpenGL" ? "OpenGL" : "Vulkan";

        public void SetEditable(bool value) { _enabled = value; Refresh_(); }

        /// <summary>Something shown changed - the window's bars follow it (OptionMarks).</summary>
        public event EventHandler Changed;

        /// <summary>Each field's bar: set here, from the game's own custom config (<paramref name="fromGame"/>,
        /// attribute names), or none. <paramref name="used"/> false: a text set by hand is in use, nothing marked.</summary>
        public void Mark(OptionMarks marks, HashSet<string> fromGame, bool used)
        {
            OptionLevel Of(bool here, string name)
                => !used ? OptionLevel.Unused : here ? OptionLevel.Here
                 : fromGame != null && fromGame.Contains(name) ? OptionLevel.GameConfig : OptionLevel.Emulator;
            marks.Set(_backend, Of(Chosen(_backend) != null, "backend-renderer"));
            marks.Set(_filter, Of(Chosen(_filter) != null, "screen-filter"));
            marks.Set(_accuracy, Of(Chosen(_accuracy) != null, "high-accuracy"));
            foreach (var cb in new[] { _surfaceSync, _vsync, _async, _fpsHack, _texCache }) marks.Set(cb, Of(OfBox(cb) != null, NameOf(cb)));
            marks.Set(_resolution, Of(_resOverride.Checked, "resolution-multiplier"));
            marks.Set(_aniso, Of(_anisoOverride.Checked, "anisotropic-filtering"));
        }

        private void Refresh_()
        {
            var renderer = EffectiveBackend();
            _openGl.Visible = renderer == "OpenGL";
            _vulkan.Visible = renderer == "Vulkan";

            // The filters of this renderer - FSR is Vulkan's only; a filter it does not have falls back to
            // the first, as in Vita3K.
            bool was = _showing;
            _showing = true;
            var current = _filter.Items.Count == 0 ? _filterShown : Chosen(_filter);
            if (_filter.Items.Count > 0 && _perGame && _filter.SelectedIndex == 0) current = null;
            var filters = renderer == "OpenGL" ? Filters.Where(f => f != "FSR") : Filters;
            if (renderer == "OpenGL" && current == "FSR") current = "Nearest";
            FillCombo(_filter, filters, Default("screen-filter"), current);
            _showing = was;

            foreach (var cb in new[] { _surfaceSync, _vsync, _async, _fpsHack, _texCache })
            {
                var label = (string)cb.Tag;
                var d = Default(NameOf(cb));
                cb.Text = cb.CheckState != CheckState.Indeterminate ? label
                        : label + "   (default: " + (NotSet(d) ? "not set" : IsTrue(d) ? "on" : string.Equals(d, "false", StringComparison.OrdinalIgnoreCase) ? "off" : d) + ")";
            }

            bool resDefault = _perGame && !_resOverride.Checked, anisoDefault = _perGame && !_anisoOverride.Checked;
            float m = _resolution.Value / 4f;
            _resValue.Text = resDefault && !TryMultiplier(Default("resolution-multiplier"), out _) ? "default: not set"
                           : (int)(960 * m) + "x" + (int)(544 * m) + " (" + m.ToString("0.##", CultureInfo.InvariantCulture) + "x)" + (resDefault ? "   - default" : "");
            _anisoValue.Text = anisoDefault && !int.TryParse(Default("anisotropic-filtering"), out _) ? "default: not set"
                             : (1 << _aniso.Value) + "x" + (anisoDefault ? "   - default" : "");

            foreach (var c in new Control[] { _backend, _filter, _accuracy, _surfaceSync, _vsync, _async, _fpsHack, _texCache, _resOverride, _anisoOverride })
                c.Enabled = _enabled;
            _resolution.Enabled = _enabled && (!_perGame || _resOverride.Checked);
            _aniso.Enabled = _enabled && (!_perGame || _anisoOverride.Checked);
            if (!_showing) Changed?.Invoke(this, EventArgs.Empty);
        }

        private string NameOf(CheckBox cb)
            => cb == _surfaceSync ? "disable-surface-sync" : cb == _vsync ? "v-sync" : cb == _async ? "async-pipeline-compilation"
             : cb == _texCache ? "texture-cache" : "fps-hack";

        /// <summary>A key a group of games can be told apart by.</summary>
        internal static string Key(Dictionary<string, string> v)
            => v == null ? "" : string.Join(";", v.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
    }
}
