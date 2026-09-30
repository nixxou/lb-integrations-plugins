// LaunchBox's "Import ROM Files" wizard, its last page's game list sorted for Flycast (Mehdi, 29/09): importing
// to Nixx-Flycast for one of its platforms - the platform picked, or its scrape-as - only the games of that
// platform stay. A whole MAME folder is what gets imported for Naomi; most of it is not Flycast's.
//
// TWO CHECKS PER PLATFORM, each its own box in the pack's configuration window (FlycastSettings):
//   QUICK, by the file as Flycast finds it: an arcade zip or 7z by its NAME - the one thing Flycast goes by
//     (naomi_cart.cpp, FindGame) - a set of Flycast's table for that system (flycast-id --sets); a legacy
//     decrypted rom (.bin .dat .lst) for Naomi; a Dreamcast disc by its extension (.chd .gdi .cdi .cue).
//     A GD-ROM game whose image is not beside it YET is KEPT (Mehdi, 30/09): the image can come later, and a
//     launch without it says what is missing (FlycastPlugin, PrepareEmulatorForLaunch).
//   CRC, by the file's CONTENT: a set kept must hold every file of its set, its parent's zip counted in (flycast-id
//     --identify); a zip under another name that IS a set of that system is told by its files and said, with the
//     name to give it - Flycast would not run it as it is; then each game kept is LOADED by flycast-id as Flycast
//     loads it, discs too (their IP.BIN) - a set whose files are all there can still be one Flycast refuses.
//     Minutes on a whole MAME folder, under a window that says where it is and can be cancelled.
//
// "FORCE USING MAME METADATA" TICKED BY DEFAULT (Mehdi, 30/09) on the custom options page, for an arcade
// platform of Flycast's (Naomi, Naomi 2, Atomiswave, a System SP one - not the Dreamcast) imported to Nixx-Flycast:
// its games are MAME sets, and LaunchBox names them from MAME's list only with that box. Once per wizard - a
// box the user unticks stays unticked, back and forth. The box is the page's bool property whose name says
// "Mame"; what was found is in the log.
//
// THE MAME PAGE, EVERY "SKIP ..." BOX UNTICKED (Mehdi, 30/09), for the same imports, once per wizard: which sets
// Flycast runs is its own table's say (the filter below), not MAME's categories - Naomi has quiz and mahjong games,
// and many sets MAME calls unplayable run in Flycast. "Skip games unplayable in MAME" is hidden besides: it says
// nothing about Flycast - and kept false, "Check All" included. Only what a CHECKBOX is bound to is touched: the
// clone mode is three radio buttons on properties of the same kind. The user may tick the others back; what is
// put back after the import follows them (FlycastImportFinished).
//
// WHILE LAUNCHBOX FILLS THE LIST (Mehdi, 30/09: a minute on a MAME folder, Finish greyed, nothing said), a window
// says so from the moment the list page shows, the lines counted as they come, then the check takes over.
//
// BACK IS GREYED once the list is sorted (Mehdi, 30/09): back and forth over a sorted list left it empty or
// broken - Finish or Cancel from there.
//
// WHICH EMULATOR: the wizard's own choice, looked for among its pages - an IEmulator, or anything with an
// ApplicationPath - and said in the log where it was found. Not Nixx-Flycast, or not found: nothing is touched.
//
// NOT IN THE WAY OF THE VITA3K PLUGIN'S (Vita3kLbImport): its own class handlers in its own DLL, no state shared,
// and a Vita platform is none of Flycast's - no import can meet both. And nothing is swapped here: lines only
// leave the list.
//
// ONLY INSIDE LAUNCHBOX, as the Vita3K one: Install() returns before touching anything in any other process.
// Lines are "[import] ..." in flycast.log.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Flycast
{
    internal static class FlycastLbImport
    {
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";
        private const string OptionsPage = "RomImportCustomOptionsViewModel";
        private const string MameOptionsPage = "RomImportMameOptionsViewModel";
        private static bool _mameDone, _mameChoicesDone;

        private static bool _installed;

        /// <summary>For the probe: no dialog at the end.</summary>
        internal static bool Quiet;
        private static object _platformPage;
        private static object _filteredList;

        /// <summary>Every page this wizard has shown, in order - the wizard holds only the one on show (measured
        /// 30/09: none of its properties reached the emulator page by the time of the game list).</summary>
        private static readonly List<object> _seenPages = new List<object>();
        private static bool _dumped;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                // THE ONE GATE: nothing of the wizard's machinery exists outside LaunchBox.
                if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "LaunchBox", StringComparison.OrdinalIgnoreCase))
                    return;
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < 120; i++)
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null) { app.Dispatcher.BeginInvoke(new Action(Hook)); return; }
                        Thread.Sleep(500);
                    }
                    Log.Warn("[import] no WPF application in this host - the import wizard is left as it is");
                })
                { IsBackground = true, Name = "Flycast import wizard" };
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
                _filteredList = null;
                _seenPages.Clear();
                _dumped = false;
                _mameDone = false;
                _mameChoicesDone = false;
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
                if (page != null && !_seenPages.Any(p => ReferenceEquals(p, page))) _seenPages.Add(page);
                if (name == PlatformPage) { _platformPage = page; return; }
                if (name == OptionsPage) { MameMetadata(wizard, page); return; }
                if (name == MameOptionsPage) { MameChoices(window, wizard, page); return; }
                if (name != GameListPage || ReferenceEquals(_filteredList, page)) return;
                // Decided once for this list, whatever the answer - the wizard notifies many times per page.
                _filteredList = page;

                var platform = Get(_platformPage, "Platform");
                var scrapeAs = Get(_platformPage, "ScrapeAs");
                var system = FlycastPlatforms.SystemOf(platform) ?? FlycastPlatforms.SystemOf(scrapeAs);
                if (system == null) return;
                bool quick = FlycastSettings.QuickCheck(system), crc = FlycastSettings.CrcCheck(system);
                if (!quick && !crc) { Log.Info("[import] " + system + ": no check ticked in the settings - the list is left as it is"); return; }
                var exe = ChosenEmulator(wizard, out var where);
                if (exe == null || !FlycastPaths.IsFlycastExecutable(exe))
                {
                    Log.Info("[import] " + system + " import, but " + (exe == null ? "no chosen emulator was found (" + where + ")" : "the emulator is not Flycast (" + exe + ")")
                             + " - the list is left as it is");
                    return;
                }
                Log.Info("[import] " + system + " import to Flycast (" + exe + ", found as " + where + "): " + (quick ? "quick" : "") + (quick && crc ? " + " : "") + (crc ? "CRC" : "") + " check");
                WhenFilled(window, page, system, quick, crc, FlycastPaths.Resolve(FlycastPlugin.ResolveFullPath(exe)));
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        /// <summary>"Force using MAME metadata" ticked, once, for an arcade platform imported to Flycast.</summary>
        private static void MameMetadata(object wizard, object page)
        {
            if (_mameDone) return;
            var system = FlycastPlatforms.SystemOf(Get(_platformPage, "Platform")) ?? FlycastPlatforms.SystemOf(Get(_platformPage, "ScrapeAs"));
            if (system == null || system == "Dreamcast") return;
            var exe = ChosenEmulator(wizard, out _);
            if (exe == null || !FlycastPaths.IsFlycastExecutable(exe)) return;
            _mameDone = true;
            var boxes = page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite && p.GetIndexParameters().Length == 0).ToList();
            var mame = boxes.Where(p => p.Name.IndexOf("Mame", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (mame.Count != 1)
            {
                Log.Info("[import] \"Force using MAME metadata\" not found for sure among the options (" + string.Join(", ", boxes.Select(p => p.Name)) + ") - left as it is");
                return;
            }
            try
            {
                if (mame[0].GetValue(page) is bool on && on) { Log.Info("[import] " + mame[0].Name + " already ticked"); return; }
                mame[0].SetValue(page, true);
                Log.Info("[import] " + system + " to Flycast: " + mame[0].Name + " ticked (MAME's names for MAME sets) - the user may untick it");
            }
            catch (Exception ex) { Log.Warn("[import] could not tick " + mame[0].Name, ex); }
        }

        /// <summary>Every "Skip ..." box of the MAME page unticked, "Skip games unplayable in MAME" hidden - once per
        /// wizard, for an arcade platform imported to Flycast.</summary>
        private static void MameChoices(System.Windows.Window window, object wizard, object page)
        {
            if (_mameChoicesDone) return;
            var system = FlycastPlatforms.SystemOf(Get(_platformPage, "Platform")) ?? FlycastPlatforms.SystemOf(Get(_platformPage, "ScrapeAs"));
            if (system == null || system == "Dreamcast") return;
            var exe = ChosenEmulator(wizard, out _);
            if (exe == null || !FlycastPaths.IsFlycastExecutable(exe)) return;
            _mameChoicesDone = true;
            var all = page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                          .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType == typeof(bool) || p.PropertyType.IsEnum || p.PropertyType == typeof(string))).ToList();
            // What the page holds, once: which of its values are the clone mode is read from here.
            Log.Info("[import] the MAME page: " + string.Join(", ", all.Select(p => { try { return p.Name + "=" + p.GetValue(page); } catch { return p.Name + "=?"; } })));
            // Once the page is drawn: ONLY WHAT A CHECKBOX IS BOUND TO is unticked (Mehdi, 30/09) - the clone mode is
            // three radio buttons on properties of the same kind (SkipClones "Skip clones and prioritize by region",
            // ImportOriginals, ImportAllClones), and a name rule unset SkipClones, leaving no mode at all. A radio
            // button is a ToggleButton, never a CheckBox, so it cannot be caught here.
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var boxes = Descendants<System.Windows.Controls.CheckBox>(window)
                        .Select(box => (Box: box, Path: System.Windows.Data.BindingOperations.GetBindingExpression(box, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.ParentBinding?.Path?.Path ?? box.Name))
                        .Where(b => !string.IsNullOrEmpty(b.Path) && b.Path.StartsWith("Skip", StringComparison.Ordinal)).ToList();
                    var unticked = new List<string>();
                    foreach (var (box, path) in boxes)
                    {
                        var p = all.FirstOrDefault(x => x.Name == path && x.PropertyType == typeof(bool) && x.CanWrite);
                        if (p == null) continue;
                        try { if (p.GetValue(page) is bool on && on) { p.SetValue(page, false); unticked.Add(p.Name); } }
                        catch (Exception ex) { Log.Warn("[import] could not untick " + p.Name, ex); }
                        if (path == "SkipUnplayable") box.Visibility = System.Windows.Visibility.Collapsed;
                    }
                    Log.Info("[import] " + system + " to Flycast: " + boxes.Count + " Skip box(es) on the page, "
                             + (unticked.Count == 0 ? "none was ticked" : "unticked " + string.Join(", ", unticked))
                             + (boxes.Any(b => b.Path == "SkipUnplayable") ? "; \"Skip games unplayable in MAME\" hidden" : "; \"Skip games unplayable in MAME\" not found to hide")
                             + " - which sets Flycast runs is its own table's say; the user may tick the others back");
                }
                catch (Exception ex) { Log.Warn("[import] could not set the MAME page's boxes", ex); }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // AND IT STAYS UNTICKED, hidden as it is: "Check All" - or anything else - sets it on the page, and a box
            // nobody can see would then leave sets out. Put back to false as soon as it changes.
            var unplayable = all.FirstOrDefault(x => x.Name == "SkipUnplayable" && x.PropertyType == typeof(bool) && x.CanWrite);
            if (unplayable != null && page is INotifyPropertyChanged watched)
                watched.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName != "SkipUnplayable" && !string.IsNullOrEmpty(a.PropertyName)) return;
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (unplayable.GetValue(page) is bool on && on)
                            {
                                unplayable.SetValue(page, false);
                                Log.Info("[import] \"Skip games unplayable in MAME\" was set again (Check All?) - put back to false, it is hidden");
                            }
                        }
                        catch { }
                    }));
                };
        }

        /// <summary>Every element of <typeparamref name="T"/> under <paramref name="root"/>, in the visual tree.</summary>
        private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
        {
            if (root == null) yield break;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var d in Descendants<T>(child)) yield return d;
            }
        }

        /// <summary>The check, once the list has stopped filling (500 ms without a change) - as Vita3kLbImport. Until
        /// then a window says LaunchBox is listing the files, and how many so far.</summary>
        private static void WhenFilled(System.Windows.Window window, object gameList, string system, bool quick, bool crc, FlycastLayout layout)
        {
            object games = null;
            try { games = gameList.GetType().GetProperty("Games")?.GetValue(gameList); } catch { }
            if (!(games is System.Collections.Specialized.INotifyCollectionChanged changes)) return;
            var since = System.Diagnostics.Stopwatch.StartNew();
            var waiting = Waiting(new WindowOwner(window));
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, window.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(500) };
            var count = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, window.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(300) };
            count.Tick += (_, _) => waiting?.Say("LaunchBox is listing the files: " + ((games as ICollection)?.Count ?? 0) + " so far...");
            count.Start();
            System.Collections.Specialized.NotifyCollectionChangedEventHandler restart = (_, _) => { timer.Stop(); timer.Start(); };
            changes.CollectionChanged += restart;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                count.Stop();
                changes.CollectionChanged -= restart;
                waiting?.Close();
                Log.Info("[import] LaunchBox listed " + ((games as ICollection)?.Count ?? 0) + " file(s) in " + since.Elapsed.TotalSeconds.ToString("0.0") + " s");
                try
                {
                    if (Filter(new WindowOwner(window), gameList, system, quick, crc, layout)) GreyBack(window);
                }
                catch (Exception ex) { Log.Warn("[import] could not sort the list", ex); }
            };
            timer.Start();
        }

        /// <summary>The wizard's Back button greyed: back and forth over a sorted list leaves it broken.</summary>
        private static void GreyBack(System.Windows.Window window)
        {
            try
            {
                var back = Descendants<System.Windows.Controls.Primitives.ButtonBase>(window)
                           .Where(b => b.Name == "Back" || string.Equals(b.Content as string, "Back", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var b in back) b.IsEnabled = false;
                Log.Info(back.Count > 0 ? "[import] Back greyed on the sorted list - Finish or Cancel from here" : "[import] no Back button found to grey");
            }
            catch (Exception ex) { Log.Warn("[import] could not grey Back", ex); }
        }

        /// <summary>A small window that says what is going on, not modal - LaunchBox goes on filling the list under it.</summary>
        private sealed class WaitingWindow
        {
            public Form Form;
            public Label Label;
            public void Say(string text) { try { if (!Form.IsDisposed) Label.Text = text; } catch { } }
            public void Close() { try { if (!Form.IsDisposed) Form.Close(); } catch { } }
        }

        private static WaitingWindow Waiting(IWin32Window owner)
        {
            if (Quiet) return null;
            try
            {
                var form = new Form
                {
                    Text = "Nixx-Flycast - checking the games", StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ControlBox = false,
                    ShowInTaskbar = false, ClientSize = new Size(460, 80), Font = new Font("Segoe UI", 9f),
                };
                var label = new Label { Location = new Point(14, 14), Size = new Size(432, 20), AutoEllipsis = true, Text = "LaunchBox is listing the files..." };
                var bar = new ProgressBar { Location = new Point(14, 42), Size = new Size(432, 18), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
                form.Controls.AddRange(new Control[] { label, bar });
                form.Show(owner);
                // CenterParent is honoured by ShowDialog only: centred on the wizard by hand.
                var r = System.Windows.Forms.Screen.FromHandle(owner.Handle).WorkingArea;
                if (GetWindowRect(owner.Handle, out var w))
                    form.Location = new Point(w.Left + (w.Right - w.Left - form.Width) / 2, w.Top + (w.Bottom - w.Top - form.Height) / 2);
                else form.Location = new Point(r.Left + (r.Width - form.Width) / 2, r.Top + (r.Height - form.Height) / 2);
                return new WaitingWindow { Form = form, Label = label };
            }
            catch (Exception ex) { Log.Warn("[import] no waiting window", ex); return null; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        // ── the checks ───────────────────────────────────────────────────────

        private sealed class Finding
        {
            public object Record;
            public string Path, Drop, Rename;
            public FlycastGameIdentity.ArcadeSet Set;
            public bool NoGdRom;          // kept, its GD-ROM image not there yet
        }

        private static readonly string[] Discs = { ".chd", ".gdi", ".cdi", ".cue" };
        private static readonly string[] Archives = { ".zip", ".7z" };
        private static readonly string[] Decrypted = { ".bin", ".dat", ".lst" };

        /// <summary>The list sorted. True when it was changed - a line out, a title set.</summary>
        internal static bool Filter(IWin32Window owner, object gameList, string system, bool quick, bool crc, FlycastLayout layout)
        {
            var timing = System.Diagnostics.Stopwatch.StartNew();
            var list = GetProperty(gameList, "Games") as IList;
            if (list == null || list.Count == 0) return false;
            var findings = list.Cast<object>().Select(r => new Finding { Record = r, Path = GetProperty(r, "ApplicationPath") as string ?? "" }).ToList();
            bool arcade = system != "Dreamcast";
            Dictionary<string, FlycastGameIdentity.ArcadeSet> sets = null;
            if (arcade)
            {
                sets = FlycastGameIdentity.Sets(layout, out var why);
                if (sets == null) { Log.Warn("[import] Flycast's sets could not be listed (" + why + ") - the list is left as it is"); return false; }
            }

            // Both checks under one window with a bar (Mehdi, 30/09: a bar for the quick check too - a whole MAME
            // folder is tens of thousands of lines): the quick one first - and what CRC needs to know first, the
            // set a name is - then the CRC one when it is ticked.
            var tSets = timing.Elapsed.TotalSeconds;
            if (!Check(owner, findings, system, sets, layout, crc)) { Log.Info("[import] the check was cancelled - the list is left as it is"); return false; }
            var tCheck = timing.Elapsed.TotalSeconds;
            // Without the quick check, a file the name alone would drop stays unless the content dropped it too.
            if (!quick) foreach (var f in findings) if (f.Drop != null && f.Drop.StartsWith("?")) f.Drop = null;

            // IN ONE GO (measured 30/09: 36 563 lines taken out one by one, each looked up in the whole list and
            // each redrawn by the grid, took 27 s and made the titles flicker). The findings are in the list's own
            // order, so line i is finding i; the list - Caliburn's BindableCollection - is told nothing until the
            // end (IsNotifying), then refreshed once. Without that switch, the lines go one by one all the same.
            int kept = 0, dropped = 0, titled = 0;
            bool silenced = SetProperty(list, "IsNotifying", false);
            try
            {
                for (int i = findings.Count - 1; i >= 0; i--)
                {
                    if (i >= list.Count || !ReferenceEquals(list[i], findings[i].Record)) continue;   // changed meanwhile: left alone
                    if (findings[i].Drop == null) { kept++; continue; }
                    list.RemoveAt(i);
                    dropped++;
                }
                // A game LaunchBox could not name keeps its file's name ("wldkicksj"): Flycast's own name for its set.
                foreach (var f in findings.Where(x => x.Drop == null && x.Set?.Description != null))
                {
                    var title = GetProperty(f.Record, "Title") as string ?? "";
                    if (Plain(title) != Plain(Path.GetFileNameWithoutExtension(f.Path))) continue;
                    if (SetProperty(f.Record, "Title", f.Set.Description)) titled++;
                }
            }
            finally
            {
                if (silenced)
                {
                    SetProperty(list, "IsNotifying", true);
                    try { list.GetType().GetMethod("Refresh", Type.EmptyTypes)?.Invoke(list, null); } catch { }
                }
            }
            Notify(gameList, "GameCount");
            var tRemove = timing.Elapsed.TotalSeconds;
            Log.Info("[import] timing: sets " + tSets.ToString("0.0") + " s, check " + (tCheck - tSets).ToString("0.0") + " s, lines out and titles "
                     + (tRemove - tCheck).ToString("0.0") + " s");

            // The log: what was removed, by reason - each file named but for the bulk of a MAME folder.
            var removed = findings.Where(f => f.Drop != null).GroupBy(f => Reason(f.Drop)).OrderByDescending(g => g.Count());
            var lines = new List<string>();
            foreach (var g in removed)
            {
                lines.Add("[import]   " + g.Count() + " removed - " + g.Key);
                if (g.Key != UnknownName)
                    foreach (var f in g) lines.Add("[import]     " + Path.GetFileName(f.Path) + " - " + f.Drop.TrimStart('?'));
            }
            var noImage = findings.Where(f => f.Drop == null && f.NoGdRom).ToList();
            if (noImage.Count > 0)
            {
                lines.Add("[import]   " + noImage.Count + " kept without their GD-ROM image yet - a launch says what is missing:");
                foreach (var f in noImage) lines.Add("[import]     " + Path.GetFileName(f.Path) + " - " + f.Set.Name + "\\" + f.Set.GdRom + ".chd not there");
            }
            if (lines.Count > 0) Log.Info(string.Join(Environment.NewLine, lines));
            var renames = findings.Where(f => f.Rename != null).ToList();
            Log.Info("[import] " + system + ": " + kept + " game(s) kept, " + dropped + " removed, " + titled + " titled from Flycast's names"
                     + (renames.Count > 0 ? ", " + renames.Count + " under another name" : ""));
            if (renames.Count > 0 && !Quiet)
                MessageBox.Show(owner,
                    renames.Count + " file(s) are " + system + " games Flycast knows, under another name. Flycast finds a set by its file's name only, "
                    + "so they were left out - rename them and import them again:\n\n"
                    + string.Join("\n", renames.Take(20).Select(f => Path.GetFileName(f.Path) + "  ->  " + f.Rename + Path.GetExtension(f.Path)))
                    + (renames.Count > 20 ? "\n(and " + (renames.Count - 20) + " more, in flycast.log)" : ""),
                    "Nixx-Flycast - import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return dropped > 0 || titled > 0;
        }

        private const string UnknownName = "not a set Flycast knows by that name";

        /// <summary>A reason without what is particular to one file, to count the files by.</summary>
        private static string Reason(string drop)
        {
            var r = drop.TrimStart('?');
            if (r.StartsWith("its GD-ROM image is not there")) return "its GD-ROM image is not there";
            if (r.StartsWith("it is ")) return "a set under another name";
            if (r.StartsWith("Flycast cannot load it")) return "Flycast cannot load it";
            if (r.StartsWith("not every file of")) return "not every file of its set is there";
            if (r.StartsWith("not an arcade set") || r.StartsWith("not a Dreamcast disc")) return r.Substring(0, r.IndexOf(" (") > 0 ? r.IndexOf(" (") : r.Length);
            return r;
        }

        /// <summary>A title or a file name made comparable: case, and LaunchBox's "_" for a space.</summary>
        private static string Plain(string s) => (s ?? "").Replace('_', ' ').Trim().ToLowerInvariant();

        /// <summary>The quick check - a verdict by name or extension. A drop the content may overturn (an unknown
        /// name, when only the CRC check is on) starts with "?".</summary>
        private static void Quick(Finding f, string system, Dictionary<string, FlycastGameIdentity.ArcadeSet> sets)
        {
            var ext = (Path.GetExtension(f.Path) ?? "").ToLowerInvariant();
            if (sets == null)
            {
                if (!Discs.Contains(ext)) f.Drop = "not a Dreamcast disc Flycast reads (" + (ext.Length > 0 ? ext : "no extension") + ")";
                return;
            }
            if (Decrypted.Contains(ext)) { if (system != "Naomi") f.Drop = "a decrypted Naomi rom, not " + system; return; }
            if (!Archives.Contains(ext)) { f.Drop = "not an arcade set (" + (ext.Length > 0 ? ext : "no extension") + ")"; return; }
            var name = Path.GetFileNameWithoutExtension(f.Path);
            if (!sets.TryGetValue(name, out var set)) { f.Drop = "?" + UnknownName; return; }
            f.Set = set;
            if (set.System == FlycastGameIdentity.BiosSystem) { f.Drop = "a BIOS, not a game - Flycast wants it in its data folder"; return; }
            if (!string.Equals(set.System, system, StringComparison.OrdinalIgnoreCase))
            { f.Drop = (set.System.StartsWith("A") ? "an " : "a ") + set.System + " set, not " + system; return; }
            if (set.GdRom != null && FlycastGameIdentity.GdRomImage(f.Path, set) == null) f.NoGdRom = true;
        }

        /// <summary>The checks, off the UI thread under a window with a bar: the quick one always (the names), the
        /// CRC one when <paramref name="crc"/>. False when cancelled.</summary>
        private static bool Check(IWin32Window owner, List<Finding> findings, string system, Dictionary<string, FlycastGameIdentity.ArcadeSet> sets, FlycastLayout layout, bool crc)
        {
            using var form = new Form
            {
                Text = "Nixx-Flycast - checking the games", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ControlBox = false,
                ShowInTaskbar = false, ClientSize = new Size(460, 110), Font = new Font("Segoe UI", 9f),
            };
            var label = new Label { Location = new Point(14, 14), Size = new Size(432, 20), AutoEllipsis = true, Text = "Reading the list..." };
            var bar = new ProgressBar { Location = new Point(14, 40), Size = new Size(432, 18), Maximum = 1 };
            var cancel = new Button { Text = "Cancel", Location = new Point(356, 70), Width = 90 };
            form.Controls.AddRange(new Control[] { label, bar, cancel });
            bool cancelled = false;
            cancel.Click += (_, _) => { cancelled = true; cancel.Enabled = false; };
            void Say(string text, int value, int max)
            {
                try { form.BeginInvoke(new Action(() => { label.Text = text; bar.Maximum = Math.Max(1, max); bar.Value = Math.Min(Math.Max(0, value), bar.Maximum); })); } catch { }
            }

            form.Shown += (_, _) =>
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        // 0. The quick check: each line by its name or extension.
                        for (int i = 0; i < findings.Count && !cancelled; i++)
                        {
                            if (i % 50 == 0) Say("Checking the names " + (i + 1) + "/" + findings.Count, i, findings.Count);
                            Quick(findings[i], system, sets);
                        }
                        if (!crc || cancelled) { try { form.BeginInvoke(new Action(() => form.Close())); } catch { } return; }

                        // 1. The zips by their files' CRCs: those named as a set, and those named as none.
                        if (sets != null)
                        {
                            var zips = findings.Where(f => (Path.GetExtension(f.Path) ?? "").Equals(".zip", StringComparison.OrdinalIgnoreCase)
                                                           && (f.Drop == null || f.Drop.StartsWith("?"))).ToList();
                            string why = null;
                            var found = zips.Count == 0 ? new Dictionary<string, FlycastGameIdentity.Identified>()
                                : FlycastGameIdentity.Identify(layout, zips.Select(z => z.Path).ToList(),
                                                               n => Say("Reading the files of " + n + "/" + zips.Count, n, zips.Count), () => cancelled, out why);
                            if (found == null) { if (!cancelled) Log.Warn("[import] the CRC check could not run (" + why + ")"); }
                            else foreach (var f in zips)
                            {
                                if (!found.TryGetValue(f.Path, out var id)) continue;
                                if (f.Set != null && f.Drop == null && !id.OwnWhole) f.Drop = "not every file of " + f.Set.Name + " is there";
                                else if (f.Set == null && id.BestWhole && sets.TryGetValue(id.Best, out var real)
                                         && string.Equals(real.System, system, StringComparison.OrdinalIgnoreCase))
                                {
                                    f.Rename = id.Best;
                                    f.Drop = "it is " + id.Best + " by its files: Flycast finds a set by its name - rename it " + id.Best + Path.GetExtension(f.Path);
                                }
                                else if (f.Set == null) f.Drop = "not a set Flycast knows, by its name or its files";
                            }
                        }
                        // 2. What is kept, loaded as Flycast loads it.
                        // (not a GD-ROM game without its image: it cannot load until the image is there)
                        var keep = findings.Where(f => f.Drop == null && !f.NoGdRom).ToList();
                        for (int i = 0; i < keep.Count && !cancelled; i++)
                        {
                            Say("Loading " + (i + 1) + "/" + keep.Count + ": " + Path.GetFileName(keep[i].Path), i, keep.Count);
                            if (FlycastGameIdentity.Of(layout, keep[i].Path, 20000, out var why) == null)
                                keep[i].Drop = "Flycast cannot load it: " + why;
                        }
                    }
                    catch (Exception ex) { Log.Warn("[import] the CRC check", ex); }
                    try { form.BeginInvoke(new Action(() => form.Close())); } catch { }
                })
                { IsBackground = true, Name = "Flycast import check" };
                thread.Start();
            };
            form.ShowDialog(owner);
            return !cancelled;
        }

        // ── the wizard's objects ─────────────────────────────────────────────

        /// <summary>The emulator the wizard was told to import to: its application path, and where it was found -
        /// or null, and what was looked at.</summary>
        private static string ChosenEmulator(object wizard, out string where)
        {
            where = null;
            var seen = new List<string>();
            foreach (var page in _seenPages.Concat(Pages(wizard)).Distinct())
            {
                foreach (var p in page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.Name.IndexOf("Emulator", StringComparison.OrdinalIgnoreCase) >= 0) seen.Add(page.GetType().Name + "." + p.Name);
                    object v;
                    try { v = p.GetValue(page); } catch { continue; }
                    var path = PathOf(v) ?? (v == null ? null : PathOf(GetProperty(v, "Emulator")));
                    if (path == null) continue;
                    where = page.GetType().Name + "." + p.Name;
                    return path;
                }
            }
            where = seen.Count == 0 ? "no page property names an emulator" : "looked at " + string.Join(", ", seen);
            Dump(wizard);
            return null;
        }

        /// <summary>Once per wizard, when the emulator was not found: every page it showed and every property of
        /// theirs, with its type and a glimpse of its value - what finding it next time is made of.</summary>
        private static void Dump(object wizard)
        {
            if (_dumped) return;
            _dumped = true;
            try
            {
                var lines = new List<string> { "[import] the wizard, as seen - to find where it keeps the emulator chosen:" };
                void Describe(string label, object o)
                {
                    if (o == null) return;
                    lines.Add("[import]   " + label + " " + o.GetType().FullName);
                    foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (p.GetIndexParameters().Length > 0) continue;
                        string shown;
                        try
                        {
                            var v = p.GetValue(o);
                            shown = v == null ? "null" : v is string s ? "\"" + (s.Length > 80 ? s.Substring(0, 80) + "..." : s) + "\""
                                  : v.GetType().IsPrimitive || v is Enum ? v.ToString() : v.GetType().Name;
                        }
                        catch (Exception ex) { shown = "(" + ex.GetType().Name + ")"; }
                        lines.Add("[import]     ." + p.Name + " : " + p.PropertyType.Name + " = " + shown);
                    }
                }
                Describe("wizard", wizard);
                foreach (var page in _seenPages) Describe("page", page);
                foreach (var l in lines) Log.Info(l);
            }
            catch (Exception ex) { Log.Warn("[import] could not describe the wizard", ex); }
        }

        private static string PathOf(object v)
        {
            if (v is IEmulator e) { try { return e.ApplicationPath; } catch { return null; } }
            return v == null || v is string ? null : GetProperty(v, "ApplicationPath") as string;
        }

        /// <summary>The wizard's pages: every property holding one, or a list of them.</summary>
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
            try
            {
                var active = wizard.GetType().GetProperty("ActivePageViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(wizard);
                if (active != null) return active;
            }
            catch { }
            return null;
        }

        private static bool IsPage(Type t)
        {
            for (var b = t; b != null; b = b.BaseType) if (b.Name == "WizardPageViewModelBase") return true;
            return false;
        }

        private static string Get(object vm, string name) => GetProperty(vm, name) as string;

        private static object GetProperty(object o, string name)
        {
            try { return o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o); }
            catch { return null; }
        }

        private static bool SetProperty(object o, string name, object value)
        {
            try
            {
                var p = o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return false;
                p.SetValue(o, value);
                return true;
            }
            catch { return false; }
        }

        private static void Notify(object vm, string property)
        {
            try { vm?.GetType().GetMethod("NotifyOfPropertyChange", new[] { typeof(string) })?.Invoke(vm, new object[] { property }); }
            catch { }
        }

        private sealed class WindowOwner : IWin32Window
        {
            public WindowOwner(System.Windows.Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
            public IntPtr Handle { get; }
        }
    }
}
