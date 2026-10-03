// LaunchBox's "Import ROM Files" wizard, made to import Vita games as ROM files.
//
// THE PROBLEM: choose "Sony Playstation Vita" on the wizard's platform page - or a platform whose
// ScrapeAs is that - and it switches to its own Vita handling: the files chosen are ignored and the
// games INSTALLED in Vita3K's filesystem are listed instead, even with no emulator at all. With this
// plugin's disposable console, and the games kept as zips, that list is empty. MEASURED 27/09: the
// choice keys on the platform picked on that page, not on the emulator - LaunchBox's own Vita3K
// detection (Vita3kPathResolver.IsVita3kApplicationPath) is only asked from inside the Vita scan.
//
// THE FIX (Mehdi's idea): for the scan and the scan only, the wizard is shown a platform that is not
// Vita - "Sony Playstation Vita Temp" in place of the platform, the scrape-as or both - and the real
// values go back before anything is imported. The wizard then scans the files like any other ROMs,
// and every page that matters - emulator, metadata, images - saw the real platform.
//
// NO HARMONY. LaunchBox's core is obfuscated: decoy bodies, the real ones handed to the JIT at run time,
// and no Harmony patch possible at all - Harmony rebuilds a method from its IL, and the decoy's exception
// table points past its 4 bytes. But the wizard is WPF, and WPF is not: a class handler on every
// window's Loaded finds the wizard, a class handler on every button's Click runs BEFORE the wizard's own
// handler, and its view models' properties have readable names - Platform, ScrapeAs, Games - whose
// setters, CALLED, run their real bodies.
//
// MEASURED 27/09 ON LAUNCHBOX 14, and each point decided something:
//   - the pages: welcome, the files (chosen BEFORE the platform), the platform
//     (ImportWizardPlatformSelectViewModel), emulator, move/copy, metadata, images, EmuMovies, custom
//     options (RomImportCustomOptionsViewModel), the scan (RomImportLocationParseViewModel), the game
//     list (RomImportGameListViewModel.Games). So the swap is made on "Next" of the custom options, the
//     last page before the scan.
//   - the game list is EMPTY when its page appears and filled just after: a restore there found no
//     record, the game went in as "…Temp" and LaunchBox created that platform. So the restore is made
//     on "Finish", before the wizard acts - and until then the stand-in stays, so that "Back" then
//     "Next" scans the files again.
//   - a record's Platform is READ-ONLY (a get-only auto-property, its readonly field's name obfuscated).
//     So a record's STRING FIELDS are written: every one holding exactly the stand-in goes back to
//     "Sony Playstation Vita" - the platform and the scrape-as alike.
//   - ScrapeAs is a drop-down: a value its list does not hold can be put back to null by the binding.
//     So the stand-in is in ScrapeAsPlatforms for the time of the swap, and every value is read back.
//
// ON BY DEFAULT, and turned off in the pack's configuration window (Tools > Nixx Integration Plugins
// Configuration..., the Vita3K tab): see Vita3kSettings.BypassVitaImport, read at each import.
//
// AND THE LIST IS PUT RIGHT once the scan has filled it (Vita3kImportCleanup): titles from the
// param.sfo, updates and DLC out of the list and into the emulator's index, what is not a Vita game
// out. The list fills AFTER its page appears (measured), so the cleanup waits until it has stopped
// changing. Its own setting (Vita3kSettings.CleanImportList), on by default.
//
// ONLY INSIDE LAUNCHBOX - AND NOTHING AT ALL ANYWHERE ELSE. Install() looks at the process first and
// returns before touching anything unless it is LaunchBox.exe: in LiteBox and in Big Box no class
// handler is registered, no WPF type is looked at, no view model is read by reflection, and
// Vita3kImportCleanup - reached only from here - never runs. Everything below knows LaunchBox's
// wizard by its obfuscated-core names (WizardViewModel, RomImportGameListViewModel...) and must stay
// behind that one check: a host that merely shares the WPF runtime is no reason to go looking.
//
// LITEBOX, WHEN IT HAS ITS OWN IMPORT, IS NOT TO BE SERVED FROM HERE. It will ASK - a method call on
// the plugin of the emulator chosen, over the list it is about to import (see the note at the end of
// src\Catalog\LbCatalog.cs) - so nothing of this file is needed there: no swap (LiteBox has no Vita
// import of its own to get around), no capture, no reflection. What it will want is the part of the
// cleanup that decides what each file is - see Vita3kImportCleanup's header.
//
// Lines are "[import] ..." in vita3k.log - one for each swap and each restore.

