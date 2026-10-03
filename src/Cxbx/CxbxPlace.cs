// Where a launch unpacks the game, and the unpacking - XeniaExtras' rules for the game itself (Mehdi, 01/10), without
// title updates or DLC (the original Xbox has neither in this form):
//
//   1. a game already unpacked on the disk, from this very file (same size, same date), is opened from there -
//      unless its options say "always the RAM disk";
//   2. else, under the threshold and when a RAM disk can be had, it is unpacked onto one, for the session;
//   3. else onto the disk: <folder>\<the game's file name>\game, with lbip-game.txt saying which file it came from.
//
// THE SIZE LIMIT (0 = none): before unpacking onto the disk, the games launched longest ago lose their folder, whole,
// until the new one fits - never the game being launched, never a game whose options say "keep". The user's own
// files are never touched: what is deleted is what this plugin unpacked.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.Cxbx
{
    internal sealed class CxbxPlan
    {
        public CxbxRom Rom;
        public string Folder;          // the game's folder on the disk
        public bool OnDisk;            // already unpacked there, from this file
        public bool Ram;               // the next launch unpacks it onto a RAM disk
        public bool Attach;            // the next launch reads it where it is: the disc attached as a disk (AIM)
        public string Why = "";
        public string Placement = "auto";
        public bool Keep;
        public long Bytes => Rom?.Bytes ?? 0;
        public bool Unpacks => Rom != null && Rom.Kind != CxbxRomKind.Xbe;
    }

    internal static class CxbxPlace
    {
        private const string Manifest = "lbip-game.txt";
        private const string Done = ".lbip-done";

        public static string GamesFolder(string installDir)
        {
            var set = CxbxSettings.Read().TryGetValue("folder", out var f) ? f : null;
            return !string.IsNullOrWhiteSpace(set) ? set : Path.Combine(installDir, "lbip-games");
        }

        public static long Limit() => (long)(CxbxSettings.Number(CxbxSettings.Read(), "limit_gb", 0) * 1024 * 1024 * 1024);

        /// <summary>What the next launch would do, and why. Nothing is unpacked or mounted.</summary>
        public static CxbxPlan Plan(string rom, string gameId, string installDir, CxbxRom described = null)
            => Plan(rom, CxbxSettings.ReadGame(gameId), installDir, described);

        /// <summary>The same, for a choice not saved yet - the game's options window, as its boxes change.</summary>
        public static CxbxPlan Plan(string rom, IDictionary<string, string> choice, string installDir, CxbxRom described = null)
        {
            var d = described ?? CxbxGame.Describe(rom);
            var p = new CxbxPlan { Rom = d, Placement = CxbxSettings.Placement(choice), Keep = CxbxSettings.Keep(choice) };
            if (d.Problem != null || d.Kind == CxbxRomKind.Unknown) { p.Why = d.Problem ?? "not an Xbox game"; return p; }
            if (!p.Unpacks) { p.Why = "an XBE: opened where it is"; return p; }
            p.Folder = GameFolder(installDir, rom);
            p.OnDisk = IsDone(Path.Combine(p.Folder, "game"), d.Stamp);
            // A bare disc image read where it is, nothing copied (AIM) - first, when on and possible: the placement below
            // is where a game is UNPACKED, which an attached disc is not.
            string noAttach = null;
            if (d.Kind == CxbxRomKind.Image || d.Kind == CxbxRomKind.Zar)
            {
                noAttach = CxbxRamSession.WhyNotDisc(installDir, choice, d.Path);
                if (noAttach == null)
                {
                    p.Attach = true;
                    p.Why = "read where it is: the " + (d.Kind == CxbxRomKind.Zar ? "ZArchive" : "disc") + " attached as a disk through AIM, nothing copied";
                    return p;
                }
            }
            if (p.Placement == "disk") { p.Why = "the game's options say: always the disk"; return p; }
            if (p.Placement == "auto" && p.OnDisk) { p.Why = "it is unpacked on the disk already - opened from there"; return p; }
            var no = CxbxRamSession.WhyNot(installDir, p.Bytes, forced: p.Placement == "ram");
            p.Ram = no == null;
            p.Why = (p.Ram ? (p.Placement == "ram" ? "the game's options say: always the RAM disk" : "it fits under the RAM disk threshold") : no)
                  + (noAttach != null ? " (not attached where it is: " + noAttach + ")" : "");
            return p;
        }

        /// <summary>The XBE to hand Cxbx-Reloaded for <paramref name="rom"/>, the game unpacked first when it has to be -
        /// or null, with <paramref name="problem"/> saying why the launch cannot be made.</summary>
        public static string Prepare(string rom, string gameId, string exe, out string problem, out CxbxRom described)
        {
            problem = null;
            described = null;
            var installDir = Path.GetDirectoryName(exe);
            CxbxRamSession.Release("a game is being launched");

            var d = CxbxGame.Describe(rom);
            described = d;
            if (d.Problem != null && d.Kind != CxbxRomKind.ImageInArchive) { problem = d.Problem; return null; }
            if (d.Kind == CxbxRomKind.Xbe) return rom;

            var plan = Plan(rom, gameId, installDir, d);
            var gameDir = Path.Combine(plan.Folder, "game");
            if (plan.OnDisk && !plan.Ram)
            {
                Log.Info("game " + Path.GetFileName(rom) + ": " + plan.Why);
                WriteManifest(plan.Folder, rom, plan.Keep);
                var there = XbeIn(gameDir, d);
                if (there != null) { Remember(d, there); return there; }
                Log.Info("the copy on the disk has no default.xbe any more - unpacked again");
            }

            using var window = CxbxProgressWindow.Open("Nixx-Cxbx - Preparing the game");
            var name = Path.GetFileNameWithoutExtension(rom);

            // A disc: its listing first - kept from an earlier launch, or read now (nothing written). The game's EXACT
            // size, before the RAM disk is chosen and sized.
            XdvdfsResult listing = null;
            if (d.Kind == CxbxRomKind.Image || d.Kind == CxbxRomKind.ImageInArchive)
            {
                listing = Listed(d, (step, f) => window?.Report(step + " " + name, f), out problem);
                if (listing == null) return null;
                if (listing.Root("default.xbe") == null)
                {
                    problem = listing.Root("default.xex") != null ? "this is an Xbox 360 disc (default.xex) - it is Xenia's, not Cxbx-Reloaded's" : "the disc has no default.xbe at its root";
                    return null;
                }
                d.Bytes = listing.Bytes;
                plan = Plan(rom, gameId, installDir, d);
            }
            Log.Info("game " + Path.GetFileName(rom) + ": " + d.Kind + ", " + (d.Bytes >> 20) + " MB"
                     + (d.Xbe != null ? ", " + d.Xbe.TitleIdText + " \"" + d.Xbe.TitleName + "\"" : "") + " - " + (plan.Attach ? "attached" : plan.Ram ? "RAM disk" : "disk") + ": " + plan.Why);

            if (plan.Attach)
            {
                window?.Report("Attaching " + name, null);
                var root = CxbxRamSession.AttachDisc(installDir, rom);
                // A ZArchive's default.xbe sits where it is in it (its shallowest - EntryKey), a disc's at its root.
                var attached = root == null ? null
                             : d.Kind == CxbxRomKind.Zar ? (File.Exists(Path.Combine(root, d.EntryKey.Replace('/', '\\'))) ? Path.Combine(root, d.EntryKey.Replace('/', '\\')) : null)
                             : XbeIn(root, d);
                if (attached != null) { Remember(d, attached); return attached; }
                if (root != null) { Log.Info("the attached disc shows no default.xbe - unpacked instead"); CxbxRamSession.Release("no default.xbe on the attached disc"); }
                // Unpacked, then: where Plan would have put it without the attach.
                plan.Attach = false;
                plan.Ram = CxbxRamSession.WhyNot(installDir, plan.Bytes, forced: false) == null;
                Log.Info("game " + Path.GetFileName(rom) + ": unpacked onto the " + (plan.Ram ? "RAM disk" : "disk") + " instead");
            }

            string baseDir = plan.Ram ? CxbxRamSession.Open(installDir, Safe(Path.GetFileName(plan.Folder)), plan.Bytes, forced: plan.Placement == "ram") : null;
            if (baseDir != null) gameDir = Path.Combine(baseDir, "game");
            else
            {
                Directory.CreateDirectory(plan.Folder);
                WriteManifest(plan.Folder, rom, plan.Keep);
                MakeRoom(GamesFolder(installDir), plan.Folder, plan.Bytes);
            }

            try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, recursive: true); }       // a half-done copy
            catch (Exception ex) { problem = "the previous copy of this game could not be cleared (" + ex.Message + ")"; return null; }
            Directory.CreateDirectory(gameDir);

            string error = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                error = Unpack(d, listing, gameDir, (step, f) => window?.Report(step + " " + name, f));
                // A kept listing that does not describe this disc after all: forgotten, read again, once.
                if (error != null && listing != null && error.StartsWith("the disc is not the one listed"))
                {
                    Log.Info("the kept listing does not match the disc (" + error + ") - read again");
                    CxbxListing.Forget(rom);
                    listing = Listed(d, (step, f) => window?.Report(step + " " + name, f), out var again);
                    error = listing == null ? again : Unpack(d, listing, gameDir, (step, f) => window?.Report(step + " " + name, f));
                }
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            if (error == null) Log.Info("unpacking took " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s" + (listing != null ? " (" + listing.Passes + " pass(es) over the image in all)" : ""));
            if (error != null)
            {
                problem = "the game could not be unpacked: " + error;
                try { Directory.Delete(gameDir, recursive: true); } catch { }
                return null;
            }

            var xbe = XbeIn(gameDir, d);
            if (xbe == null)
            {
                problem = File.Exists(Path.Combine(gameDir, "default.xex")) ? "this is an Xbox 360 disc (default.xex) - it is Xenia's, not Cxbx-Reloaded's"
                                                                            : "the disc has no default.xbe at its root";
                return null;
            }
            File.WriteAllText(DonePath(gameDir), d.Stamp);
            Remember(d, xbe);
            Log.Info("unpacked " + (d.Bytes >> 20) + " MB " + (baseDir != null ? "onto a RAM disk at " : "onto the disk at ") + gameDir);
            return xbe;
        }

        /// <summary>The certificate read from the unpacked XBE, the size counted - what titles.tsv keeps for an archive.</summary>
        private static void Remember(CxbxRom d, string xbe)
        {
            var info = Xbe.Read(xbe);
            if (info != null) d.Xbe = info;
            try { d.Bytes = Directory.EnumerateFiles(Path.GetDirectoryName(xbe), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { }
            d.Problem = null;
            CxbxGame.Remember(d);
        }

        private static string XbeIn(string gameDir, CxbxRom d)
        {
            var xbe = Path.Combine(gameDir, "default.xbe");
            return File.Exists(xbe) ? xbe : null;
        }

        /// <summary>What a game file is, fully, for the import (CxbxLbImport): described, and for a disc its listing - kept, else
        /// read now and kept, so that the game's first launch only unpacks - and its certificate (title, region, version),
        /// learnt and kept in titles.tsv. Problem set when it is not an original Xbox game.</summary>
        public static CxbxRom Identify(string rom, Action<string, double?> progress)
        {
            progress ??= (_, _) => { };
            var d = CxbxGame.Describe(rom);
            try
            {
                if (d.Kind != CxbxRomKind.Image && d.Kind != CxbxRomKind.ImageInArchive) return d;
                var listing = Listed(d, progress, out var problem);
                if (listing == null) { d.Problem = problem; return d; }
                if (listing.Root("default.xbe") is not XdvdfsFile xbe)
                {
                    d.Problem = listing.Root("default.xex") != null ? "an Xbox 360 disc (default.xex)" : "no default.xbe at the disc's root";
                    return d;
                }
                d.Bytes = listing.Bytes;
                if (d.Xbe == null)
                {
                    var head = listing.XbeHead;
                    if (head == null)
                    {
                        progress?.Invoke("Reading its executable", null);
                        if (d.Kind == CxbxRomKind.Image) head = Xdvdfs.ReadHead(d.Path, xbe, Xbe.HeadBytes);
                        else
                        {
                            using var archive = ArchiveFactory.Open(d.Path);
                            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && CxbxGame.Norm(e.Key) == d.EntryKey);
                            head = entry == null ? null : Xdvdfs.ReadHead(() => entry.OpenEntryStream(), false, xbe, Xbe.HeadBytes);
                        }
                    }
                    d.Xbe = Xbe.Parse(head);
                }
                if (d.Xbe == null) { d.Problem = "its default.xbe is not an Xbox executable"; return d; }
                d.Problem = null;
                CxbxGame.Remember(d);
            }
            catch (Exception ex) { d.Problem = ex.GetType().Name + ": " + ex.Message; }
            return d;
        }

        /// <summary>A disc's listing: kept, else read (nothing written) and kept. Null with <paramref name="problem"/>.</summary>
        private static XdvdfsResult Listed(CxbxRom d, Action<string, double?> progress, out string problem)
        {
            problem = null;
            var kept = CxbxListing.Load(d.Path);
            if (kept != null) { Log.Info("listing kept from an earlier launch: " + kept.Files.Count + " files, " + (kept.Bytes >> 20) + " MB"); return kept; }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            XdvdfsResult r;
            if (d.Kind == CxbxRomKind.Image)
            {
                r = Xdvdfs.List(d.Path);
            }
            else
            {
                using var archive = ArchiveFactory.Open(d.Path);
                var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && CxbxGame.Norm(e.Key) == d.EntryKey);
                if (entry == null) { problem = "the disc image is no longer in the archive"; return null; }
                long length = entry.Size;
                r = Xdvdfs.List(() => entry.OpenEntryStream(), false, length,
                                (pos, pass) => progress(pass > 1 ? "Reading the disc (pass " + pass + ")" : "Reading the disc", length > 0 ? (double)pos / length : (double?)null));
            }
            if (r.Error != null || !r.Found) { problem = r.Error ?? "this is not an Xbox disc image"; return null; }
            Log.Info("listing read in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s: " + r.Files.Count + " files, " + (r.Bytes >> 20) + " MB, "
                     + r.Passes + " pass(es)" + (r.TablesFromMemory > 0 ? ", " + r.TablesFromMemory + " table(s) taken from memory" : ""));
            CxbxListing.Save(d.Path, r);
            r.Passes = 0;           // from here, the passes counted are the extraction's
            return r;
        }

        /// <summary>The game's files under <paramref name="target"/>. Null when done, else what went wrong.</summary>
        private static string Unpack(CxbxRom d, XdvdfsResult listing, string target, Action<string, double?> progress)
        {
            switch (d.Kind)
            {
                case CxbxRomKind.Image:
                {
                    // Plain, CSO, CCI or CHD: the disc's bytes, opened once.
                    using var disc = Disc.DiscImages.Open(d.Path);
                    long length = disc.Length;
                    return Xdvdfs.ExtractListed(listing, () => Disc.DiscImages.Shared(disc), true, target,
                                                (pos, pass) => progress("Unpacking", length > 0 ? (double)pos / length : (double?)null));
                }
                case CxbxRomKind.ImageInArchive:
                {
                    using var archive = ArchiveFactory.Open(d.Path);
                    var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && CxbxGame.Norm(e.Key) == d.EntryKey);
                    if (entry == null) return "the disc image is no longer in the archive";
                    long length = entry.Size;
                    var error = Xdvdfs.ExtractListed(listing, () => entry.OpenEntryStream(), false, target,
                                                     (pos, pass) => progress(pass > 1 ? "Unpacking (pass " + pass + ")" : "Unpacking", length > 0 ? (double)pos / length : (double?)null));
                    if (error == null && listing.Passes > 1) Log.Info("the image was read " + listing.Passes + " times to unpack: some files share their data");
                    return error;
                }
                case CxbxRomKind.Zar:
                {
                    // The game's files out of the ZArchive - those under its default.xbe's folder, that folder as the root.
                    using var z = Zar.ZArchive.Open(d.Path);
                    if (z == null) return "the ZArchive does not read";
                    var prefix = CxbxGame.Folder(d.EntryKey);
                    var root = Path.GetFullPath(target);
                    var entries = z.Entries.Where(e => !e.IsDirectory && e.Path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    long total = Math.Max(1, entries.Sum(e => e.Length)), done = 0;
                    var buffer = new byte[1 << 20];
                    foreach (var e in entries)
                    {
                        var rel = e.Path.Replace('\\', '/').Substring(prefix.Length).Replace('/', '\\');
                        var path = Path.GetFullPath(Path.Combine(root, rel));
                        if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return "a ZArchive entry escapes the game's folder: " + e.Path;
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var s = z.OpenRead(e))
                        using (var f = File.Create(path))
                        {
                            int n;
                            while ((n = s.Read(buffer, 0, buffer.Length)) > 0) { f.Write(buffer, 0, n); done += n; progress("Unpacking", (double)done / total); }
                        }
                    }
                    return null;
                }
                case CxbxRomKind.TreeInArchive:
                {
                    using var archive = ArchiveFactory.Open(d.Path);
                    var prefix = CxbxGame.Folder(d.EntryKey);
                    var root = Path.GetFullPath(target);
                    var entries = archive.Entries.Where(e => !e.IsDirectory && CxbxGame.Norm(e.Key).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    long total = Math.Max(1, entries.Sum(e => e.Size)), done = 0;
                    var buffer = new byte[1 << 20];
                    foreach (var e in entries)
                    {
                        var rel = CxbxGame.Norm(e.Key).Substring(prefix.Length).Replace('/', '\\');
                        var path = Path.GetFullPath(Path.Combine(root, rel));
                        if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return "an archive entry escapes the game's folder: " + e.Key;
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var s = e.OpenEntryStream())
                        using (var f = File.Create(path))
                        {
                            int n;
                            while ((n = s.Read(buffer, 0, buffer.Length)) > 0) { f.Write(buffer, 0, n); done += n; progress("Unpacking", (double)done / total); }
                        }
                    }
                    return null;
                }
                default: return d.Problem ?? "nothing to unpack";
            }
        }

        // ── the game's folder on the disk ────────────────────────────────────

        /// <summary>&lt;games folder&gt;\&lt;the game's file name&gt; - taken by another game already, a number is added.</summary>
        public static string GameFolder(string installDir, string rom)
        {
            var root = GamesFolder(installDir);
            var name = Safe(Path.GetFileNameWithoutExtension(rom));
            foreach (var candidate in new[] { name }.Concat(Enumerable.Range(2, 50).Select(i => name + " (" + i + ")")))
            {
                var dir = Path.Combine(root, candidate);
                var owner = ManifestValue(dir, "rom");
                if (owner == null || string.Equals(owner, rom, StringComparison.OrdinalIgnoreCase)) return dir;
            }
            return Path.Combine(root, name + " " + Guid.NewGuid().ToString("N").Substring(0, 6));
        }

        private static bool IsDone(string gameDir, string stamp)
        {
            try { var f = DonePath(gameDir); return File.Exists(f) && File.ReadAllText(f).Trim() == stamp; }
            catch { return false; }
        }

        /// <summary>Beside the game's folder, not in it: that folder is the Xbox's D: drive.</summary>
        private static string DonePath(string gameDir) => Path.Combine(Path.GetDirectoryName(gameDir), Done);

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

        private static void WriteManifest(string dir, string rom, bool keep)
            => File.WriteAllLines(Path.Combine(dir, Manifest), new[]
            {
                "# Which game this folder is - the Nixx-Cxbx plugin unpacked it here. Safe to delete: it is unpacked again at its next launch.",
                "rom=" + rom, "last_used=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), "keep=" + (keep ? "on" : "off"),
            });

        /// <summary>The game's "keep" written into its folder at once - the options window's box, without waiting for a launch.</summary>
        public static void SetKeep(string folder, bool keep)
        {
            try
            {
                var path = Path.Combine(folder, Manifest);
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("keep=", StringComparison.OrdinalIgnoreCase)).ToList();
                lines.Add("keep=" + (keep ? "on" : "off"));
                File.WriteAllLines(path, lines);
            }
            catch (Exception ex) { Log.Warn("keep of " + folder, ex); }
        }

        /// <summary>Room for <paramref name="needed"/> bytes under the limit: the games launched longest ago lose their
        /// folder, whole - never <paramref name="keep"/>, never one kept.</summary>
        private static void MakeRoom(string root, string keep, long needed)
        {
            long limit = Limit();
            if (limit <= 0 || needed <= 0 || !Directory.Exists(root)) return;
            var games = Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, Manifest)) && ManifestValue(d, "keep") != "on")
                .Select(d => (Dir: d, Used: DateTime.TryParse(ManifestValue(d, "last_used"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : DateTime.MinValue,
                              Size: SizeOf(Path.Combine(d, "game"))))
                .ToList();
            long total = games.Where(g => !string.Equals(g.Dir, keep, StringComparison.OrdinalIgnoreCase)).Sum(g => g.Size);
            foreach (var g in games.Where(g => !string.Equals(g.Dir, keep, StringComparison.OrdinalIgnoreCase)).OrderBy(g => g.Used))
            {
                if (total + needed <= limit) break;
                try { Directory.Delete(g.Dir, recursive: true); total -= g.Size; Log.Info(g.Dir + " removed to make room (last launched " + g.Used.ToLocalTime() + ")"); }
                catch (Exception ex) { Log.Warn("could not remove " + g.Dir, ex); }
            }
            if (total + needed > limit) Log.Info((total + needed) / (1024 * 1024) + " MB is over the limit even so - this game alone needs it");
        }

        public static long SizeOf(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0; } catch { return 0; }
        }
    }
}
