// LaunchBox's "Import ROM Files" wizard, for the PSP (Mehdi, 03/10) - CxbxLbImport's watch of the wizard and its touch after it,
// the rules the pack's PlayStation imports share:
//
//   THE LIST, while it is on show (before "Finish"): each line judged by its PARAM.SFO (PspDiscId.SfoOf - .iso .cso .zso .chd
//   .pbp; a .zip .elf .prx .ppdmp has none to read and is KEPT, never judged by its name):
//     a game - CATEGORY UG (UMD), EG (PSN), MG (memory stick, homebrew), ME (PlayStation classic) - stays
//     a GAME UPDATE (PG) goes, and is NOTED for its game (PpssppUpdates): proposed in its options window, never installed
//     a FIRMWARE update (MSTKUPDATE, "PSP Update ...") goes - PPSSPP needs no firmware
//     anything else (save data...) goes, and the log says why
//   ITS NAME: the pack's rule (LbipImportTitle) - the original when LaunchBox's database knows it on Sony PSP, else the
//   PARAM.SFO's TITLE in the database's spelling when it knows that, else the original unless it is no name (a serial).
//   AFTER THE IMPORT (LbImportFinished), through the data API: that title again, and the REGION its DISC_ID says - the third
//   letter, the same in a UMD's serial and a PSN one's (ULUS / NPUH: North America; ULES / NPEH: Europe; ULJM / NPJH: Japan;
//   A: Asia, K: Korea, H: Asia). Matched by ApplicationPath, games and additional versions alike.
// Each one on by default, the PPSSPP tab of the Nixx window turns it off. ONLY INSIDE LAUNCHBOX (the wizard is WPF, watched
// through class handlers - see XeniaLbImport).

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

namespace LbIntegrations.Ppsspp
{
    internal static class PpssppLbImport
    {
        private const string Platform = "Sony PSP";
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";
        private static readonly string[] GameCategories = { "UG", "EG", "MG", "ME" };

