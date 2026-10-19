// A game's state in Ymir's own compatibility list - shown in the game's window and as it starts (Mehdi 20/10).
//
// THE LIST (docs\ymir.md §10): the Google Sheet the README links, 1SLZzL9LelSlpEmTKy8cjaQnE7mew2uW1rfCgcekO58Q, one tab per
// region - NTSC-U, NTSC-J, NTSC-A, PAL - and ROM Hacks, each readable without an account as CSV (/gviz/tq?tqx=out:csv&
// sheet=<tab>). Its first row names the columns ("Game name" or "Base game name", "Serial", "Disc hash", "Compatibility",
// "Ymir version", "BIOS region/version", "Tester", "Additional notes"), the second describes them; read BY NAME, so a
// column moved or added is no matter. The Homebrew tab has no disc hash: nothing of it can be matched to a disc.
//
// MATCHING: the DISC HASH first (YmirDisc - measured equal to the list's, 20/10), exact - a disc of the list. Else the product
// code from the disc's header, said as such: two releases may share one (the French and European Atlantis do), so it is
// "the list has <name> (same product code)", never the game's own line.
//
// THE COPY is the plugin's, in <data>\compat\<tab>.csv: downloaded at an install or update, when there is none, when it is
// older than a week (in the background, never on a launch's time - RefreshLater), and from the Nixx page. A download that
// fails, does not read as the list, or has lost more than half its lines KEEPS the copy there (the PC ports module's rule).
// Offline with no copy yet: nothing is shown.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LbIntegrations.Lbip;

namespace LbIntegrations.Ymir
{
    internal sealed class YmirCompatEntry
    {
        public string Name, Serial, Hash, State, Version, Bios, Notes, Tab;
    }

    internal sealed class YmirCompatResult
    {
        /// <summary>The disc's own line (by its hash); null when the list does not have it.</summary>
        public YmirCompatEntry Exact;
        /// <summary>Lines of the same product code, when there is no exact one - maybe another release.</summary>
        public List<YmirCompatEntry> SameProduct = new List<YmirCompatEntry>();
        public bool Found => Exact != null || SameProduct.Count > 0;
        public string State => Exact?.State ?? (SameProduct.Select(e => e.State).Distinct().Count() == 1 ? SameProduct[0].State : null);
    }

    internal static class YmirCompat
    {
        public const string Sheet = "1SLZzL9LelSlpEmTKy8cjaQnE7mew2uW1rfCgcekO58Q";
        public const string Page = "https://docs.google.com/spreadsheets/d/" + Sheet;
        public static readonly string[] Tabs = { "NTSC-U", "NTSC-J", "NTSC-A", "PAL", "ROM Hacks" };
        private static readonly TimeSpan Stale = TimeSpan.FromDays(7);

        /// <summary>The list's states, worst to best, and their colours - the sheet's own words.</summary>
        public static readonly (string State, Color Color, string Meaning)[] States =
        {
            ("Nothing", Color.FromArgb(0xD0, 0x30, 0x30), "does not boot"),
            ("Intro", Color.FromArgb(0xC7, 0x15, 0x85), "shows its intro, goes no further"),
            ("Menus", Color.FromArgb(0x1D, 0x76, 0xDB), "reaches its menus, not the game"),
            ("In-game", Color.FromArgb(0xE0, 0x8A, 0x1E), "plays, with problems"),
            ("Playable", Color.FromArgb(0x5C, 0xB8, 0x2A), "plays through, small problems"),
            ("Perfect", Color.FromArgb(0x0E, 0x8A, 0x16), "plays as on the console"),
            ("Unknown", Color.FromArgb(0x9A, 0x9A, 0x9A), "not tested yet"),
        };

        public static Color ColorOf(string state) => States.FirstOrDefault(s => string.Equals(s.State, state, StringComparison.OrdinalIgnoreCase)).Color is Color c && c.A != 0 ? c : Color.FromArgb(0x9A, 0x9A, 0x9A);

        public static string MeaningOf(string state) => States.FirstOrDefault(s => string.Equals(s.State, state, StringComparison.OrdinalIgnoreCase)).Meaning;

        /// <summary>A state that may keep the game from being played through (the "only when the game may not play" choice).</summary>
        public static bool MayNotPlay(string state)
            => !string.Equals(state, "Perfect", StringComparison.OrdinalIgnoreCase) && !string.Equals(state, "Playable", StringComparison.OrdinalIgnoreCase);

        // ── the copy ─────────────────────────────────────────────────────────

