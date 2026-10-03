// ExtendDB's Utility\Normalizer, to the letter - PerformSanitize and NormalizeCompareName - so that a name matches here
// exactly when it would there. LiteBox carries the same copy (Host\Web\Db\TitleNormalizer.cs). Change the original first,
// and this with it.
//
// MEASURED 03/10 ON LaunchBox.Metadata.db: its Games.CompareName and GameAlternateTitles.AltNameCompareValue ARE
// PerformSanitize's output - "Grand Theft Auto III" is GRAND THEFT AUTO 3, "Grand Theft Auto: The Trilogy" is GRAND THEFT
// AUTO TRILOGY, "Blinx 2: Battle of Time & Space" is BLINX 2 BATTLE OF TIME SPACE, accents kept (LIEBESGRÜSSE).
//
//   PerformSanitize       brackets (USA) [PCSA00017] {...} dropped, - : & ! , / ? \ to spaces, ' . " removed, Roman
//                         numerals II..VIII to digits, English articles dropped, upper-cased. Keeps accents, ™ and ®.
//   NormalizeCompareName  accents decomposed and dropped, only [A-Z0-9] kept. VALID only when at least 5 characters
//                         long and no more than 20% of the non-blank characters were dropped.

using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.Lbip
{
    internal static class LbipTitleNormalizer
    {
        internal static string NormalizeCompareName(string input, out bool isValid)
        {
            isValid = false;
            if (string.IsNullOrEmpty(input))
                return null;

            if (input.Trim().EndsWith('+') || input.Trim().EndsWith('#'))
                return null;

            string original = input;
            int totalCharsForRatio = original.Count(c => !char.IsWhiteSpace(c));

            string normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    if (char.IsLetterOrDigit(c))
                        sb.Append(char.ToUpperInvariant(c));
                }
            }

            string result = Regex.Replace(sb.ToString(), @"[^A-Z0-9]", "");
            int replacements = totalCharsForRatio - result.Length;

            isValid = result.Length >= 5 && ((double)replacements / totalCharsForRatio) <= 0.2;

            return string.IsNullOrEmpty(result) ? null : result;
        }

        internal static string PerformSanitize(string name)
        {
            string sanitized = name;

            sanitized = Regex.Replace(sanitized, @"(?<=\S)?\([^)]*\)(?=\S)?", match =>
            {
                char before = match.Index > 0 ? sanitized[match.Index - 1] : ' ';
                char after = match.Index + match.Length < sanitized.Length ? sanitized[match.Index + match.Length] : ' ';
                return (!char.IsWhiteSpace(before) && !char.IsWhiteSpace(after)) ? "" : " ";
            });

            sanitized = Regex.Replace(sanitized, @"(?<=\S)?\[[^\]]*\](?=\S)?", match =>
            {
                char before = match.Index > 0 ? sanitized[match.Index - 1] : ' ';
                char after = match.Index + match.Length < sanitized.Length ? sanitized[match.Index + match.Length] : ' ';
                return (!char.IsWhiteSpace(before) && !char.IsWhiteSpace(after)) ? "" : " ";
            });

            sanitized = Regex.Replace(sanitized, @"(?<=\S)?\{[^}]*\}(?=\S)?", match =>
            {
                char before = match.Index > 0 ? sanitized[match.Index - 1] : ' ';
                char after = match.Index + match.Length < sanitized.Length ? sanitized[match.Index + match.Length] : ' ';
                return (!char.IsWhiteSpace(before) && !char.IsWhiteSpace(after)) ? "" : " ";
            });

            var invalidChars = Path.GetInvalidFileNameChars()
                                   .Where(c => c != '*' && c != '`' && c != '|' && c != '>' && c != '<' && c != '~')
                                   .ToArray();

            char[] additionalBlacklistToSpace = new char[] { '-', ':', '&', '!', ',', '/', '\\', '?' };
            char[] additionalBlacklistToVoid = new char[] { '\'', '.', '"' };

            sanitized = new string(sanitized.Select(c =>
            {
                if (invalidChars.Contains(c) || additionalBlacklistToSpace.Contains(c))
                    return ' ';
                if (additionalBlacklistToVoid.Contains(c))
                    return '\0';
                return c;
            }).Where(c => c != '\0').ToArray());

            sanitized = Regex.Replace(sanitized, " {2,}", " ").Trim();

            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)II(?=\s|$)", "2");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)III(?=\s|$)", "3");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)IV(?=\s|$)", "4");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)VIII(?=\s|$)", "8");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)VII(?=\s|$)", "7");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)VI(?=\s|$)", "6");
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)V(?=\s|$)", "5");

            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)a(?=\s|$)", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)an(?=\s|$)", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)and(?=\s|$)", " ", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"(?<=^|\s)the(?=\s|$)", "", RegexOptions.IgnoreCase);

            sanitized = Regex.Replace(sanitized, " {2,}", " ").Trim();
            sanitized = sanitized.ToUpper();

            return sanitized;
        }
    }
}
