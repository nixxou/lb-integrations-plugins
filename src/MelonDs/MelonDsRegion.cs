// Which DSi a DSiWare title will run on, and which one a NAND came out of.
//
// A DSi NAND is region-locked. Its system menu is the thing that launches an installed title, and it
// is built for one region; a Japanese title installed into an American NAND is refused by the menu,
// with no explanation that reaches anyone. So the NAND has to be chosen, and choosing it means
// knowing two things - what the title wants, and what each NAND is.
//
// WHAT A NAND IS, IT SAYS ITSELF. 0:/sys/HWINFO_S.dat, byte 0x90, the value of melonDS's
// ConsoleRegion enum (DSi_NAND.h:210-218). The offset is measured rather than derived: the file
// announces EntrySize = 0x1C, and 128 (the RSA-SHA1 HMAC) + 4 + 4 + 28 is 164, which is exactly the
// static_assert on DSiSerialData. Checked against six dumps - AUS, CHN, EUR, JPN, KOR, USA - and the
// region read out of each matched its name in all six, with language masks that match melonDS's
// AmericaLanguages, EuropeLanguages and the rest.
//
// So a NAND is identified by its CONTENTS and never by its file name. The dumps in circulation carry
// a version in the name (DSi_Nand_USA_1.4.5.bin) and there is no reason to make a user rename
// anything, nor to be fooled when they do.
//
// WHAT A TITLE WANTS is in its header, three ways, and they agree - measured on a USA dump:
//
//   DSiRegionMask at 0x1B0   a BITMASK of console regions (NDS_Header.h:29-39)   read 0x02 = USA
//   GameCode at 0x0C         four ASCII letters, the fourth is the region        read "K99E"
//   title id low at 0x230    IS that game code                                   read 4b393945
//
// The mask is tried first because it is the only one of the three that can say "several regions" or
// "none in particular": a region-free title is 0xFFFFFFFF, and a title sold in two regions has two
// bits. A letter cannot express that without a table of special cases, and the table would be a
// guess where the mask is a statement.

using System;
using System.Collections.Generic;
using System.IO;

namespace LbIntegrations.MelonDs
{
    /// <summary>The six regions a DSi is built for. THE ORDER IS melonDS's ConsoleRegion
    /// (DSi_NAND.h:210-218), because that enum's value is the byte read straight out of a NAND -
    /// renumbering this would silently mis-identify every dump.</summary>
    internal enum DsiRegion
    {
        Japan = 0,
        Usa = 1,
        Europe = 2,
        Australia = 3,
        China = 4,
        Korea = 5,
    }

    internal static class MelonDsRegion
    {
        /// <summary>Where the console's own description sits inside its NAND.</summary>
        public const string HardwareInfoInNand = "0:/sys/HWINFO_S.dat";

        /// <summary>The region byte inside that file. See the header for why this offset is known
        /// rather than assumed.</summary>
        private const int RegionOffset = 0x90;

        /// <summary>DSiRegionMask, a bitmask of the regions whose consoles accept the title.</summary>
        private const int RegionMaskOffset = 0x1B0;

        /// <summary>The bit each region has in that mask (NDS_Header.h:29-39).</summary>
        private static readonly Dictionary<DsiRegion, uint> Bit = new Dictionary<DsiRegion, uint>
        {
            [DsiRegion.Japan] = 1u << 0,
            [DsiRegion.Usa] = 1u << 1,
            [DsiRegion.Europe] = 1u << 2,
            [DsiRegion.Australia] = 1u << 3,
            [DsiRegion.China] = 1u << 4,
            [DsiRegion.Korea] = 1u << 5,
        };

