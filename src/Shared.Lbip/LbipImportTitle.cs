// THE IMPORT'S ONE RULE FOR A GAME'S NAME (Mehdi, 03/10) - Cxbx, Xenia, Vita3K and PPSSPP alike. A name LaunchBox's database
// knows is the one that gets the game its metadata; a title read in the game (a PARAM.SFO's in capitals or in Japanese, a
// "PlayStation Vita" suffix, a compatibility list's spelling) can lose that match where the file's name had it. So:
//
//   1. the ORIGINAL - the line's title in the wizard, or its file's name - is in the database on the platform (its name,
//      else an alternate title, compared as the database compares - LbipMetadataDb.GamesNamed): KEPT, nothing renamed.
//   2. else the first of the CANDIDATES, in the caller's order, that the database knows: the database's spelling of it
//      when it names one game, else the candidate.
//        Cxbx   the compatibility site's name for its serial, then its certificate's
//        Xenia  the compatibility list's name, then its XEX's
//        Vita3K / PPSSPP   its PARAM.SFO's TITLE, cleaned
//   3. none known: the original kept - unless it is no name at all (a bare serial like PCSB00400 or 4D5307E6, a long
//      run of letters and digits like a store's file name): then the first candidate.
// The database unreadable is "none known": the original stays, not a guess.

#nullable disable

using System.Linq;
using System.Text.RegularExpressions;

namespace LbIntegrations.Lbip
{
    internal static class LbipImportTitle
    {
        /// <summary>The name to give the game, or null to keep the one it has - and why, for the log.</summary>
        public static string Choose(string platform, string current, string fileName, out string why, params string[] candidates)
        {
            foreach (var o in new[] { current, fileName })
                if (Known(o, platform, out var w, out _)) { why = "\"" + o + "\" is in LaunchBox's database (" + w + ") - kept"; return null; }

            foreach (var c in candidates)
                if (!string.IsNullOrWhiteSpace(c) && Known(c, platform, out var w, out var names))
                {
                    // The database's own spelling when it names one game: "Grand Theft Auto: Liberty City Stories", not the
                    // PARAM.SFO's GRAND THEFT AUTO LIBERTY CITY STORIES.
                    var t = names.Distinct().Count() == 1 ? names[0] : c;
                    why = "\"" + c + "\" is in LaunchBox's database (" + w + ")" + (t != c ? ", as \"" + t + "\"" : "");
                    return string.Equals(t, current, System.StringComparison.Ordinal) ? null : t;
                }

            var first = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            if (first != null && !IsAName(string.IsNullOrWhiteSpace(current) ? fileName : current))
            {
                why = "no name of it is in LaunchBox's database, and \"" + (current ?? fileName) + "\" is not a title";
                return string.Equals(first, current, System.StringComparison.Ordinal) ? null : first;
            }
            why = "no name of it is in LaunchBox's database - the original kept";
            return null;
        }

        private static bool Known(string name, string platform, out string why, out System.Collections.Generic.List<string> names)
        {
            why = null;
            names = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            names = LbipMetadataDb.GamesNamed(name, platform, out why);
            return names != null && names.Count > 0;
        }

        /// <summary>Is this a title rather than a serial or a store file's code.</summary>
        internal static bool IsAName(string s)
        {
            var v = LbipTitleNormalizer.PerformSanitize(s ?? "").Trim();
            if (v.Length == 0) return false;
            if (v.Contains(' ')) return true;
            if (Regex.IsMatch(v, "^[A-Z]{4}[0-9]{5}$")) return false;          // PSP / Vita / PS3 serials: PCSB00400, ULUS10041
            if (Regex.IsMatch(v, "^[0-9A-F]{8}$")) return false;               // an Xbox title id: 4D5307E6
            if (v.Length >= 16) return false;                                   // a store's code: XYBSXPQLZYNOMAYKOLCGCW...
            return true;
        }
    }
}
