// THE LABEL, AND HOW IT IS TOLD FROM THE NAME.
//
// A label is written into the emulator's title, in memory, between two invisible format characters:
//
//     "RetroArch" + U+2063 + "  [Playable]" + U+2064
//
// U+2063 (INVISIBLE SEPARATOR) and U+2064 (INVISIBLE PLUS) are format characters (Cf): WPF draws
// them as nothing, so the user sees "RetroArch  [Playable]". They are what makes the label
// removable with certainty: a title the user typed does not contain them, so stripping every
// U+2063 ... U+2064 span gives back the exact original, whatever text the label holds - and it is
// what SaveGuard does to anything LaunchBox is about to write to disk.

using System.Text;

namespace LbIntegrations.EmuLabels
{
    internal static class Label
    {
        public const char Open = '⁣';
        public const char Close = '⁤';

        public static bool Has(string s) => s != null && s.IndexOf(Open) >= 0;

        /// <summary>The title with every label removed. The same instance when there is none.</summary>
        public static string Strip(string s)
        {
            if (!Has(s)) return s;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                int open = s.IndexOf(Open, i);
                if (open < 0) { sb.Append(s, i, s.Length - i); break; }
                sb.Append(s, i, open - i);
                int close = s.IndexOf(Close, open + 1);
                // An Open with no Close is not ours to judge: drop the Open alone, keep the rest.
                i = close < 0 ? open + 1 : close + 1;
            }
            return sb.ToString();
        }

        public static string Apply(string original, string text)
            => string.IsNullOrEmpty(text) ? original : original + Open + text + Close;
    }
}
