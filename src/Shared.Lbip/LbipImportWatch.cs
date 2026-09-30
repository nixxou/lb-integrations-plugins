// When an import in LaunchBox's wizard has put its games in the library - told to every plugin that
// asked (LbIntegrations.Catalog, ILbImportFinished), with what was asked for and what really went in.
//
// WHY. LaunchBox drops some files of an import without a word. Measured 30/09 on Naomi 2: of 45 sets
// the list held when Finish was clicked, four never reached the library - vf4b, vf4o, vf4cart ("Virtua
// Fighter 4") and clubkprz ("Club Kart Prize"). It files a set as a VERSION of a game only when their
// MAME titles are identical, and these four fall on the same game of its database as a game already
// made under another title (152524 "Virtua Fighter 4 Version C", 36060 "Club Kart Prize Version B"):
// neither a version nor a new game, so nothing. The emulator's plugin can put them back, through the
// data API - and to do so it has to know when the games are in and which ones are not.
//
// WHEN, EXACTLY. The start is the click on Finish on the game list page, seen by a WPF class handler -
// the list is photographed then, before the wizard reads it. The end is the LIBRARY, not a message: the
// wizard tells the user in more than one way, and none of them is a door a plugin can hold. Every
// second, on the host's thread, the platform's games and their versions are read through the data API:
//   - PRESENT = the photographed files found there, as a game or a version, full paths, case ignored
//     (no platform yet, or a call that throws, reads as nothing present);
//   - DONE when every one is present;
//   - or when PRESENT is not empty and has not changed for long enough: five seconds, or twice the
//     time from Finish to its last change if that is longer - a slower machine or a larger import is
//     slower at every step (Mehdi, 30/09). Not "all", since LaunchBox drops some on purpose, and never
//     on nothing, which is an import not started yet. Measured 30/09 on 45 sets: all 41 at once, 2 s
//     after Finish, told at 7 s;
//   - or after ten minutes, told anyway and marked incomplete.
// NOT THE END OF THE METADATA. LaunchBox goes on filling metadata and media afterwards, and that is the
// part that queries LaunchBox.Metadata.db - so a quiet database says nothing about the games being in
// (Mehdi, 30/09). What is added through the API meanwhile is in the library held in memory, which the
// host's next save writes out.
//
// ONE WATCHER FOR THE PROCESS. Every plugin calls Install; the first one under LaunchBox sets
// LbImportFinished.Watching and hooks the wizard, the others see it and only register their listener -
// the shape of LbEmulatorOpened. Nothing here is typed on a Catalog member outside a NoInlining method
// under try/catch: an older Catalog in the process must cost this feature, never the plugin.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LbIntegrations.Catalog;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Lbip
{
    internal static class LbipImportWatch
    {
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";

        /// <summary>Seconds with nothing new, at the least, before the games are taken as in.</summary>
        private const int StableReadings = 5;

        /// <summary>Readings (one a second) before giving up and telling what is there.</summary>
        private const int MaxReadings = 600;

        private static bool _installed;

        // One wizard at a time: LaunchBox's is modal.
        private static object _platformPage;
        private static readonly List<object> _seenPages = new List<object>();
        private static System.Windows.Threading.DispatcherTimer _timer;

        /// <summary>Watch the wizard if nobody does yet. Never throws.</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                if (!string.Equals(Process.GetCurrentProcess().ProcessName, "LaunchBox", StringComparison.OrdinalIgnoreCase)) return;
                if (!Claim()) return;
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < 120; i++)
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null) { app.Dispatcher.BeginInvoke(new Action(Hook)); return; }
                        Thread.Sleep(500);
                    }
                    LbipLog.Warn("[import-watch] no WPF application in this host - imports are not watched");
                })
                { IsBackground = true, Name = "import watch" };
                thread.Start();
            }
            catch (Exception ex) { LbipLog.Warn("[import-watch] could not watch the import wizard", ex); }
        }

        /// <summary>Be the one watcher - false when another plugin already is, or this Catalog is too old.</summary>
        private static bool Claim()
        {
            try { return ClaimCore(); }
            catch (Exception ex) { LbipLog.Info("[import-watch] no import contract in this Catalog (" + ex.GetType().Name + ") - not watching"); return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool ClaimCore()
        {
            if (LbImportFinished.Watching) return false;
            LbImportFinished.Watching = true;
            return true;
        }

        private static void Hook()
        {
            try
            {
                System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),
                    System.Windows.FrameworkElement.LoadedEvent, new System.Windows.RoutedEventHandler(WindowLoaded));
                // A class handler runs before the button's own, so the list is read before the wizard
                // acts on it; handledEventsToo, so a click it marks handled is still seen.
                System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase),
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new System.Windows.RoutedEventHandler(ButtonClicked), true);
                LbipLog.Info("[import-watch] watching LaunchBox's import wizard");
            }
            catch (Exception ex) { LbipLog.Warn("[import-watch] could not watch the import wizard", ex); }
        }

        private static void WindowLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Window window) || window.DataContext?.GetType().Name != WizardType) return;
                _platformPage = null;
                _seenPages.Clear();
                if (window.DataContext is INotifyPropertyChanged notify)
                    notify.PropertyChanged += (s, a) => PageShown(s);
            }
            catch (Exception ex) { LbipLog.Warn("[import-watch] wizard opened", ex); }
        }

        private static void PageShown(object wizard)
        {
            try
            {
                var page = CurrentPage(wizard);
                if (page == null) return;
                if (!_seenPages.Any(p => ReferenceEquals(p, page))) _seenPages.Add(page);
                if (page.GetType().Name == PlatformPage) _platformPage = page;
            }
            catch { }
        }

        private static void ButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Controls.Primitives.ButtonBase button)) return;
                var window = System.Windows.Window.GetWindow(button);
                if (window == null || window.DataContext?.GetType().Name != WizardType) return;
                if (!(button.Content is string label) || label != "Finish") return;
                var page = CurrentPage(window.DataContext);
                if (page?.GetType().Name != GameListPage) return;
                Photograph(window, page);
            }
            catch (Exception ex) { LbipLog.Warn("[import-watch] Finish", ex); }
        }

        // ── the photo, and the watch ─────────────────────────────────────────

        private sealed class Watch
        {
            public readonly Stopwatch Since = Stopwatch.StartNew();
            public System.Windows.Window Window;
            public bool WindowClosed;
            public string Platform, ScrapeAs, Emulator;
            public readonly List<string> Wanted = new List<string>();
            public readonly Dictionary<string, string> TitleOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Options = new Dictionary<string, string>(StringComparer.Ordinal);
            public HashSet<string> Present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int Readings;
            public double LastChange;           // seconds after Finish of the last reading that differed
        }

        private static void Photograph(System.Windows.Window window, object gameList)
        {
            var w = new Watch { Window = window };
            if (GetProperty(gameList, "Games") is IEnumerable games)
                foreach (var record in games)
                {
                    var path = Full(GetProperty(record, "ApplicationPath") as string);
                    if (string.IsNullOrEmpty(path) || w.TitleOf.ContainsKey(path)) continue;
                    w.Wanted.Add(path);
                    w.TitleOf[path] = GetProperty(record, "Title") as string ?? "";
                }
            w.Emulator = Full(ChosenEmulator(window.DataContext));
            // The choices, as the pages hold them at Finish: every plain value of every options page shown.
            foreach (var page in _seenPages.Where(p => p.GetType().Name.EndsWith("OptionsViewModel", StringComparison.Ordinal)))
                foreach (var p in page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    var t = p.PropertyType;
                    if (!(t == typeof(bool) || t == typeof(string) || t == typeof(int) || t.IsEnum)) continue;
                    try { w.Options[page.GetType().Name + "." + p.Name] = p.GetValue(page)?.ToString(); } catch { }
                }
            if (w.Wanted.Count == 0) { LbipLog.Info("[import-watch] Finish on an empty list - nothing to watch"); return; }
            // Not a signal of anything: measured 30/09, the window closes 43 ms after Finish and the games
            // come 2 s later. Kept in the log, where it says which of the two a reading came before.
            window.Closed += (_, _) => w.WindowClosed = true;
            LbipLog.Info("[import-watch] Finish: " + w.Wanted.Count + " file(s) to import, emulator " + (w.Emulator ?? "none found"));
            var mame = w.Options.Where(o => o.Key.StartsWith("RomImportMameOptionsViewModel.", StringComparison.Ordinal)).ToList();
            if (mame.Count > 0)
                LbipLog.Info("[import-watch] MAME choices: " + string.Join(", ", mame.Select(o => o.Key.Substring(o.Key.IndexOf('.') + 1) + "=" + o.Value)));

            _timer?.Stop();
            _timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, window.Dispatcher)
            { Interval = TimeSpan.FromSeconds(1) };
            var timer = _timer;
            timer.Tick += (_, _) =>
            {
                try { if (Read(w)) { timer.Stop(); Tell(w, complete: w.Readings < MaxReadings); } }
                catch (Exception ex) { timer.Stop(); LbipLog.Warn("[import-watch] watching the import", ex); }
            };
            timer.Start();
        }

        /// <summary>One reading. True when the watch is over.</summary>
        private static bool Read(Watch w)
        {
            w.Readings++;
            // The platform is read here rather than at the click: another plugin's Finish handler may be
            // putting it back (Vita3kLbImport's stand-in) when ours runs.
            if (w.Platform == null)
            {
                w.Platform = GetProperty(_platformPage, "Platform") as string;
                w.ScrapeAs = GetProperty(_platformPage, "ScrapeAs") as string;
                LbipLog.Info("[import-watch] into platform \"" + w.Platform + "\"" + (string.IsNullOrEmpty(w.ScrapeAs) || w.ScrapeAs == w.Platform ? "" : " scraped as \"" + w.ScrapeAs + "\""));
            }

            var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var platform = string.IsNullOrEmpty(w.Platform) ? null : PluginHelper.DataManager?.GetPlatformByName(w.Platform);
                var wanted = new HashSet<string>(w.Wanted, StringComparer.OrdinalIgnoreCase);
                foreach (var game in platform?.GetAllGames(true, true) ?? Array.Empty<IGame>())
                {
                    var p = Full(game.ApplicationPath);
                    if (p != null && wanted.Contains(p)) now.Add(p);
                    foreach (var app in game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                    {
                        var a = Full(app.ApplicationPath);
                        if (a != null && wanted.Contains(a)) now.Add(a);
                    }
                }
            }
            catch (Exception ex) { LbipLog.Info("[import-watch] +" + Seconds(w) + " the library could not be read (" + ex.GetType().Name + ": " + ex.Message + ")"); now.Clear(); }

            bool changed = !now.SetEquals(w.Present);
            var elapsed = w.Since.Elapsed.TotalSeconds;
            if (changed) w.LastChange = elapsed;
            w.Present = now;
            if (changed)
                LbipLog.Info("[import-watch] +" + Seconds(w) + " " + now.Count + "/" + w.Wanted.Count + " in the library"
                             + (w.WindowClosed ? "" : ", wizard still open"));

            if (now.Count == w.Wanted.Count) return true;
            // Quiet for long enough, in proportion to how long the import has taken so far (Mehdi, 30/09: a
            // slower machine or a larger import is slower in every step, and may add in batches): at least
            // StableReadings seconds, or twice the time it took to reach the last change.
            var quietFor = Math.Max(StableReadings, 2 * w.LastChange);
            if (now.Count > 0 && elapsed - w.LastChange >= quietFor) return true;
            if (w.Readings >= MaxReadings) { LbipLog.Warn("[import-watch] still " + now.Count + "/" + w.Wanted.Count + " after " + MaxReadings + " s - told as it is"); return true; }
            return false;
        }

        private static void Tell(Watch w, bool complete)
        {
            var missing = w.Wanted.Where(p => !w.Present.Contains(p)).ToList();
            LbipLog.Info("[import-watch] +" + Seconds(w) + " the games are in: " + w.Present.Count + "/" + w.Wanted.Count
                         + (missing.Count == 0 ? "" : ", " + missing.Count + " left out by LaunchBox: "
                            + string.Join(", ", missing.Take(30).Select(p => Path.GetFileName(p) + " \"" + w.TitleOf[p] + "\""))
                            + (missing.Count > 30 ? " and " + (missing.Count - 30) + " more" : "")));
            try { TellCore(w, missing, complete); }
            catch (Exception ex) { LbipLog.Info("[import-watch] nobody told (" + ex.GetType().Name + ")"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TellCore(Watch w, List<string> missing, bool complete)
        {
            var done = new LbImportDone
            {
                Platform = w.Platform, ScrapeAs = w.ScrapeAs, EmulatorPath = w.Emulator, Complete = complete,
                Wanted = new List<string>(w.Wanted),
                TitleOf = new Dictionary<string, string>(w.TitleOf, StringComparer.OrdinalIgnoreCase),
                Imported = w.Wanted.Where(p => w.Present.Contains(p)).ToList(),
                Missing = missing,
            };
            // Set apart from the initializer: a Catalog of 30/09 morning has no Options, and this line alone fails then.
            try { SetOptions(done, w.Options); } catch { }
            LbImportFinished.Finished(done);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetOptions(LbImportDone done, Dictionary<string, string> options)
            => done.Options = new Dictionary<string, string>(options, StringComparer.Ordinal);

        private static string Seconds(Watch w) => ((int)w.Since.Elapsed.TotalSeconds) + "s";

        // ── the wizard's objects (as FlycastLbImport reads them) ─────────────

        /// <summary>The emulator the wizard imports to - its path as LaunchBox holds it, or null.</summary>
        private static string ChosenEmulator(object wizard)
        {
            foreach (var page in _seenPages.Concat(Pages(wizard)).Distinct())
                foreach (var p in page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    object v;
                    try { v = p.GetValue(page); } catch { continue; }
                    var path = PathOf(v) ?? (v == null ? null : PathOf(GetProperty(v, "Emulator")));
                    if (path != null) return path;
                }
            return null;
        }

        private static string PathOf(object v)
        {
            if (v is IEmulator e) { try { return e.ApplicationPath; } catch { return null; } }
            return v == null || v is string ? null : GetProperty(v, "ApplicationPath") as string;
        }

        private static IEnumerable<object> Pages(object wizard)
        {
            if (wizard == null) yield break;
            foreach (var p in wizard.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = p.GetValue(wizard); } catch { continue; }
                if (v != null && IsPage(v.GetType())) yield return v;
                else if (v is IEnumerable many && !(v is string))
                    foreach (var item in many) if (item != null && IsPage(item.GetType())) yield return item;
            }
        }

        private static object CurrentPage(object wizard)
        {
            try { return wizard?.GetType().GetProperty("ActivePageViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(wizard); }
            catch { return null; }
        }

        private static bool IsPage(Type t)
        {
            for (var b = t; b != null; b = b.BaseType) if (b.Name == "WizardPageViewModelBase") return true;
            return false;
        }

        private static object GetProperty(object o, string name)
        {
            try { return o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o); }
            catch { return null; }
        }

        // ── paths ────────────────────────────────────────────────────────────

        private static string _root;

        /// <summary>A path as LaunchBox stores it - often relative to its root - made full, or null.</summary>
        internal static string Full(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(LaunchBoxRoot(), path));
            }
            catch { return null; }
        }

        /// <summary>A full path the way LaunchBox writes one of its own: relative to its root when it is
        /// on the same drive, as the import wrote the others ("..\MAME 0.288 ROMs (non-merged)\vf4.zip").</summary>
        internal static string AsLaunchBoxWrites(string fullPath)
        {
            try
            {
                var root = LaunchBoxRoot();
                if (!string.Equals(Path.GetPathRoot(root), Path.GetPathRoot(fullPath), StringComparison.OrdinalIgnoreCase)) return fullPath;
                return Path.GetRelativePath(root, fullPath);
            }
            catch { return fullPath; }
        }

        /// <summary>LaunchBox's root: up from this plugin's folder to the first one holding Core and Data.</summary>
        internal static string LaunchBoxRoot()
        {
            if (_root != null) return _root;
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(Assembly.GetExecutingAssembly().Location));
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (Directory.Exists(Path.Combine(dir, "Core")) && Directory.Exists(Path.Combine(dir, "Data"))) return _root = dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
            try { return _root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..")); }
            catch { return _root = Environment.CurrentDirectory; }
        }
    }
}
