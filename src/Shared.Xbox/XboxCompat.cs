// What the two Xbox emulators' compatibility lists say of a game - each plugin's copy READ by the other (Mehdi, 05/10:
// "afficher dans chaque fenêtre le statut de l'autre émulateur"), nothing fetched for it.
//
// XEMU'S (Nixx-Xemu fetches them, XemuCompat), both measured 05/10:
//   https://reports.xemu.app/compatibility - 1.2 MB, no ETag: the newest report of each title, 1,104 of them -
//     {"created_at": 1756426515, "xbe_cert_title_id": 1297285124 (decimal), "xbe_headers_sha256": "...", "xemu_version":
//      "0.8.96", "os_platform": "Windows", "gl_renderer": "...", "compat_rating": "Playable", "compat_comments": "..."}
//     xbe_headers_sha256 does NOT say which disc (measured 05/10 on Batman): xemu sends the headers as they are in the
//     console's memory at 0x10000 (xemu-xbe.c), which the kernel rewrites as the game runs - the entry point and thunk
//     decoded, each section's reference counts - so the same disc hashes differently from one moment to the next.
//   https://xemu.app/compat.json - 287 KB, ETag: the site's titles, 1,024 - {"name", "url": "/titles/5443000d#...",
//     "status"} - the status the site shows (its title's newest report, aliases folded in), the name a clean one.
//   Ratings: Perfect, Playable, Starts, Intro, Broken; a title in neither is Unknown.
//   Kept in <plugin data of Nixx-Xemu>\xemu-compat-reports.json and xemu-compat-titles.json.
// CXBX-RELOADED'S (Nixx-Cxbx's CxbxCompat): <plugin data of Nixx-Cxbx>\cxbx-compat.json - else, in Nixx-Xemu, the copy
// embedded when it was built (Cxbx\compat.json, LogicalName cxbx-compat.json).
//
// <plugin data> is <Plugins>\.data\<PluginId>; the two plugins may sit in Local\Plugins (LaunchBox 14) or in Plugins:
// both are looked in.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LbIntegrations.Xbox
{
    internal sealed class XemuCompatReport
    {
        public string TitleId;                 // "4d530004"
        public string Rating;                  // Perfect, Playable, Starts, Intro, Broken
        public string Comment, XemuVersion, Platform, Gpu;
        public DateTime When;
    }

    internal sealed class XemuCompatTitle
    {
        public string TitleId, Name, Status, Url;
    }

    /// <summary>Cxbx-Reloaded's list as Nixx-Cxbx keeps it (CxbxCompatEntry's fields).</summary>
    internal sealed class CxbxCompatPeek
    {
        public string Title { get; set; } = "";
        public string Serial { get; set; } = "";
        public string Version { get; set; } = "";
        public string Region { get; set; } = "";
        public string State { get; set; } = "";
        public string Updated { get; set; } = "";
        public string Url { get; set; } = "";
    }

    internal static class XboxCompat
    {
        public const string XemuPluginId = "c54b75ab-94aa-4a1e-b36c-b6af7115c51c", CxbxPluginId = "9b0138fb-62bf-4f60-9e2b-578b9fe89720";
        public const string ReportsFile = "xemu-compat-reports.json", TitlesFile = "xemu-compat-titles.json", CxbxFile = "cxbx-compat.json";
        public const string SiteRoot = "https://xemu.app";
        private static readonly object Gate = new object();

#pragma warning disable CS0649
        /// <summary>For the probe: every plugin's data in this folder (its .data). Set by reflection.</summary>
        internal static string DataOverride;
#pragma warning restore CS0649

        // ── where ────────────────────────────────────────────────────────────

        /// <summary>The .data folders to look in: beside this plugin's own folder, and the other plugins root of LaunchBox.</summary>
        private static List<string> DataRoots()
        {
            var roots = new List<string>();
            if (DataOverride != null) { roots.Add(DataOverride); return roots; }
            try
            {
                var dll = typeof(XboxCompat).Assembly.Location;
                var plugins = Path.GetDirectoryName(Path.GetDirectoryName(dll)) ?? Path.GetDirectoryName(dll);
                roots.Add(Path.Combine(plugins, ".data"));
                var parent = Path.GetDirectoryName(plugins);
                if (parent != null)
                {
                    if (string.Equals(Path.GetFileName(parent), "Local", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(parent) is string lb)
                        roots.Add(Path.Combine(lb, "Plugins", ".data"));
                    else roots.Add(Path.Combine(parent, "Local", "Plugins", ".data"));
                }
            }
            catch { }
            return roots;
        }

        public const string XemuPluginType = "LbIntegrations.Xemu.XemuPlugin", CxbxPluginType = "LbIntegrations.Cxbx.CxbxPlugin";

        /// <summary>Is the other plugin LOADED in this LaunchBox (Mehdi, 05/10: a plugin not there, or turned off, is not shown)?
        /// A plugin turned off is never loaded; its files left on disk say nothing. Its class looked for in the assemblies
        /// loaded - not in this one: Nixx-Xemu carries some of Nixx-Cxbx's files (Xbe.cs...) but never its plugin.</summary>
        public static bool PluginLoaded(string typeName)
        {
            if (PluginOverride != null) return PluginOverride(typeName);
            try
            {
                var own = typeof(XboxCompat).Assembly;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a == own || a.IsDynamic) continue;
                    try { if (a.GetType(typeName, false) != null) return true; } catch { }
                }
            }
            catch { }
            return false;
        }

#pragma warning disable CS0649
        /// <summary>For the probe: which plugins count as loaded. Set by reflection.</summary>
        internal static Func<string, bool> PluginOverride;
#pragma warning restore CS0649

        /// <summary>The first <paramref name="file"/> of <paramref name="pluginId"/>'s data there is, else null.</summary>
        public static string Find(string pluginId, string file)
            => DataRoots().Select(r => Path.Combine(r, pluginId, file)).FirstOrDefault(File.Exists);

        // ── xemu ─────────────────────────────────────────────────────────────

        private static Dictionary<string, XemuCompatReport> _reports;
        private static Dictionary<string, XemuCompatTitle> _titles;
        private static string _reportsKey, _titlesKey;

        public static List<XemuCompatReport> ParseReports(string json)
        {
            var list = new List<XemuCompatReport>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("not a JSON array");
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                string Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (!r.TryGetProperty("xbe_cert_title_id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetUInt32(out var tid) || tid == 0) continue;
                long when = r.TryGetProperty("created_at", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var t) ? t : 0;
                list.Add(new XemuCompatReport
                {
                    TitleId = tid.ToString("x8"), Rating = Str("compat_rating"), Comment = Str("compat_comments")?.Trim(),
                    XemuVersion = Str("xemu_version"), Platform = Str("os_platform")?.Trim(), Gpu = Str("gl_renderer")?.Trim(),
                    When = when > 0 ? DateTimeOffset.FromUnixTimeSeconds(when).UtcDateTime : default,
                });
            }
            return list;
        }

        public static List<XemuCompatTitle> ParseTitles(string json)
        {
            var list = new List<XemuCompatTitle>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("not a JSON array");
            foreach (var g in doc.RootElement.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.Object) continue;
                string Str(string n) => g.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var url = Str("url");
                var m = url == null ? null : System.Text.RegularExpressions.Regex.Match(url, "/titles/([0-9a-fA-F]{8})");
                if (m == null || !m.Success) continue;
                list.Add(new XemuCompatTitle { TitleId = m.Groups[1].Value.ToLowerInvariant(), Name = Str("name"), Status = Str("status"), Url = SiteRoot + url });
            }
            return list;
        }

        private static string KeyOf(string path) { try { return path + "|" + File.GetLastWriteTimeUtc(path).Ticks; } catch { return path; } }

        /// <summary>xemu's newest report of this title, and the site's title - each null when unknown.</summary>
        public static (XemuCompatReport Report, XemuCompatTitle Title) Xemu(string titleId)
        {
            if (string.IsNullOrWhiteSpace(titleId)) return (null, null);
            var id = titleId.Trim().ToLowerInvariant();
            lock (Gate)
            {
                try
                {
                    var rp = Find(XemuPluginId, ReportsFile);
                    if (rp == null) _reports = null;
                    else if (_reports == null || _reportsKey != KeyOf(rp))
                    {
                        _reports = ParseReports(File.ReadAllText(rp)).GroupBy(r => r.TitleId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.When).First());
                        _reportsKey = KeyOf(rp);
                    }
                }
                catch { _reports = null; }
                try
                {
                    var tp = Find(XemuPluginId, TitlesFile);
                    if (tp == null) _titles = null;
                    else if (_titles == null || _titlesKey != KeyOf(tp))
                    {
                        _titles = ParseTitles(File.ReadAllText(tp)).GroupBy(t => t.TitleId).ToDictionary(g => g.Key, g => g.First());
                        _titlesKey = KeyOf(tp);
                    }
                }
                catch { _titles = null; }
                return (_reports != null && _reports.TryGetValue(id, out var r) ? r : null, _titles != null && _titles.TryGetValue(id, out var t) ? t : null);
            }
        }

        /// <summary>Is there an xemu list at all (to say "not in it" rather than "no list")?</summary>
        public static bool HasXemuList() => Find(XemuPluginId, ReportsFile) != null || Find(XemuPluginId, TitlesFile) != null;

        /// <summary>The state to show: the report's rating, else the site's, else Unknown.</summary>
        public static string XemuState(XemuCompatReport r, XemuCompatTitle t)
            => !string.IsNullOrEmpty(r?.Rating) ? r.Rating : !string.IsNullOrEmpty(t?.Status) ? t.Status : "Unknown";

        public static string XemuPage(string titleId, XemuCompatTitle t) => t?.Url ?? SiteRoot + "/titles/" + titleId.ToLowerInvariant();

        public static Color XemuColor(string rating)
        {
            switch (rating)
            {
                case "Perfect": return Color.FromArgb(0x1A, 0x7F, 0x37);
                case "Playable": return Color.FromArgb(0x2E, 0xA0, 0x43);
                case "Starts": return Color.FromArgb(0xB0, 0x9A, 0x00);
                case "Intro": return Color.FromArgb(0xD0, 0x8A, 0x00);
                case "Broken": return Color.FromArgb(0xD7, 0x3A, 0x49);
                default: return Color.Gray;
            }
        }

        public static string Day(DateTime when) => when == default ? "" : when.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        // ── Cxbx-Reloaded ────────────────────────────────────────────────────

        private static List<CxbxCompatPeek> _cxbx;
        private static string _cxbxKey;

        /// <summary>Cxbx-Reloaded's entry for this disc (its version, else any of the title), from Nixx-Cxbx's copy, else
        /// the one embedded in this assembly. Null when unknown; <paramref name="hasList"/> whether there was a list at all.</summary>
        public static CxbxCompatPeek Cxbx(uint titleId, uint version, out bool hasList)
        {
            char a = (char)((titleId >> 24) & 0xFF), b = (char)((titleId >> 16) & 0xFF);
            hasList = false;
            if (a < 0x20 || a > 0x7E || b < 0x20 || b > 0x7E) return null;
            var serial = "" + a + b + "-" + (titleId & 0xFFFF).ToString("000", CultureInfo.InvariantCulture);
            var ver = "v" + (version & 0xFF).ToString(CultureInfo.InvariantCulture) + "." + (version >> 8).ToString(CultureInfo.InvariantCulture);
            List<CxbxCompatPeek> all;
            lock (Gate)
            {
                try
                {
                    var p = Find(CxbxPluginId, CxbxFile);
                    var key = p != null ? KeyOf(p) : "embedded";
                    if (_cxbx == null || _cxbxKey != key)
                    {
                        _cxbx = p != null ? JsonSerializer.Deserialize<List<CxbxCompatPeek>>(File.ReadAllText(p)) : Embedded();
                        _cxbxKey = key;
                    }
                }
                catch { _cxbx = Embedded(); _cxbxKey = "embedded"; }
                all = _cxbx;
            }
            if (all == null || all.Count == 0) return null;
            hasList = true;
            return all.FirstOrDefault(e => e.Serial == serial && e.Version == ver) ?? all.FirstOrDefault(e => e.Serial == serial);
        }

        private static List<CxbxCompatPeek> Embedded()
        {
            try
            {
                using var s = typeof(XboxCompat).Assembly.GetManifestResourceStream(CxbxFile);
                if (s == null) return null;
                using var r = new StreamReader(s);
                return JsonSerializer.Deserialize<List<CxbxCompatPeek>>(r.ReadToEnd());
            }
            catch { return null; }
        }

        public static Color CxbxColor(string state)
        {
            switch (state)
            {
                case "Playable": return Color.FromArgb(0x2E, 0xA0, 0x43);
                case "In-Game": return Color.FromArgb(0x6B, 0x9E, 0x2A);
                case "Boots": return Color.FromArgb(0xD0, 0x8A, 0x00);
                case "Nothing": return Color.FromArgb(0xD7, 0x3A, 0x49);
                default: return Color.Gray;
            }
        }
    }
}
