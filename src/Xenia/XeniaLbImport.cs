// LaunchBox's "Import ROM Files" wizard, its game list put right for Xbox 360 - after LaunchBox's scan, while the list
// is on show, so the user sees the result and can still change it before "Finish" (Mehdi, 01/10).
//
// WHAT IT DOES: the folders of the files listed are scanned (XeniaScan - every Xbox 360 file there sorted by its
// content, with a progress window, and kept in the cache), then each line of the list is judged by what its file is:
//   - a GAME (a disc image, an extracted disc, an executable, an Arcade, Indie or Games on Demand package - loose or in
//     an archive) stays;
//   - a title update, a DLC, any other package (a theme...) goes: it is not a game. Nothing needs recording - the cache
//     holds it, and a launch, or the game's options window, finds it there by its title id and its digest;
//   - a file that is not Xbox 360 content goes, and the log says why.
// The list is LaunchBox's - Caliburn's BindableCollection, the one the grid shows: a record removed from it is gone
// from the grid and from the import. A game's title, when Xenia's compatibility list has its title id, is the list's
// (Mehdi, 03/10, as the Cxbx plugin does - import_title=off keeps LaunchBox's); never the package's own, often in
// capitals.
//
// WHEN: the platform page said "Microsoft Xbox 360", by its platform or its scrape-as. On by default, turned off in the
// Nixx window's Xenia tab (content.ini, import_clean=off).
//
// AND THE SCAN IS MADE AS FOR ANY ROM (Mehdi, 01/10): an Xbox 360 import listed the ISO of a folder and none of its
// zips. As Vita3kLbImport does for Vita, the wizard is shown "Microsoft Xbox 360 Temp" for the scan only - from "Next"
// on the custom options to "Finish" - and the real platform goes back onto every record and the platform page before
// anything is imported.
//
// ONLY INSIDE LAUNCHBOX. Install() looks at the process first: in LiteBox and Big Box nothing is registered and no
// view model is read. The wizard is WPF, its core obfuscated: class handlers on every window's Loaded and every
// button's Click, and the view models' readable property names - as Vita3kLbImport, measured 27/09 on LaunchBox 14.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Xenia
{
    internal static class XeniaLbImport
    {
        private const string Platform = "Microsoft Xbox 360";
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";
        private const string OptionsPage = "RomImportCustomOptionsViewModel";
        internal const string StandIn = "Microsoft Xbox 360 Temp";

        private static bool _installed;
        private static object _platformPage, _cleanedList;

        /// <summary>What was swapped, and from what - null for a value left alone.</summary>
        private static string _swappedPlatform, _swappedScrapeAs;
        private static bool Swapped => _swappedPlatform != null || _swappedScrapeAs != null;

        public static bool Wanted
            => !(XeniaExtras.ReadSettings().TryGetValue("import_clean", out var v) && string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));

        /// <summary>Each game named as Xenia's compatibility list names its title id, when the list has it (Mehdi, 03/10 - as
        /// the Cxbx plugin does); else LaunchBox's name stays. On by default (content.ini, import_title=off).</summary>
        public static bool Titles
            => !(XeniaExtras.ReadSettings().TryGetValue("import_title", out var v) && string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));

        /// <summary>Each game's region set after the import from its executable's region flags (XeniaImportFinished) - on by
        /// default, as the Cxbx plugin's (content.ini, import_region=off).</summary>
        public static bool Regions
            => !(XeniaExtras.ReadSettings().TryGetValue("import_region", out var v) && string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));

        /// <summary>Is the list read at all: to clean it, to name its games, or to know their regions.</summary>
        private static bool Active => Wanted || Titles || Regions;

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
                { IsBackground = true, Name = "Xenia import wizard" };
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
                // A CLASS handler runs before the wizard's own Next and Finish; handledEventsToo, as for Vita.
                System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase),
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new System.Windows.RoutedEventHandler(ButtonClicked), true);
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
                _swappedPlatform = _swappedScrapeAs = null;
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
                if (name == PlatformPage)
                {
                    _platformPage = page;
                    _cleanedList = null;
                    if (Swapped) Undo(page, "back on the platform page");
                }
                else if (name == GameListPage && !ReferenceEquals(_cleanedList, page) && IsXbox360() && Active)
                {
                    _cleanedList = page;
                    WhenFilled(window, page);
                }
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        private static bool IsXbox360()
            => Swapped || (_platformPage != null && (Is360(Get(_platformPage, "Platform")) || Is360(Get(_platformPage, "ScrapeAs"))));

        private static bool Is360(string platform) => string.Equals(platform, Platform, StringComparison.OrdinalIgnoreCase);

        /// <summary>The list fills after its page appears (measured on Vita): the cleanup once it has stopped changing, 500 ms.</summary>
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
                try { Clean(gameList); }
                catch (Exception ex) { Log.Warn("[import] could not put the list right", ex); }
            };
            timer.Start();
        }

        /// <summary>The verdict on each line, by its file's content.</summary>
        internal static void Clean(object gameList)
        {
            var list = gameList.GetType().GetProperty("Games")?.GetValue(gameList) as IList;
            if (list == null || list.Count == 0) return;
            var records = list.Cast<object>().Select(r => (Record: r, Path: Full(Get(r, "ApplicationPath")))).ToList();
            // What a line of LaunchBox's list is - logged, for the day the plugin adds the games LaunchBox did not list.
            try
            {
                var first = list[0];
                Log.Info("[import] the list: " + list.GetType().FullName + " of " + list.Count + " - a line is " + first.GetType().FullName + ": "
                         + string.Join(", ", first.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0)
                               .Select(p => { object v = null; try { v = p.GetValue(first); } catch { } return p.Name + (p.CanWrite ? "(rw)" : "") + "=" + (v is string s ? s : v?.GetType().Name ?? "null"); })));
                Log.Info("[import] its constructors: " + string.Join(" | ", first.GetType().GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               .Select(c => "(" + string.Join(", ", c.GetParameters().Select(x => x.ParameterType.Name)) + ")")));
            }
            catch (Exception ex) { Log.Info("[import] the list's line type: " + ex.Message); }

            // Every folder of the list, scanned - the cache answers for what it knows already.
            var folders = records.Select(r => r.Path == null ? null : Path.GetDirectoryName(r.Path)).Where(d => d != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = new List<XeniaScanEntry>();
            using (var window = XeniaProgressWindow.Open("Nixx-Xenia - Reading the Xbox 360 files"))
                for (int i = 0; i < folders.Count; i++)
                {
                    int n = i;
                    found.AddRange(XeniaScan.Scan(folders[i], (step, f) => window?.Report("(" + (n + 1) + "/" + folders.Count + ") " + step, f)));
                }

            int games = 0, extras = 0, invalid = 0, renamed = 0;
            bool clean = Wanted, titles = Titles;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var path = records.FirstOrDefault(r => ReferenceEquals(r.Record, list[i])).Path;
                if (path == null) continue;
                var of = found.Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase) || e.Path.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList();
                var name = Path.GetFileName(path);
                if (of.Any(e => e.Kind == XeniaFileKind.Game || e.Kind == XeniaFileKind.GameNoId))
                {
                    games++;
                    // Its region, from its executable - set once it is in the library (XeniaImportFinished).
                    var region = Xex.RegionName(of.Where(e => e.Kind == XeniaFileKind.Game).Select(e => e.Region).FirstOrDefault(r => r != 0));
                    if (region != null) XeniaImportFinished.Prepare(path, region);
                    if (titles && Rename(list, i, of)) renamed++;
                    continue;
                }
                if (!clean) continue;
                list.RemoveAt(i);
                if (of.Any(e => e.Kind == XeniaFileKind.Update || e.Kind == XeniaFileKind.Dlc || e.Kind == XeniaFileKind.Other))
                {
                    extras++;
                    var e = of.First(x => x.Kind != XeniaFileKind.Invalid);
                    Log.Info("[import]   removed " + name + " - " + (e.Kind == XeniaFileKind.Update ? "title update" : e.Kind == XeniaFileKind.Dlc ? "DLC" : "content")
                             + " \"" + e.Name + "\" of " + e.TitleId + ": found by its game at launch");
                }
                else
                {
                    invalid++;
                    Log.Info("[import]   removed " + name + " - " + (of.FirstOrDefault()?.Problem is string p && p.Length > 0 ? p : "not Xbox 360 content"));
                }
            }
            try { gameList.GetType().GetMethod("NotifyOfPropertyChange", new[] { typeof(string) })?.Invoke(gameList, new object[] { "GameCount" }); } catch { }
            Log.Info("[import] the list put right: " + games + " game(s) kept, " + renamed + " named from the compatibility list, "
                     + (clean ? extras + " title update(s)/DLC removed (kept for their game), " + invalid + " file(s) that are not Xbox 360 games removed" : "nothing removed (cleaning turned off)"));
        }

        /// <summary>Line <paramref name="i"/>, a game, named as Xenia's compatibility list names its title id - when the list
        /// has it and the name differs. Out and back at its place, as Vita3K does: the grid redraws a line only when the
        /// list says it changed.</summary>
        private static bool Rename(IList list, int i, List<XeniaScanEntry> of)
        {
            try
            {
                var id = of.Where(e => e.Kind == XeniaFileKind.Game && e.TitleId.Length == 8).Select(e => e.TitleId).FirstOrDefault();
                if (id == null) return false;
                var title = XeniaCompat.Lookup(id)?.Select(e => e.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim();
                if (title == null) return false;
                var record = list[i];
                var p = record.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite || p.PropertyType != typeof(string)) return false;
                var before = p.GetValue(record) as string;
                if (string.Equals(before, title, StringComparison.Ordinal)) return false;
                p.SetValue(record, title);
                list.RemoveAt(i);
                list.Insert(i, record);
                Log.Info("[import]   " + id + ": \"" + before + "\" -> \"" + title + "\" (compatibility list)");
                return true;
            }
            catch (Exception ex) { Log.Warn("[import] could not rename a line of the list", ex); return false; }
        }

        // ── the swap (Mehdi, 01/10: LaunchBox may filter an Xbox 360 import - as Vita3kLbImport, a stand-in for the scan) ──

        private static void ButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Controls.Primitives.ButtonBase button)) return;
                var window = System.Windows.Window.GetWindow(button);
                if (window == null || window.DataContext?.GetType().Name != WizardType) return;
                var label = button.Content as string;
                var page = CurrentPage(window.DataContext);
                var name = page?.GetType().Name;
                if (label == "Next" && name == OptionsPage) BeforeScan();
                else if (label == "Finish" && name == GameListPage) BeforeImport(page);
            }
            catch (Exception ex) { Log.Warn("[import] wizard button", ex); }
        }

        /// <summary>"Next" on the custom options: the scan comes next, and must not see Xbox 360 - by the platform or the scrape-as.</summary>
        private static void BeforeScan()
        {
            _cleanedList = null;                    // the scan fills the list again
            var page = _platformPage;
            if (page == null || Swapped || !Active) return;
            var platform = Get(page, "Platform");
            var scrapeAs = Get(page, "ScrapeAs");
            bool byName = Is360(platform), byScrape = Is360(scrapeAs);
            if (!byName && !byScrape) return;
            if (byName)
            {
                if (Set(page, "Platform", StandIn) && Get(page, "Platform") == StandIn) _swappedPlatform = platform;
                else Log.Warn("[import] the platform could not be swapped - it reads \"" + Get(page, "Platform") + "\"");
            }
            if (byScrape)
            {
                // In the drop-down's list first, or the binding may put it back to null.
                if (ScrapeAsList(page) is IList list && !list.Contains(StandIn)) list.Add(StandIn);
                if (Set(page, "ScrapeAs", StandIn) && Get(page, "ScrapeAs") == StandIn) _swappedScrapeAs = scrapeAs;
                else Log.Warn("[import] the scrape-as could not be swapped - it reads \"" + Get(page, "ScrapeAs") + "\"");
            }
            if (Swapped)
                Log.Info("[import] Xbox 360 games scanned as ROM files: platform \"" + platform + "\" -> \"" + Get(page, "Platform")
                         + "\", scrape-as \"" + scrapeAs + "\" -> \"" + Get(page, "ScrapeAs") + "\" until Finish");
        }

        /// <summary>"Finish" on the game list, before the wizard imports: the real values go back - onto every record, then onto
        /// the platform page.</summary>
        private static void BeforeImport(object gameList)
        {
            if (!Swapped) return;
            int records = 0, left = 0;
            if (gameList?.GetType().GetProperty("Games")?.GetValue(gameList) is IEnumerable list)
                foreach (var record in list.Cast<object>().ToList())
                {
                    records++;
                    SetFields(record, StandIn, Platform);
                    if (Get(record, "Platform") == StandIn) left++;
                }
            Undo(_platformPage, null);
            if (left == 0) Log.Info("[import] restored before the import: " + records + " game(s), platform \"" + Get(_platformPage, "Platform") + "\", scrape-as \"" + Get(_platformPage, "ScrapeAs") + "\"");
            else Log.Warn("[import] " + left + " of " + records + " game(s) still carry \"" + StandIn + "\" - LaunchBox will create that platform");
        }

        private static void Undo(object page, string why)
        {
            if (_swappedPlatform != null) Set(page, "Platform", _swappedPlatform);
            if (_swappedScrapeAs != null)
            {
                Set(page, "ScrapeAs", _swappedScrapeAs);
                if (ScrapeAsList(page) is IList list && list.Contains(StandIn)) list.Remove(StandIn);
            }
            _swappedPlatform = _swappedScrapeAs = null;
            if (why != null) Log.Info("[import] " + why + ": the swap is undone");
        }

        private static object ScrapeAsList(object page)
        {
            try { return page?.GetType().GetProperty("ScrapeAsPlatforms", BindingFlags.Public | BindingFlags.Instance)?.GetValue(page); }
            catch { return null; }
        }

        private static bool Set(object vm, string name, object value)
        {
            try
            {
                var p = vm?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return false;
                p.SetValue(vm, value);
                return true;
            }
            catch (Exception ex) { Log.Warn("[import] could not set " + name, ex); return false; }
        }

        /// <summary>Every string field of a record holding exactly <paramref name="was"/> set to <paramref name="now"/> - a record's
        /// Platform is read-only, its field's name obfuscated (measured on Vita, 27/09).</summary>
        private static void SetFields(object record, string was, string now)
        {
            for (var t = record?.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(string)) continue;
                    try { if (string.Equals(f.GetValue(record) as string, was, StringComparison.Ordinal)) f.SetValue(record, now); }
                    catch (Exception ex) { Log.Warn("[import] could not set a field of a game", ex); }
                }
        }
        private static string Full(string path)
        {
            try { return string.IsNullOrWhiteSpace(path) ? null : XeniaPlugin.ResolveFullPathForUi(path); } catch { return null; }
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
}
