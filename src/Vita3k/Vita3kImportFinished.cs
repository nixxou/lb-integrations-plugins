// Vita3K after an import to it (LbImportFinished, src\Catalog\LbCatalog.cs): each game's REGION and VERSION,
// worked out while the list was on show and set once the games are in - through the data API only.
//
// PREPARED EARLY (Mehdi, 30/09): Vita3kImportCleanup reads every file's param.sfo when the list shows; it hands
// each game kept to Prepare, by its full path. After the import, a game - or a version of one - whose
// ApplicationPath is one of those paths gets what was prepared for it; nothing else is touched.
//
//   REGION, from the game itself: its CONTENT_ID begins with the store it was sold in (EP4403-PCSB00400_00-...:
//   E, Europe), else its TITLE_ID says the same by its prefix (PCSB: Europe) - RegionOf.
//   VERSION, from the file's name: only what it holds between brackets or parentheses, one space between each -
//   "[PCSA00017] [USA] [NoNpDRM]". Nothing of the kind, nothing: LaunchBox otherwise puts in whatever is left
//   of the name ("XYbSXpQlzYNomayKolCGcw..." for King Oddball) - VersionOf.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Vita3k
{
    internal sealed class Vita3kImportFinished : ILbImportFinished
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (string Region, string Version)> Prepared =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>What a game of the list gets once it is in the library. Called by Vita3kImportCleanup.</summary>
        internal static void Prepare(string path, string contentId, string titleId)
        {
            var full = LbipImportWatch.Full(path);
            if (full == null) return;
            lock (Gate) Prepared[full] = (RegionOf(contentId, titleId), VersionOf(path));
        }

        // ── the table ──────────────────────────────────────────────────────

        /// <summary>The first letter of a CONTENT_ID: the store the game was sold in.</summary>
        private static readonly Dictionary<char, string> ByStore = new Dictionary<char, string>
        {
            ['U'] = "North America", ['E'] = "Europe", ['J'] = "Japan", ['H'] = "Asia", ['K'] = "Korea",
        };

        /// <summary>A TITLE_ID's prefix, when the CONTENT_ID says nothing.</summary>
        private static readonly Dictionary<string, string> ByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PCSA"] = "North America", ["PCSE"] = "North America",
            ["PCSB"] = "Europe", ["PCSF"] = "Europe",
            ["PCSC"] = "Japan", ["PCSG"] = "Japan", ["VCJS"] = "Japan", ["VLJS"] = "Japan", ["VLJM"] = "Japan",
            ["PCSD"] = "Asia", ["PCSH"] = "Asia", ["VCAS"] = "Asia", ["VLAS"] = "Asia",
            ["VCKS"] = "Korea", ["VLKS"] = "Korea",
        };

        internal static string RegionOf(string contentId, string titleId)
        {
            if (!string.IsNullOrEmpty(contentId) && ByStore.TryGetValue(char.ToUpperInvariant(contentId[0]), out var r)) return r;
            if (!string.IsNullOrEmpty(titleId) && titleId.Length >= 4 && ByTitle.TryGetValue(titleId.Substring(0, 4), out r)) return r;
            return null;
        }

        /// <summary>The name's [tags] and (tags), one space between each - "" when it has none.</summary>
        internal static string VersionOf(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path ?? "");
            return string.Join(" ", Regex.Matches(name, @"\[[^\[\]]*\]|\([^()]*\)").Cast<Match>().Select(m => m.Value.Trim()));
        }

        // ── after the import ───────────────────────────────────────────────

        public void AfterImport(LbImportDone done)
        {
            try { Run(done); }
            catch (Exception ex) { Log.Warn("[import] after the import: could not set the regions and versions", ex); }
        }

        private static void Run(LbImportDone done)
        {
            if (done == null || !Vita3kPaths.IsVita3kExecutable(done.EmulatorPath)) return;
            Dictionary<string, (string Region, string Version)> prepared;
            lock (Gate) { prepared = new Dictionary<string, (string, string)>(Prepared, StringComparer.OrdinalIgnoreCase); Prepared.Clear(); }
            Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count + " in the library"
                     + (done.Complete ? "" : " (gave up waiting)") + ", " + prepared.Count + " prepared from the list");
            if (prepared.Count == 0) return;
            if (!Vita3kSettings.ImportRegionVersion) { Log.Info("[import] regions and versions left as LaunchBox set them: turned off in the settings"); return; }

            int games = 0, versions = 0;
            foreach (var game in PluginHelper.DataManager?.GetPlatformByName(done.Platform)?.GetAllGames(true, true) ?? Array.Empty<IGame>())
            {
                if (prepared.TryGetValue(LbipImportWatch.Full(game.ApplicationPath) ?? "", out var p) && Apply(game.Title, p,
                        () => game.Region, v => game.Region = v, () => game.Version, v => game.Version = v))
                    games++;
                foreach (var app in game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                    if (prepared.TryGetValue(LbipImportWatch.Full(app.ApplicationPath) ?? "", out var q) && Apply(game.Title + " / " + app.Name, q,
                            () => app.Region, v => app.Region = v, () => app.Version, v => app.Version = v))
                        versions++;
            }
            if (games + versions == 0) { Log.Info("[import] regions and versions: nothing to change"); return; }
            PluginHelper.DataManager.Save(true);
            Log.Info("[import] regions and versions set on " + games + " game(s) and " + versions + " version(s), saved through the data API");
        }

        private static bool Apply(string what, (string Region, string Version) p,
                                  Func<string> region, Action<string> setRegion, Func<string> version, Action<string> setVersion)
        {
            var changes = new List<string>();
            if (p.Region != null && !string.Equals(region(), p.Region, StringComparison.Ordinal))
            { changes.Add("region " + (region() ?? "none") + " -> " + p.Region); setRegion(p.Region); }
            if (!string.Equals(version() ?? "", p.Version, StringComparison.Ordinal))
            { changes.Add("version \"" + (version() ?? "") + "\" -> \"" + p.Version + "\""); setVersion(p.Version); }
            if (changes.Count > 0) Log.Info("[import]   " + what + ": " + string.Join(", ", changes));
            return changes.Count > 0;
        }
    }
}