        /// <summary>The region letter of a game code, as dsibrew documents it.
        ///
        /// The single-country European codes all map to Europe, because that is the console a French
        /// or a German title runs on - the letter names the market, the NAND names the hardware, and
        /// only the second one matters here.</summary>
        private static readonly Dictionary<char, DsiRegion[]> ByLetter = new Dictionary<char, DsiRegion[]>
        {
            ['J'] = new[] { DsiRegion.Japan },
            ['E'] = new[] { DsiRegion.Usa },
            ['P'] = new[] { DsiRegion.Europe },
            ['U'] = new[] { DsiRegion.Australia },
            ['C'] = new[] { DsiRegion.China },
            ['K'] = new[] { DsiRegion.Korea },

            ['O'] = new[] { DsiRegion.Usa, DsiRegion.Europe, DsiRegion.Australia },
            ['T'] = new[] { DsiRegion.Usa, DsiRegion.Australia },
            ['V'] = new[] { DsiRegion.Europe, DsiRegion.Australia },

            ['L'] = new[] { DsiRegion.Usa },          // Canada
            ['B'] = new[] { DsiRegion.Usa },          // Brazil, on American hardware

            ['D'] = new[] { DsiRegion.Europe },       // Germany
            ['F'] = new[] { DsiRegion.Europe },       // France
            ['H'] = new[] { DsiRegion.Europe },       // Belgium / Netherlands
            ['I'] = new[] { DsiRegion.Europe },       // Italy
            ['M'] = new[] { DsiRegion.Europe },       // Sweden
            ['N'] = new[] { DsiRegion.Europe },       // Norway
            ['Q'] = new[] { DsiRegion.Europe },       // Denmark
            ['R'] = new[] { DsiRegion.Europe },       // Russia
            ['S'] = new[] { DsiRegion.Europe },       // Spain
            ['W'] = new[] { DsiRegion.Europe },       // multi-language European
            ['X'] = new[] { DsiRegion.Europe },
            ['Y'] = new[] { DsiRegion.Europe },
            ['Z'] = new[] { DsiRegion.Europe },
        };

        /// <summary>What a name in parentheses says, for the last resort. Longest first, so
        /// "(Europe, Australia)" is not mistaken for "(Europe)" and stopped there.</summary>
        private static readonly (string Text, DsiRegion[] Regions)[] ByName =
        {
            ("usa, europe, australia", new[] { DsiRegion.Usa, DsiRegion.Europe, DsiRegion.Australia }),
            ("europe, australia", new[] { DsiRegion.Europe, DsiRegion.Australia }),
            ("usa, australia", new[] { DsiRegion.Usa, DsiRegion.Australia }),
            ("world", new[] { DsiRegion.Japan, DsiRegion.Usa, DsiRegion.Europe,
                              DsiRegion.Australia, DsiRegion.China, DsiRegion.Korea }),
            ("australia", new[] { DsiRegion.Australia }),
            ("new zealand", new[] { DsiRegion.Australia }),
            ("europe", new[] { DsiRegion.Europe }),
            ("japan", new[] { DsiRegion.Japan }),
            ("korea", new[] { DsiRegion.Korea }),
            ("china", new[] { DsiRegion.China }),
            ("usa", new[] { DsiRegion.Usa }),
            ("france", new[] { DsiRegion.Europe }),
            ("germany", new[] { DsiRegion.Europe }),
            ("italy", new[] { DsiRegion.Europe }),
            ("spain", new[] { DsiRegion.Europe }),
            ("netherlands", new[] { DsiRegion.Europe }),
        };

        /// <summary>Every region whose console can run this title, best first, or an empty list when
        /// nothing in the ROM or its name says.
        ///
        /// THE ORDER OF THE THREE SOURCES IS THE ORDER OF THEIR AUTHORITY. The mask is what the DSi
        /// itself is handed; the letter is a convention about markets; the file name is whoever
        /// renamed the dump. Each is tried only when the one before it said nothing.</summary>
        public static List<DsiRegion> RegionsFor(NdsRom rom, string romPath, out string how)
        {
            how = null;

            var fromMask = FromMask(rom);
            if (fromMask.Count > 0)
            {
                how = "the ROM's region mask";
                // A single-region mask is already an answer; a wide one can still be narrowed by a
                // name, which is what "(Europe, Australia)" in a file name is for.
                if (fromMask.Count > 1)
                {
                    var narrowed = Narrow(fromMask, FromName(romPath));
                    if (narrowed.Count > 0 && narrowed.Count < fromMask.Count)
                    {
                        how = "the ROM's region mask, narrowed by its file name";
                        return narrowed;
                    }
                }
                return fromMask;
            }

            var fromLetter = FromLetter(rom);
            if (fromLetter.Count > 0)
            {
                how = "the region letter of its game code";
                if (fromLetter.Count > 1)
                {
                    var narrowed = Narrow(fromLetter, FromName(romPath));
                    if (narrowed.Count > 0 && narrowed.Count < fromLetter.Count)
                    {
                        how = "the region letter of its game code, narrowed by its file name";
                        return narrowed;
                    }
                }
                return fromLetter;
            }

            var fromName = FromName(romPath);
            if (fromName.Count > 0) { how = "its file name"; return fromName; }

            return new List<DsiRegion>();
        }

