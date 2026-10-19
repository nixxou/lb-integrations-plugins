// A game's own options for Ymir (Mehdi 20/10) - written in Ymir.toml for its session only (YmirSession), put back after it.
//
// YMIR HAS NO COMMAND-LINE SETTING and no per-game settings (read 20/10): its Ymir.toml is one for every game. So each option
// here is a few keys of that file, written before the game and put back once Ymir has quit; "As set in Ymir" (the default)
// writes nothing - Ymir's own value, set in its menus, applies. Names and values are 0.4.1's (app\settings.cpp: each Parse and
// ToTOML), written as Ymir writes them:
//   Picture     Deinterlace, transparent meshes (Video.Enhancements), aspect ratio (Video.ForceAspectRatio + ForcedAspect: Ymir's own
//               buttons 4:3, 3:2, 16:10, 16:9), integer scaling, rotation (Normal / 90CW / 180 / 90CCW), hardware rendering
//   Emulation   SH-2 cache, SH-2 clock (25-500 %, 100 the console's), CD read speed (2-200, 2 the console's), region (AutoDetectRegion
//               off, PreferredRegionOrder with the region first, VideoStandard NTSC / PAL), BIOS (System.IPL Override + Path, a file of
//               roms\ipl - Ymir writes Path back relative to that folder)
//   Pads        what is plugged in port 1 / 2 (Input.PortN PeripheralType)
//   Sound       sample interpolation (Nearest / Linear)
//   Cartridge   an expansion cartridge forced (Type DRAM + Capacity 8 / 32 / 48 Mbit, or ROM + a file of roms\cart), Ymir's own
//               choice off (AutoLoadGameCarts) - "As set in Ymir" lets Ymir plug the one its database names
//
// CHANGED IN YMIR DURING THE GAME, an option becomes the game's own (YmirSession.Recover) - FromToml reads it back. A value this
// table does not list (a newer Ymir's) is kept as Ymir wrote it ("raw:" + the TOML), written back as it is, shown "(set in Ymir)".
// AN OPTION THIS YMIR DOES NOT HAVE (YmirLaunch.Knows: a newer ConfigVersion without one of its keys) is left out, said.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LbIntegrations.Ymir
{
    internal sealed class YmirGameOption
    {
        public string Key, Label, Hint, Group;
        /// <summary>The keys it writes: table, key, and for a path the folder Ymir writes it relative to ("ipl", "cart"; null: none).</summary>
        public (string Table, string Key, string Base)[] Keys;
        /// <summary>The values offered (stored value, text), for the Ymir at that exe.</summary>
        public Func<string, List<(string Value, string Text)>> Choices;
        /// <summary>A value's raw TOML for each key, in Keys' order; null: that key is not written.</summary>
        public Func<string, string[]> Write;
        /// <summary>The value the file says (a getter of raw TOML by table and key); null when nothing can be said.</summary>
        public Func<Func<string, string, string>, string> Read;
    }

    internal static class YmirGameOptions
    {
        public const string Raw = "raw:";
        public static readonly string[] Groups = { "Picture", "Emulation", "Pads", "Sound and cartridge" };

        private static YmirGameOption Simple(string group, string key, string label, string table, string tomlKey, string hint, params (string Raw, string Text)[] values)
            => new YmirGameOption
            {
                Group = group, Key = key, Label = label, Hint = hint, Keys = new[] { (table, tomlKey, (string)null) },
                Choices = _ => values.Select(v => (v.Raw, v.Text)).ToList(),
                Write = v => new[] { v.StartsWith(Raw) ? v.Substring(Raw.Length) : v },
                Read = get =>
                {
                    var now = get(table, tomlKey);
                    if (now == null) return null;
                    var known = values.FirstOrDefault(v => string.Equals(v.Raw, now.Trim(), StringComparison.OrdinalIgnoreCase)).Raw;
                    return known ?? Raw + now.Trim();
                },
            };

        private static (string, string)[] OnOff(string on = "On", string off = "Off") => new[] { ("true", on), ("false", off) };

        private static readonly string[] Regions = { "NorthAmerica", "Japan", "EuropePAL", "AsiaNTSC" };

        public static readonly YmirGameOption[] All =
        {
            // ── picture ──
            Simple("Picture", "Deinterlace", "Deinterlacing", "Video.Enhancements", "Deinterlace",
                "Smooths the games that draw their high-resolution screens interlaced (they flicker without it).", OnOff()),
            Simple("Picture", "TransparentMeshes", "Transparent meshes", "Video.Enhancements", "TransparentMeshes",
                "Draws the Saturn's checkerboard \"mesh\" shadows and windows as true transparency.", OnOff()),
            new YmirGameOption
            {
                Group = "Picture", Key = "Aspect", Label = "Aspect ratio",
                Hint = "The picture's shape: the console's own, or forced - 16:9 for the games made for wide screens (anamorphic).",
                Keys = new[] { ("Video", "ForceAspectRatio", (string)null), ("Video", "ForcedAspect", (string)null) },
                Choices = _ => new List<(string, string)> { ("auto", "The game's own"), ("'4:3'", "4:3"), ("'3:2'", "3:2"), ("'16:10'", "16:10"), ("'16:9'", "16:9") },
                Write = v => v == "auto" ? new[] { "false", null } : new[] { "true", v.StartsWith(Raw) ? v.Substring(Raw.Length) : v },
                Read = get =>
                {
                    var force = get("Video", "ForceAspectRatio");
                    if (force == null) return null;
                    if (force.Trim() != "true") return "auto";
                    var r = (get("Video", "ForcedAspect") ?? "").Trim();
                    return new[] { "'4:3'", "'3:2'", "'16:10'", "'16:9'" }.Contains(r) ? r : Raw + r;
                },
            },
            Simple("Picture", "IntegerScaling", "Integer scaling", "Video", "ForceIntegerScaling",
                "Scales the picture by whole steps only: sharp pixels, a border around.", OnOff()),
            Simple("Picture", "Rotation", "Rotation", "Video", "Rotation",
                "Turns the picture - for the vertical shoot'em ups, on a screen turned on its side (TATE).",
                ("'Normal'", "Normal"), ("'90CW'", "90° clockwise"), ("'180'", "180°"), ("'90CCW'", "90° counter-clockwise")),
            Simple("Picture", "Hardware", "Rendering", "Video", "UseHardwareAcceleration",
                "Draws with the graphics card instead of the processor - newer in Ymir, faster on a weak processor.",
                ("false", "Software"), ("true", "Graphics card")),

            // ── emulation ──
            Simple("Emulation", "SH2Cache", "SH-2 cache", "System", "EmulateSH2Cache",
                "Emulates the processors' cache: a few games need it, slower. Ymir turns it on itself for those its database names.", OnOff()),
            Simple("Emulation", "Overclock", "CPU clock", "System", "SH2ClockFactor",
                "Runs the Saturn's processors faster: fewer slowdowns in the games that slow down on the console - some break.",
                ("100", "100 % (the console's)"), ("125", "125 %"), ("150", "150 %"), ("200", "200 %"), ("300", "300 %")),
            Simple("Emulation", "ReadSpeed", "CD read speed", "CDBlock", "ReadSpeed",
                "Shorter loadings - a few games time their scenes on the drive and go wrong faster.",
                ("2", "2x (the console's)"), ("4", "4x"), ("8", "8x"), ("16", "16x"), ("32", "32x")),
            new YmirGameOption
            {
                Group = "Emulation", Key = "Region", Label = "Region",
                Hint = "The console the game runs on: by default Ymir picks it from the disc. A Japanese console for a Japanese-only menu, a "
                       + "PAL one for a European game at 50 Hz... It needs that region's BIOS in roms\\ipl.",
                Keys = new[] { ("System", "AutoDetectRegion", (string)null), ("System", "PreferredRegionOrder", (string)null), ("System", "VideoStandard", (string)null) },
                Choices = _ => new List<(string, string)>
                {
                    ("auto", "From the disc"), ("NorthAmerica", "North America (NTSC)"), ("Japan", "Japan (NTSC)"), ("EuropePAL", "Europe (PAL)"), ("AsiaNTSC", "Asia (NTSC)"),
                },
                Write = v => v == "auto" ? new[] { "true", null, null }
                    : new[] { "false", YmirLaunch.ListText(new[] { v }.Concat(Regions.Where(r => r != v))), v == "EuropePAL" ? "'PAL'" : "'NTSC'" },
                Read = get =>
                {
                    var auto = get("System", "AutoDetectRegion");
                    if (auto == null) return null;
                    if (auto.Trim() == "true") return "auto";
                    var first = YmirLaunch.ListOf(get("System", "PreferredRegionOrder")).FirstOrDefault();
                    return Regions.Contains(first) ? first : null;
                },
            },
            new YmirGameOption
            {
                Group = "Emulation", Key = "Bios", Label = "BIOS",
                Hint = "By default Ymir picks the BIOS of the disc's region among those in roms\\ipl. Forced: this one, whatever the disc.",
                Keys = new[] { ("System.IPL", "Override", (string)null), ("System.IPL", "Path", "ipl") },
                Choices = exe =>
                {
                    var list = new List<(string, string)> { ("auto", "Ymir's choice (the disc's region)") };
                    if (exe == null) return list;
                    var dir = YmirLaunch.IplDirOf(exe);
                    foreach (var f in YmirBios.InFolder(exe))
                        list.Add((Path.GetRelativePath(dir, f.Path), Path.GetFileName(f.Path) + " - " + (f.Known?.Label ?? "a BIOS Ymir does not know")));
                    return list;
                },
                Write = v => v == "auto" ? new[] { "false", null } : new[] { "true", YmirToml.Literal(v.StartsWith(Raw) ? YmirToml.Text(v.Substring(Raw.Length)) : v) },
                Read = get =>
                {
                    var over = get("System.IPL", "Override");
                    if (over == null) return null;
                    if (over.Trim() != "true") return "auto";
                    var path = YmirToml.Text(get("System.IPL", "Path"));
                    return string.IsNullOrWhiteSpace(path) ? "auto" : path;
                },
            },

            // ── pads ──
            Port(1), Port(2),

            // ── sound and cartridge ──
            Simple("Sound and cartridge", "Interpolation", "Sound interpolation", "Audio", "InterpolationMode",
                "Linear: smoother sound. Nearest: the console's raw samples, crisper.", ("'Linear'", "Linear"), ("'Nearest'", "Nearest")),
            new YmirGameOption
            {
                Group = "Sound and cartridge", Key = "Expansion", Label = "Expansion cartridge",
                Hint = "By default Ymir plugs the cartridge its database names for the game (RAM expansion, KoF '95 / Ultraman ROM). Forced: for a "
                       + "game it does not know - a homebrew, a translation. Forced, the game's backup cartridge is not plugged.",
                Keys = new[] { ("Cartridge", "Type", (string)null), ("Cartridge", "AutoLoadGameCarts", (string)null), ("Cartridge.DRAM", "Capacity", (string)null), ("Cartridge.ROM", "ImagePath", "cart") },
                Choices = exe =>
                {
                    var list = new List<(string, string)>
                    {
                        ("auto", "Ymir's choice (its database)"), ("none", "None"), ("dram8", "RAM expansion 1 MB (8 Mbit)"), ("dram32", "RAM expansion 4 MB (32 Mbit)"), ("dram48", "RAM expansion 6 MB (48 Mbit)"),
                    };
                    if (exe != null)
                    {
                        var dir = YmirLaunch.CartDirOf(exe);
                        if (Directory.Exists(dir))
                            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                                list.Add(("rom:" + Path.GetRelativePath(dir, f), "ROM cartridge: " + Path.GetFileName(f)));
                    }
                    return list;
                },
                Write = v => v switch
                {
                    "auto" => new string[] { null, "true", null, null },
                    "none" => new[] { "'None'", "false", null, null },
                    "dram8" => new[] { "'DRAM'", "false", "'8Mbit'", null },
                    "dram32" => new[] { "'DRAM'", "false", "'32Mbit'", null },
                    "dram48" => new[] { "'DRAM'", "false", "'48Mbit'", null },
                    _ when v.StartsWith("rom:") => new[] { "'ROM'", "false", null, YmirToml.Literal(v.Substring(4)) },
                    _ => new string[] { null, null, null, null },
                },
                Read = get =>
                {
                    var auto = get("Cartridge", "AutoLoadGameCarts");
                    if (auto == null) return null;
                    if (auto.Trim() == "true") return "auto";
                    var type = YmirToml.Text(get("Cartridge", "Type"));
                    if (type == "None") return "none";
                    if (type == "DRAM") { var c = YmirToml.Text(get("Cartridge.DRAM", "Capacity")); return c == "8Mbit" ? "dram8" : c == "48Mbit" ? "dram48" : "dram32"; }
                    if (type == "ROM") { var p = YmirToml.Text(get("Cartridge.ROM", "ImagePath")); return string.IsNullOrWhiteSpace(p) ? null : "rom:" + p; }
                    return null;   // the backup cartridge: the plugin's own (YmirCart), not this option
                },
            },
        };

        private static YmirGameOption Port(int n) => Simple("Pads", "Port" + n, "Port " + n, "Input.Port" + n, "PeripheralType",
            "What is plugged in the console's port " + n + " for this game - its buttons are the ones set in Ymir for that device (F10 > Input).",
            ("'ControlPad'", "Control Pad"), ("'AnalogPad'", "3D Control Pad (analog - NiGHTS)"), ("'ArcadeRacer'", "Arcade Racer (wheel)"),
            ("'MissionStick'", "Mission Stick"), ("'VirtuaGun'", "Virtua Gun (light gun)"), ("'ShuttleMouse'", "Shuttle Mouse"), ("'None'", "Nothing"));

        public static YmirGameOption Option(string key) => All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));

        // ── the game's values ────────────────────────────────────────────────

        private static string IniKey(string key) => "opt." + key;

        /// <summary>The game's own value, or null: "As set in Ymir".</summary>
        public static string GameValue(string gameId, string key) => YmirSettings.GameRaw(gameId, IniKey(key));

        public static void SetGameValue(string gameId, string key, string value) => YmirSettings.SetGameRaw(gameId, IniKey(key), value);

        /// <summary>What a value is called in the lists - one this table does not list, as Ymir wrote it.</summary>
        public static string TextOf(YmirGameOption o, string value, string exe)
        {
            if (value == null) return null;
            var hit = o.Choices(exe).FirstOrDefault(c => c.Value == value);
            if (hit.Text != null) return hit.Text;
            var shown = value.StartsWith(Raw) ? YmirToml.Text(value.Substring(Raw.Length)) : value.StartsWith("rom:") ? "ROM cartridge: " + Path.GetFileName(value.Substring(4)) : value;
            return shown + " (set in Ymir)";
        }

        /// <summary>The option's value in a Ymir.toml now (Ymir's own, or what a game left there): see FromToml.</summary>
        public static string Current(YmirGameOption o, YmirTomlDoc doc) => o.Read((t, k) => doc?.Get(t, k));

        /// <summary>For the session's end: the value the option has in Ymir.toml now.</summary>
        public static string FromToml(string key, YmirTomlDoc doc, string exe) => Option(key) is YmirGameOption o ? Current(o, doc) : null;

        /// <summary>This Ymir has every key the option writes (YmirLaunch.Knows).</summary>
        public static bool InYmir(YmirGameOption o, YmirTomlDoc doc) => doc == null || o.Keys.All(k => YmirLaunch.Knows(doc, k.Table, k.Key));

        /// <summary>The keys a game's options write for its launch, each with its path base and option; and the options left out
        /// because this Ymir does not have them.</summary>
        public static (List<(string Table, string Key, string Value, string Base, string Option)> Keys, List<string> LeftOut) KeysFor(string gameId, string exe, YmirTomlDoc doc)
        {
            var keys = new List<(string, string, string, string, string)>();
            var leftOut = new List<string>();
            foreach (var o in All)
            {
                var value = GameValue(gameId, o.Key);
                if (value == null) continue;
                if (!InYmir(o, doc)) { leftOut.Add(o.Label); continue; }
                var raws = o.Write(value);
                for (int i = 0; i < o.Keys.Length; i++)
                {
                    var (table, key, pathBase) = o.Keys[i];
                    // A key the value leaves alone is held as it is all the same: what Ymir writes there during the game (the BIOS
                    // it picked, the video standard of the disc) is put back with the rest, never left to the other games.
                    if (raws[i] == null) raws[i] = doc?.Get(table, key);
                    if (raws[i] == null) continue;
                    keys.Add((table, key, raws[i], pathBase == "ipl" ? YmirLaunch.IplDirOf(exe, doc) : pathBase == "cart" ? YmirLaunch.CartDirOf(exe, doc) : null, o.Key));
                }
            }
            return (keys, leftOut);
        }
    }
}
