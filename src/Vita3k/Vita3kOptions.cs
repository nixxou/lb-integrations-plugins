// The options this plugin reads off a game's command line, and how the options window writes them
// back - OUR FLAGS ONLY. Everything else on the line stays exactly as it was written, character for
// character, quotes included: a word of ours is cut out by its position in the line, never by
// rebuilding the line from its words.
//
// WHERE A GAME'S OPTIONS LIVE. LaunchBox's rule (GetEffectiveCommandLine, and LiteBox's copy of it):
// the game's OWN CommandLine when it has one, and it REPLACES the emulator's; otherwise the emulator's
// line for the game's platform, otherwise the emulator's own. So a game that inherits and is given a
// flag gets a line of its own - the inherited one, plus the flag - and from then on a change to the
// emulator's line no longer reaches it. The window says so. And the way back: a game left with no
// flag of ours whose line is the inherited one, word for word, is given back an EMPTY line, so that
// it inherits again rather than keep a frozen copy.
//
// The same parser as the launch: Vita3kPlugin.IsOurFlag decides what is ours, and the values are read
// by the very functions PrepareEmulatorForLaunch reads them with.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kOptions
    {
        public bool UseVhdx;        // --use-vhdx
        public string VhdxDir;      // --use-vhdx=<dir>; null for the emulator's own vhdx folder
        public bool NoRamDisk;      // --no-ramdisk - also what a VHDX session falls back to
        public int? MarginMb;       // --ramdisk-margin=
        public int? Vita3kRamMb;    // --vita3k-ram=

        /// <summary>What a launch with this line would read.</summary>
        public static Vita3kOptions From(string line, string romPath)
        {
            var dir = Vita3kPlugin.VhdxFrom(line, romPath, out bool vhdx);
            return new Vita3kOptions
            {
                UseVhdx = vhdx,
                VhdxDir = vhdx ? dir : null,
                NoRamDisk = Vita3kPlugin.Carries(line, Vita3kPlugin.NoRamDiskFlag),
                MarginMb = Vita3kPlugin.MarginFrom(line, out _),
                Vita3kRamMb = Vita3kPlugin.Vita3kRamFrom(line, out _),
            };
        }

        /// <summary>The flags, in a fixed order, "=" spelling (Mehdi's), a word holding a space quoted.</summary>
        public List<string> Words()
        {
            var words = new List<string>();
            if (UseVhdx) words.Add(Quote(Vita3kPlugin.UseVhdxFlag + (string.IsNullOrWhiteSpace(VhdxDir) ? "" : "=" + VhdxDir.Trim())));
            if (NoRamDisk) words.Add(Vita3kPlugin.NoRamDiskFlag);
            if (MarginMb != null) words.Add(Vita3kPlugin.RamDiskMarginFlag + "=" + MarginMb.Value);
            if (Vita3kRamMb != null) words.Add(Vita3kPlugin.Vita3kRamFlag + "=" + Vita3kRamMb.Value);
            return words;
        }

        /// <summary>The same options, as one string - what the window groups games by.</summary>
        public string Key => string.Join(" ", Words());

        public bool None => Words().Count == 0;

        private static string Quote(string word) => word.IndexOfAny(new[] { ' ', '\t' }) >= 0 ? "\"" + word + "\"" : word;
    }

    internal static class Vita3kCommandLines
    {
        /// <summary>The line without a word of ours - every other word as it was written. A cut word
        /// takes the space before it along (the one after it when it opens the line), so no gap is
        /// left and no two words are glued together.</summary>
        public static string Strip(string line, string romPath)
        {
            line ??= "";
            var spans = Vita3kPlugin.TokenizeSpans(line);
            var words = spans.ConvertAll(s => s.Text);
            var cut = new List<(int Start, int End)>();
            for (int i = 0; i < spans.Count; i++)
            {
                if (!Vita3kPlugin.IsOurFlag(words, i, romPath, out bool withValue)) continue;
                int start = spans[i].Start, end = spans[withValue ? i + 1 : i].End;
                if (withValue) i++;
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

        /// <summary>The line with exactly these options: ours cut out, the new ones added at the end.</summary>
        public static string With(string line, Vita3kOptions options, string romPath)
        {
            var rest = Strip(line, romPath).TrimEnd();
            var ours = string.Join(" ", options.Words());
            if (ours.Length == 0) return rest;
            return rest.Length == 0 ? ours : rest + " " + ours;
        }

        /// <summary>The game's OWN line once these options are applied - "" when it should inherit.
        ///
        /// <paramref name="own"/> is the game's CommandLine, <paramref name="inherited"/> what it would
        /// run with none. A game that inherits and gets no flag stays inheriting; one left with no flag
        /// whose remaining line IS the inherited one goes back to inheriting.</summary>
        public static string NewOwnLine(string own, string inherited, Vita3kOptions options, string romPath)
        {
            bool inherits = string.IsNullOrWhiteSpace(own);
            var from = inherits ? inherited ?? "" : own;
            if (options.None)
            {
                if (inherits) return "";
                var rest = Strip(own, romPath);
                return Same(rest, inherited) ? "" : rest.Trim();
            }
            return With(from, options, romPath);
        }

        /// <summary>The same line, give or take the spaces between its words.</summary>
        public static bool Same(string a, string b) => Collapse(a) == Collapse(b);

        private static string Collapse(string s)
            => string.Join(" ", (s ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool IsBlank(char c) => c == ' ' || c == '\t';
    }
}
