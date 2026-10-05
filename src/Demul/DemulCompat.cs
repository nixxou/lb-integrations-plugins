// What the DEmul team says of each arcade romset: their arcade_compat.txt of 2018-02-01, embedded as it is (see
// vendor\demul\SOURCE.md - the site's compatibility page is gone, the 2025 build ships no list). Demul is no longer
// developed: the list is hard-coded, never fetched (Mehdi, 06/10).
//
// THE FILE: a MAME-style table, a section line per board ("| Naomi cartridge system |") then a row per set -
//     |romname | Pl | Rot | Control Type | Bios | Test | Boot | Game | Notes |
// Test: it enters test mode; Boot: it boots and draws; Game: it can be coined up and played without problems -
// "if the three are all yes and there aren't any notes, game is fully working" (the file's own words). Bios: the
// region its BIOS must be (Jap, Usa, Exp, Cust), when it matters.
//
// No Dreamcast list: the file is the arcade boards'. A set added to Demul after 2018 is not in it - said as such.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace LbIntegrations.Demul
{
    internal sealed class DemulCompatEntry
    {
        public string Name, Section, Players, Rotation, Controls, Bios, Notes;
        public bool Test, Boot, Game;

        /// <summary>One word for the pill.</summary>
        public string Verdict => Game ? (string.IsNullOrEmpty(Notes) ? "Works" : "Playable") : Boot ? "Boots" : Test ? "Test mode" : "Broken";

        /// <summary>What the verdict means, in a sentence.</summary>
        public string Meaning => Game ? (string.IsNullOrEmpty(Notes) ? "boots and plays, nothing noted" : "plays - with a note: " + Notes)
                               : Boot ? "boots and draws, but cannot be played" + (string.IsNullOrEmpty(Notes) ? "" : " - " + Notes)
                               : Test ? "reaches its test mode only" + (string.IsNullOrEmpty(Notes) ? "" : " - " + Notes)
                               : "does not boot" + (string.IsNullOrEmpty(Notes) ? "" : " - " + Notes);

        public Color Color => Game ? (string.IsNullOrEmpty(Notes) ? Color.FromArgb(0x32, 0x8C, 0x46) : Color.FromArgb(0x5A, 0x9A, 0x32))
                            : Boot ? Color.FromArgb(0xC8, 0x8C, 0x1E) : Test ? Color.FromArgb(0xC8, 0x64, 0x1E) : Color.FromArgb(0xB4, 0x3C, 0x3C);
    }

    internal static class DemulCompat
    {
        private static Dictionary<string, DemulCompatEntry> _all;

        /// <summary>The list's date, for the window.</summary>
        public const string Date = "2018-02-01";

        /// <summary>By set name, case-insensitive. Empty, never null, if the resource cannot be read.</summary>
        public static Dictionary<string, DemulCompatEntry> All
        {
            get
            {
                if (_all != null) return _all;
                var all = new Dictionary<string, DemulCompatEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("demul-compat.txt");
                    using var reader = new StreamReader(stream);
                    string line, section = null;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!line.StartsWith("|")) continue;
                        var f = line.Trim().Trim('|').Split('|');
                        if (f.Length == 1) { section = f[0].Trim(); continue; }          // "| Naomi cartridge system |"
                        if (f.Length < 9 || f[0].Trim() == "romname") continue;
                        var name = f[0].Trim();
                        if (name.Length == 0) continue;
                        all[name] = new DemulCompatEntry
                        {
                            Name = name, Section = section, Players = f[1].Trim(), Rotation = f[2].Trim(), Controls = f[3].Trim(), Bios = f[4].Trim(),
                            Test = Yes(f[5]), Boot = Yes(f[6]), Game = Yes(f[7]), Notes = f[8].Trim(),
                        };
                    }
                    Log.Info("compatibility list: " + all.Count + " sets");
                }
                catch (Exception ex) { Log.Warn("the compatibility list could not be read", ex); }
                return _all = all;
            }
        }

        private static bool Yes(string s) => s.Trim().StartsWith("Yes", StringComparison.OrdinalIgnoreCase);

        /// <summary>A game's entry, by its file's name (the set's name, as Demul goes by), or null.</summary>
        public static DemulCompatEntry Of(string romPath)
        {
            if (string.IsNullOrWhiteSpace(romPath)) return null;
            return All.TryGetValue(Path.GetFileNameWithoutExtension(romPath), out var e) ? e : null;
        }

        /// <summary>"H" / "V" as words.</summary>
        public static string RotationText(string r) => r == "V" ? "vertical" : r == "H" ? "horizontal" : r;

        public static string BiosText(string b) => b switch
        {
            "Jap" => "a Japanese BIOS",
            "Usa" => "a US BIOS",
            "Exp" => "an export BIOS",
            "Cust" => "a custom BIOS",
            _ => string.IsNullOrEmpty(b) ? null : b,
        };
    }
}
