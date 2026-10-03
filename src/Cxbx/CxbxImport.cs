// LaunchBox's "Import ROM Files" wizard, for the original Xbox (Mehdi, 03/10) - XeniaLbImport's watch of the wizard and
// Vita3kImportFinished's touch after it, put together:
//
//   THE LIST, while it is on show (before "Finish"): each line judged by what its file is, read by content - an original
//   Xbox game (a disc image with a default.xbe, an archive holding one, a game unpacked) stays; an Xbox 360 disc, or
//   anything that is not an Xbox game, goes, and the log says why. Each disc is listed on the way (CxbxPlace.Identify):
//   the listing is kept, so the game's first launch only unpacks, and its certificate - title id, version, region - too.
//   AFTER THE IMPORT (LbImportFinished), through the data API, each game of the list gets:
//   - its TITLE: the compatibility site's name for its serial (CxbxCompat), else the one its certificate carries;
//   - its REGION: the site's entry for its serial and version ("Europe: Classics" -> Europe, "USA" -> North America),
//     else its certificate's (North America, Japan, the rest of the world as Europe; several: World).
// Each one on by default, turned off at the top of the Nixx window's Cxbx-Reloaded tab.
//
// WHEN: the platform page says "Microsoft Xbox", by its platform or its scrape-as. ONLY INSIDE LAUNCHBOX (the wizard is
// WPF, watched through class handlers - see XeniaLbImport). Not done: Xenia's swap of the platform for the scan (LaunchBox
// left an Xbox 360 import's zips out) - to be measured for the original Xbox first: the log lists what LaunchBox found.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using LbIntegrations.Catalog;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxLbImport
    {
        private const string Platform = "Microsoft Xbox";
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";

        private static bool _installed;
        private static object _platformPage, _cleanedList;

        public static bool Clean => CxbxSettings.On(CxbxSettings.Read(), "import_clean", true);
        public static bool Title => CxbxSettings.On(CxbxSettings.Read(), "import_title", true);
        /// <summary>A zip / 7z named as an Xbox game of LaunchBox's database is kept without being read (03/10) - off by
        /// default (Mehdi): every file read is what the import is sure of.</summary>
        public static bool TrustNames => CxbxSettings.On(CxbxSettings.Read(), "import_by_name", false);
        public static bool Region => CxbxSettings.On(CxbxSettings.Read(), "import_region", true);

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "LaunchBox", StringComparison.OrdinalIgnoreCase)) return;
                var thread = new System.Threading.Thread(() =>
                {
                    for (int i = 0; i < 120; i++)
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null) { app.Dispatcher.BeginInvoke(new Action(Hook)); return; }
                        System.Threading.Thread.Sleep(500);
                    }
                    Log.Warn("[import] no WPF application in this host - the import wizard is left as it is");
                })
                { IsBackground = true, Name = "Cxbx import wizard" };
                thread.Start();
            }
            catch (Exception ex) { Log.Warn("[import] could not watch the import wizard", ex); }
        }

        private static void Hook()
        {
            try
            {
                System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),
                    System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler(WindowLoaded));
            }
            catch (Exception ex) { Log.Warn("[import] could not watch the import wizard", ex); }
        }

        private static void WindowLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Window window) || window.DataContext?.GetType().Name != WizardType) return;
                _platformPage = null;
                _cleanedList = null;
                if (window.DataContext is INotifyPropertyChanged notify)
                    notify.PropertyChanged += (s, a) => PageShown(window, s);
            }
            catch (Exception ex) { Log.Warn("[import] wizard opened", ex); }
        }

        private static void PageShown(System.Windows.Window window, object wizard)
        {
            try
            {
                var page = CurrentPage(wizard);
                var name = page?.GetType().Name;
                if (name == PlatformPage) { _platformPage = page; _cleanedList = null; }
                else if (name == GameListPage && !ReferenceEquals(_cleanedList, page) && IsXbox() && (Clean || Title || Region))
                {
                    _cleanedList = page;
                    WhenFilled(window, page);
                }
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        private static bool IsXbox()
            => _platformPage != null && (Is(Get(_platformPage, "Platform")) || Is(Get(_platformPage, "ScrapeAs")));

        private static bool Is(string platform) => string.Equals(platform, Platform, StringComparison.OrdinalIgnoreCase);

        /// <summary>The list fills after its page appears: the work once it has stopped changing, 500 ms.</summary>
        private static void WhenFilled(System.Windows.Window window, object gameList)
        {
            object games = null;
            try { games = gameList.GetType().GetProperty("Games")?.GetValue(gameList); } catch { }
            if (!(games is System.Collections.Specialized.INotifyCollectionChanged changes)) return;
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, window.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(500) };
            System.Collections.Specialized.NotifyCollectionChangedEventHandler restart = (_, _) => { timer.Stop(); timer.Start(); };
            changes.CollectionChanged += restart;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                changes.CollectionChanged -= restart;
                try { Judge(gameList); }
                catch (Exception ex) { Log.Warn("[import] could not put the list right", ex); }
            };
            timer.Start();
        }

        /// <summary>Each line read by its content: kept or removed (when cleaning), and what it will get after the import.</summary>
        internal static void Judge(object gameList)
        {
            var list = gameList.GetType().GetProperty("Games")?.GetValue(gameList) as IList;
            if (list == null || list.Count == 0) return;
            var records = list.Cast<object>().Select(r => (Record: r, Path: Full(Get(r, "ApplicationPath")))).ToList();
            Log.Info("[import] the list: " + records.Count + " file(s) - " + string.Join(", ", records.Select(r => System.IO.Path.GetFileName(r.Path ?? "?"))
                     .GroupBy(n => System.IO.Path.GetExtension(n).ToLowerInvariant()).Select(g => g.Count() + " " + (g.Key.Length == 0 ? "(none)" : g.Key))));

            bool clean = Clean;
            int kept = 0, removed = 0;
            var verdicts = new Dictionary<object, CxbxRom>();
            var renamed = new HashSet<object>();
            using (var window = CxbxProgressWindow.Open("Nixx-Cxbx - Reading the Xbox games"))
                for (int i = 0; i < records.Count; i++)
                {
                    var (record, path) = records[i];
                    if (path == null) continue;
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    int n = i;
                    // A ZIP OR 7Z NAMED AS AN XBOX GAME IS ONE (Mehdi, 03/10): reading the disc inside means decompressing
                    // it up to its last table - 13.7 s for GTA San Andreas' 2.9 GB. Its name is looked up first in
                    // LaunchBox's database, on Microsoft Xbox only, compared as the database compares (CompareName);
                    // only a name it does not know is read. Its disc is then listed at its first launch instead.
                    window?.Report("(" + (n + 1) + "/" + records.Count + ") Looking up " + name, (double)n / records.Count);
                    if (ByName(path, out var known))
                    {
                        verdicts[record] = new CxbxRom { Path = path, Kind = CxbxRomKind.ImageInArchive };
                        Log.Info("[import]   " + System.IO.Path.GetFileName(path) + ": kept by its name - " + known + " (not read)");
                        continue;
                    }
                    var d = CxbxPlace.Identify(path, (step, f) => window?.Report("(" + (n + 1) + "/" + records.Count + ") " + step + " " + name, f));
                    verdicts[record] = d;
                    if (d.Problem == null && d.Xbe != null)
                    {
                        var title = CxbxImportFinished.Prepare(path, d.Xbe);
                        // The name shown in the list too (03/10: "...(It).xiso.iso" showed as "Commandos 2: Men of Courage .xiso"
                        // until Finish) - LaunchBox then matches its metadata on the right name. Set again after the import.
                        if (Title && title != null && SetTitle(record, title)) renamed.Add(record);
                    }
                }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!verdicts.TryGetValue(list[i], out var d)) continue;
                if (d.Problem == null)
                {
                    kept++;
                    // Out and back at its place, as Vita3K does: the grid redraws a line only when the list says it changed.
                    if (renamed.Contains(list[i])) { var r = list[i]; list.RemoveAt(i); list.Insert(i, r); }
                    continue;
                }
                Log.Info("[import]   " + (clean ? "removed " : "kept, though ") + System.IO.Path.GetFileName(d.Path) + " - " + d.Problem);
                if (!clean) continue;
                list.RemoveAt(i);
                removed++;
            }
            try { gameList.GetType().GetMethod("NotifyOfPropertyChange", new[] { typeof(string) })?.Invoke(gameList, new object[] { "GameCount" }); } catch { }
            Log.Info("[import] the list read: " + kept + " Xbox game(s)" + (clean ? ", " + removed + " file(s) that are not one removed" : " (cleaning turned off: nothing removed)"));
        }

        /// <summary>An archive whose name, its extensions and brackets aside, is a Microsoft Xbox game of LaunchBox's
        /// database. <paramref name="known"/>: which, for the log.</summary>
        private static bool ByName(string path, out string known)
        {
            known = null;
            if (!TrustNames) return false;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".zip" && ext != ".7z" && ext != ".rar") return false;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (var inner in new[] { ".xiso", ".iso" })
                if (name.EndsWith(inner, StringComparison.OrdinalIgnoreCase)) { name = name.Substring(0, name.Length - inner.Length); break; }
            var games = LbIntegrations.Lbip.LbipMetadataDb.GamesNamed(name, Platform, out var why);
            if (games == null) { Log.Info("[import]   " + System.IO.Path.GetFileName(path) + ": its name not looked up (" + why + ") - read"); return false; }
            if (games.Count == 0) return false;
            known = "\"" + string.Join("\", \"", games.Take(3)) + "\" on " + Platform + ", " + why;
            return true;
        }

        /// <summary>A line of the wizard's list renamed - its Title, when it has one that can be written. True when it
        /// changed: the caller then puts the line back in the list for the grid to redraw it.</summary>
        private static bool SetTitle(object record, string title)
        {
            try
            {
                var p = record.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite || p.PropertyType != typeof(string)) { Log.Info("[import]   the list's lines have no Title to set"); return false; }
                var before = p.GetValue(record) as string;
                if (string.Equals(before, title, StringComparison.Ordinal)) return false;
                p.SetValue(record, title);
                Log.Info("[import]   renamed in the list: \"" + before + "\" -> \"" + title + "\"");
                return true;
            }
            catch (Exception ex) { Log.Warn("[import] could not rename a line of the list", ex); return false; }
        }

        private static string Full(string path)
        {
            try { return string.IsNullOrWhiteSpace(path) ? null : LbipImportWatch.Full(CxbxPlugin.ResolveFullPathForUi(path)); } catch { return null; }
        }

        private static object CurrentPage(object wizard)
        {
            try
            {
                var active = wizard.GetType().GetProperty("ActivePageViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(wizard);
                if (active != null) return active;
            }
            catch { }
            foreach (var p in wizard.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                for (var b = p.PropertyType; b != null; b = b.BaseType)
                    if (b.Name == "WizardPageViewModelBase") { try { var v = p.GetValue(wizard); if (v != null) return v; } catch { } break; }
            }
            return null;
        }

        private static string Get(object o, string name)
        {
            try { return o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o) as string; }
            catch { return null; }
        }
    }

    /// <summary>After the import: each game of the list prepared gets its title and region - through the data API only.</summary>
    internal sealed class CxbxImportFinished : ILbImportFinished
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (string Title, string Region)> Prepared = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The title and region this game gets after the import, worked out and kept. Returns the title.</summary>
        internal static string Prepare(string path, XbeInfo xbe)
        {
            var full = LbipImportWatch.Full(path);
            if (full == null) return null;
            var (exact, others) = CxbxCompat.For(xbe);
            var title = exact?.Title ?? others.FirstOrDefault()?.Title;
            if (string.IsNullOrWhiteSpace(title)) title = string.IsNullOrWhiteSpace(xbe.TitleName) ? null : xbe.TitleName;
            var region = exact != null && exact.Version == CxbxCompat.VersionOf(xbe.Version) ? RegionOfSite(exact.Region) : null;
            region ??= RegionOf(xbe.Region);
            lock (Gate) Prepared[full] = (title, region);
            Log.Info("[import]   " + System.IO.Path.GetFileName(path) + ": " + CxbxCompat.SerialOf(xbe.TitleId) + " " + CxbxCompat.VersionOf(xbe.Version)
                     + " -> title \"" + (title ?? "?") + "\"" + (exact != null ? " (compatibility list)" : " (certificate)") + ", region " + (region ?? "?"));
            return title;
        }

        /// <summary>"Europe: Classics" -> "Europe", "USA: Platinum Hits, ..." -> "North America": the site's region, as LaunchBox names it.</summary>
        internal static string RegionOfSite(string siteRegion)
        {
            if (string.IsNullOrWhiteSpace(siteRegion)) return null;
            var at = siteRegion.IndexOf(':');
            var r = (at >= 0 ? siteRegion.Substring(0, at) : siteRegion).Trim();
            switch (r)
            {
                case "USA": case "United States": case "North America": return "North America";
                case "UK": case "United Kingdom": return "United Kingdom";
                case "": return null;
                default: return r;          // Europe, Japan, Germany, France, Australia, Korea, Asia...
            }
        }

        /// <summary>The certificate's regions: one - North America, Japan, or the rest of the world as Europe; several - World.</summary>
        internal static string RegionOf(uint regions)
        {
            switch (regions & 7)
            {
                case 1: return "North America";
                case 2: return "Japan";
                case 4: return "Europe";
                case 0: return null;
                default: return "World";
            }
        }

        public void AfterImport(LbImportDone done)
        {
            try { Run(done); }
            catch (Exception ex) { Log.Warn("[import] after the import: could not set the titles and regions", ex); }
        }

        private static void Run(LbImportDone done)
        {
            if (done == null || !CxbxPaths.IsCxbx(done.EmulatorPath)) return;
            Dictionary<string, (string Title, string Region)> prepared;
            lock (Gate) { prepared = new Dictionary<string, (string, string)>(Prepared, StringComparer.OrdinalIgnoreCase); Prepared.Clear(); }
            Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count + " in the library"
                     + (done.Complete ? "" : " (gave up waiting)") + ", " + prepared.Count + " prepared from the list");
            bool title = CxbxLbImport.Title, region = CxbxLbImport.Region;
            if (prepared.Count == 0 || (!title && !region)) return;

            int changed = 0;
            foreach (var game in PluginHelper.DataManager?.GetPlatformByName(done.Platform)?.GetAllGames(true, true) ?? Array.Empty<IGame>())
            {
                if (!prepared.TryGetValue(LbipImportWatch.Full(game.ApplicationPath) ?? "", out var p)) continue;
                var changes = new List<string>();
                if (title && p.Title != null && !string.Equals(game.Title, p.Title, StringComparison.Ordinal)) { changes.Add("title \"" + game.Title + "\" -> \"" + p.Title + "\""); game.Title = p.Title; }
                if (region && p.Region != null && !string.Equals(game.Region, p.Region, StringComparison.Ordinal)) { changes.Add("region " + (game.Region ?? "none") + " -> " + p.Region); game.Region = p.Region; }
                if (changes.Count == 0) continue;
                changed++;
                Log.Info("[import]   " + System.IO.Path.GetFileName(game.ApplicationPath) + ": " + string.Join(", ", changes));
            }
            if (changed == 0) { Log.Info("[import] titles and regions: nothing to change"); return; }
            PluginHelper.DataManager.Save(true);
            Log.Info("[import] titles and regions set on " + changed + " game(s), saved through the data API");
        }
    }
}
