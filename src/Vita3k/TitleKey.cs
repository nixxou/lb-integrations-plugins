// A title reduced to the key two spellings of the same game share - "LittleBigPlanet™ PlayStation®Vita"
// and a folder called "LittleBigPlanet - PlayStation Vita" both give LITTLEBIGPLANETPLAYSTATIONVITA.
//
// NOT A NEW RULE: ExtendDB's Utility\Normalizer (PerformSanitize, then NormalizeCompareName on its
// result, the order SearchRom uses when it adds a game), so that a folder matches a game here exactly
// when it would there - the pack's one copy is Shared.Lbip\LbipTitleNormalizer.cs.
//
//   loose   brackets (USA) [PCSA00017] {...} dropped, - : & ! , / ? \ to spaces, ' . " removed,
//           Roman numerals II..VIII to digits, English articles dropped, upper-cased. Keeps ™ and ®.
//   strict  accents decomposed and dropped, only [A-Z0-9] kept - this is what removes ™ and ®.
//           VALID only when at least 5 characters long and no more than 20% of the non-blank
//           characters were dropped: a folder called "DLC" or "Vita" must not match anything.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.Vita3k
{
    internal static class TitleKey
    {
        /// <summary>The strict key of <paramref name="title"/>, or null when there is no valid one.</summary>
        public static string Of(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var key = NormalizeCompareName(PerformSanitize(title), out bool valid);
            return valid ? key : null;
        }

        // ExtendDB's Normalizer, to the letter - one copy for the pack, in Shared.Lbip.
        internal static string NormalizeCompareName(string input, out bool isValid) => LbIntegrations.Lbip.LbipTitleNormalizer.NormalizeCompareName(input, out isValid);

        internal static string PerformSanitize(string name) => LbIntegrations.Lbip.LbipTitleNormalizer.PerformSanitize(name);
    }
}
