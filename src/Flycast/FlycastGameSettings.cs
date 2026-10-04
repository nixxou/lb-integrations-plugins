// Flycast's SYSTEM settings - what the Nixx window's Flycast tab edits for every game (FlycastSystemPanel): Flycast's own
// words, keys and defaults (ui/settings_general.cpp, core/cfg/option.cpp), in the order of its settings.
//
// A GAME'S OWN SETTINGS ARE FLYCAST'S OWN (Mehdi, 04/10): "Make Game Config", in Flycast's settings while the game runs,
// a [<GAME ID>] section of emu.cfg. Until then this file was also the plugin's own per-game layer - an options window
// (Video, Rendering, System, Advanced), given on the command line as -config, with the keys a game's own section set
// taken out for the session (FlycastGameConfigSession): all gone. What is left of the session is putting back what one
// of before took out - Raw is its keys' shape. The values saved then, <install>\lbip-settings.tsv and
// lbip-settings-advanced.tsv, are no longer read.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LbIntegrations.Flycast
{
    internal static class FlycastGameSettings
    {
        internal enum Kind { Bool, Choice, Int }

        /// <summary>Which games a setting means something for.</summary>
        [Flags] internal enum Games { Dreamcast = 1, Arcade = 2, All = 3 }

        internal sealed class Setting
        {
            public string Section = "config", Key, Label, Tab;
            public Kind Kind;
            public (string Value, string Text)[] Choices;
            public int Min, Max;
            public string Builtin;          // Flycast's own default (core/cfg/option.cpp), for a key emu.cfg lacks
            public Games For = Games.All;
            public string Id => Section + ":" + Key;
        }

        /// <summary>A key of emu.cfg: [section] key = value - what a session of before took out of a game's section.</summary>
        internal sealed class Raw { public string Section, Key, Value; public string Id => Section + ":" + Key; }

        public const string SystemTab = "System";

        private static (string, string)[] Range(int from, params string[] texts)
            => texts.Select((t, i) => ((from + i).ToString(CultureInfo.InvariantCulture), t)).ToArray();

        private static Setting B(string key, string label, bool builtin, Games games = Games.All)
            => new Setting { Tab = SystemTab, Key = key, Label = label, Kind = Kind.Bool, Builtin = builtin ? "yes" : "no", For = games };

        private static Setting C(string key, string label, string builtin, (string, string)[] choices, Games games = Games.All)
            => new Setting { Tab = SystemTab, Key = key, Label = label, Kind = Kind.Choice, Builtin = builtin, Choices = choices, For = games };

        private static Setting I(string key, string label, int builtin, int min, int max, Games games = Games.All)
            => new Setting { Tab = SystemTab, Key = key, Label = label, Kind = Kind.Int, Builtin = builtin.ToString(CultureInfo.InvariantCulture), Min = min, Max = max, For = games };

        internal static readonly Setting[] Offered =
        {
            C("Dreamcast.Region", "Region", "1", Range(0, "Japan", "USA", "Europe (arcade: Export)", "Default (arcade: Korea)")),
            C("Dreamcast.Language", "Dreamcast Language", "1", Range(0, "Japanese", "English", "German", "French", "Spanish", "Italian", "Default"), Games.Dreamcast),
            C("Dreamcast.Broadcast", "Broadcast", "0", Range(0, "NTSC", "PAL", "PAL/M", "PAL/N", "Default"), Games.Dreamcast),
            C("Dreamcast.Cable", "Cable", "3", new[] { ("0", "VGA"), ("2", "RGB Component"), ("3", "TV Composite") }, Games.Dreamcast),
            I("Sh4Clock", "SH4 Clock - MHz", 200, 100, 300),
            I("aica.Volume", "Volume Level - %", 100, 0, 100),
            B("aica.DSPEnabled", "Enable DSP", false),
            B("UseReios", "HLE BIOS", false, Games.Dreamcast),
            B("FastGDRomLoad", "Fast GD-ROM Loading", false, Games.Dreamcast),
            B("Dreamcast.RamMod32MB", "Dreamcast 32MB RAM Mod", false, Games.Dreamcast),
            B("ForceFreePlay", "Naomi Free Play", true, Games.Arcade),
        };

        internal static bool IsYes(string v)
            => v != null && new[] { "yes", "true", "on", "1" }.Any(t => t.Equals(v.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>section:key -> what emu.cfg holds now, else Flycast's built-in value - the settings of every game.
        /// <paramref name="product"/>: a game's id - its own section's values over them, those keys in
        /// <paramref name="gameConfig"/> - or null.</summary>
        public static Dictionary<string, string> DefaultsOf(FlycastLayout layout, string product, out HashSet<string> gameConfig)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            gameConfig = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cfg = layout?.ConfigFile;
            foreach (var group in Offered.GroupBy(s => s.Section))
            {
                var held = string.IsNullOrEmpty(cfg) ? new Dictionary<string, string>() : FlycastIni.Read(cfg, group.Key, group.Select(s => s.Key).ToArray());
                foreach (var s in group) values[s.Id] = held.TryGetValue(s.Key, out var v) ? v : s.Builtin;
            }
            if (!string.IsNullOrWhiteSpace(product) && !string.IsNullOrEmpty(cfg))
            {
                var mine = FlycastIni.Read(cfg, product, Offered.Select(s => s.Section + "." + s.Key).ToArray());
                foreach (var s in Offered)
                    if (mine.TryGetValue(s.Section + "." + s.Key, out var v)) { values[s.Id] = v; gameConfig.Add(s.Id); }
            }
            return values;
        }
    }
}
