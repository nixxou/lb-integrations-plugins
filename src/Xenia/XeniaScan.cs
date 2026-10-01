// Every Xbox 360 file of a folder and its subfolders, sorted into what it is - games, title updates, DLC - with the
// files that look like ours and are not, and why (Mehdi, 01/10: "detect and sort everything, note what is invalid,
// and when we look again, use the cache").
//
// WHICH FILES. A folder of Xbox 360 games holds little else, so the rule is wide and the CONTENT decides:
//   - .iso, .xex, .zar;
//   - a file with NO extension when it is named as a package is (Mehdi, 01/10: a regex): 40 to 42 hex digits - the
//     console names a package by up to 42 characters (XCONTENT_DATA.file_name_raw[42]), and DLC fill them - or a title
//     update's names, "TU_..." (XboxUnity) and "tu<8 hex>_<8 hex>"; or, whatever its name, when it sits where the
//     console puts one: <title id>\<content type>\<file>;
//   - an "extension" holding a digit - the names XboxUnity gives title updates, "TU_10ID8B4_0000014000000.00000000000O4";
//   - .zip and .7z: No-Intro's digital sets ship each package zipped with the console's tree around it -
//     "Real Steel (World) (XBLA).zip" holds 584111E0/000D0000/62939F79...58. Every entry the same rule takes is read
//     WITHOUT EXTRACTING: its first 0x1711 bytes decompressed, no further, nothing written. A disc image in an archive
//     is noted and not read (Mehdi: nobody zips an ISO).
// Each one is opened and its first bytes read: "CON " / "LIVE" / "PIRS" is an STFS package, whose header says its
// type, its title id, its version (Stfs); XEX2 is an executable; an XDVDFS volume is a disc image. A file the rule
// takes that none of these is, is INVALID, with the reason - listed, never used.
//
// AN INDIE GAME IS FILED AS DLC: Xbox Live Indie Games are marketplace content (00000002) of one common title id,
// 584E07D2 - measured on QbTron 3D (XBLIG), 584E07D2/00000002/<42 hex>. That pair is a game here, not an add-on.
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
        /// <summary>A game's: the SHA-1 of its default.xex's RSA signature. An update's: the digest_source of its patch - the
        /// game it applies to is the one with that digest, Xenia's own test (KernelState::IsPatchSignatureProper). Empty
        /// when it could not be read (a Games on Demand package, a .zar).</summary>
        public string Digest = "";
        /// <summary>An update's patch: the version it takes the game from, and to ("0.0.0.5" -> "0.0.3.5").</summary>
        public uint PatchFrom, PatchTo;
        /// <summary>A package's content id: two files holding the same package have the same one.</summary>
        public string ContentId = "";

        public string VersionText => XeniaScan.VersionText(Version);

        public override string ToString()
            => Kind + " " + TitleId + " " + (Kind == XeniaFileKind.Update ? XeniaScan.VersionText(PatchFrom) + " -> " + XeniaScan.VersionText(PatchTo) + " " : "") + Name
               + (Digest.Length > 0 ? " [" + Digest.Substring(0, 8) + "]" : "") + (Problem.Length > 0 ? " - " + Problem : "");
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

        private static readonly string[] ArchiveExtensions = { ".zip", ".7z" };
        private static readonly System.Text.RegularExpressions.Regex PackageName =
            new System.Text.RegularExpressions.Regex("^([0-9A-Fa-f]{40,42}|TU_.+|tu[0-9A-Fa-f]{8}_[0-9A-Fa-f]{8})$");
        private static readonly System.Text.RegularExpressions.Regex Hex8 = new System.Text.RegularExpressions.Regex("^[0-9A-Fa-f]{8}$");

        /// <summary>The Indie games' common title id - see the header.</summary>
        public const string IndieTitleId = "584E07D2";

        /// <summary>Is this a file the rule looks at? By its name - and, for a name with no extension, where it sits:
        /// <paramref name="path"/> is a file path or an archive entry's, "/" or "\" alike.</summary>
        internal static bool Taken(string path)
        {
            var parts = path.Split('\\', '/');
            var name = parts[parts.Length - 1];
            var ext = System.IO.Path.GetExtension(name);
            if (string.IsNullOrEmpty(ext))
                return PackageName.IsMatch(name)
                       || (parts.Length >= 3 && Hex8.IsMatch(parts[parts.Length - 2]) && Hex8.IsMatch(parts[parts.Length - 3]));
            if (Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return true;
            if (PackageName.IsMatch(name)) return true;
            return ext.Skip(1).Any(char.IsDigit);
        }

        internal static bool IsArchive(string path) => ArchiveExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = files.Count + folders.Count, done = 0, read = 0;
            bool stopped = false;
            foreach (var path in folders.Concat(files))
            {
                if (cancel?.Invoke() == true) { Log.Info("scan of " + root + ": stopped after " + done + " of " + n); stopped = true; break; }
                done++;
                long size = 0, ticks = 0;
                try
                {
                    if (Directory.Exists(path)) { ticks = File.GetLastWriteTimeUtc(System.IO.Path.Combine(path, "default.xex")).Ticks; }
                    else { var fi = new FileInfo(path); size = fi.Length; ticks = fi.LastWriteTimeUtc.Ticks; }
                }
                catch { continue; }

                if (IsArchive(path))
                {
                    // An archive is its entries, "<archive>|<entry>", all kept while the archive is the same.
                    var prefix = path + "|";
                    var known = cache.Values.Where(e => e.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (known.Count > 0 && known.All(e => e.Size == size && e.Ticks == ticks))
                    {
                        result.AddRange(known);
                        foreach (var e in known) seen.Add(e.Path);
                        continue;
                    }
                    progress?.Invoke("Reading " + System.IO.Path.GetFileName(path), (double)done / Math.Max(1, n));
                    foreach (var stale in known) cache.Remove(stale.Path);
                    foreach (var e in ClassifyArchive(path, size, ticks))
                    {
                        cache[e.Path] = e;
                        seen.Add(e.Path);
                        result.Add(e);
                        read++;
                    }
                    continue;
                }

                seen.Add(path);
                if (cache.TryGetValue(path, out var cached) && cached.Size == size && cached.Ticks == ticks) { result.Add(cached); continue; }
                progress?.Invoke("Reading " + System.IO.Path.GetFileName(path), (double)done / Math.Max(1, n));
                var entry = Classify(path, size, ticks);
                read++;
                cache[path] = entry;
                result.Add(entry);
            }

            // What was under this root and is not any more goes - unless the scan was stopped, and did not see everything.
            if (!stopped) foreach (var gone in cache.Keys.Where(k => k.StartsWith(under, StringComparison.OrdinalIgnoreCase) && !seen.Contains(k)).ToList())
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
            foreach (var f in names) if (Taken(f) || IsArchive(f)) files.Add(f);
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
                    var xex = System.IO.Path.Combine(path, "default.xex");
                    XexInfo info;
                    using (var fs = File.OpenRead(xex)) info = Xex.Info(XeniaPackageRead.Reader(fs));
                    if (info == null || !info.HasExecutionInfo) return Invalid(e, "an extracted disc whose default.xex does not read");
                    return FromExecutable(e, info);
                }
                if (size < 4) return Invalid(e, "empty");
                using var file = File.OpenRead(path);
                return FromContent(e, XeniaPackageRead.Reader(file), System.IO.Path.GetExtension(path), () => Directory.Exists(path + ".data"), disc: true);
            }
            catch (Exception ex) { return Invalid(e, "could not be read: " + ex.Message); }
        }

        /// <summary>What a file's content makes of an entry - a file on disk or an archive's entry, read through
        /// <paramref name="read"/>. <paramref name="disc"/>: may it be a disc image (never in an archive: that would mean
        /// decompressing gigabytes to find its executable).</summary>
        private static XeniaScanEntry FromContent(XeniaScanEntry e, Func<long, int, byte[]> read, string ext, Func<bool> hasData, bool disc)
        {
            var head = read(0, Stfs.HeaderSize);
            if (head == null || head.Length < 4) return Invalid(e, "empty");
            if (Stfs.IsStfs(head))
            {
                var info = Stfs.Parse(head);
                FromPackage(e, info, hasData);
                if (info == null) return e;
                e.ContentId = info.ContentId;
                if (e.Kind == XeniaFileKind.Update)
                {
                    // The truth of an update is its patch: the game it applies to (the digest) and the versions.
                    var patch = XeniaPackageRead.UpdatePatch(read);
                    if (patch != null && patch.IsPatch) { e.Digest = patch.PatchDigestSource; e.PatchFrom = patch.PatchSourceVersion; e.PatchTo = patch.PatchTargetVersion; }
                    else e.Problem = "its patch (default.xexp) does not read: which game it applies to is not known";
                }
                else if (e.Kind == XeniaFileKind.Game && info.VolumeType == 0)
                {
                    var xex = XeniaPackageRead.GameExecutable(read);
                    if (xex != null) e.Digest = xex.SignatureDigest;
                }
                return e;
            }
            if (Xex.IsXex(head))
            {
                var info = Xex.Info(read);
                if (info == null || !info.HasExecutionInfo) return Invalid(e, "an executable without a title id");
                return FromExecutable(e, info);
            }
            if (ext.Equals(".zar", StringComparison.OrdinalIgnoreCase)) { e.Kind = XeniaFileKind.GameNoId; return e; }
            if (disc)
            {
                var info = XeniaPackageRead.DiscExecutable(read);
                if (info != null && info.HasExecutionInfo) return FromExecutable(e, info);
            }
            return Invalid(e, ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) ? "not an Xbox 360 disc image" : "not Xbox 360 content");
        }

        private static XeniaScanEntry FromExecutable(XeniaScanEntry e, XexInfo info)
        {
            e.Kind = XeniaFileKind.Game;
            e.TitleId = Xex.Format(info.TitleId);
            e.MediaId = info.MediaId; e.Version = info.Version; e.BaseVersion = info.BaseVersion; e.Disc = info.DiscNumber;
            e.Digest = info.SignatureDigest;
            return e;
        }
        /// <summary>What a package's header makes of an entry - a file's or an archive entry's. <paramref name="hasData"/>:
        /// is its &lt;name&gt;.data folder there (a Games on Demand package is nothing without it)?</summary>
        private static XeniaScanEntry FromPackage(XeniaScanEntry e, StfsInfo info, Func<bool> hasData)
        {
            if (info == null) return Invalid(e, "an STFS package whose header does not read");
            e.TitleId = info.TitleId == 0 ? "" : Xex.Format(info.TitleId);
            e.ContentType = info.ContentType; e.MediaId = info.MediaId; e.Version = info.Version; e.BaseVersion = info.BaseVersion; e.Disc = info.DiscNumber;
            var name = info.DisplayName.Length > 0 ? info.DisplayName : info.TitleName;
            if (name.Length > 0) e.Name = name;
            if (info.TitleId == 0) return Invalid(e, "an STFS package with no title id");
            switch (info.ContentType)
            {
                case 0x000B0000: e.Kind = XeniaFileKind.Update; break;
                case 0x00000002: e.Kind = e.TitleId == IndieTitleId ? XeniaFileKind.Game : XeniaFileKind.Dlc; break;
                case 0x00007000:   // Games on Demand
                case 0x000D0000:   // Xbox Live Arcade
                case 0x00080000:   // demo
                case 0x000A0000:   // game title
                case 0x00004000:   // installed game
                case 0x02000000:   // community (Indie) game
                    e.Kind = XeniaFileKind.Game;
                    if (info.VolumeType == 1 && !hasData()) return Invalid(e, "a Games on Demand package without its .data folder");
                    break;
                default: e.Kind = XeniaFileKind.Other; break;
            }
            return e;
        }

        /// <summary>The entries of an archive the rule takes, each read without extracting: its first bytes decompressed,
        /// nothing written. An archive that holds none of ours is ONE entry, Invalid, so it is noted - and cached.</summary>
        internal static List<XeniaScanEntry> ClassifyArchive(string path, long size, long ticks)
        {
            var found = new List<XeniaScanEntry>();
            try
            {
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(path);
                var entries = archive.Entries.Where(x => !x.IsDirectory && x.Key != null).ToList();
                var keys = new HashSet<string>(entries.Select(x => x.Key.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    var key = entry.Key.Replace('\\', '/');
                    // A Games on Demand package's pieces: <package>.data/Data0000...
                    var slash = key.LastIndexOf('/');
                    var folder = slash > 0 ? key.Substring(0, slash) : "";
                    if (folder.EndsWith(".data", StringComparison.OrdinalIgnoreCase) && keys.Contains(folder.Substring(0, folder.Length - 5))) continue;
                    if (!Taken(key)) continue;

                    var e = new XeniaScanEntry { Path = path + "|" + key, Size = size, Ticks = ticks, Name = key.Substring(slash + 1) };
                    try
                    {
                        if (System.IO.Path.GetExtension(key).Equals(".iso", StringComparison.OrdinalIgnoreCase))
                        {
                            e.Kind = XeniaFileKind.GameNoId;
                            e.Problem = "a disc image inside an archive: not read";
                        }
                        else
                        {
                            // Decompressed as far as the reading goes - a header, a patch, an executable's headers - and no further.
                            using var s = entry.OpenEntryStream();
                            var seq = new SequentialRead(s);
                            FromContent(e, seq.Read, System.IO.Path.GetExtension(key),
                                        () => keys.Any(k => k.StartsWith(key + ".data/", StringComparison.OrdinalIgnoreCase)), disc: false);
                        }
                    }
                    catch (Exception ex) { Invalid(e, "could not be read: " + ex.Message); }
                    found.Add(e);
                }
            }
            catch (Exception ex)
            {
                found.Clear();
                found.Add(Invalid(new XeniaScanEntry { Path = path + "|", Size = size, Ticks = ticks, Name = System.IO.Path.GetFileName(path) }, "the archive could not be opened: " + ex.Message));
                return found;
            }
            if (found.Count == 0)
                found.Add(Invalid(new XeniaScanEntry { Path = path + "|", Size = size, Ticks = ticks, Name = System.IO.Path.GetFileName(path) }, "an archive with no Xbox 360 content in it"));
            return found;
        }

        /// <summary>At most <paramref name="max"/> bytes of a stream - fewer when it ends first. Decompresses that far, no further.</summary>
        private static byte[] ReadUpTo(Stream s, int max)
        {
            var buffer = new byte[max];
            int done = 0;
            while (done < max)
            {
                int n = s.Read(buffer, done, max - done);
                if (n <= 0) break;
                done += n;
            }
            if (done == max) return buffer;
            var shorter = new byte[done];
            Array.Copy(buffer, shorter, done);
            return shorter;
        }

        private static XeniaScanEntry Invalid(XeniaScanEntry e, string why)
        {
            e.Kind = XeniaFileKind.Invalid;
            e.Problem = why;
            return e;
        }

        // ── the cache file ───────────────────────────────────────────────────

        /// <summary>The first line - and the format's version: a cache written by an older one is read again from the files.</summary>
        private const string Header = "path\tsize\tticks\tkind\ttitle_id\tcontent_type\tmedia_id\tversion\tbase_version\tdisc\tname\tproblem\tdigest\tpatch_from\tpatch_to\tcontent_id";

        private static Dictionary<string, XeniaScanEntry> Load()
        {
            var map = new Dictionary<string, XeniaScanEntry>(StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(CachePath)) return map;
                    var lines = File.ReadAllLines(CachePath, Encoding.UTF8);
                    if (lines.Length == 0 || lines[0] != Header) { Log.Info("scan cache: written by an older version - every file is read again"); return map; }
                    foreach (var line in lines.Skip(1))
                    {
                        var c = line.Split('\t');
                        if (c.Length < 16 || !Enum.TryParse<XeniaFileKind>(c[3], out var kind)) continue;
                        map[c[0]] = new XeniaScanEntry
                        {
                            Path = c[0], Size = L(c[1]), Ticks = L(c[2]), Kind = kind, TitleId = c[4],
                            ContentType = U(c[5]), MediaId = U(c[6]), Version = U(c[7]), BaseVersion = U(c[8]), Disc = (byte)L(c[9]),
                            Name = c[10], Problem = c[11], Digest = c[12], PatchFrom = U(c[13]), PatchTo = U(c[14]), ContentId = c[15],
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
                        e.Disc.ToString(CultureInfo.InvariantCulture), Clean(e.Name), Clean(e.Problem), e.Digest, e.PatchFrom.ToString("X8"), e.PatchTo.ToString("X8"), e.ContentId)));
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