        private static bool _installed;
        private static object _platformPage, _cleanedList;

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
                })
                { IsBackground = true, Name = "PPSSPP import wizard" };
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
                else if (name == GameListPage && !ReferenceEquals(_cleanedList, page) && IsPsp()
                         && (PpssppSettings.CleanImport || PpssppSettings.TitleImport || PpssppSettings.RegionImport))
                {
                    _cleanedList = page;
                    WhenFilled(window, page);
                }
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        private static bool IsPsp() => _platformPage != null && (Is(Get(_platformPage, "Platform")) || Is(Get(_platformPage, "ScrapeAs")));
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

        /// <summary>Each line judged by its PARAM.SFO - see the header.</summary>
        internal static void Judge(object gameList)
        {
            var list = gameList.GetType().GetProperty("Games")?.GetValue(gameList) as IList;
            if (list == null || list.Count == 0) return;
            bool clean = PpssppSettings.CleanImport, titles = PpssppSettings.TitleImport;
            int games = 0, updates = 0, firmware = 0, other = 0, unread = 0, renamed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var record = list[i];
                var path = Full(Get(record, "ApplicationPath"));
                if (path == null) continue;
                var file = System.IO.Path.GetFileName(path);
                var sfo = PspDiscId.SfoOf(path);
                if (sfo == null) { unread++; Log.Info("[import]   " + file + ": no PARAM.SFO to read - kept as it is"); continue; }

                string drop = null;
                if (PpssppUpdates.IsFirmware(sfo)) { drop = "a firmware update - PPSSPP needs none"; firmware++; }
                else if (PpssppUpdates.IsUpdate(sfo)) { drop = "an update of " + sfo.GetString("DISC_ID") + ", noted for its game (its options window, tab Updates)"; updates++; PpssppUpdates.Note(path, sfo); }
                else if (!GameCategories.Contains((sfo.GetString("CATEGORY") ?? "").Trim().ToUpperInvariant()))
                { drop = "category " + (sfo.GetString("CATEGORY") ?? "?") + " is not a game"; other++; }
                if (drop != null)
                {
                    Log.Info("[import]   " + (clean ? "removed " : "kept, though ") + file + " - " + drop);
                    if (clean) list.RemoveAt(i);
                    continue;
                }

                games++;
                var current = Get(record, "Title");
                var title = PpssppImportFinished.Prepare(path, sfo, current);
                if (titles && title != null && SetTitle(record, title))
                {
                    // Out and back at its place: the grid redraws a line only when the list says it changed.
                    list.RemoveAt(i);
                    list.Insert(i, record);
                    renamed++;
                }
            }
            try { gameList.GetType().GetMethod("NotifyOfPropertyChange", new[] { typeof(string) })?.Invoke(gameList, new object[] { "GameCount" }); } catch { }
            Log.Info("[import] the list put right: " + games + " PSP game(s), " + renamed + " renamed, " + unread + " not read (kept), "
                     + (clean ? updates + " game update(s) noted for their game, " + firmware + " firmware update(s) and " + other + " other file(s) removed"
                              : "nothing removed (cleaning turned off)"));
        }

        private static bool SetTitle(object record, string title)
        {
            try
            {
                var p = record.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite || p.PropertyType != typeof(string)) return false;
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
            try { return string.IsNullOrWhiteSpace(path) ? null : LbipImportWatch.Full(path); } catch { return null; }
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

        internal static string Get(object o, string name)
        {
            try { return o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o) as string; }
            catch { return null; }
        }
    }

    /// <summary>After the import: each PSP game prepared from the list gets its title and region - through the data API only.</summary>
    internal sealed class PpssppImportFinished : ILbImportFinished
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (string Title, string Region)> Prepared = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The title (null: kept) and region this game gets, worked out and kept. Returns the title.</summary>
        internal static string Prepare(string path, LbIntegrations.Psf.ParamSfo sfo, string current)
        {
            var full = LbipImportWatch.Full(path);
            if (full == null) return null;
            var sfoTitle = Clean(sfo.GetString("TITLE"));
            var title = LbipImportTitle.Choose("Sony PSP", current, System.IO.Path.GetFileNameWithoutExtension(path), out var why, sfoTitle);
            var id = (sfo.GetString("DISC_ID") ?? "").Trim().ToUpperInvariant();
            var region = RegionOf(id);
            lock (Gate) Prepared[full] = (title, region);
            Log.Info("[import]   " + System.IO.Path.GetFileName(path) + ": " + id + " - " + why + ", region " + (region ?? "?"));
            return title;
        }

        /// <summary>A TITLE fit for the library: line breaks and trademark signs to spaces, spaces collapsed.</summary>
        internal static string Clean(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var t = new string(title.Select(c => c == '™' || c == '®' || c == '©' || char.IsControl(c) ? ' ' : c).ToArray());
            return string.Join(" ", t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>The region a PSP serial says, by its third letter - UMD (ULUS, UCES...) and PSN (NPUH, NPEH...) alike.</summary>
        internal static string RegionOf(string discId)
        {
            if (string.IsNullOrEmpty(discId) || discId.Length < 4) return null;
            switch (discId[2])
            {
                case 'U': return "North America";
                case 'E': return "Europe";
                case 'J': return "Japan";
                case 'K': return "Korea";
                case 'A': case 'H': return "Asia";
                default: return null;
            }
        }

        public void AfterImport(LbImportDone done)
        {
            try { Run(done); }
            catch (Exception ex) { Log.Warn("[import] after the import: could not set the titles and regions", ex); }
        }

        private static void Run(LbImportDone done)
        {
            if (done == null || !PpssppPaths.IsPpssppExecutable(done.EmulatorPath)) return;
            Dictionary<string, (string Title, string Region)> prepared;
            lock (Gate) { prepared = new Dictionary<string, (string, string)>(Prepared, StringComparer.OrdinalIgnoreCase); Prepared.Clear(); }
            Log.Info("[import] after the import to " + done.Platform + ": " + done.Imported.Count + "/" + done.Wanted.Count + " in the library"
                     + (done.Complete ? "" : " (gave up waiting)") + ", " + prepared.Count + " prepared from the list");
            bool title = PpssppSettings.TitleImport, region = PpssppSettings.RegionImport;
            if (prepared.Count == 0 || (!title && !region)) return;

            int changed = 0;
            var platform = PluginHelper.DataManager?.GetPlatformByName(done.Platform);
            foreach (var game in platform?.GetAllGames(true, true) ?? Array.Empty<IGame>())
            {
                var changes = new List<string>();
                if (prepared.TryGetValue(LbipImportWatch.Full(game.ApplicationPath) ?? "", out var p))
                {
                    if (title && p.Title != null && !string.Equals(game.Title, p.Title, StringComparison.Ordinal)) { changes.Add("title \"" + game.Title + "\" -> \"" + p.Title + "\""); game.Title = p.Title; }
                    if (region && p.Region != null && !string.Equals(game.Region, p.Region, StringComparison.Ordinal)) { changes.Add("region " + (game.Region ?? "none") + " -> " + p.Region); game.Region = p.Region; }
                }
                // An additional version of the game: its own region, by its own path.
                foreach (var app in game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                    if (region && prepared.TryGetValue(LbipImportWatch.Full(app.ApplicationPath) ?? "", out var q) && q.Region != null
                        && !string.Equals(app.Region, q.Region, StringComparison.Ordinal))
                    { changes.Add("version " + System.IO.Path.GetFileName(app.ApplicationPath) + ": region " + q.Region); app.Region = q.Region; }
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
