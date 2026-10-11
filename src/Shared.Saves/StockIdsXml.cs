// Lot 7: the ids of LaunchBox's own integrations given back their own form in a platform XML - the setup's uninstall,
// LaunchBox closed (docs\saves-lb14-routing.md §4b, lot 7; docs\saves-lot4.md).
//
// While the pack runs, RetroArch's, Dolphin's and PCSX2's cards carry our JSON ids (lot 4, StockIds), and LaunchBox writes
// them into its XML when it saves a game. Without the pack nothing decodes them: RetroArch matches its Saturn cards only on
// a saturn- id, PCSX2 decides by its pcsx2: prefix. So before the pack goes, every such id goes back: `native` as it was
// (StockIds.Reverse), a by-name id (no native id) a new GUID - one per game and id, so the rows of one card stay one card -
// and a MatchLineageId follows the card it names. Every other id (the pack's own plugins', LiteBox's, a GUID, a native id)
// is left exactly as it is: the pack's rows stay (Mehdi, 11/10), with their backups.
//
// Text in, text out: only the content of <SaveGroupId> and <MatchLineageId> inside <GameSave> rows changes, every other
// byte of the file stays where it was. Pure: no file, no LaunchBox - the setup reads and writes, the probe tests this.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LbIntegrations.Saves
{
    internal static class StockIdsXml
    {
        private static readonly Regex RowRx = new Regex(@"(?s)<GameSave>(.*?)</GameSave>", RegexOptions.CultureInvariant);
        private static readonly string[] Fields = { "SaveGroupId", "MatchLineageId" };

        /// <summary><paramref name="xml"/> with every translated id of a LaunchBox integration given back; <paramref name="rows"/>:
        /// the rows changed. <paramref name="freshGuid"/>: a new id for a card that had none.</summary>
        public static string Reverse(string xml, Func<string> freshGuid, out int rows)
        {
            rows = 0;
            // One new form per (game, id): a by-name card's rows - and the lineages naming it - all get the same GUID.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string Back(string game, string id)
            {
                var key = game + "\u0001" + id;
                if (!map.TryGetValue(key, out var back)) map[key] = back = StockIds.Reverse(id, freshGuid) ?? id;
                return back;
            }

            var sb = new StringBuilder(xml.Length);
            int at = 0;
            foreach (Match m in RowRx.Matches(xml))
            {
                var body = m.Groups[1];
                var text = body.Value;
                var game = Unescape(Field(text, "GameId")?.Value ?? "");
                var changed = text;
                foreach (var name in Fields)
                {
                    var f = Field(changed, name);
                    if (f == null) continue;
                    var id = Unescape(f.Value);
                    if (!Translated(id)) continue;
                    changed = changed.Substring(0, f.Index) + Escape(Back(game, id)) + changed.Substring(f.Index + f.Length);
                }
                if (ReferenceEquals(changed, text) || changed == text) continue;
                rows++;
                sb.Append(xml, at, body.Index - at).Append(changed);
                at = body.Index + body.Length;
            }
            sb.Append(xml, at, xml.Length - at);
            return sb.ToString();
        }

        /// <summary>The ids of a LaunchBox integration still in our form in <paramref name="xml"/> - none after Reverse.</summary>
        public static int Remaining(string xml)
        {
            int n = 0;
            foreach (Match m in RowRx.Matches(xml))
                foreach (var name in Fields)
                    if (Field(m.Groups[1].Value, name) is { } f && Translated(Unescape(f.Value))) n++;
            return n;
        }

        /// <summary>One of our JSON ids of a LaunchBox integration ("lb.retroarch"...): the ones that go back.</summary>
        private static bool Translated(string? id)
            => SaveId.TryRead(id, out var d) && d!.Emu != null && d.Emu.StartsWith(SaveOwner.LaunchBoxPrefix, StringComparison.Ordinal);

        /// <summary>The content of &lt;name&gt;...&lt;/name&gt; in a row (null when absent or written empty as &lt;name /&gt;).</summary>
        private static Group? Field(string row, string name)
        {
            var m = Regex.Match(row, "(?s)<" + name + ">(.*?)</" + name + ">", RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1] : null;
        }

        private static string Unescape(string s)
            => s.IndexOf('&') < 0 ? s : System.Net.WebUtility.HtmlDecode(s);

        /// <summary>As LaunchBox writes element text: &amp;, &lt; and &gt; escaped, quotes as they are.</summary>
        private static string Escape(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
