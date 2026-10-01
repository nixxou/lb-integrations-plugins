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
// from the grid and from the import. No title is changed: the file's name (No-Intro's, usually) reads better than a
// package's own, often in capitals.
//
// WHEN: the platform page said "Microsoft Xbox 360", by its platform or its scrape-as. On by default, turned off in the
// Nixx window's Xenia tab (content.ini, import_clean=off).
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

        private static bool _installed;
        private static object _platformPage, _cleanedList;

        public static bool Wanted
            => !(XeniaExtras.ReadSettings().TryGetValue("import_clean", out var v) && string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));

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
                else if (name == GameListPage && !ReferenceEquals(_cleanedList, page) && IsXbox360() && Wanted)
                {
                    _cleanedList = page;
                    WhenFilled(window, page);
                }
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        private static bool IsXbox360()
            => _platformPage != null && (string.Equals(Get(_platformPage, "Platform"), Platform, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(Get(_platformPage, "ScrapeAs"), Platform, StringComparison.OrdinalIgnoreCase));

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

            int games = 0, extras = 0, invalid = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var path = records.FirstOrDefault(r => ReferenceEquals(r.Record, list[i])).Path;
                if (path == null) continue;
                var of = found.Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase) || e.Path.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList();
                var name = Path.GetFileName(path);
                if (of.Any(e => e.Kind == XeniaFileKind.Game || e.Kind == XeniaFileKind.GameNoId)) { games++; continue; }
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
            Log.Info("[import] the list put right: " + games + " game(s) kept, " + extras + " title update(s)/DLC removed (kept for their game), " + invalid + " file(s) that are not Xbox 360 games removed");
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
