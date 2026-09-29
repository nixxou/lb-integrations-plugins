// The flags of OURS a game's command line can carry, and how they are read and cut out - the same rule
// for the launch (PrepareEmulatorForLaunch takes them off the line before melonDS sees it) and for the
// options window (which writes them), so the window edits exactly what a launch reads.
//
//   --no-ramdisk     a DSiWare title keeps its working NAND on the disk, not on a RAM disk
//
// Shaped like the Vita3K plugin's (Vita3kSaves / Vita3kOptions), and small on purpose: one flag today,
// more to come with the window's other tabs. A word of ours is cut out BY ITS POSITION in the line,
// never by rebuilding the line from its words - everything else stays exactly as it was written,
// quotes included.

using System;
using System.Collections.Generic;
using System.Text;

namespace LbIntegrations.MelonDs
{
    /// <summary>What a game's line asks of this plugin.</summary>
    internal sealed class MelonDsOptions
    {
        public bool NoRamDisk;      // --no-ramdisk

        public static MelonDsOptions From(string line) => new MelonDsOptions
        {
            NoRamDisk = MelonDsCommandLine.Carries(line, MelonDsCommandLine.NoRamDiskFlag),
        };

        /// <summary>The flags, in a fixed order.</summary>
        public List<string> Words()
        {
            var words = new List<string>();
            if (NoRamDisk) words.Add(MelonDsCommandLine.NoRamDiskFlag);
            return words;
        }

        public bool None => Words().Count == 0;
    }

    internal static class MelonDsCommandLine
    {
        internal const string NoRamDiskFlag = "--no-ramdisk";

        private static readonly string[] OurFlags = { NoRamDiskFlag };

        public static bool IsOurFlag(string word)
            => Array.Exists(OurFlags, f => string.Equals(f, word, StringComparison.OrdinalIgnoreCase));

        public static bool Carries(string line, string flag)
            => Tokens(line).Exists(t => string.Equals(t.Text, flag, StringComparison.OrdinalIgnoreCase));

        /// <summary>The line without a word of ours - every other word as it was written. A cut word takes
        /// the space before it along (the one after it when it opens the line), so no gap is left and no
        /// two words are glued together.</summary>
        public static string Strip(string line)
        {
            line ??= "";
            var cut = new List<(int Start, int End)>();
            foreach (var t in Tokens(line))
            {
                if (!IsOurFlag(t.Text)) continue;
                int start = t.Start, end = t.End;
                while (start > 0 && IsBlank(line[start - 1])) start--;
                if (start == 0) while (end < line.Length && IsBlank(line[end])) end++;
                cut.Add((start, end));
            }
            if (cut.Count == 0) return line;
            var kept = new StringBuilder();
            int at = 0;
            foreach (var (start, end) in cut)
            {
                if (start > at) kept.Append(line, at, start - at);
                at = Math.Max(at, end);
            }
            if (at < line.Length) kept.Append(line, at, line.Length - at);
            return kept.ToString();
        }

        /// <summary>The game's OWN line once these options are applied - "" when it should inherit. A game
        /// that inherits and gets no flag stays inheriting; one left with no flag whose remaining line IS
        /// the inherited one goes back to inheriting.</summary>
        public static string NewOwnLine(string own, string inherited, MelonDsOptions options)
        {
            bool inherits = string.IsNullOrWhiteSpace(own);
            if (options.None)
            {
                if (inherits) return "";
                var rest = Strip(own);
                return Same(rest, inherited) ? "" : rest.Trim();
            }
            var from = Strip(inherits ? inherited ?? "" : own).TrimEnd();
            var ours = string.Join(" ", options.Words());
            return from.Length == 0 ? ours : from + " " + ours;
        }

        private static bool Same(string a, string b) => Collapse(a) == Collapse(b);

        private static string Collapse(string s)
            => string.Join(" ", (s ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool IsBlank(char c) => c == ' ' || c == '\t';

        /// <summary>The words of a Windows command line and where each is - [Start, End), quotes included.
        /// Spaces outside quotes separate; quotes group and are dropped from the text.</summary>
        private static List<(string Text, int Start, int End)> Tokens(string line)
        {
            line ??= "";
            var tokens = new List<(string, int, int)>();
            var current = new StringBuilder();
            bool quoted = false, any = false;
            int start = -1;
            for (int k = 0; k < line.Length; k++)
            {
                var c = line[k];
                if (!quoted && IsBlank(c))
                {
                    if (any) { tokens.Add((current.ToString(), start, k)); current.Clear(); any = false; }
                    continue;
                }
                if (!any) { start = k; any = true; }
                if (c == '"') { quoted = !quoted; continue; }
                current.Append(c);
            }
            if (any) tokens.Add((current.ToString(), start, line.Length));
            return tokens;
        }
    }
}