using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace LbIntegrations.Vita3k
{
    internal static class Vita3kLbImport
    {
        internal const string VitaPlatform = "Sony Playstation Vita";
        internal const string StandIn = "Sony Playstation Vita Temp";

        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string OptionsPage = "RomImportCustomOptionsViewModel";
        private const string GameListPage = "RomImportGameListViewModel";

        private static bool _installed;

        /// <summary>The platform page of the wizard in progress - the swap and the restore write to it.</summary>
        private static object _platformPage;

        /// <summary>What was swapped, and from what - null for a value left alone.</summary>
        private static string _swappedPlatform, _swappedScrapeAs;

        private static bool Swapped => _swappedPlatform != null || _swappedScrapeAs != null;

        /// <summary>The game list already put right for the scan that filled it - a new scan clears it.</summary>
        private static object _cleanedList;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                // THE ONE GATE: nothing of the wizard's machinery exists outside LaunchBox - see the header.
                if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "LaunchBox", StringComparison.OrdinalIgnoreCase))
                    return;

                // The application may not be up yet when plugins are built: waited for, off its thread.
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
                { IsBackground = true, Name = "Vita3K import wizard" };
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
                // A CLASS handler runs before the element's own - before the wizard's "Next" and "Finish".
                // handledEventsToo: a click the wizard marks handled is still seen.
                System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase),
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new System.Windows.RoutedEventHandler(ButtonClicked), true);
            }
            catch (Exception ex) { Log.Warn("[import] could not watch the import wizard", ex); }
        }

        // ── the wizard and its pages ─────────────────────────────────────────

        private static bool IsWizard(object vm) => vm != null && vm.GetType().Name == WizardType;

        private static void WindowLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Window window) || !IsWizard(window.DataContext)) return;
                _platformPage = null;
                _swappedPlatform = _swappedScrapeAs = null;
                _cleanedList = null;
                if (window.DataContext is INotifyPropertyChanged notify)
                    notify.PropertyChanged += (s, a) => PageShown(window, s);
            }
            catch (Exception ex) { Log.Warn("[import] wizard opened", ex); }
        }

        /// <summary>A page change: the platform page is kept when it shows - and if the user came back
        /// to it mid-swap, the swap is undone there (no record exists yet; the next scan swaps again),
        /// so that it never shows the stand-in.</summary>
        private static void PageShown(System.Windows.Window window, object wizard)
        {
            try
            {
                var page = CurrentPage(wizard);
                var name = page?.GetType().Name;
                if (name == PlatformPage)
                {
                    _platformPage = page;
                    if (Swapped) Undo(page, "back on the platform page");
                }
                else if (name == GameListPage && Swapped && !ReferenceEquals(_cleanedList, page)
                     && (Vita3kSettings.CleanImportList || Vita3kSettings.ImportTitle || Vita3kSettings.ImportRegionVersion))
                {
                    _cleanedList = page;
                    WhenFilled(window, page);
                }
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        /// <summary>The cleanup, once the list has stopped filling: 500 ms without a change. Not from
        /// inside the list's own notification - WPF refuses a collection changed while it notifies.</summary>
        private static void WhenFilled(System.Windows.Window window, object gameList)
        {
            object games = null;
            try { games = gameList.GetType().GetProperty("Games")?.GetValue(gameList); } catch { }
            if (!(games is System.Collections.Specialized.INotifyCollectionChanged changes)) return;

            // WHILE LAUNCHBOX FILLS THE LIST (Mehdi, 30/09, as for Flycast): a window says so, the lines counted as
            // they come, until the reading below takes over with its own bar.
            var since = System.Diagnostics.Stopwatch.StartNew();
            var waiting = Waiting(new WindowOwner(window));
            var count = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, window.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(300) };
            count.Tick += (_, _) => waiting?.Say("LaunchBox is listing the files: " + ((games as System.Collections.ICollection)?.Count ?? 0) + " so far...");
            count.Start();
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, window.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(500) };
            System.Collections.Specialized.NotifyCollectionChangedEventHandler restart = (_, _) => { timer.Stop(); timer.Start(); };
            changes.CollectionChanged += restart;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                count.Stop();
                changes.CollectionChanged -= restart;
                waiting?.Close();
                Log.Info("[import] LaunchBox listed " + ((games as System.Collections.ICollection)?.Count ?? 0) + " file(s) in " + since.Elapsed.TotalSeconds.ToString("0.0") + " s");
                try { Vita3kImportCleanup.Run(new WindowOwner(window), gameList); }
                catch (Exception ex) { Log.Warn("[import] could not put the list right", ex); }
            };
            timer.Start();
        }

        /// <summary>A small window that says what is going on, not modal - LaunchBox goes on filling the list under it.</summary>
        private sealed class WaitingWindow
        {
            public System.Windows.Forms.Form Form;
            public System.Windows.Forms.Label Label;
            public void Say(string text) { try { if (!Form.IsDisposed) Label.Text = text; } catch { } }
            public void Close() { try { if (!Form.IsDisposed) Form.Close(); } catch { } }
        }

        private static WaitingWindow Waiting(System.Windows.Forms.IWin32Window owner)
        {
            try
            {
                var form = new System.Windows.Forms.Form
                {
                    Text = "Nixx-Vita3K - reading the games", StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ControlBox = false,
                    ShowInTaskbar = false, ClientSize = new System.Drawing.Size(460, 80), Font = new System.Drawing.Font("Segoe UI", 9f),
                };
                var label = new System.Windows.Forms.Label { Location = new System.Drawing.Point(14, 14), Size = new System.Drawing.Size(432, 20), AutoEllipsis = true, Text = "LaunchBox is listing the files..." };
                var bar = new System.Windows.Forms.ProgressBar { Location = new System.Drawing.Point(14, 42), Size = new System.Drawing.Size(432, 18), Style = System.Windows.Forms.ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
                form.Controls.AddRange(new System.Windows.Forms.Control[] { label, bar });
                if (GetWindowRect(owner.Handle, out var w))
                    form.Location = new System.Drawing.Point(w.Left + (w.Right - w.Left - form.Width) / 2, w.Top + (w.Bottom - w.Top - form.Height) / 2);
                form.Show(owner);
                return new WaitingWindow { Form = form, Label = label };
            }
            catch (Exception ex) { Log.Warn("[import] no waiting window", ex); return null; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        /// <summary>A WPF window as the owner of a WinForms dialog.</summary>
        private sealed class WindowOwner : System.Windows.Forms.IWin32Window
        {
            public WindowOwner(System.Windows.Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
            public IntPtr Handle { get; }
        }

        /// <summary>The wizard's page on show: ActivePageViewModel (measured), or failing that the first
        /// property holding a wizard page.</summary>
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
                if (p.GetIndexParameters().Length > 0 || !IsPage(p.PropertyType)) continue;
                try { var v = p.GetValue(wizard); if (v != null) return v; } catch { }
            }
            return null;
        }

        private static bool IsPage(Type t)
        {
            for (var b = t; b != null; b = b.BaseType) if (b.Name == "WizardPageViewModelBase") return true;
            return false;
        }

        private static void ButtonClicked(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                if (!(sender is System.Windows.Controls.Primitives.ButtonBase button)) return;
                var window = System.Windows.Window.GetWindow(button);
                if (window == null || !IsWizard(window.DataContext)) return;
                var label = button.Content as string;
                var page = CurrentPage(window.DataContext)?.GetType().Name;
                if (label == "Next" && page == OptionsPage) BeforeScan();
                else if (label == "Finish" && page == GameListPage) BeforeImport(CurrentPage(window.DataContext));
            }
            catch (Exception ex) { Log.Warn("[import] wizard button", ex); }
        }

        // ── the swap ─────────────────────────────────────────────────────────

        private static bool IsVita(string platform) => string.Equals(platform, VitaPlatform, StringComparison.OrdinalIgnoreCase);

        /// <summary>"Next" on the custom options: the scan comes next, and must not see Vita - by the
        /// platform or by the scrape-as.</summary>
        private static void BeforeScan()
        {
            _cleanedList = null;                    // the scan fills the list again
            var page = _platformPage;
            if (page == null || Swapped) return;   // no platform page seen / still swapped from a scan before
            var platform = Get(page, "Platform");
            var scrapeAs = Get(page, "ScrapeAs");
            bool byName = IsVita(platform), byScrape = IsVita(scrapeAs);
            if (!byName && !byScrape) return;
            // THE USER'S CHOICE, read now: the configuration window's Vita3K tab, on by default.
            if (!Vita3kSettings.BypassVitaImport)
            { Log.Info("[import] a Vita import, left to LaunchBox's own Vita handling - the bypass is off in the settings"); return; }

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
                Log.Info("[import] Vita games scanned as ROM files: platform \"" + platform + "\" -> \"" + Get(page, "Platform")
                         + "\", scrape-as \"" + scrapeAs + "\" -> \"" + Get(page, "ScrapeAs") + "\" until Finish");
            else
                Log.Warn("[import] nothing could be swapped - the wizard stays in its Vita mode");
        }

        /// <summary>"Finish" on the game list, before the wizard imports: the real values go back - onto
        /// every record, then onto the platform page.</summary>
        private static void BeforeImport(object gameList)
        {
            if (!Swapped) return;
            int records = 0, left = 0;
            object games = null;
            try { games = gameList?.GetType().GetProperty("Games")?.GetValue(gameList); } catch { }
            if (games is IEnumerable list)
                foreach (var record in list.Cast<object>().ToList())
                {
                    records++;
                    // Both stand-ins were "Sony Playstation Vita": whatever field held one, that is its value.
                    SetFields(record, StandIn, VitaPlatform);
                    if (Get(record, "Platform") == StandIn) left++;
                }
            Undo(_platformPage, null);
            if (left == 0)
                Log.Info("[import] restored before the import: " + records + " game(s), platform \"" + Get(_platformPage, "Platform")
                         + "\", scrape-as \"" + Get(_platformPage, "ScrapeAs") + "\"");
            else
                Log.Warn("[import] " + left + " of " + records + " game(s) still carry \"" + StandIn + "\" - LaunchBox will create that platform");
        }

        /// <summary>The platform page given its real values back, and the stand-in taken out of the
        /// scrape-as list.</summary>
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

        // ── reading and writing LaunchBox's objects ──────────────────────────

        private static object ScrapeAsList(object page)
        {
            try { return page?.GetType().GetProperty("ScrapeAsPlatforms", BindingFlags.Public | BindingFlags.Instance)?.GetValue(page); }
            catch { return null; }
        }

        private static string Get(object vm, string name)
        {
            try { return vm?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(vm) as string; }
            catch { return null; }
        }

        /// <summary>A property set through its setter - CALLED, so its real body runs, notification and all.</summary>
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

        /// <summary>Every string field of <paramref name="record"/> holding exactly <paramref name="was"/>
        /// set to <paramref name="now"/> - readonly or not, whatever the obfuscator named it. True when
        /// at least one was.</summary>
        internal static bool SetFields(object record, string was, string now)
        {
            bool any = false;
            for (var t = record?.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(string)) continue;
                    try
                    {
                        if (!string.Equals(f.GetValue(record) as string, was, StringComparison.Ordinal)) continue;
                        f.SetValue(record, now);
                        any = true;
                    }
                    catch (Exception ex) { Log.Warn("[import] could not set a field of a game", ex); }
                }
            return any;
        }
    }
}
