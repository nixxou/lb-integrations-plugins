// A PSP game's UPDATES (Mehdi, 03/10) - noted, proposed, never installed by default.
//
// WHAT AN UPDATE IS: a PBP whose PARAM.SFO says CATEGORY "PG", its DISC_ID the game's and its DISC_VERSION the disc version it
// patches; on a PSP it is installed as ms0:/PSP/GAME/<DISC_ID>/PBOOT.PBP. PPSSPP DOES BOOT IT (Core/PSPLoaders.cpp,
// FindGameUpdatePBOOT): an installed PBOOT.PBP whose DISC_VERSION is the disc's is started instead of the disc's executable.
// (A FIRMWARE update - DISC_ID MSTKUPDATE, "PSP Update ver ..." - is not one: PPSSPP needs no firmware, the import drops it.)
//
//   NOTED    by the import (PpssppLbImport - out of the list, it is not a game) in <plugin data>\updates.tsv: its path, the
//            game's DISC_ID, DISC_VERSION, APP_VER and title. And looked for again beside the game's own file when its
//            options window opens - an update put there after the import is found too.
//   PROPOSED in the game's options window, tab Updates: each one, its version, whether its DISC_VERSION is the game's.
//   INSTALLED only from there, once - PPSSPP's memory stick is permanent, unlike Vita3K's disposable console: copied as
//            PSP\GAME\<DISC_ID>\PBOOT.PBP, with lbip-update.txt beside it naming what was copied. REMOVED only when that note
//            says the PBOOT.PBP there is ours: one put there by hand is never touched.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    internal sealed class PspUpdate
    {
        public string Path, DiscId, DiscVersion, AppVer, Title;
        public override string ToString() => (string.IsNullOrWhiteSpace(Title) ? "update" : Title) + " - version " + (AppVer ?? "?") + " (for disc version " + (DiscVersion ?? "?") + ")";
    }

    internal static class PpssppUpdates
    {
        private const string IndexName = "updates.tsv", NoteName = "lbip-update.txt";
        private static readonly object Gate = new object();

        private static string IndexPath => System.IO.Path.Combine(PpssppSettings.Dir, IndexName);

        /// <summary>A PARAM.SFO that is a game update's.</summary>
        public static bool IsUpdate(LbIntegrations.Psf.ParamSfo sfo) => string.Equals(sfo?.GetString("CATEGORY")?.Trim(), "PG", StringComparison.OrdinalIgnoreCase);

        /// <summary>A PARAM.SFO that is a firmware update's - dropped, never noted.</summary>
        public static bool IsFirmware(LbIntegrations.Psf.ParamSfo sfo)
        {
            var id = sfo?.GetString("DISC_ID")?.Trim() ?? "";
            var title = sfo?.GetString("TITLE")?.Trim() ?? "";
            return id.Equals("MSTKUPDATE", StringComparison.OrdinalIgnoreCase) || title.StartsWith("PSP Update", StringComparison.OrdinalIgnoreCase);
        }

        private static PspUpdate From(string path, LbIntegrations.Psf.ParamSfo sfo) => new PspUpdate
        {
            Path = System.IO.Path.GetFullPath(path),
            DiscId = (sfo.GetString("DISC_ID") ?? "").Trim().Replace("-", "").ToUpperInvariant(),
            DiscVersion = sfo.GetString("DISC_VERSION")?.Trim(),
            AppVer = sfo.GetString("APP_VER")?.Trim(),
            Title = sfo.FirstString("PBOOT_TITLE", "TITLE")?.Replace("\r", " ").Replace("\n", " ").Trim(),
        };

        /// <summary>Note an update - the import's.</summary>
        public static void Note(string path, LbIntegrations.Psf.ParamSfo sfo)
        {
            try
            {
                var u = From(path, sfo);
                if (u.DiscId.Length == 0) return;
                lock (Gate)
                {
                    var all = ReadIndex();
                    all.RemoveAll(x => string.Equals(x.Path, u.Path, StringComparison.OrdinalIgnoreCase));
                    all.Add(u);
                    WriteIndex(all);
                }
                Log.Info("[import]   update noted for " + u.DiscId + ": " + u);
            }
            catch (Exception ex) { Log.Warn("could not note a game update", ex); }
        }

        /// <summary>The updates known for <paramref name="discId"/>: the noted ones still there, and any beside the game's file.</summary>
        public static List<PspUpdate> For(string discId, string romPath)
        {
            var found = new List<PspUpdate>();
            if (string.IsNullOrWhiteSpace(discId)) return found;
            lock (Gate) found.AddRange(ReadIndex().Where(u => u.DiscId == discId && File.Exists(u.Path)));
            try
            {
                var dir = romPath == null ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(romPath));
                if (dir != null && Directory.Exists(dir))
                    foreach (var pbp in Directory.EnumerateFiles(dir, "*.pbp", SearchOption.AllDirectories).Take(200))
                    {
                        if (found.Any(u => string.Equals(u.Path, System.IO.Path.GetFullPath(pbp), StringComparison.OrdinalIgnoreCase))) continue;
                        var sfo = PspDiscId.SfoOf(pbp);
                        if (sfo != null && IsUpdate(sfo) && From(pbp, sfo).DiscId == discId) found.Add(From(pbp, sfo));
                    }
            }
            catch (Exception ex) { Log.Verbose("could not look for updates beside " + romPath + " - " + ex.Message); }
            return found.OrderBy(u => u.AppVer, StringComparer.Ordinal).ToList();
        }

        // ── installed ────────────────────────────────────────────────────────

        private static string GameDir(PpssppLayout layout, string discId) => System.IO.Path.Combine(layout.PspDir, "GAME", discId);

        /// <summary>The update installed for the game: its PBOOT.PBP's own versions, and whether this plugin put it there.</summary>
        public static (PspUpdate Update, bool Ours)? Installed(PpssppLayout layout, string discId)
        {
            try
            {
                if (layout?.PspDir == null || string.IsNullOrWhiteSpace(discId)) return null;
                var pboot = System.IO.Path.Combine(GameDir(layout, discId), "PBOOT.PBP");
                if (!File.Exists(pboot)) return null;
                var sfo = PspDiscId.SfoOf(pboot);
                var u = sfo == null ? new PspUpdate { Path = pboot, DiscId = discId } : From(pboot, sfo);
                var note = System.IO.Path.Combine(GameDir(layout, discId), NoteName);
                bool ours = File.Exists(note) && File.ReadAllLines(note).FirstOrDefault()?.Trim() == Sha256(pboot);
                return (u, ours);
            }
            catch { return null; }
        }

        /// <summary>Install <paramref name="u"/> as the game's PBOOT.PBP. Null, or why not.</summary>
        public static string Install(PpssppLayout layout, PspUpdate u)
        {
            try
            {
                if (PpssppIni.RunningEmulatorProcess() != null) return "PPSSPP is running - close it first";
                var now = Installed(layout, u.DiscId);
                if (now != null && !now.Value.Ours) return "a PBOOT.PBP that is not this plugin's is already in " + GameDir(layout, u.DiscId) + " - it is left as it is";
                var dir = GameDir(layout, u.DiscId);
                Directory.CreateDirectory(dir);
                var pboot = System.IO.Path.Combine(dir, "PBOOT.PBP");
                File.Copy(u.Path, pboot + ".tmp", overwrite: true);
                File.Copy(pboot + ".tmp", pboot, overwrite: true);
                File.Delete(pboot + ".tmp");
                File.WriteAllLines(System.IO.Path.Combine(dir, NoteName), new[] { Sha256(pboot), "installed by Nixx-PPSSPP from " + u.Path }, new UTF8Encoding(false));
                Log.Info("update installed for " + u.DiscId + ": " + u + " -> " + pboot);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Take out the game's PBOOT.PBP - only when this plugin installed it. Null, or why not.</summary>
        public static string Remove(PpssppLayout layout, string discId)
        {
            try
            {
                if (PpssppIni.RunningEmulatorProcess() != null) return "PPSSPP is running - close it first";
                var now = Installed(layout, discId);
                if (now == null) return null;
                if (!now.Value.Ours) return "the PBOOT.PBP there was not put by this plugin - it is left as it is";
                var dir = GameDir(layout, discId);
                File.Delete(System.IO.Path.Combine(dir, "PBOOT.PBP"));
                File.Delete(System.IO.Path.Combine(dir, NoteName));
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                Log.Info("update removed for " + discId);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ── updates.tsv ──────────────────────────────────────────────────────

        private static List<PspUpdate> ReadIndex()
        {
            var all = new List<PspUpdate>();
            try
            {
                if (!File.Exists(IndexPath)) return all;
                foreach (var line in File.ReadAllLines(IndexPath))
                {
                    var c = line.Split('\t');
                    if (c.Length >= 5) all.Add(new PspUpdate { Path = c[0], DiscId = c[1], DiscVersion = c[2], AppVer = c[3], Title = c[4] });
                }
            }
            catch (Exception ex) { Log.Warn("could not read " + IndexPath, ex); }
            return all;
        }

        private static void WriteIndex(List<PspUpdate> all)
        {
            Directory.CreateDirectory(PpssppSettings.Dir);
            File.WriteAllLines(IndexPath, all.Select(u => string.Join("\t", u.Path, u.DiscId, u.DiscVersion ?? "", u.AppVer ?? "", (u.Title ?? "").Replace('\t', ' '))), new UTF8Encoding(false));
        }

        private static string Sha256(string path)
        {
            using var s = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
        }
    }
}
