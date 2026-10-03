// The optimized settings of xenia-manager's optimized-settings repository (Mehdi, 03/10): per game, the Xenia settings
// the community found it runs best on - Halo 3, 4D5307E6.toml:
//     [GPU]
//     depth_bias_shader_offset = true   # Fixes the rare Z-fighting decal
//     readback_resolve = "full"         # Fixes gamma
// - a port of A1eNaz's wiki of optimized settings. data/settings.json lists the games (345 on 03/10:
// {"id": "4D5307E6", "title": "Halo 3", "last_modified": "..."}), settings/<TITLE ID>.toml holds each one's.
//
// HOW THEY WEIGH (Mehdi, 03/10): the most specific wins - the game's own options, then these, then every game's, then
// Xenia's own. A fix for one game (Halo 3's gamma) is not undone by a preference for every game; a game's own choice
// still beats it. On for every game by default (the Nixx window's Xenia tab), on, off or as every game for one game.
//
// PASSED ON THE COMMAND LINE as every option of this pack (XeniaOptions) - and only a cvar this Xenia knows: one it
// does not (a renamed or removed setting) would stop cxxopts, so Xenia, at start. Known = in the TOML Xenia writes
// with every cvar it has (xenia-manager's own rule: a value applies only where the config has it).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaOptimizedValue
    {
        public string Section, Key;
        /// <summary>As the file writes it: true, 2, "full".</summary>
        public string Raw;
        /// <summary>What goes after --key=: the string unquoted.</summary>
        public string Value;
        /// <summary>The file's comment for it ("Fixes gamma"), or "".</summary>
        public string Why;
    }

    internal static class XeniaOptimized
    {
        public const string SettingKey = "optimized";
        private const string IndexName = "optimized-settings.json";
        private static readonly string[] IndexUrls =
        {
            "https://xenia-manager.github.io/optimized-settings/data/settings.json",
            "https://raw.githubusercontent.com/xenia-manager/optimized-settings/main/data/settings.json",
        };
        private static readonly Regex Hex8 = new Regex("^[0-9A-Fa-f]{8}$", RegexOptions.CultureInvariant);

        private static string[] GameUrls(string id) => new[]
        {
            "https://xenia-manager.github.io/optimized-settings/settings/" + id + ".toml",
            "https://raw.githubusercontent.com/xenia-manager/optimized-settings/main/settings/" + id + ".toml",
        };

        private static string GameName(string id) => "optimized-" + id + ".toml";

        /// <summary>Is it on for this game: its own choice (on / off), else every game's - on by default.</summary>
        public static bool On(IDictionary<string, string> game, IDictionary<string, string> every)
        {
            if (game != null && game.TryGetValue(SettingKey, out var g) && (g == "on" || g == "off")) return g == "on";
            return !(every != null && every.TryGetValue(SettingKey, out var e) && e == "off");
        }

        // ── the index ───────────────────────────────────────────────────────

        /// <summary>The title ids the database has, from the copy - null when there is no copy.</summary>
        public static HashSet<string> Known()
        {
            var json = XeniaRemote.Cached(IndexName);
            return json == null ? null : ParseIndex(json);
        }

        internal static HashSet<string> ParseIndex(string json)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return ids;
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Hex8.IsMatch(id.GetString()))
                        ids.Add(id.GetString().ToUpperInvariant());
            }
            catch { }
            return ids;
        }

        private static bool IndexReads(string json) => ParseIndex(json).Count >= 20;

        public static void RefreshIndexSoon() => XeniaRemote.RefreshSoon(IndexName, IndexUrls, TimeSpan.FromSeconds(20), IndexReads);

        public static HashSet<string> Index(TimeSpan timeout)
        {
            var json = XeniaRemote.Get(IndexName, IndexUrls, timeout, IndexReads);
            return json == null ? null : ParseIndex(json);
        }

        // ── a game's ────────────────────────────────────────────────────────

        /// <summary>A game's optimized settings - empty when the database has none for it. <paramref name="why"/>: what
        /// was found, for the log and the window. Within <paramref name="timeout"/> when something must be fetched.</summary>
        public static List<XeniaOptimizedValue> For(string titleId, TimeSpan timeout, out string why)
        {
            why = null;
            if (string.IsNullOrWhiteSpace(titleId) || !Hex8.IsMatch(titleId.Trim())) { why = "no title id"; return new List<XeniaOptimizedValue>(); }
            var id = titleId.Trim().ToUpperInvariant();
            var known = XeniaRemote.IsFresh(IndexName) ? Known() : Index(timeout);
            if (known != null && !known.Contains(id))
            {
                // A copy of the game's file from an older index still stands: the game was in the database once.
                var stale = XeniaRemote.Cached(GameName(id));
                if (stale == null) { why = "not in the optimized settings database"; return new List<XeniaOptimizedValue>(); }
            }
            var toml = XeniaRemote.Get(GameName(id), GameUrls(id), timeout, t => Parse(t).Count > 0 || t.Contains("title_id"));
            if (toml == null) { why = known == null ? "the optimized settings could not be fetched" : "its optimized settings could not be fetched"; return new List<XeniaOptimizedValue>(); }
            var values = Parse(toml);
            why = values.Count == 0 ? "no setting in its optimized settings" : values.Count + " optimized setting(s)";
            return values;
        }

        /// <summary>The settings of an optimized-settings TOML, sections and comments kept. The title's own header keys
        /// (title_name, title_id, before any section) are not settings.</summary>
        internal static List<XeniaOptimizedValue> Parse(string toml)
        {
            var list = new List<XeniaOptimizedValue>();
            string section = null;
            foreach (var raw in (toml ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Trim('[', ']').Trim(); continue; }
                if (section == null) continue;
                if (!XeniaToml.Split(line, out var key, out var value, out var comment) || value.Length == 0) continue;
                var v = value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"' ? value.Substring(1, value.Length - 2) : value;
                list.Add(new XeniaOptimizedValue { Section = section, Key = key, Raw = value, Value = v, Why = (comment ?? "").TrimStart('#').Trim() });
            }
            return list;
        }

        /// <summary>A value as a command-line flag, quoted when it holds a space.</summary>
        public static string Flag(XeniaOptimizedValue v)
        {
            var f = "--" + v.Key + "=" + v.Value;
            return f.IndexOf(' ') >= 0 ? "\"" + f + "\"" : f;
        }
    }
}
