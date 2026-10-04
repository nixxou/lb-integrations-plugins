// A game's state in Vita3K's own compatibility list - shown while it is prepared (Vita3kProgressWindow)
// and at the top of its Options' Compatibility tab (Mehdi, 29/09): a coloured dot, the state, the other
// labels of its report, and a link to the report.
//
// THE LIST IS VITA3K'S: portable\cache\app_compat_db.xml, downloaded and kept up to date by Vita3K itself
// (compat/src/compat.cpp) - one <app title_id=".."> per game, its issue number on github.com/Vita3K/
// compatibility and the ids of that issue's labels. It is also fetched HERE, from the same place, at every
// install and update and when there is none at start-up (Mehdi, 04/10: a Vita3K just installed had none) - Download.
//
// THE LIST HOLDS NUMBERS ONLY - a label's id, never its name. So, IT RUNS OFFLINE (Mehdi, 29/09):
//   - THE STATE, as Vita3K reads it: its seven state label ids (compat/src/compat.cpp, LabelId), the LAST
//     one of a game's labels winning (parse_xml), and Vita3K's own words and colours for them
//     (gui-qt apps_list_table.cpp, compat_text / compat_color) - nothing asked of anybody;
//   - THE REPORT'S OTHER LABELS - its problems, "graphics bug", "slow"... - by the names GitHub gives them
//     (the compatibility repository's labels), kept in portable\cache\lbip-compat-labels.tsv and asked
//     again when Vita3K's list is newer than them or names a label they do not know - in the
//     background, never on a launch's time. Offline, with no names kept yet, they are simply not shown;
//   - a state label Vita3K does not know yet (a newer list) is still one when GitHub's description of it
//     says "State label", as the seven say - shown with GitHub's own colour.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace LbIntegrations.Vita3k
{
    internal sealed class VitaCompat
    {
        public string TitleId;
        public int IssueId;
        public string State;                 // "Playable"... null when the labels' names are not known yet
        public string StateColor;            // "0E8A16", GitHub's
        public string StateDescription;
        public List<string> Problems = new List<string>();
        public string Url => "https://github.com/Vita3K/compatibility/issues/" + IssueId.ToString(CultureInfo.InvariantCulture);
    }

    internal static class Vita3kCompat
    {
        private const string LabelsApi = "https://api.github.com/repos/Vita3K/compatibility/labels?per_page=100&page=";

        private sealed class Label { public string Name, Color, Description; }

        /// <summary>Vita3K's seven state labels: id, its word, its colour - compat.cpp and apps_list_table.cpp.</summary>
        private static readonly (long Id, string Name, string Color)[] States =
        {
            (1260231569, "Nothing", "FF0000"),
            (1344750319, "Boots", "621FA5"),
            (1260231381, "Intro", "C71585"),
            (1344751053, "Menu", "1D76DB"),
            (1344752299, "Ingame", "E08A1E"),
            (1260231985, "Ingame+", "FFD700"),
            (920344019, "Playable", "0E8A16"),
        };

        private static string CacheDir(Vita3kLayout layout)
        {
            var portable = Vita3kPaths.PortableDirOf(layout?.InstallDir);
            return string.IsNullOrEmpty(portable) ? null : Path.Combine(portable, "cache");
        }

        private static string DbPath(Vita3kLayout layout) => CacheDir(layout) is string d ? Path.Combine(d, "app_compat_db.xml") : null;
        private static string LabelsPath(Vita3kLayout layout) => CacheDir(layout) is string d ? Path.Combine(d, "lbip-compat-labels.tsv") : null;

        /// <summary>The game's entry, or null: no list yet, or the game is not in it. Never throws; reads
        /// the list as a stream, stopping at the game.</summary>
        public static VitaCompat Lookup(Vita3kLayout layout, string titleId)
        {
            try
            {
                var db = DbPath(layout);
                if (string.IsNullOrWhiteSpace(titleId) || db == null || !File.Exists(db)) return null;
                VitaCompat found = null;
                var labelIds = new List<long>();
                using (var reader = XmlReader.Create(db, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true }))
                {
                    while (reader.ReadToFollowing("app"))
                    {
                        if (!string.Equals(reader.GetAttribute("title_id"), titleId, StringComparison.OrdinalIgnoreCase)) continue;
                        found = new VitaCompat { TitleId = titleId };
                        using var app = reader.ReadSubtree();
                        app.Read();
                        // ReadElementContentAsString moves on by itself: a Read() after it would skip the next label.
                        while (!app.EOF)
                        {
                            if (app.NodeType == XmlNodeType.Element && app.Name == "issue_id")
                            { if (int.TryParse(app.ReadElementContentAsString(), out var id)) found.IssueId = id; continue; }
                            if (app.NodeType == XmlNodeType.Element && app.Name == "label")
                            { if (long.TryParse(app.ReadElementContentAsString(), out var l)) labelIds.Add(l); continue; }
                            app.Read();
                        }
                        break;
                    }
                }
                if (found == null || found.IssueId <= 0) return null;

                var labels = ReadLabels(layout);
                bool unknown = false;
                foreach (var id in labelIds)
                {
                    labels.TryGetValue(id, out var label);
                    var known = States.FirstOrDefault(x => x.Id == id);
                    if (known.Name != null)
                    {
                        // Vita3K's reading: the last state label wins.
                        found.State = known.Name;
                        found.StateColor = known.Color;
                        found.StateDescription = label?.Description?.Replace("State label", "").Trim();
                        continue;
                    }
                    if (label == null) { unknown = true; continue; }
                    if ((label.Description ?? "").IndexOf("State label", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found.State = label.Name;
                        found.StateColor = label.Color;
                        found.StateDescription = label.Description.Replace("State label", "").Trim();
                    }
                    else found.Problems.Add(label.Name);
                }
                if (unknown) RefreshLabelsLater(layout, "a label of " + titleId + "'s report is not known yet");
                return found;
            }
            catch (Exception ex) { Log.Warn("compatibility list: could not read it", ex); return null; }
        }

        private static Dictionary<long, Label> ReadLabels(Vita3kLayout layout)
        {
            var labels = new Dictionary<long, Label>();
            try
            {
                var path = LabelsPath(layout);
                if (path == null || !File.Exists(path)) return labels;
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split('\t');
                    if (f.Length >= 4 && long.TryParse(f[0], out var id)) labels[id] = new Label { Color = f[1], Name = f[2], Description = f[3] };
                }
            }
            catch (Exception ex) { Log.Warn("compatibility list: could not read the labels", ex); }
            return labels;
        }

        /// <summary>Ask GitHub for the labels when there are none yet, or Vita3K's list is newer than them -
        /// in the background. For the plugin's start-up check.</summary>
        public static void RefreshIfStale(Vita3kLayout layout)
        {
            try
            {
                var db = DbPath(layout);
                var labels = LabelsPath(layout);
                if (db == null || !File.Exists(db)) return;
                if (File.Exists(labels) && File.GetLastWriteTimeUtc(labels) >= File.GetLastWriteTimeUtc(db)) return;
                RefreshLabelsLater(layout, File.Exists(labels) ? "Vita3K's compatibility list is newer than its labels" : "no labels yet");
            }
            catch { }
        }

        private const string DbUrl = "https://github.com/Vita3K/compatibility/releases/download/compat_db/app_compat_db.xml";

        /// <summary>THE LIST ITSELF, from where Vita3K takes it (compat.cpp), into portable\cache where Vita3K reads it -
        /// at every install and update (Mehdi, 04/10: a Vita3K just installed had none, so no game had a state), then its
        /// labels. Written only once read whole as Vita3K's list (a &lt;compatibility&gt; root, at least one &lt;app&gt;):
        /// a cut download or an error page never takes the place of a good list. Never throws: a list that does not
        /// come is logged and the install goes on. Returns what to add to the install's message, "" when all went well.</summary>
        public static string Download(Vita3kLayout layout, Func<bool> cancelled = null)
        {
            if (NoNetwork) return "";
            var db = DbPath(layout);
            if (db == null) return "";
            var tmp = db + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(db));
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("lbip-vita3k");
                    var bytes = http.GetByteArrayAsync(DbUrl).GetAwaiter().GetResult();
                    if (cancelled?.Invoke() == true) return "";
                    File.WriteAllBytes(tmp, bytes);
                }
                int apps = 0;
                string updated = null;
                using (var reader = XmlReader.Create(tmp, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true }))
                {
                    reader.MoveToContent();
                    if (reader.Name != "compatibility") throw new InvalidDataException("not Vita3K's list (root <" + reader.Name + ">)");
                    updated = reader.GetAttribute("db_updated_at");
                    while (reader.ReadToFollowing("app")) apps++;
                }
                if (apps == 0) throw new InvalidDataException("the list names no game");
                File.Move(tmp, db, overwrite: true);
                Log.Info("compatibility list: " + apps + " game(s), updated " + (updated ?? "?") + " - downloaded to " + db);
                // Its labels' names, now that the list is newer than them.
                try { RefreshLabels(layout, "the compatibility list was just downloaded"); }
                catch (Exception ex) { Log.Info("compatibility list: the labels' names could not be asked of GitHub (" + ex.GetType().Name + ": " + ex.Message + ") - the state is shown without them"); }
                return "";
            }
            catch (Exception ex)
            {
                Log.Warn("compatibility list: could not download it", ex);
                return File.Exists(db) ? "" : " Vita3K's compatibility list could not be downloaded: games show no state until it is.";
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        private static int _downloading;

        /// <summary>The list when there is none at all - in the background, for the plugin's start-up check (an install
        /// made before the list was downloaded at install, or one whose download failed). Once per start.</summary>
        public static void DownloadIfMissing(Vita3kLayout layout)
        {
            try
            {
                var db = DbPath(layout);
                if (NoNetwork || db == null || File.Exists(db) || !Directory.Exists(layout?.InstallDir)) return;
                if (System.Threading.Interlocked.Exchange(ref _downloading, 1) == 1) return;
                System.Threading.Tasks.Task.Run(() => Download(layout));
            }
            catch { }
        }

        private static int _refreshing;
        private static DateTime _lastTry = DateTime.MinValue;

        /// <summary>For the probe: never ask GitHub - a test of the offline reading must not race a download.</summary>
        internal static bool NoNetwork;

        private static void RefreshLabelsLater(Vita3kLayout layout, string why)
        {
            if (NoNetwork) return;
            // Once at a time, and not again within the hour when GitHub did not answer.
            if (System.Threading.Interlocked.Exchange(ref _refreshing, 1) == 1) return;
            if ((DateTime.UtcNow - _lastTry).TotalMinutes < 60) { _refreshing = 0; return; }
            _lastTry = DateTime.UtcNow;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { RefreshLabels(layout, why); }
                // Offline is not a fault: the state needs nothing from GitHub, only the problems' names do.
                catch (Exception ex) { Log.Info("compatibility list: the labels' names could not be asked of GitHub (" + ex.GetType().Name + ": " + ex.Message + ") - the state is shown without them"); }
                finally { _refreshing = 0; }
            });
        }

        private static void RefreshLabels(Vita3kLayout layout, string why)
        {
            var path = LabelsPath(layout);
            if (path == null) return;
            var lines = new List<string>();
            using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("lbip-vita3k");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                for (int page = 1; page <= 10; page++)
                {
                    var json = http.GetStringAsync(LabelsApi + page).GetAwaiter().GetResult();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    int count = 0;
                    foreach (var l in doc.RootElement.EnumerateArray())
                    {
                        count++;
                        string S(string k) => l.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : "";
                        string Clean(string x) => (x ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                        if (l.TryGetProperty("id", out var id))
                            lines.Add(id.GetInt64().ToString(CultureInfo.InvariantCulture) + "\t" + Clean(S("color")) + "\t" + Clean(S("name")) + "\t" + Clean(S("description")));
                    }
                    if (count < 100) break;
                }
            }
            if (lines.Count == 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            Log.Info("compatibility list: " + lines.Count + " label(s) asked of GitHub (" + why + ")");
        }
    }
}
