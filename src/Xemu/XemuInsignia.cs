// Insignia's own list of the games it serves (https://insignia.live/games, Mehdi, 06/10: "l'option de run with insignia seulement
// sur ceux-là ... on peut utiliser leur identifiant"): a game is played online only when its title id is in it.
//
// THE PAGE (read 06/10): one table row per game - its link games/<title id, 8 hex digits>, its name, its serial ("MS-100")
// above the same id in capitals, and five icons marked "visible" or "invisible": Live Aware, Matchmaking, Leaderboards, User
// Generated Content, Clans. No API: the HTML is read, row by row.
//
// KEPT in the plugin's data (insignia-games.tsv), asked again when older than a day, in the background (RefreshSoon); a list
// that comes back far shorter than the one kept (under 50 games, or under half) is refused - a page changed or cut, not
// Insignia dropping its games. Until the first download: the copy embedded in the plugin (the list of 06/10).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Xemu
{
    internal sealed class InsigniaGame
    {
        public string TitleId, Name, Serial;
        public bool LiveAware, Matchmaking, Leaderboards, Ugc, Clans;

        public string Features => string.Join(", ", new[] { LiveAware ? "Live Aware" : null, Matchmaking ? "matchmaking" : null, Leaderboards ? "leaderboards" : null,
                                                            Ugc ? "user content" : null, Clans ? "clans" : null }.Where(s => s != null));
    }

    internal static class XemuInsignia
    {
        public const string Url = "https://insignia.live/games";
        private const string FileName = "insignia-games.tsv";
        private static Dictionary<string, InsigniaGame> _all;
        private static readonly object Gate = new object();
        private static bool _asked;

        private static string CachePath => Path.Combine(XemuSettings.Dir, FileName);

        /// <summary>By title id (8 hex digits, lower case). The downloaded list, else the embedded one; empty, never null.</summary>
        public static Dictionary<string, InsigniaGame> All
        {
            get
            {
                lock (Gate)
                {
                    if (_all != null) return _all;
                    string text = null;
                    try { if (File.Exists(CachePath)) text = File.ReadAllText(CachePath); } catch { }
                    if (text == null)
                        try { using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName); if (s != null) text = new StreamReader(s).ReadToEnd(); } catch { }
                    return _all = FromTsv(text ?? "");
                }
            }
        }

        /// <summary>The game Insignia serves with this title id, or null.</summary>
        public static InsigniaGame Of(string titleId)
            => titleId != null && All.TryGetValue(titleId.Trim().ToLowerInvariant(), out var g) ? g : null;

        /// <summary>When the list was last downloaded whole, or null (the embedded one in use).</summary>
        public static DateTime? Downloaded() { try { return File.Exists(CachePath) ? File.GetLastWriteTime(CachePath) : (DateTime?)null; } catch { return null; } }

        /// <summary>The list asked again in the background when older than a day - once per run.</summary>
        public static void RefreshSoon()
        {
            if (_asked) return;
            _asked = true;
            if (Downloaded() is DateTime d && DateTime.Now - d < TimeSpan.FromDays(1)) return;
            Task.Run(() => { var why = Fetch(); if (why != null) Log.Info("insignia: the list was not refreshed - " + why); });
        }

        /// <summary>The Nixx window's "download the whole list again". Null when done, else why not.</summary>
        public static string FetchWhole(LbipListJob job) => Fetch();

        /// <summary>The page read, parsed, checked against the list kept, written. Null when done, else why not.</summary>
        public static string Fetch()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Nixx-Xemu)");
                var html = http.GetStringAsync(Url).GetAwaiter().GetResult();
                var games = Parse(html);
                int before = All.Count;
                if (games.Count < 50) return "the page held " + games.Count + " games - not taken";
                if (games.Count < before / 2) return "the page held " + games.Count + " games, against " + before + " kept - not taken";
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                var tmp = CachePath + ".tmp";
                File.WriteAllText(tmp, ToTsv(games));
                File.Move(tmp, CachePath, true);
                lock (Gate) _all = games.GroupBy(g => g.TitleId).ToDictionary(x => x.Key, x => x.First());
                Log.Info("insignia: " + games.Count + " games in Insignia's list");
                return null;
            }
            catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>The games of Insignia's page, row by row.</summary>
        internal static List<InsigniaGame> Parse(string html)
        {
            var list = new List<InsigniaGame>();
            foreach (var row in Regex.Split(html ?? "", @"<tr[\s>]", RegexOptions.IgnoreCase))
            {
                var link = Regex.Match(row, @"insignia\.live/games/([0-9a-fA-F]{8})""[^>]*>\s*([^<]+?)\s*</a>");
                if (!link.Success) continue;
                bool Has(string title) => Regex.IsMatch(row, @"ms-2 visible""\s+title=""" + Regex.Escape(title) + @"""", RegexOptions.IgnoreCase);
                var serial = Regex.Match(row, @"<td>\s*([A-Z0-9]{1,4}-\d{1,4})\s*<br", RegexOptions.IgnoreCase);
                list.Add(new InsigniaGame
                {
                    TitleId = link.Groups[1].Value.ToLowerInvariant(),
                    Name = System.Net.WebUtility.HtmlDecode(link.Groups[2].Value).Trim(),
                    Serial = serial.Success ? serial.Groups[1].Value : "",
                    LiveAware = Has("Live Aware"), Matchmaking = Has("Matchmaking"), Leaderboards = Has("Leaderboards"),
                    Ugc = Has("User Generated Content"), Clans = Has("Clans"),
                });
            }
            return list;
        }

        internal static string ToTsv(IEnumerable<InsigniaGame> games)
            => "# Insignia's games (" + Url + ") - title id, serial, name, live-aware matchmaking leaderboards ugc clans (1/0)\n"
               + string.Join("\n", games.Select(g => string.Join("\t", g.TitleId, g.Serial, g.Name.Replace('\t', ' '),
                   string.Concat(new[] { g.LiveAware, g.Matchmaking, g.Leaderboards, g.Ugc, g.Clans }.Select(b => b ? '1' : '0'))))) + "\n";

        private static Dictionary<string, InsigniaGame> FromTsv(string text)
        {
            var all = new Dictionary<string, InsigniaGame>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 4 || f[0].Length != 8) continue;
                var b = f[3].PadRight(5, '0');
                all[f[0].ToLowerInvariant()] = new InsigniaGame { TitleId = f[0].ToLowerInvariant(), Serial = f[1], Name = f[2],
                    LiveAware = b[0] == '1', Matchmaking = b[1] == '1', Leaderboards = b[2] == '1', Ugc = b[3] == '1', Clans = b[4] == '1' };
            }
            return all;
        }
    }
}
