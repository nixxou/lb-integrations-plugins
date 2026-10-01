// Every Xbox 360 file of a folder and its subfolders, sorted into what it is - games, title updates, DLC - with the
// files that look like ours and are not, and why (Mehdi, 01/10: "detect and sort everything, note what is invalid,
// and when we look again, use the cache").
//
// WHICH FILES. A folder of Xbox 360 games holds little else, so the rule is wide and the CONTENT decides:
//   - .iso, .xex, .zar;
//   - a file with NO extension - title updates, DLC, Xbox Live Arcade and Games on Demand packages almost always
//     have none ("4D5307E6..." of 42 hex digits);
//   - an "extension" holding a digit - the names XboxUnity gives title updates, "TU_10ID8B4_0000014000000.00000000000O4".
// Each one is opened and its first bytes read: "CON " / "LIVE" / "PIRS" is an STFS package, whose header says its
// type, its title id, its version (Stfs); XEX2 is an executable; an XDVDFS volume is a disc image. A file the rule
// takes that none of these is, is INVALID, with the reason - listed, never used.
//
// TWO SHAPES ARE ONE ENTRY, NOT MANY FILES: a folder holding default.xex is an extracted disc (the folder is the game,
// nothing under it is looked at); the <name>.data folder beside a Games on Demand package is that package's data.
//
// THE CACHE: <plugin data>\xenia-scan.tsv, one line per file ever scanned, keyed by its full path, kept while its
// size and its date are the same. Looking again at a folder walks it - cheap - and READS only what is new or changed;
// what is gone is dropped. So the first scan of a big folder takes its time, and every later one is quick: the walk
// and a lookup per file. Plain text, readable.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xenia
{
    internal enum XeniaFileKind
    {
        Game,           // a disc image, an extracted disc, an executable, an STFS game (Arcade, Games on Demand, demo)
        GameNoId,       // a .zar: a game, but nothing in it says which
        Update,         // STFS 000B0000
        Dlc,            // STFS 00000002
        Other,          // any other STFS package: a theme, a gamer picture, a save...
        Invalid,        // taken by the rule, and not Xbox 360 content: Problem says why
    }

    internal sealed class XeniaScanEntry
    {
        public string Path;
        public long Size;
        public long Ticks;              // last write, UTC
        public XeniaFileKind Kind;
        public string TitleId = "";     // 8 hex digits, empty when unknown
        public uint ContentType, MediaId, Version, BaseVersion;
        public byte Disc;
        public string Name = "";        // the package's name, or the file's
        public string Problem = "";     // why it is Invalid

        public string VersionText => XeniaScan.VersionText(Version);

        public override string ToString()
            => Kind + " " + TitleId + " " + (Kind == XeniaFileKind.Update ? "v" + VersionText + " " : "") + Name + (Problem.Length > 0 ? " - " + Problem : "");
    }

    internal static class XeniaScan
    {
        private static readonly string[] Extensions = { ".iso", ".xex", ".zar" };
        private static readonly object Gate = new object();

#pragma warning disable CS0649
        /// <summary>For the probe: the cache somewhere else. Set by reflection.</summary>
        internal static string CacheOverride;
#pragma warning restore CS0649

        public static string CachePath => CacheOverride ?? System.IO.Path.Combine(XeniaSettings.Dir, "xenia-scan.tsv");

        /// <summary>Is this a file the rule looks at? By its name only.</summary>
        internal static bool Taken(string path)
        {
            var ext = System.IO.Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return true;
            if (Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return true;
            return ext.Skip(1).Any(char.IsDigit);
        }

        /// <summary>xex2_version: major.minor.build.qfe, from the top bits down.</summary>
        public static string VersionText(uint v)
            => (v >> 28) + "." + ((v >> 24) & 0xF) + "." + ((v >> 8) & 0xFFFF) + "." + (v & 0xFF);

        /// <summary>Everything under <paramref name="root"/>, read where the cache does not already know it. <paramref name="progress"/>
        /// gets a line and a fraction; <paramref name="cancel"/> stops the reading (what was read is kept). Never throws:
        /// a folder that cannot be read is skipped, and logged.</summary>
        public static List<XeniaScanEntry> Scan(string root, Action<string, double?> progress = null, Func<bool> cancel = null)
        {
            var result = new List<XeniaScanEntry>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
            root = System.IO.Path.GetFullPath(root);
            var started = DateTime.UtcNow;

            progress?.Invoke("Looking for Xbox 360 files...", null);
            var files = new List<string>();
            var folders = new List<string>();
            // A game at the root of a drive (G:\game.iso) is looked at beside it, never through the whole drive.
            bool driveRoot = string.Equals(root.TrimEnd('\\'), (System.IO.Path.GetPathRoot(root) ?? "").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            Walk(root, files, folders, deep: !driveRoot);

            var cache = Load();
            var under = root.TrimEnd('\\') + "\\";
            int n = files.Count + folders.Count, done = 0, read = 0;
            foreach (var path in folders.Concat(files))
            {
                if (cancel?.Invoke() == true) { Log.Info("scan of " + root + ": stopped after " + done + " of " + n); break; }
                done++;
                long size = 0, ticks = 0;
                try
                {
                    if (Directory.Exists(path)) { ticks = File.GetLastWriteTimeUtc(System.IO.Path.Combine(path, "default.xex")).Ticks; }
                    else { var fi = new FileInfo(path); size = fi.Length; ticks = fi.LastWriteTimeUtc.Ticks; }
                }
                catch { continue; }
                if (cache.TryGetValue(path, out var known) && known.Size == size && known.Ticks == ticks) { result.Add(known); continue; }
                progress?.Invoke("Reading " + System.IO.Path.GetFileName(path), (double)done / Math.Max(1, n));
                var entry = Classify(path, size, ticks);
                read++;
                cache[path] = entry;
                result.Add(entry);
            }

            // What was under this root and is not any more goes.
            var seen = new HashSet<string>(folders.Concat(files), StringComparer.OrdinalIgnoreCase);
            foreach (var gone in cache.Keys.Where(k => k.StartsWith(under, StringComparison.OrdinalIgnoreCase) && !seen.Contains(k)).ToList())
                cache.Remove(gone);
            Save(cache);

            Log.Info("scan of " + root + ": " + result.Count + " file(s), " + read + " read, " + (result.Count - read) + " from the cache, "
                     + string.Join(", ", result.GroupBy(e => e.Kind).OrderBy(g => g.Key).Select(g => g.Count() + " " + g.Key))
                     + " - " + (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms");
            return result;
        }

        /// <summary>Scan, with a progress window when it takes a while (nothing on screen for a folder the cache knows).
        /// Never throws.</summary>
        public static List<XeniaScanEntry> ScanShowing(string root, string title)
        {
            var window = XeniaProgressWindow.Open(title);
            try { return Scan(root, (step, fraction) => window?.Report(step, fraction)); }
            catch (Exception ex) { Log.Warn("scan of " + root, ex); return new List<XeniaScanEntry>(); }
            finally { window?.Dispose(); }
        }

        /// <summary>What the cache knows of the files under <paramref name="root"/>, without looking at the disk.</summary>
        public static List<XeniaScanEntry> Cached(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return new List<XeniaScanEntry>();
            var under = System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return Load().Values.Where(e => e.Path.StartsWith(under, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void Walk(string folder, List<string> files, List<string> folders, bool deep = true)
        {
            string[] subs, names;
            try { subs = Directory.GetDirectories(folder); names = Directory.GetFiles(folder); }
            catch (Exception ex) { Log.Info("scan: " + folder + " skipped (" + ex.Message + ")"); return; }
            foreach (var f in names) if (Taken(f)) files.Add(f);
            if (!deep) return;
            foreach (var d in subs)
            {
                var name = System.IO.Path.GetFileName(d);
                // A Games on Demand package's data: <package>.data beside <package>.
                if (name.EndsWith(".data", StringComparison.OrdinalIgnoreCase) && File.Exists(d.Substring(0, d.Length - 5))) continue;
                // An extracted disc: the folder is the game, and nothing under it is ours to sort.
                if (File.Exists(System.IO.Path.Combine(d, "default.xex"))) { folders.Add(d); continue; }
                try { if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                Walk(d, files, folders);
            }
        }

        // ── what a file is ───────────────────────────────────────────────────

        internal static XeniaScanEntry Classify(string path, long size, long ticks)
        {
            var e = new XeniaScanEntry { Path = path, Size = size, Ticks = ticks, Name = System.IO.Path.GetFileName(path) };
            try
            {
                if (Directory.Exists(path))
                {
                    var id = XeniaTitleId.Of(path);
                    if (id == null) return Invalid(e, "an extracted disc whose default.xex does not read");
                    e.Kind = XeniaFileKind.Game; e.TitleId = id;
                    return e;
                }
                if (size < 4) return Invalid(e, "empty");
                byte[] head;
                using (var fs = File.OpenRead(path)) head = Xex.ReadAt(fs, 0, 4);
                if (head == null) return Invalid(e, "unreadable");

                if (Stfs.IsStfs(head))
                {
                    var info = Stfs.Read(path);
                    if (info == null) return Invalid(e, "an STFS package whose header does not read");
                    e.TitleId = info.TitleId == 0 ? "" : Xex.Format(info.TitleId);
                    e.ContentType = info.ContentType; e.MediaId = info.MediaId; e.Version = info.Version; e.BaseVersion = info.BaseVersion; e.Disc = info.DiscNumber;
                    var name = info.DisplayName.Length > 0 ? info.DisplayName : info.TitleName;
                    if (name.Length > 0) e.Name = name;
                    if (info.TitleId == 0) return Invalid(e, "an STFS package with no title id");
                    switch (info.ContentType)
                    {
                        case 0x000B0000: e.Kind = XeniaFileKind.Update; break;
                        case 0x00000002: e.Kind = XeniaFileKind.Dlc; break;
                        case 0x00007000:   // Games on Demand
                        case 0x000D0000:   // Xbox Live Arcade
                        case 0x00080000:   // demo
                        case 0x000A0000:   // game title
                        case 0x00004000:   // installed game
                            e.Kind = XeniaFileKind.Game;
                            if (info.VolumeType == 1 && !Directory.Exists(path + ".data")) return Invalid(e, "a Games on Demand package without its " + System.IO.Path.GetFileName(path) + ".data folder");
                            break;
                        default: e.Kind = XeniaFileKind.Other; break;
                    }
                    return e;
                }
                if (Xex.IsXex(head))
                {
                    var id = Xex.TitleIdOfFile(path);
                    if (!id.HasValue) return Invalid(e, "an executable without a title id");
                    e.Kind = XeniaFileKind.Game; e.TitleId = Xex.Format(id.Value);
                    return e;
                }
                var ext = System.IO.Path.GetExtension(path);
                if (ext.Equals(".zar", StringComparison.OrdinalIgnoreCase)) { e.Kind = XeniaFileKind.GameNoId; return e; }
                var disc = Xdvdfs.TitleIdOfImage(path);
                if (disc.HasValue) { e.Kind = XeniaFileKind.Game; e.TitleId = Xex.Format(disc.Value); return e; }
                return Invalid(e, ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) ? "not an Xbox 360 disc image" : "not Xbox 360 content");
            }
            catch (Exception ex) { return Invalid(e, "could not be read: " + ex.Message); }
        }

        private static XeniaScanEntry Invalid(XeniaScanEntry e, string why)
        {
            e.Kind = XeniaFileKind.Invalid;
            e.Problem = why;
            return e;
        }

        // ── the cache file ───────────────────────────────────────────────────

        private const string Header = "path\tsize\tticks\tkind\ttitle_id\tcontent_type\tmedia_id\tversion\tbase_version\tdisc\tname\tproblem";

        private static Dictionary<string, XeniaScanEntry> Load()
        {
            var map = new Dictionary<string, XeniaScanEntry>(StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(CachePath)) return map;
                    foreach (var line in File.ReadAllLines(CachePath, Encoding.UTF8).Skip(1))
                    {
                        var c = line.Split('\t');
                        if (c.Length < 12 || !Enum.TryParse<XeniaFileKind>(c[3], out var kind)) continue;
                        map[c[0]] = new XeniaScanEntry
                        {
                            Path = c[0], Size = L(c[1]), Ticks = L(c[2]), Kind = kind, TitleId = c[4],
                            ContentType = U(c[5]), MediaId = U(c[6]), Version = U(c[7]), BaseVersion = U(c[8]), Disc = (byte)L(c[9]),
                            Name = c[10], Problem = c[11],
                        };
                    }
                }
                catch (Exception ex) { Log.Warn("scan cache: could not be read, starting over", ex); }
            }
            return map;
        }

        private static void Save(Dictionary<string, XeniaScanEntry> map)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CachePath));
                    var lines = new List<string> { Header };
                    lines.AddRange(map.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => string.Join("\t",
                        e.Path, e.Size.ToString(CultureInfo.InvariantCulture), e.Ticks.ToString(CultureInfo.InvariantCulture), e.Kind, e.TitleId,
                        e.ContentType.ToString("X8"), e.MediaId.ToString("X8"), e.Version.ToString("X8"), e.BaseVersion.ToString("X8"),
                        e.Disc.ToString(CultureInfo.InvariantCulture), Clean(e.Name), Clean(e.Problem))));
                    var tmp = CachePath + ".tmp";
                    File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
                    File.Move(tmp, CachePath, overwrite: true);
                }
                catch (Exception ex) { Log.Warn("scan cache: could not be written", ex); }
            }
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        private static long L(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        private static uint U(string s) => uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
