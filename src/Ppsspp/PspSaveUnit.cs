// The PSP save unit: which folders under PSP\SAVEDATA make up ONE save, and how to copy them.
//
// This is the file that has to agree with Argosy, because the unit definition IS the interop
// contract. Argosy's PrefixBundleFolderHandler.folderMatches is a case-insensitive
// startsWith(discId) over the siblings of SAVEDATA, and its extractDownload DELETES every prefix
// match before unpacking a restore. So an incomplete unit does not merely sync badly - it destroys
// the folders it left out, permanently, on the other device.
//
//   PSP\SAVEDATA\ULUS10064DATA00\     the game's save
//   PSP\SAVEDATA\ULUS10064SETTINGS\   its options
//   PSP\SAVEDATA\ULUS10064SYSTEM\     its system data
//                ^^^^^^^^^ the 9-character DISC_ID; the suffix is the game's own invention
//
// All three are ONE save. The suffix follows no template - do not try to parse it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Ppsspp
{
    /// <summary>One PSP save: a disc id and the folders that carry it.</summary>
    internal sealed class PspSaveUnit
    {
        public string DiscId;

        /// <summary>Absolute paths of every folder in the unit, ordered so <see cref="PrimaryPath"/>
        /// is first and the rest follow by name.</summary>
        public List<string> Folders = new List<string>();

        /// <summary>The folder LiteBox points FileLocation at. NOT the SAVEDATA parent: SaveManager's
        /// NeedsBackup has no container arm, so a FileLocation on SAVEDATA would make it hash every
        /// game on the memstick at every page render.</summary>
        public string PrimaryPath => Folders.Count > 0 ? Folders[0] : null;

        /// <summary>What to show for this save: SAVEDATA_TITLE ("Chapter 3 - Midgar"), else the game's
        /// title, else the disc id. From the primary folder's PARAM.SFO.</summary>
        public string Title;

        /// <summary>The GAME's title, PARAM.SFO's TITLE key. Distinct from <see cref="Title"/> on
        /// purpose: TITLE names the game, SAVEDATA_TITLE names the save within it, and matching a
        /// library entry against the latter compares a game name to "Chapter 3 - Midgar". This is the
        /// one to match on when no disc id could be read from the ROM.</summary>
        public string GameTitle;

        public long SizeBytes;
        public DateTime LastWriteUtc;
    }

    internal static class PspSaveUnits
    {
        public const string ParamSfoName = "PARAM.SFO";

        /// <summary>A disc id is 9 characters (ULUS10064). PPSSPP mints fake ones for homebrew in the
        /// same shape, so the length holds there too.</summary>
        public const int DiscIdLength = 9;

        // Argosy's rule for telling a real save from installed game data, reproduced exactly:
        // PrefixBundleFolderHandler.isGameDataInstall, in
        //   argosy-launcher/app/src/main/kotlin/com/nendo/argosy/data/sync/platform/PlatformSaveHandlerRegistry.kt
        // A folder is game data when its PARAM.SFO is a readable file of at most 64 KiB containing
        // NEITHER marker. Note what that implies for every other case: no PARAM.SFO, an unreadable
        // one, or one over the cap, and the folder is KEPT. Argosy does not guess, and neither do we.
        //
        // This duplicates a rule that lives in Kotlin and will drift. It is duplicated because there
        // is nothing to share it through; the citation above is the mitigation.
        private const long MaxSfoBytesForGameDataTest = 64 * 1024;
        private static readonly string[] SaveDataMarkers = { "SAVEDATA_PARAMS", "SAVEDATA_FILE_LIST" };

        /// <summary>Every save unit under <paramref name="saveDataDir"/>, one per disc id.</summary>
        public static List<PspSaveUnit> Enumerate(string saveDataDir)
        {
            var units = new List<PspSaveUnit>();
            if (string.IsNullOrWhiteSpace(saveDataDir) || !SafeDirExists(saveDataDir)) return units;

            var byDiscId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(saveDataDir).ToList(); }
            catch (Exception ex) { Log.Warn("could not list " + saveDataDir, ex); return units; }

            foreach (var dir in dirs)
            {
                string name;
                try { name = Path.GetFileName(dir); } catch { continue; }
                if (string.IsNullOrEmpty(name) || name.Length < DiscIdLength) continue;
                if (IsGameDataInstall(dir)) { Log.Info("skipping installed game data: " + name); continue; }

                string discId = name.Substring(0, DiscIdLength).ToUpperInvariant();
                if (!byDiscId.TryGetValue(discId, out var list)) byDiscId[discId] = list = new List<string>();
                list.Add(dir);
            }

            foreach (var kv in byDiscId) units.Add(Build(kv.Key, kv.Value));
            return units;
        }

        /// <summary>The unit for one disc id, or null when nothing matches.</summary>
        public static PspSaveUnit ForDiscId(string saveDataDir, string discId)
        {
            if (string.IsNullOrWhiteSpace(discId)) return null;
            var folders = MatchingFolders(saveDataDir, discId);
            return folders.Count == 0 ? null : Build(discId.ToUpperInvariant(), folders);
        }

        /// <summary>Folders whose name starts with the disc id, game data excluded. The same predicate
        /// as Argosy's folderMatches, case-insensitive.</summary>
        public static List<string> MatchingFolders(string saveDataDir, string discId)
        {
            var hits = new List<string>();
            if (string.IsNullOrWhiteSpace(saveDataDir) || string.IsNullOrWhiteSpace(discId)) return hits;
            if (!SafeDirExists(saveDataDir)) return hits;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(saveDataDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name == null || !name.StartsWith(discId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsGameDataInstall(dir)) continue;
                    hits.Add(dir);
                }
            }
            catch (Exception ex) { Log.Warn("could not list " + saveDataDir, ex); }
            return hits;
        }

        private static PspSaveUnit Build(string discId, List<string> folders)
        {
            // Deterministic order, shortest name first: a plain "<discId>DATA00" sorts before
            // "<discId>SETTINGS", so the primary is the folder a human would call the save.
            var ordered = folders
                .OrderBy(f => (Path.GetFileName(f) ?? "").Length)
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var unit = new PspSaveUnit { DiscId = discId, Folders = ordered };
            var sfo = ParamSfo.FromFile(Path.Combine(ordered[0], ParamSfoName));
            unit.GameTitle = sfo?.FirstString("TITLE");
            unit.Title = sfo?.FirstString("SAVEDATA_TITLE", "TITLE") ?? discId;

            foreach (var folder in ordered)
            {
                foreach (var file in SafeFiles(folder))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        unit.SizeBytes += fi.Length;
                        if (fi.LastWriteTimeUtc > unit.LastWriteUtc) unit.LastWriteUtc = fi.LastWriteTimeUtc;
                    }
                    catch { }
                }
            }
            return unit;
        }

        /// <summary>Copy every folder of the unit into <paramref name="destinationFolder"/>, each under
        /// its OWN name and with NO intermediate level.
        ///
        /// This shape is the storage contract, not a detail. LiteBox copies this folder verbatim into
        /// the vault, then hashes it with SaveHash.OfDirectory and serves it with WriteFolderZip, both
        /// of which name entries relative to the folder's root. Written this way, the entries come out
        /// as "ULUS10064DATA00/PARAM.SFO" - what Argosy zips and hashes. One level too many here and
        /// every hash diverges; measured against sigil's golden vector.</summary>
        public static bool CopyInto(PspSaveUnit unit, string destinationFolder, out string error)
        {
            error = null;
            try
            {
                if (unit == null || unit.Folders.Count == 0)
                { error = "This save has no folders on disk."; return false; }
                if (string.IsNullOrWhiteSpace(destinationFolder))
                { error = "No destination folder was given."; return false; }

                Directory.CreateDirectory(destinationFolder);
                foreach (var src in unit.Folders)
                {
                    var name = Path.GetFileName(src);
                    if (string.IsNullOrEmpty(name)) continue;
                    CopyDirectory(src, Path.Combine(destinationFolder, name));
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("could not copy the save unit", ex);
                error = ex.Message;
                return false;
            }
        }

        public static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.EnumerateDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        /// <summary>Argosy's isGameDataInstall, to the letter. See the note above on the duplication.</summary>
        public static bool IsGameDataInstall(string folder)
        {
            try
            {
                var sfoPath = Path.Combine(folder, ParamSfoName);
                var fi = new FileInfo(sfoPath);
                if (!fi.Exists || fi.Length > MaxSfoBytesForGameDataTest) return false;   // keep it
                byte[] bytes;
                try { bytes = File.ReadAllBytes(sfoPath); } catch { return false; }       // keep it
                foreach (var marker in SaveDataMarkers)
                    if (ContainsAscii(bytes, marker)) return false;                       // a real save
                return true;
            }
            catch { return false; }
        }

        private static bool ContainsAscii(byte[] haystack, string needle)
        {
            var pattern = Encoding.ASCII.GetBytes(needle);
            if (pattern.Length == 0 || haystack.Length < pattern.Length) return false;
            for (int i = 0; i <= haystack.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && haystack[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return true;
            }
            return false;
        }

        private static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        private static bool SafeDirExists(string p)
        {
            try { return Directory.Exists(p); } catch { return false; }
        }
    }
}