        internal static string Dir => Path.Combine(YmirSettings.DataDir, "compat");
        private static string FileOf(string tab) => Path.Combine(Dir, tab.Replace(' ', '-') + ".csv");
        private static string StampPath => Path.Combine(Dir, "downloaded.txt");

        /// <summary>When the copy was last downloaded whole, or null.</summary>
        public static DateTime? Downloaded()
        {
            try
            {
                if (!File.Exists(StampPath)) return null;
                return DateTime.TryParse(File.ReadAllText(StampPath).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : (DateTime?)null;
            }
            catch { return null; }
        }

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("lb-integrations-plugins/1.0");
            return c;
        }

        private static readonly object DownloadGate = new object();

        /// <summary>Every tab downloaded, each kept only when it reads as the list. A line to say what was done; never throws.</summary>
        public static string Download(Func<bool> cancelled = null)
        {
            lock (DownloadGate)
            {
                int ok = 0, lines = 0;
                var failed = new List<string>();
                try { Directory.CreateDirectory(Dir); } catch (Exception ex) { return "the compatibility list could not be kept: " + ex.Message; }
                foreach (var tab in Tabs)
                {
                    if (cancelled?.Invoke() == true) break;
                    try
                    {
                        var url = Page + "/gviz/tq?tqx=out:csv&sheet=" + Uri.EscapeDataString(tab);
                        var text = Http.GetStringAsync(url).GetAwaiter().GetResult();
                        var rows = Parse(text, tab);
                        if (rows == null) { failed.Add(tab + " (not the list's columns)"); continue; }
                        var path = FileOf(tab);
                        int before = File.Exists(path) ? (Parse(File.ReadAllText(path), tab)?.Count ?? 0) : 0;
                        if (rows.Count < before / 2) { failed.Add(tab + " (" + rows.Count + " lines instead of " + before + ")"); continue; }
                        File.WriteAllText(path + ".tmp", text, new UTF8Encoding(false));
                        File.Move(path + ".tmp", path, overwrite: true);
                        ok++; lines += rows.Count;
                    }
                    catch (Exception ex) { failed.Add(tab + " (" + ex.Message + ")"); }
                }
                if (ok == Tabs.Length) try { File.WriteAllText(StampPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)); } catch { }
                _failedAt = failed.Count > 0 ? DateTime.UtcNow : (DateTime?)null;
                var said = "compatibility list: " + ok + " of " + Tabs.Length + " tabs downloaded (" + lines + " lines)"
                           + (failed.Count > 0 ? ", kept the copy there for " + string.Join(", ", failed) : "");
                if (failed.Count > 0) Log.Warn(said); else Log.Info(said);
                return said;
            }
        }

        private static DateTime? _failedAt;
        private static int _refreshing;

        /// <summary>Downloaded in the background when there is no copy or it is older than a week - at most one try an hour
        /// after a failure. Never on the caller's time.</summary>
        public static void RefreshLater()
        {
            try
            {
                var when = Downloaded();
                if (when != null && DateTime.Now - when.Value < Stale) return;
                if (_failedAt != null && DateTime.UtcNow - _failedAt.Value < TimeSpan.FromHours(1)) return;
                if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
                Task.Run(() => { try { Download(); } finally { Interlocked.Exchange(ref _refreshing, 0); } });
            }
            catch { }
        }

        // ── reading ──────────────────────────────────────────────────────────

        private sealed class Index
        {
            public Dictionary<string, YmirCompatEntry> ByHash = new Dictionary<string, YmirCompatEntry>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<YmirCompatEntry>> BySerial = new Dictionary<string, List<YmirCompatEntry>>(StringComparer.OrdinalIgnoreCase);
            public int Count;
        }

        /// <summary>The copy, read once per download (kept while downloaded.txt and the tabs are unchanged).</summary>
        private static Index Load()
        {
            var key = string.Join("|", Tabs.Select(t => { try { var f = new FileInfo(FileOf(t)); return f.Exists ? f.Length + ":" + f.LastWriteTimeUtc.Ticks : "-"; } catch { return "?"; } }));
            lock (IndexGate)
            {
                if (_index != null && _indexKey == key) return _index;
                var idx = new Index();
                foreach (var tab in Tabs)
                {
                    try
                    {
                        var path = FileOf(tab);
                        if (!File.Exists(path)) continue;
                        foreach (var e in Parse(File.ReadAllText(path), tab) ?? new List<YmirCompatEntry>())
                        {
                            idx.Count++;
                            if (!string.IsNullOrEmpty(e.Hash) && !idx.ByHash.ContainsKey(e.Hash)) idx.ByHash[e.Hash] = e;
                            foreach (var serial in Serials(e.Serial))
                            {
                                if (!idx.BySerial.TryGetValue(serial, out var list)) idx.BySerial[serial] = list = new List<YmirCompatEntry>();
                                list.Add(e);
                            }
                        }
                    }
                    catch (Exception ex) { Log.Warn("compatibility list: could not read the " + tab + " copy", ex); }
                }
                _index = idx; _indexKey = key;
                return idx;
            }
        }

