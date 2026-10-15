// ZiNc EX's own per-game settings - zinc-games.cfg beside it - read and written for the game's window, and its global
// values read for "As set in ZiNc EX (...)".
//
// THE FILE (gui-c\gamecfg.c, read 15/10): "[game <ZiNc's game number>]" sections, one key=value per line, a key absent =
// "Same As Global"; ZiNc EX writes it whole from what it holds (gamecfg_save), and deletes it when no game has any. Its
// keys and their values:
//   renderer           a folder of renderers\ (Direct3D11, OpenGL, Direct3D)
//   rotate             -1 game default, 0 none, 1 90, 2 180, 3 270
//   fullscreen         0 window, 1 full screen          size     <w>x<h>, the full-screen resolution
//   bezel              a PNG of bezels\, or "-" for none
//   borderless         0 full screen (resolution changed), 1 borderless window   (the Direct3D 11 renderer's keys:)
//   aspect             0 stretch, 1 4:3, 2 16:9, 3 pixel perfect
//   scale              0 auto, 1..4                     xbrz     0 off, 1 all graphics, 2 2D objects only
//   texture_smoothing, fxaa, vsync                      0 / 1
//   dither_smoothing   0 off, 1 low, 2 medium, 3 high   overscan pixels: 0, 4, 8, 12, 16, 24, 32
//   slow_geometry, sound, sound_filter, surround, exciter, trainer      0 / 1
//   profile            a controls profile (profiles\<name>.cfg)
// ZiNc EX takes them at the game's start (settings.c zinc_args_ex), so a value written here is the game's next time -
// ZiNc EX's own Game Settings window shows it too. Written as ZiNc EX writes it: its header, the sections in the order
// they were, the keys in its order, a line it does not know kept as it was.
//
// THE GLOBAL VALUES: zinc-settings.cfg (renderer, rotate, use_sound, sound_filter, surround_lite, stereo_exciter,
// slow_geometry, trainer_enabled) and renderer.cfg (FullScreen, XSize/YSize, InternalScale, XBRZ, Borderless, KeepAspect,
// FXAA, TextureSmoothing, Dedither, VSync, Overscan, BezelImage), with ZiNc EX's defaults for what they do not hold
// (settings.c settings_defaults, renderer_defaults).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExGameCfg
    {
        public const string FileName = "zinc-games.cfg";
        private const string Header = "; ZiNc EX per-game settings (written by ZiNc EX; a missing line means: same as the main settings)";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>ZiNc EX's keys, in the order it writes them.</summary>
        public static readonly string[] Keys =
        {
            "renderer", "rotate", "fullscreen", "size", "scale", "xbrz", "borderless", "aspect", "fxaa", "texture_smoothing",
            "dither_smoothing", "vsync", "overscan", "sound", "sound_filter", "surround", "exciter", "slow_geometry", "profile", "bezel", "trainer",
        };

        private sealed class Section
        {
            public int Id;
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Other = new List<string>();
        }

        /// <summary>The game's own values (key -> value as written), empty when it has none.</summary>
        public static Dictionary<string, string> Of(string exeDir, int id)
        {
            var s = Load(exeDir).FirstOrDefault(x => x.Id == id);
            return s == null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                             : new Dictionary<string, string>(s.Values, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The games' values set (a null value taken out), every other game's and line kept. Throws on an I/O failure.</summary>
        public static void Write(string exeDir, IDictionary<int, IDictionary<string, string>> changes)
        {
            var sections = Load(exeDir);
            foreach (var change in changes)
            {
                var s = sections.FirstOrDefault(x => x.Id == change.Key);
                if (s == null) { s = new Section { Id = change.Key }; sections.Add(s); }
                foreach (var kv in change.Value)
                {
                    if (kv.Value == null) s.Values.Remove(kv.Key);
                    else s.Values[kv.Key] = kv.Value;
                }
            }
            var path = Path.Combine(exeDir, FileName);
            var kept = sections.Where(s => s.Values.Count > 0 || s.Other.Count > 0).ToList();
            if (kept.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            var b = new StringBuilder(Header + "\r\n");
            foreach (var s in kept)
            {
                b.Append("\r\n[game ").Append(s.Id.ToString(CultureInfo.InvariantCulture)).Append("]\r\n");
                foreach (var k in Keys) if (s.Values.TryGetValue(k, out var v)) b.Append(k).Append('=').Append(v).Append("\r\n");
                foreach (var kv in s.Values.Where(kv => !Keys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))) b.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                foreach (var line in s.Other) b.Append(line).Append("\r\n");
            }
            var temp = path + ".lbip-tmp";
            File.WriteAllText(temp, b.ToString(), Utf8);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }

        private static List<Section> Load(string exeDir)
        {
            var list = new List<Section>();
            var path = Path.Combine(exeDir ?? "", FileName);
            if (!File.Exists(path)) return list;
            Section cur = null;
            foreach (var raw in File.ReadAllLines(path, Utf8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    cur = null;
                    var inner = line.Trim('[', ']').Trim();
                    if (inner.StartsWith("game ", StringComparison.OrdinalIgnoreCase) && int.TryParse(inner.Substring(5).Trim(), out var id))
                    {
                        cur = list.FirstOrDefault(s => s.Id == id);
                        if (cur == null) list.Add(cur = new Section { Id = id });
                    }
                    continue;
                }
                if (cur == null) continue;
                var at = line.IndexOf('=');
                if (at <= 0) { cur.Other.Add(raw); continue; }
                cur.Values[line.Substring(0, at).Trim().ToLowerInvariant()] = line.Substring(at + 1).Trim();
            }
            return list;
        }

        // ── ZiNc EX's global values ──────────────────────────────────────────

        /// <summary>The value every game has in ZiNc EX for one of the per-game keys, in the per-game key's terms ("1", "0",
        /// "Direct3D11", "1920x1080"...). Never null: ZiNc EX's default when its files do not say.</summary>
        public static string Global(string exeDir, string key)
        {
            var set = ReadKv(Path.Combine(exeDir, ZincExConfig.FileName), lower: true);
            var ren = ReadKv(Path.Combine(exeDir, "renderer.cfg"), lower: false);
            string S(string k, string d) => set.TryGetValue(k, out var v) && v.Length > 0 ? v : d;
            string R(string k, string d) => ren.TryGetValue(k, out var v) && v.Length > 0 ? v : d;
            string Flag(string v) => v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
            switch (key)
            {
                case "renderer": return Canonical(S("renderer", "Direct3D11"));
                case "rotate": return S("rotate", "-1");
                case "fullscreen": return Flag(R("FullScreen", "1"));
                case "size":
                    var w = R("XSize", ""); var h = R("YSize", "");
                    return w.Length > 0 && h.Length > 0 ? w + "x" + h : System.Windows.Forms.Screen.PrimaryScreen?.Bounds is { } b ? b.Width + "x" + b.Height : "";
                case "scale": return R("InternalScale", "0");
                case "xbrz": return R("XBRZ", "2");
                case "borderless": return Flag(R("Borderless", "1"));
                case "aspect": return R("KeepAspect", "1");
                case "fxaa": return Flag(R("FXAA", "1"));
                case "texture_smoothing": return Flag(R("TextureSmoothing", "1"));
                case "dither_smoothing": return R("Dedither", "3");
                case "vsync": return Flag(R("VSync", "0"));
                case "overscan": return R("Overscan", "0");
                case "sound": return Flag(S("use_sound", "1"));
                case "sound_filter": return Flag(S("sound_filter", "1"));
                case "surround": return Flag(S("surround_lite", "0"));
                case "exciter": return Flag(S("stereo_exciter", "0"));
                case "slow_geometry": return Flag(S("slow_geometry", "0"));
                case "trainer": return Flag(S("trainer_enabled", "0"));
                case "profile": return "";
                case "bezel": return R("BezelImage", "");
                default: return "";
            }
        }

        /// <summary>The old renderer names ZiNc EX reads as the new ones (settings.c canonical_renderer).</summary>
        public static string Canonical(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "winogl": case "winsoft": case "software": return "OpenGL";
                case "wind3d": return "Direct3D";
                default: return name;
            }
        }

        /// <summary>The renderers ZiNc EX offers: the folders of renderers\ holding a renderer plugin.</summary>
        public static List<string> Renderers(string exeDir)
        {
            var list = new List<string>();
            try
            {
                var dir = Path.Combine(exeDir, "renderers");
                foreach (var name in new[] { "Direct3D11", "OpenGL", "Direct3D" }.Concat(Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(Path.GetFileName) : Enumerable.Empty<string>()))
                    if (!list.Contains(name, StringComparer.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(dir, name)) && Directory.GetFiles(Path.Combine(dir, name), "*.znc").Length > 0)
                        list.Add(name);
            }
            catch { }
            return list;
        }

        /// <summary>The controls profiles (profiles\&lt;name&gt;.cfg) and the bezels (bezels\*.png), by name.</summary>
        public static List<string> Profiles(string exeDir) => Names(Path.Combine(exeDir, "profiles"), "*.cfg", true);
        public static List<string> Bezels(string exeDir) => Names(Path.Combine(exeDir, "bezels"), "*.png", false);

        private static List<string> Names(string dir, string pattern, bool noExtension)
        {
            try
            {
                return Directory.Exists(dir)
                    ? Directory.GetFiles(dir, pattern).Select(f => noExtension ? Path.GetFileNameWithoutExtension(f) : Path.GetFileName(f)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>();
            }
            catch { return new List<string>(); }
        }

        private static Dictionary<string, string> ReadKv(string path, bool lower)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return d;
                foreach (var raw in File.ReadAllLines(path, Utf8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    var at = line.IndexOf('=');
                    if (at <= 0) continue;
                    var v = line.Substring(at + 1);
                    var semi = v.IndexOf(';');   // renderer.cfg's comments, after the value
                    if (semi >= 0) v = v.Substring(0, semi);
                    d[lower ? line.Substring(0, at).Trim().ToLowerInvariant() : line.Substring(0, at).Trim()] = v.Trim();
                }
            }
            catch { }
            return d;
        }
    }
}
