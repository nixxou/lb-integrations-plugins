// A game's title updates and DLC, put where Xenia looks for them - for the game being launched, as it was chosen
// (Mehdi, 01/10).
//
// WHERE XENIA LOOKS (content_manager.cc ListContent, kernel_state.cc FindTitleUpdate): every file of
// content\0000000000000000\<title id>\000B0000 (updates - it applies the FIRST, and only when its patch names the
// game's executable) and \00000002 (DLC - the game sees them all). A package there is read whole, its header inside it:
// no .header beside it is needed. Measured 01/10 on Real Steel: the update applied ("XEX patch applied successfully:
// base version: 0.0.0.5, new version: 0.0.3.5") and the three add-ons listed, through what follows.
//
// WHAT IS DONE, at each launch:
//   1. the CANDIDATES - from the scan of the game's folder (XeniaScan), by content only: updates whose patch names this
//      game's executable (the same digest - Xenia's own test), DLC of its title id; the same package twice (one
//      content id) is one;
//   2. the CHOICE - the game's (games\<game id>.extras beside its options): one update or none, each DLC on or off. By
//      default the update that goes furthest, and every DLC;
//   3. the STORE - <content folder>\<game>\store: each package chosen, once - extracted from its archive (the one write
//      to disk this costs), or a hard link to the file when it is loose on the same drive (no space), a copy otherwise;
//   4. the LINKS - <content folder>\<game>\000B0000 and \00000002 hold hard links to the store, for what is chosen
//      and nothing else: changing the choice is instant, nothing is extracted again;
//   5. the JUNCTIONS - Xenia's content\0000000000000000\<title id>\000B0000 and \00000002 point at those two folders.
//      A junction needs no administrator and crosses drives. Two versions of one game (one title id) take turns: the
//      junction follows the game launched. A REAL folder there - Xenia's own "Install Content" - is never touched:
//      that kind of content is left as it is, and the log says so.
// THE SIZE LIMIT (0 = none): before extracting, the games launched longest ago lose their whole folder - store, links -
// until the new content fits; never the game being launched, never part of a game. The user's own files are never
// deleted: the store holds copies or links.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaExtra
    {
        public XeniaScanEntry Entry;          // the first of its copies
        public List<XeniaScanEntry> Copies = new List<XeniaScanEntry>();
        public bool Matches = true;           // an update: its patch names this game's executable
        public bool InStore;                  // already extracted (or linked) for this game
        public long Size;                     // its size once put down
        public string ContentId => Entry.ContentId.Length > 0 ? Entry.ContentId : Entry.Path;
    }

    internal sealed class XeniaGameExtras
    {
        public XeniaScanEntry Game;
        public List<XeniaExtra> Updates = new List<XeniaExtra>();
        public List<XeniaExtra> Dlc = new List<XeniaExtra>();
        public string Folder;                 // the game's folder in the content folder
    }

    internal static class XeniaExtras
    {
        public const string UpdateType = "000B0000", DlcType = "00000002";
        private const string CommonXuid = "0000000000000000";
        private const string Manifest = "lbip-game.txt";

        // ── the settings ─────────────────────────────────────────────────────

        public static string SettingsPath => Path.Combine(XeniaSettings.Dir, "content.ini");

        public static Dictionary<string, string> ReadSettings()
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath))
                    {
                        var at = line.IndexOf('=');
                        if (at > 0 && !line.TrimStart().StartsWith("#")) v[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                    }
            }
            catch (Exception ex) { Log.Warn("could not read " + SettingsPath, ex); }
            return v;
        }

        public static void WriteSettings(IDictionary<string, string> values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            var lines = new List<string> { "# Xenia: where title updates and DLC are put down for Xenia, and how much room they may take. Edited by the Nixx window." };
            lines.AddRange(values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
            File.WriteAllLines(SettingsPath, lines);
            Log.Info("content settings written -> " + SettingsPath);
        }

        /// <summary>The content folder: the setting, else &lt;Xenia's folder&gt;\lbip-content.</summary>
        public static string ContentFolder(XeniaLayout layout)
        {
            var set = ReadSettings().TryGetValue("folder", out var f) ? f : null;
            return !string.IsNullOrWhiteSpace(set) ? set : Path.Combine(layout.InstallDir, "lbip-content");
        }

        /// <summary>The limit in bytes, 0 for none.</summary>
        public static long Limit()
        {
            var s = ReadSettings().TryGetValue("limit_gb", out var v) ? v : "0";
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var gb) && gb > 0 ? (long)(gb * 1024 * 1024 * 1024) : 0;
        }

        // ── the choice ───────────────────────────────────────────────────────

        public static string ChoicePath(string gameId)
        {
            var p = XeniaSettings.GamePath(gameId);
            return p == null ? null : Path.ChangeExtension(p, ".extras");
        }

        /// <summary>The game's choice: "update" = a content id, "none", or absent (the default); "dlc.&lt;content id&gt;" = off.</summary>
        public static Dictionary<string, string> ReadChoice(string gameId)
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = ChoicePath(gameId);
            try
            {
                if (path != null && File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var at = line.IndexOf('=');
                        if (at > 0 && !line.TrimStart().StartsWith("#")) v[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim();
                    }
            }
            catch (Exception ex) { Log.Warn("could not read " + path, ex); }
            return v;
        }

        public static void WriteChoice(string gameId, IDictionary<string, string> values)
        {
            var path = ChoicePath(gameId);
            if (path == null) return;
            if (values.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, new[] { "# Xenia: this game's title update and DLC. Edited by its options window." }
                                     .Concat(values.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
        }

        /// <summary>The update chosen - null for none - and the DLC on, from a choice over candidates.</summary>
        public static (XeniaExtra Update, List<XeniaExtra> Dlc) Chosen(XeniaGameExtras x, IDictionary<string, string> choice)
        {
            XeniaExtra update;
            if (choice.TryGetValue("update", out var u))
                update = u == "none" ? null : x.Updates.FirstOrDefault(e => e.ContentId == u && e.Matches) ?? Default(x);
            else update = Default(x);
            var dlc = x.Dlc.Where(d => !(choice.TryGetValue("dlc." + d.ContentId, out var off) && off == "off")).ToList();
            return (update, dlc);
        }

        /// <summary>The update that goes furthest, of those whose patch names the game.</summary>
        public static XeniaExtra Default(XeniaGameExtras x)
            => x.Updates.Where(e => e.Matches).OrderByDescending(e => e.Entry.PatchTo).FirstOrDefault();

        // ── the candidates ───────────────────────────────────────────────────

        /// <summary>The game at <paramref name="rom"/> and what its folder holds for it - from the scan's cache (the caller has
        /// scanned). Null when the game is not among what was scanned.</summary>
        public static XeniaGameExtras For(string rom, XeniaLayout layout)
        {
            if (string.IsNullOrWhiteSpace(rom)) return null;
            // EVERYTHING THE SCANS HAVE SEEN, not only the game's folder: an import's folders too - No-Intro's sets put the
            // updates beside the games, not under them. Matched by content (title id, digest); what is no longer there is left out.
            var all = XeniaScan.CachedAll().Where(e => SourceExists(e.Path)).ToList();
            var game = all.FirstOrDefault(e => e.Kind == XeniaFileKind.Game && string.Equals(e.Path, rom, StringComparison.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(e => e.Kind == XeniaFileKind.Game && e.Path.StartsWith(rom + "|", StringComparison.OrdinalIgnoreCase));
            // A launcher (Minecraft's disc): the game it starts is what its updates and DLC go with - XeniaRelaunch.
            var started = XeniaRelaunch.TargetOf(rom);
            if (started != null)
            {
                var fi = new FileInfo(started);
                var real = XeniaScan.Classify(started, fi.Exists ? fi.Length : 0, fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0);
                if (real.Kind == XeniaFileKind.Game && real.TitleId.Length > 0) { real.Path = game?.Path ?? rom; game = real; }
            }
            if (game == null || game.TitleId.Length == 0) return null;

            var x = new XeniaGameExtras { Game = game, Folder = GameFolder(layout, rom, game.TitleId) };
            var store = StoreIndex(x.Folder);
            List<XeniaExtra> Group(IEnumerable<XeniaScanEntry> entries) => entries
                .GroupBy(e => e.ContentId.Length > 0 ? e.ContentId : e.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    // A loose file before an archive's entry: it may need no extraction at all.
                    var copies = g.OrderBy(e => e.Path.Contains('|') ? 1 : 0).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
                    var extra = new XeniaExtra { Entry = copies[0], Copies = copies };
                    extra.InStore = store.ContainsKey(extra.ContentId) && File.Exists(Path.Combine(x.Folder, "store", store[extra.ContentId]));
                    extra.Size = SizeOf(copies[0]);
                    return extra;
                }).ToList();

            x.Updates = Group(all.Where(e => e.Kind == XeniaFileKind.Update && e.TitleId == game.TitleId));
            // Xenia's test: the patch names the executable. A game whose digest is unknown (Games on Demand) takes them all.
            foreach (var u in x.Updates) u.Matches = game.Digest.Length == 0 || string.Equals(u.Entry.Digest, game.Digest, StringComparison.OrdinalIgnoreCase);
            x.Dlc = Group(all.Where(e => e.Kind == XeniaFileKind.Dlc && e.TitleId == game.TitleId));
            return x;
        }

        private static bool SourceExists(string path)
        {
            try { var bar = path.IndexOf('|'); var p = bar >= 0 ? path.Substring(0, bar) : path; return File.Exists(p) || Directory.Exists(p); }
            catch { return false; }
        }

        private static long SizeOf(XeniaScanEntry e)
        {
            try
            {
                if (!e.Path.Contains('|')) return new FileInfo(e.Path).Length;
                var (archive, key) = Split(e.Path);
                using var a = Archives.Open(archive);
                return a.Entries.FirstOrDefault(x => x.Key != null && x.Key.Replace('\\', '/') == key)?.Size ?? 0;
            }
            catch { return 0; }
        }

        private static (string Archive, string Key) Split(string path)
        {
            var bar = path.IndexOf('|');
            return (path.Substring(0, bar), path.Substring(bar + 1));
        }

        // ── the game's folder in the content folder ──────────────────────────

        /// <summary>&lt;content folder&gt;\&lt;the game's file name&gt; - another game already there under that name takes " [title id]",
        /// then a number. Which game a folder is, is in its lbip-game.txt.</summary>
        public static string GameFolder(XeniaLayout layout, string rom, string titleId)
        {
            var root = ContentFolder(layout);
            var name = Safe(Directory.Exists(rom) ? Path.GetFileName(rom.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(rom));
            foreach (var candidate in new[] { name, name + " [" + titleId + "]" }.Concat(Enumerable.Range(2, 50).Select(i => name + " [" + titleId + "] (" + i + ")")))
            {
                var dir = Path.Combine(root, candidate);
                var owner = ManifestValue(dir, "rom");
                if (owner == null || string.Equals(owner, rom, StringComparison.OrdinalIgnoreCase)) return dir;
            }
            return Path.Combine(root, name + " [" + titleId + "] " + Guid.NewGuid().ToString("N").Substring(0, 6));
        }

        private static string Safe(string name)
            => new string((name ?? "game").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');

        private static string ManifestValue(string dir, string key)
        {
            try
            {
                var path = Path.Combine(dir, Manifest);
                if (!File.Exists(path)) return null;
                foreach (var line in File.ReadAllLines(path))
                    if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return line.Substring(key.Length + 1);
            }
            catch { }
            return null;
        }

        private static void WriteManifest(string dir, string rom, string titleId, bool keep = false)
            => File.WriteAllLines(Path.Combine(dir, Manifest), new[]
            {
                "# Which game this folder is - the Xenia plugin puts its title updates and DLC down here. Safe to delete: they are put back at its next launch.",
                "rom=" + rom, "title_id=" + titleId, "last_used=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                "keep=" + (keep ? "on" : "off"),
            });

        /// <summary>The game's "keep" written into its folder at once - the Session tab's box, without waiting for a launch.</summary>
        public static void SetKeep(string gameFolder, bool keep)
        {
            try
            {
                var path = Path.Combine(gameFolder, Manifest);
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("keep=", StringComparison.OrdinalIgnoreCase)).ToList();
                lines.Add("keep=" + (keep ? "on" : "off"));
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex) { Log.Warn("extras: keep of " + gameFolder, ex); }
        }

        /// <summary>content id -> file name, for what the store holds.</summary>
        private static Dictionary<string, string> StoreIndex(string gameFolder)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(gameFolder, "store", "store.tsv");
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var c = line.Split('\t');
                        if (c.Length >= 2) map[c[0]] = c[1];
                    }
            }
            catch { }
            return map;
        }

        // ── the plan: where a launch would put the content, and why ──────────

        /// <summary>The game's placement, from its options (Mehdi, 01/10): "auto" (the rule), "ram" (always the RAM disk when one
        /// can be had, whatever the size or what is on the disk), "disk" (always the disk).</summary>
        public static string Placement(IDictionary<string, string> choice)
            => choice.TryGetValue("placement", out var p) && (p == "ram" || p == "disk") ? p : "auto";

        /// <summary>"keep" on: the game's folder on the disk is never purged, and does not count towards the size limit.</summary>
        public static bool Keep(IDictionary<string, string> choice)
            => choice.TryGetValue("keep", out var k) && (k == "on" || k == "1" || k == "true");

        internal sealed class XeniaPlan
        {
            public bool Archived;                 // the game itself comes in an archive: it is unpacked too
            public long Game, Update, Dlc;        // their sizes once unpacked (Game 0 when it is opened where it is)
            public long Total => Game + Update + Dlc;
            public bool GameOnDisk;               // the game already unpacked on the disk
            public long OnDisk, Unused;           // the game's folder on the disk now, and what of it the choice does not use
            public bool Ram;                      // the next launch puts it on a RAM disk
            public string Why = "";
            public string Placement = "auto";
            public bool Keep;
            public string Folder;                 // the game's folder on the disk
        }

        /// <summary>What the next launch would do with <paramref name="choice"/>: sizes, and RAM disk or disk, with the reason.
        /// Nothing is unpacked or mounted.</summary>
        public static XeniaPlan Plan(XeniaGameExtras x, IDictionary<string, string> choice, XeniaLayout layout)
        {
            var (update, dlc) = Chosen(x, choice);
            var p = new XeniaPlan
            {
                Archived = x.Game.Path.Contains('|'), Update = update?.Size ?? 0, Dlc = dlc.Sum(d => d.Size),
                Placement = Placement(choice), Keep = Keep(choice), Folder = x.Folder,
            };
            if (p.Archived) p.Game = GameSize(x.Game);
            p.GameOnDisk = p.Archived && File.Exists(Path.Combine(x.Folder, "game", Leaf(x.Game.Path)));
            p.OnDisk = SizeOfFolder(Path.Combine(x.Folder, "store")) + SizeOfFolder(Path.Combine(x.Folder, "game"));
            var used = new HashSet<string>((update != null ? new[] { update } : new XeniaExtra[0]).Concat(dlc).Select(e => e.ContentId), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in StoreIndex(x.Folder))
                if (!used.Contains(kv.Key)) { try { p.Unused += new FileInfo(Path.Combine(x.Folder, "store", kv.Value)).Length; } catch { } }

            bool onDisk = p.GameOnDisk || (update != null && update.InStore) || dlc.Any(d => d.InStore);
            if (p.Total == 0) { p.Why = "nothing to unpack: the game is opened where it is, with no update or DLC"; return p; }
            if (p.Placement == "disk") { p.Why = "the game's options say: always the disk"; return p; }
            if (p.Placement == "auto" && onDisk) { p.Why = "part of it is on the disk already - what is there is read from there"; return p; }
            var no = XeniaRamSession.WhyNot(layout, p.Total, forced: p.Placement == "ram");
            p.Ram = no == null;
            p.Why = p.Ram ? (p.Placement == "ram" ? "the game's options say: always the RAM disk" : "it fits under the RAM disk threshold") : no;
            return p;
        }

        /// <summary>What the game's folder holds that the choice does not use - unpacked once, unticked since - deleted. The
        /// bytes freed.</summary>
        public static long FreeUnused(XeniaGameExtras x, IDictionary<string, string> choice)
        {
            var (update, dlc) = Chosen(x, choice);
            var used = new HashSet<string>((update != null ? new[] { update } : new XeniaExtra[0]).Concat(dlc).Select(e => e.ContentId), StringComparer.OrdinalIgnoreCase);
            var store = Path.Combine(x.Folder, "store");
            var index = Path.Combine(store, "store.tsv");
            if (!File.Exists(index)) return 0;
            long freed = 0;
            var keep = new List<string>();
            foreach (var line in File.ReadAllLines(index))
            {
                var c = line.Split('\t');
                if (c.Length < 2 || used.Contains(c[0])) { keep.Add(line); continue; }
                var file = Path.Combine(store, c[1]);
                try
                {
                    foreach (var type in new[] { UpdateType, DlcType }) { var l = Path.Combine(x.Folder, type, c[1]); if (File.Exists(l)) File.Delete(l); }
                    if (File.Exists(file)) { freed += new FileInfo(file).Length; File.Delete(file); }
                }
                catch (Exception ex) { Log.Warn("extras: could not free " + file, ex); keep.Add(line); }
            }
            File.WriteAllLines(index, keep);
            Log.Info("extras: " + (freed >> 20) + " MB freed in " + x.Folder + " - what the choice no longer uses");
            return freed;
        }
        // ── at launch ────────────────────────────────────────────────────────

        /// <summary>What a launch puts down: the game's package when it is in an archive (Xenia reads none - Mehdi, 01/10: the
        /// plugin unpacks it, never LaunchBox, so that it can go to a RAM disk), and the chosen update and DLC. Returns the
        /// path to hand Xenia as --target when the game was unpacked, null when Xenia is to open the game's own file.
        /// Never throws; what it could not do is logged, and the launch goes on.</summary>
        public static string Prepare(string rom, string gameId, XeniaLayout layout, string exe)
        {
            try
            {
                XeniaRamSession.Release("a game is being launched");
                var x = For(rom, layout);
                if (x == null) { Log.Info("extras: " + rom + " is not among the scanned games - nothing put down"); return null; }
                var (update, dlc) = Chosen(x, ReadChoice(gameId));
                var extras = (update != null ? new[] { update } : new XeniaExtra[0]).Concat(dlc).ToList();
                bool archived = x.Game.Path.Contains('|');
                Log.Info("extras of " + x.Game.TitleId + ": " + (update != null ? "update \"" + update.Entry.Name + "\" (" + XeniaScan.VersionText(update.Entry.PatchTo) + ")" : "no update")
                         + ", " + dlc.Count + " of " + x.Dlc.Count + " DLC" + (x.Updates.Count(u => !u.Matches) > 0 ? ", " + x.Updates.Count(u => !u.Matches) + " update(s) for another version of the game left out" : "")
                         + (archived ? ", the game itself in its archive" : ""));

                var content = Path.Combine(layout.ContentRoot, CommonXuid, x.Game.TitleId);
                if (!archived && extras.Count == 0 && !Directory.Exists(content)) return null;   // nothing to put, nothing put before

                // WHERE (Mehdi, 01/10) - Plan: what is on the disk already stays read from the disk; else, under the threshold, a
                // RAM disk for all of it; else the disk - unless the game's options force one or the other.
                var gameFile = archived ? Path.Combine(x.Folder, "game", Leaf(x.Game.Path)) : null;
                var choice = ReadChoice(gameId);
                var plan = Plan(x, choice, layout);
                Log.Info("extras: " + (plan.Ram ? "RAM disk" : "disk") + " - " + plan.Why);
                string baseDir = null;
                if (plan.Ram) baseDir = XeniaRamSession.Open(layout, x.Game.TitleId, Safe(Path.GetFileName(x.Folder)), plan.Total, exe, forced: plan.Placement == "ram");
                bool ram = baseDir != null;
                if (!ram)
                {
                    baseDir = x.Folder;
                    Directory.CreateDirectory(Path.Combine(baseDir, "store"));
                    WriteManifest(baseDir, rom, x.Game.TitleId, plan.Keep);
                    long needed = (archived && !File.Exists(gameFile) ? GameSize(x.Game) : 0) + extras.Where(w => !w.InStore && w.Entry.Path.Contains('|')).Sum(w => w.Size);
                    MakeRoom(ContentFolder(layout), baseDir, needed, layout);
                }
                else Directory.CreateDirectory(Path.Combine(baseDir, "store"));

                string target = null;
                bool slow = ram || archived || extras.Any(e => !e.InStore);
                using (var window = slow ? XeniaProgressWindow.Open("Nixx-Xenia - Preparing the game") : null)
                {
                    int n = 0, count = (archived ? 1 : 0) + extras.Count;
                    if (archived)
                    {
                        n++;
                        target = PutGame(baseDir, x.Game, (step, f) => window?.Report("(" + n + "/" + count + ") " + step, f));
                    }
                    foreach (var w in extras)
                    {
                        n++;
                        if (ram || !w.InStore) PutInStore(baseDir, w, (step, f) => window?.Report("(" + n + "/" + count + ") " + step, f));
                    }
                }

                var store = StoreIndex(baseDir);
                Link(baseDir, UpdateType, update != null ? new[] { update } : new XeniaExtra[0], store);
                Link(baseDir, DlcType, dlc, store);
                if (extras.Count > 0 || Directory.Exists(content))
                {
                    PointAt(Path.Combine(content, UpdateType), Path.Combine(baseDir, UpdateType));
                    PointAt(Path.Combine(content, DlcType), Path.Combine(baseDir, DlcType));
                }
                Log.Info("extras: put down " + (ram ? "on a RAM disk at " : "on the disk at ") + baseDir + (target != null ? " - Xenia is handed " + target : ""));
                return target;
            }
            catch (Exception ex) { Log.Warn("extras: could not put the game's content down", ex); return null; }
        }

        private static string Leaf(string path)
        {
            var key = path.Contains('|') ? Split(path).Key : path;
            return key.Replace('\\', '/').Split('/').Last();
        }

        /// <summary>The size of an archived game unpacked: its package, and a Games on Demand package's .data pieces.</summary>
        private static long GameSize(XeniaScanEntry game)
        {
            try
            {
                var (archive, key) = Split(game.Path);
                using var a = Archives.Open(archive);
                return a.Entries.Where(e => e.Key != null && !e.IsDirectory && Belongs(e.Key, key)).Sum(e => e.Size);
            }
            catch { return 0; }
        }

        private static bool Belongs(string entryKey, string key)
        {
            var k = entryKey.Replace('\\', '/');
            return k == key || k.StartsWith(key + ".data/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>An archived game unpacked into &lt;base&gt;\game, once - a Games on Demand package with its .data folder.
        /// The package's path, for --target.</summary>
        private static string PutGame(string baseDir, XeniaScanEntry game, Action<string, double?> progress)
        {
            var (archive, key) = Split(game.Path);
            var dir = Path.Combine(baseDir, "game");
            var leaf = Leaf(game.Path);
            var target = Path.Combine(dir, leaf);
            var done = Path.Combine(dir, ".done");
            if (File.Exists(target) && File.Exists(done) && File.ReadAllText(done).Trim() == game.Path + "|" + game.Size + "|" + game.Ticks) return target;
            Directory.CreateDirectory(dir);
            using var a = Archives.Open(archive);
            var entries = a.Entries.Where(e => e.Key != null && !e.IsDirectory && Belongs(e.Key, key)).ToList();
            long total = Math.Max(1, entries.Sum(e => e.Size)), copied = 0;
            var prefix = key.Contains('/') ? key.Substring(0, key.LastIndexOf('/') + 1) : "";
            foreach (var entry in entries)
            {
                var rel = entry.Key.Replace('\\', '/').Substring(prefix.Length).Replace('/', '\\');
                var path = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Copy(entry, path, n => progress("Unpacking " + game.Name, (double)(copied + n) / total));
                copied += entry.Size;
            }
            File.WriteAllText(done, game.Path + "|" + game.Size + "|" + game.Ticks);
            Log.Info("extras: unpacked the game " + game.Path + " -> " + target);
            return target;
        }

        /// <summary>One archive entry to a file, through a temporary one.</summary>
        private static void Copy(SharpCompress.Archives.IArchiveEntry entry, string path, Action<long> progress)
        {
            var tmp = path + ".tmp";
            try
            {
                using (var src = entry.OpenEntryStream())
                using (var dst = File.Create(tmp))
                {
                    var buffer = new byte[1 << 20];
                    long done = 0;
                    int r;
                    while ((r = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        dst.Write(buffer, 0, r);
                        done += r;
                        progress(done);
                    }
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
        }

        /// <summary>One package into the store, once: from its archive, else a hard link to the loose file, else a copy.</summary>
        private static void PutInStore(string baseDir, XeniaExtra w, Action<string, double?> progress)
        {
            var store = Path.Combine(baseDir, "store");
            var e = w.Entry;
            var name = Leaf(e.Path);
            var target = Path.Combine(store, name);
            if (File.Exists(target)) target = Path.Combine(store, w.ContentId.Length <= 42 ? w.ContentId : name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                if (e.Path.Contains('|'))
                {
                    var (archive, key) = Split(e.Path);
                    using var a = Archives.Open(archive);
                    var entry = a.Entries.First(x => x.Key != null && x.Key.Replace('\\', '/') == key);
                    long size = Math.Max(1, entry.Size);
                    Copy(entry, target, n => progress("Unpacking " + e.Name, (double)n / size));
                    Log.Info("extras: unpacked " + e.Path + " -> " + target);
                }
                else if (!CreateHardLink(target, e.Path, IntPtr.Zero))
                {
                    progress("Copying " + e.Name, null);
                    File.Copy(e.Path, target + ".tmp");
                    File.Move(target + ".tmp", target);
                    Log.Info("extras: copied " + e.Path + " -> " + target + " (another drive: no hard link)");
                }
                else Log.Info("extras: linked " + e.Path + " -> " + target);
                File.AppendAllLines(Path.Combine(store, "store.tsv"), new[] { w.ContentId + "\t" + Path.GetFileName(target) + "\t" + e.Path });
            }
            catch (Exception ex) { Log.Warn("extras: " + e.Path + " could not be put down", ex); }
        }

        /// <summary>&lt;game folder&gt;\&lt;type&gt; made to hold hard links to exactly these packages of the store.</summary>
        private static void Link(string gameFolder, string type, IEnumerable<XeniaExtra> chosen, Dictionary<string, string> store)
        {
            var dir = Path.Combine(gameFolder, type);
            Directory.CreateDirectory(dir);
            var want = chosen.Where(c => store.ContainsKey(c.ContentId)).Select(c => store[c.ContentId]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(dir))
                if (!want.Contains(Path.GetFileName(f))) File.Delete(f);   // a link: the store keeps the file
            foreach (var name in want)
            {
                var link = Path.Combine(dir, name);
                if (File.Exists(link)) continue;
                if (!CreateHardLink(link, Path.Combine(gameFolder, "store", name), IntPtr.Zero))
                    Log.Warn("extras: could not link " + name + " into " + dir + " (error " + Marshal.GetLastWin32Error() + ")");
            }
        }

        /// <summary>Xenia's folder made a junction to ours - unless it is a real folder with something in it: Xenia's own install.</summary>
        private static void PointAt(string xeniaDir, string ours)
        {
            try
            {
                if (Directory.Exists(xeniaDir))
                {
                    bool link = File.GetAttributes(xeniaDir).HasFlag(FileAttributes.ReparsePoint);
                    if (!link && Directory.EnumerateFileSystemEntries(xeniaDir).Any())
                    {
                        Log.Info("extras: " + xeniaDir + " is a folder of Xenia's own, with content in it - left as it is");
                        return;
                    }
                    if (link && string.Equals(Target(xeniaDir), ours, StringComparison.OrdinalIgnoreCase)) return;
                    Directory.Delete(xeniaDir);   // a link, or an empty folder: never what a link points at
                }
                Directory.CreateDirectory(Path.GetDirectoryName(xeniaDir));
                var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("mklink"); psi.ArgumentList.Add("/J"); psi.ArgumentList.Add(xeniaDir); psi.ArgumentList.Add(ours);
                using var p = Process.Start(psi);
                var said = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                if (!Directory.Exists(xeniaDir)) Log.Warn("extras: junction " + xeniaDir + " -> " + ours + " not made: " + said.Trim());
            }
            catch (Exception ex) { Log.Warn("extras: " + xeniaDir, ex); }
        }

        private static string Target(string link)
        {
            try { return new DirectoryInfo(link).LinkTarget; } catch { return null; }
        }

        /// <summary>Room for <paramref name="needed"/> bytes under the limit: the games launched longest ago lose their folder,
        /// whole - never <paramref name="keep"/>. Xenia's junctions into a folder removed go with it.</summary>
        private static void MakeRoom(string root, string keep, long needed, XeniaLayout layout)
        {
            long limit = Limit();
            if (limit <= 0 || needed <= 0 || !Directory.Exists(root)) return;
            // A game kept (its Session tab) is never purged, and takes no part in the count (Mehdi, 01/10).
            var games = Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, Manifest)) && ManifestValue(d, "keep") != "on")
                .Select(d => (Dir: d, Used: DateTime.TryParse(ManifestValue(d, "last_used"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue,
                              Size: SizeOfFolder(Path.Combine(d, "store")) + SizeOfFolder(Path.Combine(d, "game"))))
                .ToList();
            long total = games.Sum(g => g.Size);
            foreach (var g in games.Where(g => !string.Equals(g.Dir, keep, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Used))
            {
                if (total + needed <= limit) break;
                Unlink(layout, g.Dir);
                try { Directory.Delete(g.Dir, recursive: true); total -= g.Size; Log.Info("extras: " + g.Dir + " removed to make room (last launched " + g.Used.ToLocalTime() + ")"); }
                catch (Exception ex) { Log.Warn("extras: could not remove " + g.Dir, ex); }
            }
            if (total + needed > limit) Log.Info("extras: " + (total + needed) / (1024 * 1024) + " MB is over the limit even so - this game alone needs it");
        }

        /// <summary>Xenia's junctions that point into <paramref name="gameFolder"/>, removed (the links only).</summary>
        private static void Unlink(XeniaLayout layout, string gameFolder)
        {
            try
            {
                var common = Path.Combine(layout.ContentRoot, CommonXuid);
                if (!Directory.Exists(common)) return;
                foreach (var title in Directory.GetDirectories(common))
                    foreach (var type in new[] { UpdateType, DlcType })
                    {
                        var d = Path.Combine(title, type);
                        if (!Directory.Exists(d) || !File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)) continue;
                        var t = Target(d);
                        if (t != null && t.StartsWith(gameFolder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) Directory.Delete(d);
                    }
            }
            catch (Exception ex) { Log.Warn("extras: junctions into " + gameFolder, ex); }
        }

        private static long SizeOfFolder(string dir)
        {
            // The store and the game's own unpacked package (with a Games on Demand .data folder): the 000B0000 and 00000002 folders
            // hold links to the store, and take no room of their own.
            try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0; } catch { return 0; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
    }
}