        private static readonly object IndexGate = new object();
        private static Index _index;
        private static string _indexKey;

        /// <summary>The number of games in the copy (0: none yet).</summary>
        public static int Count() { try { return Load().Count; } catch { return 0; } }

        /// <summary>A serial cell may hold several ("T-1234G, T-1235G"): each, without spaces.</summary>
        private static IEnumerable<string> Serials(string cell)
            => (cell ?? "").Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 2);

        /// <summary>The disc's lines in the copy. Never throws; empty without a copy.</summary>
        public static YmirCompatResult Lookup(SaturnDisc disc)
        {
            var r = new YmirCompatResult();
            try
            {
                if (disc == null) return r;
                var idx = Load();
                if (!string.IsNullOrEmpty(disc.Hash) && idx.ByHash.TryGetValue(disc.Hash, out var exact)) { r.Exact = exact; return r; }
                if (!string.IsNullOrEmpty(disc.Product) && idx.BySerial.TryGetValue(disc.Product.Trim(), out var same)) r.SameProduct.AddRange(same);
            }
            catch (Exception ex) { Log.Warn("compatibility list: lookup", ex); }
            return r;
        }

        /// <summary>One line to show for a disc: "Playable - tested with Ymir 0.2.0" or "Not in the list".</summary>
        public static string Line(YmirCompatResult r)
        {
            if (r == null || !r.Found) return Count() == 0 ? "Ymir's compatibility list is not downloaded yet." : "Not in Ymir's compatibility list yet.";
            if (r.Exact != null)
                return r.Exact.State + (MeaningOf(r.Exact.State) is string m ? " (" + m + ")" : "")
                       + (string.IsNullOrWhiteSpace(r.Exact.Version) ? "" : " - tested with Ymir " + r.Exact.Version.Trim());
            var names = string.Join(", ", r.SameProduct.Select(e => e.Name + ": " + e.State).Distinct().Take(3));
            return "This exact disc is not in the list; same product code: " + names;
        }

        // ── the CSV ──────────────────────────────────────────────────────────

        /// <summary>The lines of a tab, by its column names; null when it is not the list (no "Disc hash" or "Compatibility").</summary>
        internal static List<YmirCompatEntry> Parse(string csv, string tab)
        {
            var rows = Rows(csv);
            if (rows.Count == 0) return null;
            var head = rows[0].Select(h => (h ?? "").Trim()).ToList();
            int Col(params string[] names) => head.FindIndex(h => names.Any(n => string.Equals(h, n, StringComparison.OrdinalIgnoreCase)));
            int name = Col("Game name", "Base game name"), serial = Col("Serial", "Product code"), hash = Col("Disc hash"), state = Col("Compatibility"),
                version = Col("Ymir version"), bios = Col("BIOS region/version", "BIOS"), notes = Col("Additional notes", "Notes");
            if (hash < 0 || state < 0) return null;
            string Cell(List<string> row, int i) => i >= 0 && i < row.Count ? (row[i] ?? "").Trim() : "";
            var list = new List<YmirCompatEntry>();
            foreach (var row in rows.Skip(1))
            {
                var h = Cell(row, hash);
                // The description row, and blank lines: no hash of 32 hex digits.
                if (h.Length != 32 || !h.All(Uri.IsHexDigit)) continue;
                list.Add(new YmirCompatEntry
                {
                    Name = Cell(row, name), Serial = Cell(row, serial), Hash = h.ToUpperInvariant(), State = Cell(row, state),
                    Version = Cell(row, version), Bios = Cell(row, bios), Notes = Cell(row, notes), Tab = tab,
                });
            }
            return list;
        }

        /// <summary>RFC 4180: quoted fields, "" inside them, line breaks inside them.</summary>
        internal static List<List<string>> Rows(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (csv ?? "").Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == ',') { row.Add(cell.ToString()); cell.Clear(); continue; }
                if (c == '\r') continue;
                if (c == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>(); continue; }
                cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
