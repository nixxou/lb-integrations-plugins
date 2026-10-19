// A game's own options for Kronos - written in kronos.ini for its session only (KronosSession), put back after it. Ymir's
// model (YmirGameOptions): "As set in Kronos" (the default) writes nothing; a value Kronos wrote that this table does not list is
// kept as it is ("raw:" + the ini value), written back as it is, shown "(set in Kronos)".
//
// KRONOS HAS NO PER-GAME SETTINGS and its command line sets none of these (docs\kronos.md §2, §3): each option is a key or two of
// kronos.ini, written before the game and put back once Kronos has quit. Keys and values are 2.7.5's, read where Kronos reads
// them (port\qt\YabauseThread.cpp reloadSettings, at each start) and as its settings window writes them (ui\UISettings.cpp):
//   Picture     internal resolution (Video\resolution_mode: 1 = 1x, the default, 8 = 2x, 128 = 3x, 32 = 4x, 256 = 6x, 64 = the
//               window's), aspect (Video\AspectRatio 0-3), filter (Video\filter_type), upscaling (Video\upscale_type), meshes
//               (Video\MeshMode: 1 = drawn as true transparency), banding (Video\BandingMode: 1 = smoothed Gouraud shading)
//   Emulation   SH-2 cache (General\SH2Cache), the console's language (General\SystemLanguageID "0"-"5": the BIOS's own setting,
//               which many games follow for their text)
//   Cartridge   an expansion cartridge (Cartridge\Type: 0 none, 6 = 8 Mbit DRAM, 7 = 32 Mbit DRAM, 9 = 16 Mbit ROM + Cartridge\Path,
//               a file of <kronos>\cart). Forced, the game's backup cartridge (KronosCart) is not plugged. Saturn games only.
//   ST-V        the board's region (STV\Region E / U / J / T: the BIOS of that region in the set's zips is the one used - the
//               game's texts and warnings follow it). ST-V games only.
// Not here: the video core (Kronos forces its compute-shader core whatever the file says), the region (a Saturn's comes from the
// disc; STV\Region is ST-V's), the SH-2 interpreter (8 is the only one this build starts with).
//
// CHANGED IN KRONOS DURING THE GAME (its settings window, Ctrl+S), an option becomes the game's own (KronosSession.Recover).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.Kronos
{
    internal sealed class KronosGameOption
    {
        public string Key, Label, Hint, Group;
        /// <summary>Which games it is for: null every game, true the ST-V games only (KronosStv), false the Saturn's only.</summary>
        public bool? Stv;
        /// <summary>The ini keys it writes ("Video\VideoCore"), and whether each is a path.</summary>
        public (string Key, bool IsPath)[] Keys;
        /// <summary>The values offered (stored value, text), for the Kronos at that exe.</summary>
        public Func<string, List<(string Value, string Text)>> Choices;
        /// <summary>A value's TEXT for each key, in Keys' order; null: that key is not written.</summary>
        public Func<string, string[]> Write;
        /// <summary>The value the file says (a getter of text by key); null when nothing can be said.</summary>
        public Func<Func<string, string>, string> Read;
    }

    internal static class KronosGameOptions
    {
        public const string Raw = "raw:";
        public static readonly string[] Groups = { "Picture", "Emulation", "Sound and cartridge" };

        private static KronosGameOption Simple(string group, string key, string label, string iniKey, string hint, params (string Value, string Text)[] values)
            => new KronosGameOption
            {
                Group = group, Key = key, Label = label, Hint = hint, Keys = new[] { (iniKey, false) },
                Choices = _ => values.Select(v => (v.Value, v.Text)).ToList(),
                Write = v => new[] { v.StartsWith(Raw) ? v.Substring(Raw.Length) : v },
                Read = get =>
                {
                    var now = get(iniKey);
                    if (now == null) return null;
                    var known = values.FirstOrDefault(v => string.Equals(v.Value, now.Trim(), StringComparison.OrdinalIgnoreCase)).Value;
                    return known ?? Raw + now.Trim();
                },
            };

        private static (string, string)[] OnOff(string on = "On", string off = "Off") => new[] { ("true", on), ("false", off) };

        /// <summary>Cartridge\Type for the expansions offered (the backup cartridges, 2-5, are the plugin's - KronosCart).</summary>
        private const string CartNone = "0", CartDram8 = "6", CartDram32 = "7", CartRom16 = "9";

        public static readonly KronosGameOption[] All =
        {
            // ── picture ──
            Simple("Picture", "Resolution", "Internal resolution", @"Video\resolution_mode",
                "The resolution the 3D is drawn at: higher is sharper, and asks more of the graphics card. Native follows the window's size.",
                ("1", "1x (the Saturn's)"), ("8", "2x"), ("128", "3x"), ("32", "4x"), ("256", "6x"), ("64", "Native (the window's)")),
            Simple("Picture", "Aspect", "Aspect ratio", @"Video\AspectRatio",
                "The picture's shape: the console's, stretched to the screen, or in whole steps (pixel perfect).",
                ("0", "The console's"), ("1", "Stretched to the screen"), ("2", "Pixel perfect, full screen"), ("3", "Pixel perfect")),
            Simple("Picture", "Filter", "Filter", @"Video\filter_type",
                "Applied to the finished picture: smoothing, scanlines, a CRT look... Deinterlacing for the games whose high-resolution "
                + "screens flicker.",
                ("0", "None"), ("1", "Bilinear"), ("2", "Bicubic"), ("3", "Deinterlacing (adaptive)"), ("5", "Deinterlacing (bob)"),
                ("6", "Scanlines"), ("7", "Lanczos3"), ("8", "Sharp bilinear"), ("9", "FXAA"), ("10", "CRT (aperture grille)"), ("12", "Cell shading (toon)")),
            Simple("Picture", "Upscale", "Upscaling", @"Video\upscale_type",
                "Redraws the 2D (sprites, backgrounds) at a higher resolution: smoother edges, with the look of each method.",
                ("0", "None"), ("1", "HQ4x"), ("2", "4xBRZ"), ("3", "6xBRZ"), ("4", "Sharpen (adaptive)"), ("5", "FSR 1.0"), ("6", "Scale3x"), ("7", "Scale4x")),
            Simple("Picture", "Mesh", "Meshes", @"Video\MeshMode",
                "The Saturn draws shadows and windows as a checkerboard \"mesh\": as it is, or as true transparency.",
                ("0", "As the Saturn draws them"), ("1", "True transparency")),
            Simple("Picture", "Banding", "Shading", @"Video\BandingMode",
                "Gouraud shading as the Saturn draws it (visible bands), or smoothed.",
                ("0", "As the Saturn draws it"), ("1", "Smoothed")),

            // ── emulation ──
            Simple("Emulation", "SH2Cache", "SH-2 cache", @"General\SH2Cache",
                "Emulates the processors' cache: a few games need it (slower).", OnOff()),
            Simple("Emulation", "Language", "Console language", @"General\SystemLanguageID",
                "The language set in the console - many games show their text in it, when they have it.",
                ("0", "English"), ("1", "Deutsch"), ("2", "Français"), ("3", "Español"), ("4", "Italiano"), ("5", "日本語 (Japanese)")),
            ForStv(Simple("Emulation", "StvRegion", "Board region", @"STV\Region",
                "The ST-V board's region: the BIOS of that region is the one started (it must be in the set's zip or stvbios.zip) - the "
                + "game's texts, its warning screen and some of its rules follow it.",
                ("E", "Europe"), ("U", "USA"), ("J", "Japan"), ("T", "Asia (Taiwan)"))),

            // ── sound and cartridge ──
            new KronosGameOption
            {
                Group = "Sound and cartridge", Key = "Expansion", Label = "Expansion cartridge", Stv = false,
                Hint = "A cartridge in the console's slot for this game: the RAM expansion some games need (1 MB: KoF '95 era fighting games; "
                       + "4 MB: X-Men vs. Street Fighter, Marvel Super Heroes vs. Street Fighter...) or a ROM cartridge (KoF '95, Ultraman) "
                       + "- its file in Kronos's cart folder. Forced, the game's backup cartridge is not plugged.",
                Keys = new[] { (@"Cartridge\Type", false), (@"Cartridge\Path", true) },
                Choices = exe =>
                {
                    var list = new List<(string, string)> { ("none", "None"), ("dram8", "RAM expansion 1 MB (8 Mbit)"), ("dram32", "RAM expansion 4 MB (32 Mbit)") };
                    if (exe != null)
                    {
                        var dir = KronosLaunch.CartDirOf(exe);
                        if (Directory.Exists(dir))
                            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                                list.Add(("rom:" + Path.GetRelativePath(dir, f), "ROM cartridge: " + Path.GetFileName(f)));
                    }
                    return list;
                },
                Write = v => v switch
                {
                    "none" => new[] { CartNone, null },
                    "dram8" => new[] { CartDram8, null },
                    "dram32" => new[] { CartDram32, null },
                    _ when v.StartsWith("rom:") => new[] { CartRom16, v.Substring(4) },
                    _ when v.StartsWith(Raw) => new[] { v.Substring(Raw.Length), null },
                    _ => new string[] { null, null },
                },
                Read = get =>
                {
                    var type = get(@"Cartridge\Type")?.Trim();
                    if (type == null) return null;
                    switch (type)
                    {
                        case CartNone: return "none";
                        case CartDram8: return "dram8";
                        case CartDram32: return "dram32";
                        case "2": case "3": case "4": case "5": return null;   // a backup cartridge: the plugin's own (KronosCart), not this option
                        case CartRom16:
                            var p = get(@"Cartridge\Path");
                            return string.IsNullOrWhiteSpace(p) ? null : "rom:" + p;
                        default: return Raw + type;
                    }
                },
            },
        };

        private static KronosGameOption ForStv(KronosGameOption o) { o.Stv = true; return o; }

        /// <summary>Is the option for that game: an ST-V one (<paramref name="stv"/>) or a Saturn one.</summary>
        public static bool AppliesTo(KronosGameOption o, bool stv) => o.Stv == null || o.Stv == stv;

        public static KronosGameOption Option(string key) => All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));

        private static string IniKey(string key) => "opt." + key;

        /// <summary>The game's own value, or null: "As set in Kronos".</summary>
        public static string GameValue(string gameId, string key) => KronosSettings.GameRaw(gameId, IniKey(key));

        public static void SetGameValue(string gameId, string key, string value) => KronosSettings.SetGameRaw(gameId, IniKey(key), value);

        /// <summary>What a value is called in the lists - one this table does not list, as Kronos wrote it.</summary>
        public static string TextOf(KronosGameOption o, string value, string exe)
        {
            if (value == null) return null;
            var hit = o.Choices(exe).FirstOrDefault(c => c.Value == value);
            if (hit.Text != null) return hit.Text;
            if (value.StartsWith("rom:")) return "ROM cartridge: " + Path.GetFileName(value.Substring(4)) + (exe != null && !File.Exists(RomPath(exe, value)) ? " (not found)" : "");
            return (value.StartsWith(Raw) ? value.Substring(Raw.Length) : value) + " (set in Kronos)";
        }

        /// <summary>A ROM cartridge value's file: relative to Kronos's cart folder, or as Kronos wrote it.</summary>
        public static string RomPath(string exe, string value)
        {
            var p = value.Substring(4).Replace('/', '\\');
            return Path.IsPathFullyQualified(p) ? p : Path.Combine(KronosLaunch.CartDirOf(exe), p);
        }

        public static string Current(KronosGameOption o, KronosIniDoc doc) => o.Read(k => doc?.Get(k));

        /// <summary>For the session's end: the value the option has in kronos.ini now - a ROM cartridge in the cart folder said
        /// relative to it, as the window lists it.</summary>
        public static string FromIni(string key, KronosIniDoc doc, string exe)
        {
            if (!(Option(key) is KronosGameOption o)) return null;
            var v = Current(o, doc);
            if (v != null && v.StartsWith("rom:") && exe != null)
            {
                try
                {
                    var dir = Path.GetFullPath(KronosLaunch.CartDirOf(exe)).TrimEnd('\\') + "\\";
                    var full = Path.GetFullPath(v.Substring(4).Replace('/', '\\'));
                    if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) v = "rom:" + full.Substring(dir.Length);
                }
                catch { }
            }
            return v;
        }

        /// <summary>Kronos's ini keys are not versioned: every option is written (a key a newer Kronos dropped is ignored by it).</summary>
        public static bool InKronos(KronosGameOption o, KronosIniDoc doc) => true;

        /// <summary>The keys a game's options write for its launch, each with whether it is a path and its option; and why an
        /// option was left out (a ROM cartridge whose file is gone).</summary>
        public static (List<(string Key, string Value, bool IsPath, string Option)> Keys, List<string> LeftOut) KeysFor(string gameId, string exe, KronosIniDoc doc, bool stv = false)
        {
            var keys = new List<(string, string, bool, string)>();
            var leftOut = new List<string>();
            foreach (var o in All.Where(x => AppliesTo(x, stv)))
            {
                var value = GameValue(gameId, o.Key);
                if (value == null) continue;
                if (value.StartsWith("rom:") && exe != null && !File.Exists(RomPath(exe, value)))
                {
                    leftOut.Add(o.Label + " (" + RomPath(exe, value) + " is not there)");
                    continue;
                }
                var texts = o.Write(value);
                for (int i = 0; i < o.Keys.Length; i++)
                {
                    var (key, isPath) = o.Keys[i];
                    var text = texts[i];
                    if (text != null && isPath && exe != null) text = KronosIniDoc.PathText(RomPath(exe, "rom:" + text));
                    // A key the value leaves alone is held as it is all the same, so what Kronos writes there is put back.
                    text ??= doc?.Get(key);
                    if (text != null) keys.Add((key, text, isPath, o.Key));
                }
            }
            return (keys, leftOut);
        }
    }
}
