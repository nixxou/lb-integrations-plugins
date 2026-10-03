// Xenia's game patches (Mehdi, 03/10): the .patch.toml files of xenia-canary/game-patches, listed by xenia-manager's
// index, downloaded into the folder Xenia reads, turned on and off patch by patch in a game's options window.
//
// READ IN XENIA CANARY'S CODE (canary_experimental, 03/10):
//   - the folder is <storage root>\patches (emulator.cc: Patcher(storage_root_); patch_db.cc: patches_root_ / "patches");
//   - a file is read only when its name matches ^[A-Fa-f0-9]{8}.*\.patch\.toml$ (patch_db.h), and only when the cvar
//     apply_patches is on - it is by default;
//   - a file applies to a title when its title_id is the title's AND its hash list holds the running module's hash
//     (PatchDB::GetTitlePatches) - that hash is XXH3-64 of the module's code once loaded, its title update applied
//     (user_module.cc, CalculateHash): a title update changes it, and a file made for another version applies NOTHING;
//   - then each [[patch]] whose is_enabled is true is applied, and logged: "Patcher: Applying patch for: <title>(<ID>) - <name>".
// THE HASH IS NOT COMPUTED HERE - it would mean decrypting and decompressing the executable as Xenia does. Xenia logs it
// at every launch instead ("Module Hash: EAE4C4F0393C8FB4", with "XEX patch applied successfully: ... new version:"
// before it), so once a game has run, the plugin knows its version's hash (Seen) and says whether a file is for it.
//
// THE FILES STAY IN XENIA'S FOLDER, for good - Xenia's own mechanism, also when it is opened on its own. A choice made
// in the window is the file's is_enabled lines rewritten in place, the rest of the file as it was. A download never
// replaces a file that is there: an update from the index keeps each patch's choice by its name.
//
// THE INDEX: xenia-manager's database/data/patches/canary.json - [{"name", "sha", "size", "download_url"}], the
// game-patches repository's contents (sha = git's blob sha, so a file's own can be compared without asking anyone).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaPatch
    {
        public string Name, Desc, Author;
        public bool Enabled;
    }

    internal sealed class XeniaPatchFile
    {
        public string Path, TitleId, TitleName;
        public List<string> Hashes = new List<string>();
        public List<XeniaPatch> Patches = new List<XeniaPatch>();
        public string FileName => System.IO.Path.GetFileName(Path);
    }

    internal sealed class XeniaPatchSource
    {
        public string Name, Sha, Url;
        public long Size;
    }

    /// <summary>What the last launch of a title showed in Xenia's log.</summary>
    internal sealed class XeniaPatchSeen
    {
        public DateTime When;
        public string Hash, Version;
        public List<string> Applied = new List<string>();
    }

    internal static class XeniaPatches
    {
        private static readonly Regex FileName = new Regex("^[A-Fa-f0-9]{8}.*\\.patch\\.toml$", RegexOptions.CultureInvariant);
        private static readonly Regex Hex16 = new Regex("\\b[0-9A-Fa-f]{16}\\b", RegexOptions.CultureInvariant);
        private const string IndexName = "patches-canary.json";
        private static readonly string[] IndexUrls =
        {
            "https://xenia-manager.github.io/database/data/patches/canary.json",
            "https://raw.githubusercontent.com/xenia-manager/database/main/data/patches/canary.json",
        };

        public static string Folder(XeniaLayout layout) => System.IO.Path.Combine(layout.StorageRoot, "patches");

        // ── the files ───────────────────────────────────────────────────────

        /// <summary>The files of Xenia's patches folder for a title - by the title_id they declare, else by their name.</summary>
        public static List<XeniaPatchFile> ForTitle(XeniaLayout layout, string titleId)
        {
            var list = new List<XeniaPatchFile>();
            try
            {
                var dir = Folder(layout);
                if (!Directory.Exists(dir) || string.IsNullOrEmpty(titleId)) return list;
                foreach (var path in Directory.EnumerateFiles(dir, "*.patch.toml"))
                {
                    var name = System.IO.Path.GetFileName(path);
                    if (!FileName.IsMatch(name)) continue;
                    var f = Read(path);
                    var id = f?.TitleId ?? name.Substring(0, 8);
                    if (f != null && string.Equals(id, titleId, StringComparison.OrdinalIgnoreCase)) list.Add(f);
                }
            }
            catch (Exception ex) { Log.Warn("patches: could not list the folder", ex); }
            return list.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A patch file read: its header (title_id, title_name, the hashes not commented out) and each [[patch]]'s
        /// name, desc, author and is_enabled. Null when it does not read.</summary>
        public static XeniaPatchFile Read(string path)
        {
            try { return Parse(File.ReadAllText(path), path); }
            catch (Exception ex) { Log.Info("patches: " + System.IO.Path.GetFileName(path) + " does not read (" + ex.Message + ")"); return null; }
        }

        /// <summary>The same, from a file's text - one of the index's, before it is in Xenia's folder.</summary>
        public static XeniaPatchFile Parse(string text, string path)
        {
            try
            {
                var f = new XeniaPatchFile { Path = path };
                XeniaPatch current = null;
                bool inHash = false;
                foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("[[patch]]", StringComparison.Ordinal)) { current = new XeniaPatch(); f.Patches.Add(current); inHash = false; continue; }
                    if (line.StartsWith("[", StringComparison.Ordinal)) { inHash = false; continue; }     // a command table of the patch
                    if (line.StartsWith("#", StringComparison.Ordinal)) continue;                           // a hash commented out does not count
                    if (inHash)
                    {
                        var code = Code(line);
                        foreach (Match m in Hex16.Matches(code)) f.Hashes.Add(m.Value.ToUpperInvariant());
                        if (code.Contains("]")) inHash = false;
                        continue;
                    }
                    if (!XeniaToml.Split(line, out var key, out var value, out _)) continue;
                    if (current == null)
                    {
                        if (key == "title_id") f.TitleId = Unquote(value).ToUpperInvariant();
                        else if (key == "title_name") f.TitleName = Unquote(value);
                        else if (key == "hash")
                        {
                            foreach (Match m in Hex16.Matches(value)) f.Hashes.Add(m.Value.ToUpperInvariant());
                            inHash = value.StartsWith("[") && !value.Contains("]");
                        }
                    }
                    else if (key == "name" && current.Name == null) current.Name = Unquote(value);
                    else if (key == "desc" && current.Desc == null) current.Desc = Unquote(value);
                    else if (key == "author" && current.Author == null) current.Author = Unquote(value);
                    else if (key == "is_enabled") current.Enabled = value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                f.Hashes = f.Hashes.Distinct().ToList();
                return f.TitleId == null && f.Patches.Count == 0 ? null : f;
            }
            catch (Exception ex) { Log.Info("patches: " + System.IO.Path.GetFileName(path) + " does not read (" + ex.Message + ")"); return null; }
        }

        /// <summary>An index file's text - the day's copy, else asked for within <paramref name="timeout"/>.</summary>
        public static string SourceText(XeniaPatchSource s, TimeSpan timeout)
            => XeniaRemote.Get("patch-" + s.Name, new[] { s.Url }, timeout, t => t.Contains("title_id") && t.Contains("[[patch]]"));

        /// <summary>"584111F7 - Minecraft (XBLA, TU4).patch.toml" -> "XBLA, TU4": the version a file is for, in its name.</summary>
        public static string VersionLabel(string fileName)
        {
            var m = Regex.Match(fileName ?? "", "\\(([^()]*)\\)\\.patch\\.toml$", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        /// <summary>The patches' is_enabled set as asked, by patch name - in place: each [[patch]] block's is_enabled line
        /// rewritten (added under [[patch]] when the block has none), every other line as it was.</summary>
        public static void SetEnabled(string path, IDictionary<string, bool> byName)
        {
            var text = File.ReadAllText(path);
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            int block = -1, enabledAt = -1;
            string name = null;
            var edits = new List<(int Block, int EnabledAt, string Name)>();
            void Close() { if (block >= 0) edits.Add((block, enabledAt, name)); }
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("[[patch]]", StringComparison.Ordinal)) { Close(); block = i; enabledAt = -1; name = null; continue; }
                if (block < 0 || line.StartsWith("[", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!XeniaToml.Split(line, out var key, out var value, out _)) continue;
                // Only the block's own keys - before its first command table.
                if (lines.Skip(block + 1).Take(i - block - 1).Any(l => l.Trim().StartsWith("[", StringComparison.Ordinal))) continue;
                if (key == "name" && name == null) name = Unquote(value);
                else if (key == "is_enabled" && enabledAt < 0) enabledAt = i;
            }
            Close();
            int changed = 0;
            foreach (var e in edits.OrderByDescending(x => x.Block))
            {
                if (e.Name == null || !byName.TryGetValue(e.Name, out var on)) continue;
                var indent = new string(' ', lines[e.Block].Length - lines[e.Block].TrimStart().Length + 4);
                var want = indent + "is_enabled = " + (on ? "true" : "false");
                if (e.EnabledAt >= 0)
                {
                    XeniaToml.Split(lines[e.EnabledAt].Trim(), out _, out var was, out var comment);
                    if (was.Trim().Equals(on ? "true" : "false", StringComparison.OrdinalIgnoreCase)) continue;
                    var keepIndent = lines[e.EnabledAt].Substring(0, lines[e.EnabledAt].Length - lines[e.EnabledAt].TrimStart().Length);
                    lines[e.EnabledAt] = keepIndent + "is_enabled = " + (on ? "true" : "false") + (comment != null ? " " + comment : "");
                }
                else lines.Insert(e.Block + 1, want);
                changed++;
            }
            if (changed == 0) return;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, string.Join(nl, lines), new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            Log.Info("patches: " + changed + " choice(s) written -> " + System.IO.Path.GetFileName(path));
        }

        /// <summary>Is any patch of these files on - for the launch, which then makes sure apply_patches is.</summary>
        public static bool AnyEnabled(XeniaLayout layout, string titleId) => ForTitle(layout, titleId).Any(f => f.Patches.Any(p => p.Enabled));

        // ── the index and the downloads ─────────────────────────────────────

        private static List<XeniaPatchSource> ParseIndex(string json)
        {
            var list = new List<XeniaPatchSource>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    string Str(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                    var s = new XeniaPatchSource { Name = Str("name"), Sha = Str("sha"), Url = Str("download_url"),
                                                   Size = e.TryGetProperty("size", out var z) && z.ValueKind == JsonValueKind.Number && z.TryGetInt64(out var l) ? l : 0 };
                    if (s.Name != null && s.Url != null && FileName.IsMatch(s.Name)
                        && s.Url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase)) list.Add(s);
                }
            }
            catch { }
            return list;
        }

        /// <summary>The index's files for a title, from the copy - asked for within <paramref name="timeout"/> when it is
        /// old (zero: the copy only). Null when there is no index at all.</summary>
        public static List<XeniaPatchSource> Sources(string titleId, TimeSpan timeout)
        {
            var json = XeniaRemote.Get(IndexName, IndexUrls, timeout, t => ParseIndex(t).Count >= 20);
            if (json == null) return null;
            return ParseIndex(json).Where(s => s.Name.StartsWith(titleId ?? "?", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>git's blob sha of a file - what the index's "sha" is: it tells a file the index has a newer one of.</summary>
        public static string BlobSha(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var head = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
            using var sha = SHA1.Create();
            sha.TransformBlock(head, 0, head.Length, null, 0);
            sha.TransformFinalBlock(bytes, 0, bytes.Length);
            return string.Concat(sha.Hash.Select(b => b.ToString("x2")));
        }

        /// <summary>A file of the index put in Xenia's folder: new, or over the one of that name with each patch's choice
        /// kept by its name. The path, or null with <paramref name="error"/>.</summary>
        public static string Download(XeniaLayout layout, XeniaPatchSource source, TimeSpan timeout, out string error)
        {
            error = null;
            try
            {
                var text = SourceText(source, timeout);
                if (text == null) { error = "it could not be downloaded"; return null; }
                var dir = Folder(layout);
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, source.Name);
                var keep = File.Exists(path) ? Read(path)?.Patches.Where(p => p.Name != null).GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().Enabled) : null;
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                File.Move(tmp, path, overwrite: true);
                if (keep != null && keep.Count > 0) SetEnabled(path, keep);
                Log.Info("patches: " + source.Name + (keep != null ? " updated, choices kept" : " downloaded") + " -> " + dir);
                return path;
            }
            catch (Exception ex) { error = ex.Message; Log.Warn("patches: download of " + source.Name, ex); return null; }
        }

        // ── what Xenia's log said ───────────────────────────────────────────

        private static string SeenPath(string titleId) => System.IO.Path.Combine(XeniaSettings.Dir, "patches-seen", titleId.ToUpperInvariant() + ".txt");
        private static string VersionsPath(string titleId) => System.IO.Path.Combine(XeniaSettings.Dir, "patches-seen", titleId.ToUpperInvariant() + ".versions");

        /// <summary>The versions a title has run as, each with its module hash: "0.0.2.3" -> hash ("" for no title update).</summary>
        public static Dictionary<string, string> Versions(string titleId)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var path = VersionsPath(titleId);
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var f = line.Split('\t');
                        if (f.Length == 2 && Hex16.IsMatch(f[1])) map[f[0]] = f[1];
                    }
            }
            catch { }
            return map;
        }

        /// <summary>The title update number a version stands for - 0.0.2.3 is TU2: an Xbox 360 title update puts its number
        /// in the version's third field (measured on DEAD OR ALIVE Xtreme 2: base 0.0.0.3, its TU2 0.0.2.3). Null for none.</summary>
        public static int? TuNumber(string version)
        {
            var f = (version ?? "").Split('.');
            return f.Length == 4 && int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
        }

        /// <summary>Every patch file of a title the plugin knows without asking anyone: Xenia's folder's, then the index's
        /// copies of those not there. Empty when there is neither.</summary>
        public static List<XeniaPatchFile> Known(XeniaLayout layout, string titleId)
        {
            var all = ForTitle(layout, titleId);
            foreach (var s in Sources(titleId, TimeSpan.Zero) ?? new List<XeniaPatchSource>())
            {
                if (all.Any(f => string.Equals(f.FileName, s.Name, StringComparison.OrdinalIgnoreCase))) continue;
                var text = SourceText(s, TimeSpan.Zero);
                var f = text == null ? null : Parse(text, s.Name);
                if (f != null) all.Add(f);
            }
            return all;
        }

        /// <summary>The file of <paramref name="files"/> a version of the title runs with ("" = no title update): by the hash
        /// that version ran as at a launch (sure), else by the title update number in the files' names. Null when none.</summary>
        public static (XeniaPatchFile File, bool Sure) FileFor(List<XeniaPatchFile> files, string titleId, string version)
        {
            var known = Versions(titleId);
            var seen = Seen(titleId);
            if (seen != null && !known.ContainsKey(seen.Version ?? "")) known[seen.Version ?? ""] = seen.Hash;
            if (known.TryGetValue(version ?? "", out var hash))
                return (files.FirstOrDefault(f => f.Hashes.Contains(hash, StringComparer.OrdinalIgnoreCase)), true);
            var tu = string.IsNullOrEmpty(version) ? 0 : TuNumber(version);
            var byName = tu == null ? null : files.Where(f => LabelIsTu(VersionLabel(f.FileName) ?? f.FileName, tu.Value)).ToList();
            return (byName != null && byName.Count == 1 ? byName[0] : null, false);
        }

        /// <summary>Does a file's version label name that title update - "XBLA, TU4" for 4, "TU0" (or no TU at all) for none?</summary>
        public static bool LabelIsTu(string label, int tu)
        {
            var m = Regex.Matches(label ?? "", "\\bTU\\s*0*(\\d+)\\b", RegexOptions.IgnoreCase);
            if (m.Count == 0) return false;
            return m.Cast<Match>().Any(x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture) == tu);
        }

        public static XeniaPatchSeen Seen(string titleId)
        {
            try
            {
                if (string.IsNullOrEmpty(titleId)) return null;
                var path = SeenPath(titleId);
                if (!File.Exists(path)) return null;
                var s = new XeniaPatchSeen();
                foreach (var line in File.ReadAllLines(path))
                {
                    var f = line.Split(new[] { '\t' }, 2);
                    if (f.Length != 2) continue;
                    if (f[0] == "when" && DateTime.TryParse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var w)) s.When = w;
                    else if (f[0] == "hash") s.Hash = f[1];
                    else if (f[0] == "version") s.Version = f[1];
                    else if (f[0] == "applied") s.Applied.Add(f[1]);
                }
                return s.Hash == null ? null : s;
            }
            catch { return null; }
        }

        /// <summary>Once the Xenia of this launch has come and gone, its log read for the title's module hash, its version
        /// and the patches applied - in the background, a watcher that holds nothing (XeniaCompat.RefreshAfter's shape).</summary>
        public static void WatchAfter(string exePath, XeniaLayout layout, string titleId)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(exePath ?? "");
            if (name.Length == 0 || layout == null || string.IsNullOrEmpty(titleId)) return;
            Task.Run(() =>
            {
                try
                {
                    bool Running() { try { return Process.GetProcessesByName(name).Length > 0; } catch { return false; } }
                    var armed = DateTime.UtcNow;
                    while (!Running() && (DateTime.UtcNow - armed).TotalSeconds < 120) Thread.Sleep(1000);
                    while (Running()) Thread.Sleep(2000);
                    ReadLog(System.IO.Path.Combine(layout.StorageRoot, "xenia.log"), titleId);
                }
                catch (Exception ex) { Log.Info("patches: the log was not read (" + ex.Message + ")"); }
            });
        }

        /// <summary>Xenia's log of a launch read for a title: the hash of its module (the one Xenia marks XEX_MODULE_TITLE,
        /// right after "Module ...:"), the version a title update made of it, the patches applied. Kept when the log is the
        /// title's.</summary>
        internal static void ReadLog(string logPath, string titleId)
        {
            if (!File.Exists(logPath)) return;
            string text;
            using (var s = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            var id = titleId.ToUpperInvariant();
            if (text.IndexOf("Title ID: " + id, StringComparison.OrdinalIgnoreCase) < 0) { Log.Info("patches: the log is not " + id + "'s - not read"); return; }
            var hash = Regex.Match(text, "Module Hash: ([0-9A-Fa-f]{16})\\s+Module Flags: [0-9A-Fa-f]+\\s+XEX_MODULE_TITLE");
            if (!hash.Success) hash = Regex.Match(text, "Module Hash: ([0-9A-Fa-f]{16})");
            if (!hash.Success) { Log.Info("patches: no module hash in the log of " + id); return; }
            var version = Regex.Matches(text, "XEX patch applied successfully: base version: [^,]+, new version: ([0-9.]+)").Cast<Match>().LastOrDefault()?.Groups[1].Value;
            var applied = Regex.Matches(text, "Patcher: Applying patch for: .*?\\(" + id + "\\) - (.+)", RegexOptions.IgnoreCase)
                               .Cast<Match>().Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
            var lines = new List<string>
            {
                "when\t" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                "hash\t" + hash.Groups[1].Value.ToUpperInvariant(),
                "version\t" + (version ?? ""),
            };
            lines.AddRange(applied.Select(a => "applied\t" + a));
            var path = SeenPath(id);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            // Every version the title has run as, with its hash: a title update played once is then known for good.
            var known = Versions(id);
            known[version ?? ""] = hash.Groups[1].Value.ToUpperInvariant();
            File.WriteAllLines(VersionsPath(id), known.Select(kv => kv.Key + "\t" + kv.Value));
            Log.Info("patches: " + id + " ran as module " + hash.Groups[1].Value.ToUpperInvariant() + (version != null ? " (version " + version + ")" : "")
                     + ", " + applied.Count + " patch(es) applied" + (applied.Count > 0 ? ": " + string.Join(", ", applied) : ""));
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static string Code(string line)
        {
            int hash = line.IndexOf('#');
            return hash >= 0 ? line.Substring(0, hash) : line;
        }

        private static string Unquote(string v)
        {
            v = (v ?? "").Trim();
            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0]) v = v.Substring(1, v.Length - 2);
            return v.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
