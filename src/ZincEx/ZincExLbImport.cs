// LaunchBox's "Import ROM Files" wizard, its last page's game list sorted for ZiNc EX - the Demul plugin's DemulLbImport,
// taken over for ZiNc:
//
// BY THE ROMS, NOT BY THE NAME. Importing to Nixx-ZiNc-EX, the list keeps only the romsets that are one of ZiNc's 71 games,
// told as a launch tells them (ZincGames: every ROM of the game in the romset, its parent's or its BIOS's zip) - the name
// would keep MAME's sfex.zip, which ZiNc cannot run, and drop sfexu.zip, which is ZiNc's sfex. So a whole MAME folder can be
// imported: measured 16/10, 43974 zips read in 6.6 s (their directories only, never their data), on four threads here.
// ONE ROMSET PER ZINC GAME: MAME has four Brave Blades that are all ZiNc's brvblade - the one named as ZiNc names it is kept,
// else the one holding most of the game's own ROMs (as a launch picks), and the others leave the list, said in the log.
// The BIOS sets and what is no zip leave it too. A game LaunchBox could not name is titled from ZiNc's list.
//
// AS DEMUL'S, for the same reasons (see DemulLbImport): "Force using MAME metadata" ticked once per wizard, the MAME page's
// "Skip ..." boxes unticked - ZiNc's games are often MAME's CLONES (sfexu, tekken3je1) - and "Skip games unplayable in MAME"
// hidden, a window while LaunchBox fills the list and while the romsets are read, Back greyed once it is sorted.
//
// WHICH EMULATOR: the wizard's own choice, looked for among its pages. Not ZiNc EX, or not found: nothing is touched. Turned
// off on the Nixx page (ZincExSettings.SortImport), or on the Compatibility page (importWizard).
//
// ONLY INSIDE LAUNCHBOX: Install() returns before touching anything in any other process. Lines are "[import] ..." in
// zincex.log.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.ZincEx
{
    internal static class ZincExLbImport
    {
        private const string WizardType = "WizardViewModel";
        private const string GameListPage = "RomImportGameListViewModel";
        private const string OptionsPage = "RomImportCustomOptionsViewModel";
        private const string MameOptionsPage = "RomImportMameOptionsViewModel";

        /// <summary>For the probe: no window.</summary>
        internal static bool Quiet;

        private static bool _installed, _mameDone, _mameChoicesDone, _dumped;
        private static object _filteredList;
        private static readonly List<object> _seenPages = new List<object>();

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "LaunchBox", StringComparison.OrdinalIgnoreCase))
                    return;
                if (!LbIntegrations.Compat.Compat.On("importWizard")) return;
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
                { IsBackground = true, Name = "ZiNc EX import wizard" };
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
                _filteredList = null;
                _seenPages.Clear();
                _dumped = _mameDone = _mameChoicesDone = false;
                if (window.DataContext is INotifyPropertyChanged notify)
                    notify.PropertyChanged += (s, a) => PageShown(window, s);
            }
            catch (Exception ex) { Log.Warn("[import] wizard opened", ex); }
        }

        private static string ToZincEx(object wizard)
        {
            var exe = LbIntegrations.Lbip.LbipImportWatch.Full(ChosenEmulator(wizard, out _));
            return exe != null && ZincExLaunch.IsZincEx(exe) ? exe : null;
        }

        private static void PageShown(System.Windows.Window window, object wizard)
        {
            try
            {
                var page = CurrentPage(wizard);
                var name = page?.GetType().Name;
                if (page != null && !_seenPages.Any(p => ReferenceEquals(p, page))) _seenPages.Add(page);
                if (name == OptionsPage) { MameMetadata(wizard, page); return; }
                if (name == MameOptionsPage) { MameChoices(window, wizard, page); return; }
                if (name != GameListPage || ReferenceEquals(_filteredList, page)) return;
                _filteredList = page;

                var exe = LbIntegrations.Lbip.LbipImportWatch.Full(ChosenEmulator(wizard, out var where));
                if (exe == null || !ZincExLaunch.IsZincEx(exe))
                {
                    if (exe == null) Log.Verbose("[import] no chosen emulator was found (" + where + ") - the list is left as it is");
                    return;
                }
                if (!ZincExSettings.SortImport) { Log.Info("[import] to ZiNc EX: sorting turned off in the Nixx window - the list is left as it is"); return; }
                Log.Info("[import] to ZiNc EX (" + exe + ", found as " + where + ")");
                WhenFilled(window, page, exe);
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        /// <summary>"Force using MAME metadata" ticked, once, for an import to ZiNc EX: its games are MAME's sets.</summary>
        private static void MameMetadata(object wizard, object page)
        {
            if (_mameDone || ToZincEx(wizard) == null || !ZincExSettings.SortImport) return;
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
                Log.Info("[import] to ZiNc EX: " + mame[0].Name + " ticked (MAME's names for MAME sets) - the user may untick it");
            }
            catch (Exception ex) { Log.Warn("[import] could not tick " + mame[0].Name, ex); }
        }

        /// <summary>Every "Skip ..." checkbox of the MAME page unticked - ZiNc's games are often MAME's clones -, "Skip games
        /// unplayable in MAME" hidden and kept false. Once per wizard. See DemulLbImport.MameChoices.</summary>
        private static void MameChoices(System.Windows.Window window, object wizard, object page)
        {
            if (_mameChoicesDone || ToZincEx(wizard) == null || !ZincExSettings.SortImport) return;
            _mameChoicesDone = true;
            var all = page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                          .Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType == typeof(bool) && p.CanWrite).ToList();
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
                        var p = all.FirstOrDefault(x => x.Name == path);
                        if (p == null) continue;
                        try { if (p.GetValue(page) is bool on && on) { p.SetValue(page, false); unticked.Add(p.Name); } }
                        catch (Exception ex) { Log.Warn("[import] could not untick " + p.Name, ex); }
                        if (path == "SkipUnplayable") box.Visibility = System.Windows.Visibility.Collapsed;
                    }
                    Log.Info("[import] to ZiNc EX: " + boxes.Count + " Skip box(es) on the page, " + (unticked.Count == 0 ? "none was ticked" : "unticked " + string.Join(", ", unticked)));
                }
                catch (Exception ex) { Log.Warn("[import] could not set the MAME page's boxes", ex); }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            var unplayable = all.FirstOrDefault(x => x.Name == "SkipUnplayable");
            if (unplayable != null && page is INotifyPropertyChanged watched)
                watched.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName != "SkipUnplayable" && !string.IsNullOrEmpty(a.PropertyName)) return;
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { if (unplayable.GetValue(page) is bool on && on) unplayable.SetValue(page, false); }
                        catch { }
                    }));
                };
        }

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

        /// <summary>The sort, once the list is complete (LbipListFill). Until then a window says LaunchBox is
        /// listing the files; then the romsets are read off the window's thread, the window saying how far, and the list sorted
        /// on it.</summary>
        private static void WhenFilled(System.Windows.Window window, object gameList, string exe)
        {
            var waiting = Waiting(new WindowOwner(window));
            LbIntegrations.Lbip.LbipListFill.Watch(window, gameList,
                n => waiting?.Say("LaunchBox is listing the files: " + n + " so far..."),
                (n, took, again) =>
                {
                    Log.Info(again ? "[import] LaunchBox added more lines after the sort: " + n + " now - sorted again"
                                   : "[import] LaunchBox listed " + n + " file(s) in " + took.TotalSeconds.ToString("0.0") + " s");
                    // The first sort carries on in the listing window; a later one gets its own.
                    var w = again ? Waiting(new WindowOwner(window)) : waiting;
                    try { Sort(window, gameList, exe, w); }
                    catch (Exception ex) { w?.Close(); Log.Warn("[import] could not sort the list", ex); }
                });
        }

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
                    Text = ZincExPlugin.PackName + " - sorting the games", StartPosition = FormStartPosition.Manual,
                    FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ControlBox = false,
                    ShowInTaskbar = false, ClientSize = new Size(460, 80), Font = new Font("Segoe UI", 9f),
                };
                var label = new Label { Location = new Point(14, 14), Size = new Size(432, 20), AutoEllipsis = true, Text = "LaunchBox is listing the files..." };
                var bar = new ProgressBar { Location = new Point(14, 42), Size = new Size(432, 18), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
                form.Controls.AddRange(new Control[] { label, bar });
                form.Show(owner);
                if (GetWindowRect(owner.Handle, out var w))
                    form.Location = new Point(w.Left + (w.Right - w.Left - form.Width) / 2, w.Top + (w.Bottom - w.Top - form.Height) / 2);
                return new WaitingWindow { Form = form, Label = label };
            }
            catch (Exception ex) { Log.Warn("[import] no waiting window", ex); return null; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        // ── the sort ─────────────────────────────────────────────────────────

        internal sealed class Finding
        {
            public object Record;
            public string Path, Drop;
            public ZincGames.Match Match;
        }

        /// <summary>The romsets read off the window's thread, then the list sorted on it.</summary>
        private static void Sort(System.Windows.Window window, object gameList, string exe, WaitingWindow waiting)
        {
            var list = GetProperty(gameList, "Games") as IList;
            if (list == null || list.Count == 0) { waiting?.Close(); return; }
            var findings = list.Cast<object>().Select(r => new Finding { Record = r, Path = LbIntegrations.Lbip.LbipImportWatch.Full(GetProperty(r, "ApplicationPath") as string) ?? "" }).ToList();
            int done = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var progress = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
            progress.Tick += (_, _) => waiting?.Say("Reading the romsets: " + Volatile.Read(ref done) + " of " + findings.Count + "...");
            progress.Start();
            Task.Run(() =>
            {
                try { Judge(findings, exe, () => Interlocked.Increment(ref done)); }
                catch (Exception ex) { Log.Warn("[import] could not read the romsets", ex); findings = null; }
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    progress.Stop();
                    waiting?.Close();
                    if (findings == null) return;
                    Log.Info("[import] " + findings.Count + " file(s) read in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
                    try { if (Apply(gameList, list, findings)) GreyBack(window); }
                    catch (Exception ex) { Log.Warn("[import] could not sort the list", ex); }
                }));
            });
        }

        /// <summary>The verdict on every line: the ZiNc game it is, or why it leaves; then one romset kept per ZiNc game.</summary>
        internal static void Judge(List<Finding> findings, string exe, Action tick = null)
        {
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            var folders = new System.Collections.Concurrent.ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(findings, new ParallelOptions { MaxDegreeOfParallelism = 4 }, f =>
            {
                try
                {
                    if (!f.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { f.Drop = "not a zip"; return; }
                    var dir = Path.GetDirectoryName(f.Path) ?? "";
                    f.Match = ZincGames.Identify(f.Path, folders.GetOrAdd(dir, _ => ZincExLaunch.Folders(exeDir, f.Path)), out var miss);
                    if (f.Match == null)
                        f.Drop = miss?.Reason?.StartsWith(ZincGames.BiosReason) == true ? "a BIOS set"
                               : miss?.Reason?.StartsWith(ZincGames.BoardReason) == true ? "a game of a ZN board that ZiNc 1.1 does not run"
                               : "none of ZiNc's games";
                }
                catch (Exception ex) { f.Drop = "could not be read (" + ex.Message + ")"; }
                finally { tick?.Invoke(); }
            });
            // One romset per ZiNc game: the one named as ZiNc names it, else the first of the list.
            foreach (var g in findings.Where(f => f.Match != null).GroupBy(f => f.Match.Game.Set, StringComparer.OrdinalIgnoreCase))
            {
                var keep = g.FirstOrDefault(f => f.Match.ByName) ?? g.First();
                foreach (var f in g.Where(f => !ReferenceEquals(f, keep)))
                    f.Drop = "another romset of ZiNc's " + g.Key + " (" + System.IO.Path.GetFileName(keep.Path) + " is kept)";
            }
        }

        /// <summary>The list sorted, in one go. True when it was changed - a line out, a title set.</summary>
        private static bool Apply(object gameList, IList list, List<Finding> findings)
        {
            int kept = 0, dropped = 0, titled = 0;
            bool silenced = SetProperty(list, "IsNotifying", false);
            try
            {
                // By reference, not by place (Codex review 17/10): another pass may have shifted the list since the snapshot.
                kept = LbIntegrations.Lbip.LbipListRows.CountIn(list, findings.Where(f => f.Drop == null).Select(f => f.Record));
                dropped = LbIntegrations.Lbip.LbipListRows.Remove(list, findings.Where(f => f.Drop != null).Select(f => f.Record));
                // A game LaunchBox could not name keeps its file's name ("sfexu"): ZiNc's title for it.
                foreach (var f in findings.Where(x => x.Drop == null && x.Match != null))
                {
                    var title = GetProperty(f.Record, "Title") as string ?? "";
                    if (Plain(title) != Plain(System.IO.Path.GetFileNameWithoutExtension(f.Path))) continue;
                    if (SetProperty(f.Record, "Title", f.Match.Game.Title)) titled++;
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

            var lines = new List<string>();
            foreach (var g in findings.Where(f => f.Drop != null).GroupBy(f => f.Drop.StartsWith("another romset") ? "another romset of a ZiNc game kept" : f.Drop).OrderByDescending(g => g.Count()))
            {
                lines.Add("[import]   " + g.Count() + " removed - " + g.Key);
                if (g.Key != "none of ZiNc's games" && g.Key != "not a zip")
                    foreach (var f in g) lines.Add("[import]     " + System.IO.Path.GetFileName(f.Path) + " - " + f.Drop);
            }
            foreach (var f in findings.Where(x => x.Drop == null && x.Match != null))
                lines.Add("[import]   kept " + System.IO.Path.GetFileName(f.Path) + " - ZiNc's " + f.Match.Game.Set + (f.Match.ByName ? "" : " (told by its ROMs)"));
            if (lines.Count > 0) Log.Info(string.Join(Environment.NewLine, lines));
            Log.Info("[import] " + kept + " game(s) kept, " + dropped + " removed, " + titled + " titled from ZiNc's list");
            return dropped > 0 || titled > 0;
        }

        private static string Plain(string s) => (s ?? "").Replace('_', ' ').Trim().ToLowerInvariant();

        // ── the wizard's objects ─────────────────────────────────────────────

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
            if (!_dumped) { _dumped = true; Log.Verbose("[import] the chosen emulator was not found: " + where); }
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
            try { return wizard.GetType().GetProperty("ActivePageViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(wizard); }
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
