// Xenia after an import to it (LbImportFinished, src\Catalog\LbCatalog.cs): each game's REGION, worked out while the list
// was on show and set once the games are in - through the data API only, as Vita3kImportFinished and the Cxbx plugin do
// (Mehdi, 03/10: "on n'est pas capable de déterminer la région pour les jeux 360 ?").
//
// THE REGION IS THE GAME'S OWN: the region flags of its executable (xex2_security_info.region, read by the scan -
// XeniaScanEntry.Region), named as LaunchBox names regions (Xex.RegionName): one zone, or "World" for several. A game
// whose executable says nothing keeps what LaunchBox gave it. Turned off in the Nixx window's Xenia tab
// (content.ini, import_region=off).
//
// MATCHED BY THE FILE, NOT THE NAME: a game of the list is found again by its ApplicationPath, full - on a game, AND on
// an additional version of one (LaunchBox may file a disc as another game's version rather than a game of its own).

using System;
using System.Collections.Generic;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xenia
{
    internal sealed class XeniaImportFinished : ILbImportFinished
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> Prepared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The region a game of the list gets once it is in the library. Called by XeniaLbImport.</summary>
        internal static void Prepare(string path, string region)
        {
            var full = LbipImportWatch.Full(path);
            if (full == null || string.IsNullOrEmpty(region)) return;
            lock (Gate) Prepared[full] = region;
        }

        public void AfterImport(LbImportDone done)
        {
            try { Run(done); }
            catch (Exception ex) { Log.Warn("[import] after the import: could not set the regions", ex); }
        }

        private static void Run(LbImportDone done)
        {
            if (done == null || !XeniaPaths.IsXeniaExecutable(done.EmulatorPath)) return;
            Dictionary<string, string> prepared;
            lock (Gate) { prepared = new Dictionary<string, string>(Prepared, StringComparer.OrdinalIgnoreCase); Prepared.Clear(); }
            Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count + " in the library"
                     + (done.Missing.Count == 0 ? "" : ", " + done.Missing.Count + " left out by LaunchBox") + (done.Complete ? "" : " (gave up waiting)")
                     + ", " + prepared.Count + " region(s) prepared from the list");
            if (prepared.Count == 0) return;
            if (!XeniaLbImport.Regions) { Log.Info("[import] regions left as LaunchBox set them: turned off in the settings"); return; }

            int games = 0, versions = 0;
            foreach (var game in PluginHelper.DataManager?.GetPlatformByName(done.Platform)?.GetAllGames(true, true) ?? Array.Empty<IGame>())
            {
                if (prepared.TryGetValue(LbipImportWatch.Full(game.ApplicationPath) ?? "", out var r) && Apply(game.Title, r, () => game.Region, v => game.Region = v))
                    games++;
                foreach (var app in game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                    if (prepared.TryGetValue(LbipImportWatch.Full(app.ApplicationPath) ?? "", out var q) && Apply(game.Title + " / " + app.Name, q, () => app.Region, v => app.Region = v))
                        versions++;
            }
            if (games + versions == 0) { Log.Info("[import] regions: nothing to change"); return; }
            PluginHelper.DataManager.Save(true);
            Log.Info("[import] regions set on " + games + " game(s) and " + versions + " version(s), saved through the data API");
        }

        private static bool Apply(string what, string region, Func<string> current, Action<string> set)
        {
            if (string.Equals(current(), region, StringComparison.Ordinal)) return false;
            Log.Info("[import]   " + what + ": region " + (current() ?? "none") + " -> " + region);
            set(region);
            return true;
        }
    }
}