        /// <summary>DSiRegionMask, when it names at least one region. A mask of 0 means the field was
        /// never filled in, which is not the same as "no region" and is treated as "no answer".</summary>
        private static List<DsiRegion> FromMask(NdsRom rom)
        {
            var found = new List<DsiRegion>();
            if (rom == null || !rom.Known) return found;
            foreach (var pair in Bit)
                if ((rom.DSiRegionMask & pair.Value) != 0) found.Add(pair.Key);
            found.Sort();
            return found;
        }

        /// <summary>The fourth letter of the game code. The title id's low word IS the game code, and
        /// that word is read big-endian, so the letter is its LOW byte - 0x4b393945 is "K99E" and the
        /// region is 'E'.</summary>
        private static List<DsiRegion> FromLetter(NdsRom rom)
        {
            var found = new List<DsiRegion>();
            if (rom == null || !rom.Known || rom.DSiTitleIdLow == 0) return found;

            char letter = char.ToUpperInvariant((char)(rom.DSiTitleIdLow & 0xFF));
            if (letter == 'A')                      // region independent
            {
                foreach (var region in Bit.Keys) found.Add(region);
                found.Sort();
                return found;
            }
            if (ByLetter.TryGetValue(letter, out var regions)) found.AddRange(regions);
            return found;
        }

        /// <summary>What the parentheses in a file name say. Only the parenthesised parts are read: a
        /// game called "China Warrior" is not Chinese, and the brackets are what a dump's naming
        /// convention actually uses.</summary>
        private static List<DsiRegion> FromName(string romPath)
        {
            var found = new List<DsiRegion>();
            try
            {
                var name = Path.GetFileNameWithoutExtension(romPath ?? "");
                if (string.IsNullOrWhiteSpace(name)) return found;

                for (int at = name.IndexOf('('); at >= 0; at = name.IndexOf('(', at + 1))
                {
                    int close = name.IndexOf(')', at + 1);
                    if (close < 0) break;
                    var inside = name.Substring(at + 1, close - at - 1).ToLowerInvariant().Trim();

                    foreach (var entry in ByName)
                        if (inside == entry.Text)
                        {
                            foreach (var region in entry.Regions)
                                if (!found.Contains(region)) found.Add(region);
                            break;                  // one bracket says one thing
                        }
                }
                found.Sort();
            }
            catch { }
            return found;
        }

        /// <summary>The regions in both lists, or nothing when they disagree entirely - a name that
        /// contradicts the header is more likely to be a bad rename than a correction.</summary>
        private static List<DsiRegion> Narrow(List<DsiRegion> wide, List<DsiRegion> hint)
        {
            var kept = new List<DsiRegion>();
            foreach (var region in wide) if (hint.Contains(region)) kept.Add(region);
            return kept;
        }

        /// <summary>The region a NAND was dumped from, read out of its own HWINFO_S.dat, or null when
        /// the file is too short or holds a value the enum does not have.</summary>
        public static DsiRegion? RegionIn(byte[] hardwareInfo)
        {
            if (hardwareInfo == null || hardwareInfo.Length <= RegionOffset) return null;
            byte value = hardwareInfo[RegionOffset];
            return Enum.IsDefined(typeof(DsiRegion), (int)value) ? (DsiRegion?)(DsiRegion)value : null;
        }

        /// <summary>The offset of the region mask in a ROM header, for NdsHeader to read.</summary>
        public static int MaskOffset => RegionMaskOffset;

        /// <summary>A region in a sentence.</summary>
        public static string Name(DsiRegion region)
        {
            switch (region)
            {
                case DsiRegion.Japan: return "Japan";
                case DsiRegion.Usa: return "USA";
                case DsiRegion.Europe: return "Europe";
                case DsiRegion.Australia: return "Australia";
                case DsiRegion.China: return "China";
                case DsiRegion.Korea: return "Korea";
                default: return "?";
            }
        }

        public static string Names(IEnumerable<DsiRegion> regions)
        {
            var parts = new List<string>();
            foreach (var region in regions ?? new List<DsiRegion>()) parts.Add(Name(region));
            return parts.Count == 0 ? "(none)" : string.Join(" or ", parts);
        }

        /// <summary>The name this plugin would give a NAND file of this region, used when telling
        /// somebody what to go and find. Any name works - the region is read from inside.</summary>
        public static string SuggestedFileName(DsiRegion region)
            => "dsinand_" + Name(region).ToLowerInvariant() + ".bin";
    }
}
