// xemu's savestates as LaunchBox's (Mehdi, 05/10): each snapshot of a game's console (Qcow2Snapshots) mirrored as a file,
// <xemu>\lbip-states\<title id>\slot-<n>.xemustate, which LaunchBox lists in the game's savestates, backs up and restores.
//
// A FILE: a zip, compressed -
//     lbip-state.txt      lbip-xemu-state/1: the title, the snapshot's name, id and dates, its slot, the keys the console was
//                         written with when it was exported (its stamp's - XboxSaveSync), xemu's tag
//     state.qcow2         a qcow2 of its own, no backing, holding that one snapshot: its table entry and every cluster its L1
//                         reaches - the disk of the game's console as it was (only what differs from the dashboard's disk)
//                         and the VM state past the disk's end (RAM, devices, xemu's thumbnail)
//     thumbnail.png       that thumbnail, taken out (Qcow2Image.SnapshotThumbnail) when it can be - for a window to show it
//                         without reading the state; a file without it is the same file (05/10)
// Measured 05/10 on Batman: a snapshot's VM state 35 MB (QEMU skips the empty pages).
//
// THE CONSOLE IS WHAT XEMU LOADS, THE FILES WHAT LAUNCHBOX SEES; an index beside the console (hdd\games\<title id>.states)
// says which snapshot each file was made from, and its slot - so whichever side changed is known:
//   a snapshot with no file                    exported (made in xemu since)
//   a file whose snapshot is gone, indexed     removed (deleted in xemu)
//   a file not indexed (LaunchBox's Restore,   put into the console - its snapshot replacing one of the same name
//   one copied there)
//   an indexed snapshot whose file is gone     taken out of the console (LaunchBox's Remove)
// Exports and removals of files at any listing; anything that rewrites the console (Qcow2Rebuild - its other snapshots and
// its save kept, checked before it replaces anything) only at a launch (Mehdi, 05/10): LaunchBox's Restore and Remove only
// place or delete the file.
// ALL OF IT OPTIONAL (Mehdi, 05/10: settings.ini savestates=on, off by default - XemuSaveFiles.States): off, nothing runs and
// the index is forgotten, so turning it on again never takes a snapshot out of a console.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LbIntegrations.Xemu.Saves
{
    internal sealed class XemuStateFile
    {
        public string Path, Title, Name, Id;
        public int Slot;
        public uint DateSec, DateNsec;
        public ulong VmClockNsec;
        public string Keys;
        public string Identity => Name + "|" + DateSec + "|" + DateNsec + "|" + VmClockNsec;
    }

    internal static class XemuStates
    {
        public const string Extension = ".xemustate";
        private const string Tag = "lbip-xemu-state/1";
        private static readonly object Gate = new object();

        public static string Dir(string exe, string titleId) => XemuPaths.Dir(exe) is string d ? System.IO.Path.Combine(d, "lbip-states", titleId) : null;
        internal static string IndexPath(string exe, string titleId) => System.IO.Path.ChangeExtension(XemuPaths.GameHdd(exe, titleId), ".states");
        public static string SlotPath(string exe, string titleId, int slot) => System.IO.Path.Combine(Dir(exe, titleId), "slot-" + slot.ToString(CultureInfo.InvariantCulture) + Extension);
        private static string IdentityOf(Qcow2Snapshot s) => s.Name + "|" + s.DateSec + "|" + s.DateNsec + "|" + s.VmClockNsec;

        // ── the index: slot \t identity ─────────────────────────────────────

        private static Dictionary<string, int> ReadIndex(string path)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                if (path == null || !File.Exists(path)) return map;
                foreach (var line in File.ReadAllLines(path))
                {
                    var c = line.Split('\t');
                    if (c.Length == 2 && int.TryParse(c[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot)) map[c[1]] = slot;
                }
            }
            catch { }
            return map;
        }

        // ── the slots: slot \t name, never forgotten ─────────────────────────
        // A snapshot's NAME gives its slot (Mehdi, 05/10): xemu's own slots are names saved over (its "overwrite", Shift+F#), so
        // a slot's history in LaunchBox is that name's versions. A name keeps its slot for good, deleted or not; a new name takes
        // a number no name ever had. Beside the console, hdd\games\<title id>.slots - not forgotten with the index (savestates
        // off, the console deleted): it only says which number goes with which name, never which snapshot to take out.

        internal static string SlotsPath(string exe, string titleId) => System.IO.Path.ChangeExtension(XemuPaths.GameHdd(exe, titleId), ".slots");

        private static Dictionary<string, int> ReadSlots(string path)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                if (path == null || !File.Exists(path)) return map;
                foreach (var line in File.ReadAllLines(path))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0 && int.TryParse(line.Substring(0, tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) && slot > 0)
                        map[line.Substring(tab + 1)] = slot;
                }
            }
            catch { }
            return map;
        }

        private static void WriteSlots(string path, Dictionary<string, int> map)
        {
            if (path == null || map.Count == 0) return;
            var text = string.Join("\r\n", map.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Key)) + "\r\n";
            try { if (File.Exists(path) && File.ReadAllText(path) == text) return; } catch { }
            var part = path + ".part";
            File.WriteAllText(part, text);
            File.Move(part, path, overwrite: true);
        }

        private static void WriteIndex(string path, Dictionary<string, int> map)
        {
            if (map.Count == 0) { try { if (File.Exists(path)) File.Delete(path); } catch { } return; }
            var text = string.Join("\r\n", map.OrderBy(kv => kv.Value).Select(kv => kv.Value.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Key)) + "\r\n";
            try { if (File.Exists(path) && File.ReadAllText(path) == text) return; } catch { }
            File.WriteAllText(path, text);
        }

        // ── a file ───────────────────────────────────────────────────────────

        public static XemuStateFile Read(string path)
        {
            try
            {
                using var z = ZipFile.OpenRead(path);
                var meta = z.GetEntry("lbip-state.txt");
                if (meta == null || z.GetEntry("state.qcow2") == null) return null;
                string text; using (var r = new StreamReader(meta.Open(), Encoding.UTF8)) text = r.ReadToEnd();
                var v = new Dictionary<string, string>();
                foreach (var line in text.Split('\n'))
                {
                    var l = line.TrimEnd('\r'); int eq = l.IndexOf('=');
                    if (eq > 0) v[l.Substring(0, eq)] = l.Substring(eq + 1);
                }
                if (!v.TryGetValue("format", out var f) || f != Tag) return null;
                string G(string k) => v.TryGetValue(k, out var s) ? s : null;
                // The slot is the file's name (where LaunchBox's Restore put it), else the one it was exported as.
                var m = System.Text.RegularExpressions.Regex.Match(System.IO.Path.GetFileNameWithoutExtension(path), @"^slot-(\d+)$");
                return new XemuStateFile
                {
                    Path = path, Title = G("title"), Name = G("name"), Id = G("id"), Keys = G("keys"),
                    Slot = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : int.TryParse(G("slot"), out var sl) ? sl : 0,
                    DateSec = uint.TryParse(G("date_sec"), out var ds) ? ds : 0, DateNsec = uint.TryParse(G("date_nsec"), out var dn) ? dn : 0,
                    VmClockNsec = ulong.TryParse(G("vm_clock"), out var vc) ? vc : 0,
                };
            }
            catch { return null; }
        }

        /// <summary>A snapshot of <paramref name="console"/> written as a state file at <paramref name="target"/>.</summary>
        private static void Export(Qcow2Image console, Qcow2Snapshot sn, string titleId, int slot, string keys, string target)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            var qcow = target + ".qcow2.lbip-tmp";
            var part = target + ".part";
            try
            {
                var table = RebuildTable.From(console, sn.L1, sn);
                var tmp = Qcow2Rebuild.Write(qcow, null, console.Length, console.ClusterBits, new RebuildTable(), new List<RebuildTable> { table });
                Qcow2Rebuild.Commit(tmp, qcow);
                var meta = new StringBuilder()
                    .Append("format=").Append(Tag).Append('\n').Append("title=").Append(titleId).Append('\n')
                    .Append("name=").Append(sn.Name).Append('\n').Append("id=").Append(sn.Id).Append('\n')
                    .Append("slot=").Append(slot).Append('\n')
                    .Append("date_sec=").Append(sn.DateSec).Append('\n').Append("date_nsec=").Append(sn.DateNsec).Append('\n')
                    .Append("vm_clock=").Append(sn.VmClockNsec).Append('\n')
                    .Append("disk_size=").Append(console.Length).Append('\n')
                    .Append("keys=").Append(keys ?? "").Append('\n').ToString();
                using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var m = z.CreateEntry("lbip-state.txt", CompressionLevel.Optimal);
                    m.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using (var w = new StreamWriter(m.Open(), new UTF8Encoding(false))) w.Write(meta);
                    var e = z.CreateEntry("state.qcow2", CompressionLevel.Optimal);
                    e.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using (var src = File.OpenRead(qcow)) using (var dst = e.Open()) src.CopyTo(dst);
                    // Its picture beside it, when it can be had (Mehdi, 05/10) - never in the way: without it the file is the same.
                    byte[] png = null;
                    try { png = console.SnapshotThumbnail(sn); } catch { }
                    if (png != null)
                    {
                        var t = z.CreateEntry(ThumbnailEntry, CompressionLevel.NoCompression);
                        t.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using var dst = t.Open();
                        dst.Write(png, 0, png.Length);
                    }
                }
                File.Move(part, target, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(qcow)) File.Delete(qcow); } catch { }
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
        }

        /// <summary>THE INDEX BELONGS TO THE CONSOLE: the console gone (deleted by hand), its index goes too - before a new one is
        /// made - so the state files are taken for files put there and laid into the new console, never for snapshots deleted in
        /// xemu (which would remove them). Called before anything can make a console (XemuSaveFiles.Sync, Mirror).</summary>
        public static void ForgetWithoutConsole(string exe, string titleId)
        {
            try
            {
                var console = XemuPaths.GameHdd(exe, titleId);
                var index = console == null ? null : IndexPath(exe, titleId);
                if (index != null && !File.Exists(console) && File.Exists(index)) { File.Delete(index); LbIntegrations.Xemu.Log.Info("savestates: " + titleId + " - its console is gone: its index forgotten, its files kept"); }
            }
            catch { }
        }

        // ── a view, for a window ─────────────────────────────────────────────

        internal sealed class SnapshotView
        {
            public string Name, Id, Identity;
            public DateTime Date;
            public ulong VmStateSize;
            public XemuStateFile File;          // its file, when it has one
        }

        /// <summary>What the game's console holds and what its state files are, read only - nothing exported, nothing written.
        /// <paramref name="files"/> the state files whose snapshot is not in the console (restored, waiting for a launch).
        /// Null with <paramref name="problem"/> when the console cannot be read (xemu running holds it).</summary>
        public static List<SnapshotView> View(string exe, string titleId, out List<XemuStateFile> files, out string problem)
        {
            files = new List<XemuStateFile>();
            problem = null;
            lock (Gate)
            {
                var consolePath = XemuPaths.GameHdd(exe, titleId);
                var dir = Dir(exe, titleId);
                var all = dir != null && Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + Extension).Select(Read).Where(f => f != null).OrderBy(f => f.Slot).ToList() : new List<XemuStateFile>();
                var list = new List<SnapshotView>();
                if (consolePath != null && File.Exists(consolePath))
                {
                    try
                    {
                        using var console = (Qcow2Image)Qcow2Image.Open(consolePath);
                        foreach (var s in console.Snapshots)
                        {
                            var id = IdentityOf(s);
                            list.Add(new SnapshotView { Name = s.Name, Id = s.Id, Identity = id, Date = s.Date, VmStateSize = s.VmStateSize, File = all.FirstOrDefault(f => f.Identity == id) });
                        }
                    }
                    catch (Exception ex) { problem = ex.Message; return null; }
                }
                files = all.Where(f => list.All(v => v.Identity != f.Identity)).ToList();
                return list;
            }
        }

        /// <summary>The thumbnail of the snapshot <paramref name="identity"/> of the game's console (Qcow2Image.SnapshotThumbnail),
        /// or null. Read only.</summary>
        public static byte[] Thumbnail(string exe, string titleId, string identity, string file = null)
        {
            // Its file's picture first, when it has one: a few KB, against the console's whole VM state.
            if (file != null && FileThumbnail(file) is byte[] own) return own;
            lock (Gate)
            {
                try
                {
                    var consolePath = XemuPaths.GameHdd(exe, titleId);
                    if (consolePath == null || !File.Exists(consolePath)) return null;
                    using var console = (Qcow2Image)Qcow2Image.Open(consolePath);
                    var s = console.Snapshots.FirstOrDefault(x => IdentityOf(x) == identity);
                    return s == null ? null : console.SnapshotThumbnail(s);
                }
                catch { return null; }
            }
        }

        public const string ThumbnailEntry = "thumbnail.png";

        /// <summary>The picture a state file carries (thumbnail.png, 05/10 on), or null - an older file has none.</summary>
        public static byte[] FileThumbnail(string file)
        {
            try
            {
                using var z = ZipFile.OpenRead(file);
                var e = z.GetEntry(ThumbnailEntry);
                if (e == null || e.Length > 8 << 20) return null;
                using var s = e.Open();
                using var m = new MemoryStream();
                s.CopyTo(m);
                return m.ToArray();
            }
            catch { return null; }
        }

        // ── the mirror ───────────────────────────────────────────────────────

        /// <summary>The game's snapshots and state files put in step. <paramref name="rewrite"/>: the console may be rewritten
        /// (a launch - xemu not running). What it did.</summary>
        public static List<string> Mirror(string exe, string titleId, bool rewrite, Func<string> keys)
        {
            lock (Gate)
            {
                var said = new List<string>();
                var consolePath = XemuPaths.GameHdd(exe, titleId);
                var dir = Dir(exe, titleId);
                if (consolePath == null || dir == null) return said;
                ForgetWithoutConsole(exe, titleId);
                var indexPath = IndexPath(exe, titleId);
                var index = ReadIndex(indexPath);
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*" + Extension).Select(Read).Where(f => f != null).ToList() : new List<XemuStateFile>();
                if (!File.Exists(consolePath))
                {
                    if (files.Count > 0) said.Add(files.Count + " state file(s) wait for the game's console - made at its first launch");
                    return said;
                }

                var toImport = new List<XemuStateFile>();
                var toDrop = new List<string>();                     // identities taken out of the console
                var slotsPath = SlotsPath(exe, titleId);
                var slots = ReadSlots(slotsPath);
                // Made again from the files when lost (or written before 05/10): each name the slot its file is at.
                foreach (var f in files.OrderBy(f => f.Slot)) if (f.Name != null && f.Slot > 0 && !slots.ContainsKey(f.Name)) slots[f.Name] = f.Slot;
                using (var console = (Qcow2Image)Qcow2Image.Open(consolePath))
                {
                    var snaps = console.Snapshots.ToDictionary(IdentityOf, s => s, StringComparer.Ordinal);
                    var byFile = files.GroupBy(f => f.Identity).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    var live = new List<XemuStateFile>(files);          // the files still there once this loop is done
                    foreach (var f in files)
                    {
                        if (snaps.ContainsKey(f.Identity)) continue;
                        if (index.ContainsKey(f.Identity)) { File.Delete(f.Path); live.Remove(f); index.Remove(f.Identity); said.Add("\"" + f.Name + "\" deleted in xemu: its file removed"); }
                        else toImport.Add(f);
                    }
                    foreach (var (identity, sn) in snaps)
                    {
                        if (byFile.ContainsKey(identity)) { if (!index.ContainsKey(identity)) index[identity] = byFile[identity].Slot; continue; }
                        if (index.ContainsKey(identity)) { toDrop.Add(identity); continue; }   // its file removed by LaunchBox
                        // A file of the same name waits to go in (a Restore): it replaces this one - xemu's names are unique.
                        if (toImport.Any(f => f.Name == sn.Name)) continue;
                        // Its name's slot - unless a file of another name sits there; else a number no name ever had.
                        if (!slots.TryGetValue(sn.Name, out var slot) || live.Any(f => f.Slot == slot && f.Name != sn.Name))
                        {
                            slot = Math.Max(slots.Values.DefaultIfEmpty(0).Max(), live.Select(f => f.Slot).DefaultIfEmpty(0).Max()) + 1;
                            slots[sn.Name] = slot;
                        }
                        Export(console, sn, titleId, slot, keys?.Invoke(), SlotPath(exe, titleId, slot));
                        live.RemoveAll(f => f.Slot == slot);
                        live.Add(new XemuStateFile { Name = sn.Name, Slot = slot });
                        index[identity] = slot;
                        said.Add("\"" + sn.Name + "\" exported as slot " + slot);
                    }
                    WriteSlots(slotsPath, slots);
                    if (!rewrite || (toImport.Count == 0 && toDrop.Count == 0))
                    {
                        if (toImport.Count + toDrop.Count > 0) said.Add((toImport.Count + toDrop.Count) + " change(s) wait for the next launch");
                        WriteIndex(indexPath, index);
                        return said;
                    }

                    // The console written again: its active disk and the snapshots kept, the imported ones in, the dropped ones out.
                    var active = RebuildTable.From(console, console.ActiveL1);
                    var kept = console.Snapshots.Where(s => !toDrop.Contains(IdentityOf(s)) && !toImport.Any(f => f.Name == s.Name)).ToList();
                    var tables = kept.Select(s => RebuildTable.From(console, s.L1, s)).ToList();
                    var opened = new List<(Qcow2Image Image, string Temp)>();
                    try
                    {
                        int nextId = console.Snapshots.Select(s => int.TryParse(s.Id, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
                        foreach (var f in toImport)
                        {
                            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lbip-state-" + Guid.NewGuid().ToString("N") + ".qcow2");
                            using (var z = ZipFile.OpenRead(f.Path)) z.GetEntry("state.qcow2").ExtractToFile(temp);
                            var img = (Qcow2Image)Qcow2Image.Open(temp);
                            opened.Add((img, temp));
                            if (img.Length != console.Length || img.ClusterBits != console.ClusterBits || img.Snapshots.Count != 1)
                                throw new InvalidDataException("\"" + f.Name + "\" is not a savestate of this console (its disk differs)");
                            var src = img.Snapshots[0];
                            var sn = new Qcow2Snapshot
                            {
                                Id = kept.Any(k => k.Id == src.Id) || tables.Any(t => t.Snapshot.Id == src.Id) ? (nextId++).ToString(CultureInfo.InvariantCulture) : src.Id,
                                Name = src.Name, DateSec = src.DateSec, DateNsec = src.DateNsec, VmClockNsec = src.VmClockNsec,
                                VmStateSize32 = src.VmStateSize32, Extra = src.Extra, L1 = src.L1,
                            };
                            tables.Add(RebuildTable.From(img, src.L1, sn));
                        }
                        var tmp = Qcow2Rebuild.Write(consolePath, console.BackingPath, console.Length, console.ClusterBits, active, tables);
                        console.Dispose();
                        Qcow2Rebuild.Commit(tmp, consolePath);
                    }
                    finally
                    {
                        foreach (var (img, temp) in opened) { try { img.Dispose(); } catch { } try { File.Delete(temp); } catch { } }
                    }
                    foreach (var f in toImport) { index[f.Identity] = f.Slot; said.Add("\"" + f.Name + "\" put into the console (slot " + f.Slot + ")"); }
                    foreach (var id in toDrop) { index.Remove(id); said.Add("a savestate taken out of the console"); }
                    WriteIndex(indexPath, index);
                    return said;
                }
            }
        }
    }
}
