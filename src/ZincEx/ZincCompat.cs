// What a ZiNc game is known to do badly - shown in the game's window (ZincExGameMenu), as the Demul plugin shows the DEmul
// team's list.
//
// THERE IS NO COMPATIBILITY LIST OF ZINC, game by game (looked for 16/10: forum posts only, anecdotal). What there is, is ZiNc
// 1.1's own readme ("What's wrong with it?", "Troubleshooting", the release notes), which says it BY BOARD - so the notes
// here are its words, by the board of the game:
//   - "Sound is not accurate in System 11 games although the simulator does an excellent job in many cases."
//   - "Game speed is wrong in many System 11 games." (1.1: "Correct PSX root counter timing. This fixes play speed in many
//     System 11 and System 12 games such as Soul Edge, Tekken 3, Ehrgeiz, and Dancing Eyes")
//   - "Sound is not accurate in System 12 games." - and of Tekken 3 (1.0.1): "Yes, I know it sounds like hell."
//   - "The external sound board is not supported for the Raizing games, Cool Boarders, or the Taito FX-1b games."
//   - Psychic Force goes to its test menu the first time; Gallop Racer 3's ground breaks up without slow geometry.
// The board of a game: its BIOS set for the ZN boards (cpzn1 Capcom ZN-1...), the MAME driver's lists for Namco's System 11 /
// 12 (namcos11.cpp, namcos12.cpp) and Taito's FX-1A / FX-1B (taitofx1.cpp), which carry no BIOS set.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.ZincEx
{
    internal static class ZincCompat
    {
        private static readonly HashSet<string> NamcoS11 = Sets("tekken", "tekkena", "tekkenb", "tekken2", "tekken2a", "tekken2b", "souledge", "souledga",
                                                                "souledgb", "dunkmnia", "dunkmnic", "xevi3dg", "primglex", "danceyes", "starswep", "myangel3");
        private static readonly HashSet<string> NamcoS12 = Sets("tekken3", "fgtlayer", "ehrgeiz", "mrdrillr", "aquarush", "pacapp");
        private static readonly HashSet<string> TaitoFx1b = Sets("raystorm", "raystorj", "ftimpcta", "gdarius", "gdarius2");

        private static HashSet<string> Sets(params string[] s) => new HashSet<string>(s, StringComparer.OrdinalIgnoreCase);

        /// <summary>The board, in plain words.</summary>
        public static string Board(ZincGame g)
        {
            if (NamcoS11.Contains(g.Set)) return "Namco System 11";
            if (NamcoS12.Contains(g.Set)) return "Namco System 12";
            switch ((g.Bios ?? "").ToLowerInvariant())
            {
                case "cpzn1": return "Capcom ZN-1 (Sony ZN-1)";
                case "cpzn2": return "Capcom ZN-2 (Sony ZN-2)";
                case "taitofx1": return TaitoFx1b.Contains(g.Set) ? "Taito FX-1B" : "Taito FX-1A";
                case "psarc95": return "Raizing / Eighting PS Arcade 95";
                case "tps": return "Tecmo TPS";
                case "atluspsx": return "Atlus PSX";
                case "konamigv": return "Konami GV";
                default: return "Sony ZN";
            }
        }

        /// <summary>ZiNc's readme on this game or its board - empty when it says nothing.</summary>
        public static List<string> Notes(ZincGame g)
        {
            var notes = new List<string>();
            var set = g.Set.ToLowerInvariant();
            var parent = (g.Parent ?? "").ToLowerInvariant();
            if (NamcoS11.Contains(set)) notes.Add("Sound simulated, not exact; game speed wrong in many System 11 games (fixed in 1.1 for Soul Edge and Dancing Eyes).");
            if (NamcoS12.Contains(set)) notes.Add("Sound not exact on System 12" + (set == "tekken3" ? " - Tekken 3's is very poor (\"sounds like hell\", ZiNc's notes)." : "."));
            if (TaitoFx1b.Contains(set) || string.Equals(g.Bios, "psarc95", StringComparison.OrdinalIgnoreCase) || set == "cbaj")
                notes.Add("Its external sound board is not emulated: music and sound are missing or partial.");
            if (set.StartsWith("psyforc") || set == "psyfrcex" || parent == "psyforce")
                notes.Add("May start in its test menu the first time: Factory Defaults, Yes, Exit, wait for RESET, quit and start it again.");
            if (set == "glpracr3") notes.Add("Turn on Slow Geometry (Video tab): the ground breaks up without it.");
            return notes;
        }
    }
}
