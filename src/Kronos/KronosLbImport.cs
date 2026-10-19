// LaunchBox's "Import ROM Files" wizard, its last page's game list sorted for Kronos's ST-V games - the Demul plugin's
// DemulLbImport (itself the Flycast plugin's FlycastLbImport), taken over for Kronos with Mehdi's rules of 19/10: "the platform
// Sega ST-V, with a filter as for Flycast", and "the platform Arcade accepted too, filtered the same way" - Kronos runs a small
// part of what a MAME folder holds.
//
// IMPORTING TO NIXX-KRONOS for Sega ST-V (the platform picked, or its scrape-as): a file that is not an ST-V set leaves the list -
// another board's set, the BIOS (stvbios.zip), what is no MAME zip; an ST-V set Kronos 2.7.5 does not run (wwshin) is asked
// about, once for all of them, unless the Nixx window says "Kept" or "Left out" (another emulator of the platform may run it).
// FOR ARCADE: only Kronos's ST-V sets stay - every other set leaves, an ST-V set Kronos does not run too.
//
// BY THE FILE'S NAME ALONE, as Kronos goes by it (Cartridge\STVGame) - checked against kronos-stv.tsv (KronosStv); a set whose
// files are wrong is said at its launch (KronosStv.ZipsFor), not here: the CRCs of a whole MAME folder cost minutes.
//
// AS DEMUL'S, for the same reasons: "Force using MAME metadata" ticked once per wizard for Sega ST-V, the MAME page's "Skip ..."
// boxes unticked and "Skip games unplayable in MAME" hidden (an import to Arcade keeps LaunchBox's choices there), a window
// while LaunchBox fills the list, Back greyed once it is sorted, a game LaunchBox could not name titled from MAME's list.
//
// WHICH EMULATOR: the wizard's own choice, looked for among its pages. Not Kronos, or not found: nothing is touched - so this
// and the other plugins' filters never sort the same list.
//
// ONLY INSIDE LAUNCHBOX: Install() returns before touching anything in any other process. Lines are "[import] ..." in
// kronos.log.

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

namespace LbIntegrations.Kronos
{
    internal static class KronosLbImport
    {
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";
        private const string OptionsPage = "RomImportCustomOptionsViewModel";
        private const string MameOptionsPage = "RomImportMameOptionsViewModel";

        /// <summary>For the probe: no question, no window - the ST-V sets Kronos does not run are kept (Sega ST-V).</summary>
        internal static bool Quiet;

        private static bool _installed, _mameDone, _mameChoicesDone, _dumped;
        private static object _platformPage, _filteredList;
        private static readonly List<object> _seenPages = new List<object>();

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            try
            {
                if (!string.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName, "LaunchBox", StringComparison.OrdinalIgnoreCase))
                    return;
                // The Compatibility page's "Import help" (importWizard): off, LaunchBox's wizard as it is.
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
                { IsBackground = true, Name = "Kronos import wizard" };
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
                _dumped = _mameDone = _mameChoicesDone = false;
                if (window.DataContext is INotifyPropertyChanged notify)
                    notify.PropertyChanged += (s, a) => PageShown(window, s);
            }
            catch (Exception ex) { Log.Warn("[import] wizard opened", ex); }
        }

        /// <summary>The two imports sorted: to Sega ST-V, and to the catch-all Arcade.</summary>
        internal const string Stv = "Sega ST-V", AnyArcade = "Arcade";

        private static bool IsArcadeImport()
            => KronosStv.IsArcade(Get(_platformPage, "Platform")) || KronosStv.IsArcade(Get(_platformPage, "ScrapeAs"));

        /// <summary>"Sega ST-V" when the platform picked or scraped as is it, else null.</summary>
        private static string Board()
            => KronosStv.IsStv(Get(_platformPage, "Platform")) || KronosStv.IsStv(Get(_platformPage, "ScrapeAs")) ? Stv : null;

        private static bool ToKronos(object wizard) { var exe = ChosenEmulator(wizard, out _); return exe != null && IsKronos(exe); }

        private static bool IsKronos(string path)
        {
            try { return string.Equals(Path.GetFileName(path.Trim().Trim('"')), KronosLaunch.ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
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
                _filteredList = page;

                // "Arcade" (Mehdi, 19/10): a MAME folder imported to the catch-all platform keeps Kronos's ST-V sets. Only the list
                // is sorted - the MAME pages keep LaunchBox's choices (Board(): null).
                var board = Board() ?? (IsArcadeImport() ? AnyArcade : null);
                if (board == null) return;
                if (KronosSettings.Value(KronosSettings.ImportSort) != "true") { Log.Info("[import] " + board + " import: sorting turned off in the Nixx window - the list is left as it is"); return; }
                var exe = ChosenEmulator(wizard, out var where);
                if (exe == null || !IsKronos(exe))
                {
                    Log.Info("[import] " + board + " import, but " + (exe == null ? "no chosen emulator was found (" + where + ")" : "the emulator is not Kronos (" + exe + ")")
                             + " - the list is left as it is");
                    return;
                }
                Log.Info("[import] " + board + " import to Kronos (" + exe + ", found as " + where + ")");
                WhenFilled(window, page, board);
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        /// <summary>"Force using MAME metadata" ticked, once, for Sega ST-V imported to Kronos.</summary>
        private static void MameMetadata(object wizard, object page)
        {
            if (_mameDone) return;
            var board = Board();
            if (board == null || !ToKronos(wizard)) return;
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
                Log.Info("[import] " + board + " to Kronos: " + mame[0].Name + " ticked (MAME's names for MAME sets) - the user may untick it");
            }
            catch (Exception ex) { Log.Warn("[import] could not tick " + mame[0].Name, ex); }
        }

        /// <summary>Every "Skip ..." checkbox of the MAME page unticked, "Skip games unplayable in MAME" hidden and kept
        /// false - once per wizard, for Sega ST-V imported to Kronos. See FlycastLbImport.MameChoices.</summary>
        private static void MameChoices(System.Windows.Window window, object wizard, object page)
        {
            if (_mameChoicesDone) return;
            var board = Board();
            if (board == null || !ToKronos(wizard)) return;
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
                    Log.Info("[import] " + board + " to Kronos: " + boxes.Count + " Skip box(es) on the page, "
                             + (unticked.Count == 0 ? "none was ticked" : "unticked " + string.Join(", ", unticked)));
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

        /// <summary>The sort, once the list is complete (LbipListFill). Until then a window says
        /// LaunchBox is listing the files, and how many so far.</summary>
        private static void WhenFilled(System.Windows.Window window, object gameList, string board)
        {
            var waiting = Waiting(new WindowOwner(window));
            LbIntegrations.Lbip.LbipListFill.Watch(window, gameList,
                n => waiting?.Say("LaunchBox is listing the files: " + n + " so far..."),
                (n, took, again) =>
                {
                    waiting?.Close();
                    Log.Info(again ? "[import] LaunchBox added more lines after the sort: " + n + " now - sorted again"
                                   : "[import] LaunchBox listed " + n + " file(s) in " + took.TotalSeconds.ToString("0.0") + " s");
                    try { if (Filter(new WindowOwner(window), gameList, board)) GreyBack(window); }
                    catch (Exception ex) { Log.Warn("[import] could not sort the list", ex); }
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
                    Text = "Nixx-Kronos - sorting the games", StartPosition = FormStartPosition.Manual,
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

        private sealed class Finding
        {
            public object Record;
            public string Path, Drop;
            public KronosStvSet Set;
            /// <summary>An ST-V set Kronos does not run (Sega ST-V import): kept or not on the user's answer.</summary>
            public bool Unknown;
        }

        /// <summary>The verdict on one line, by its name and extension (KronosStv's table).</summary>
        internal static void Judge(string path, string board, Dictionary<string, KronosStvSet> sets, out string drop, out KronosStvSet set, out bool unknown)
        {
            drop = null; set = null; unknown = false;
            var ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (ext != ".zip") { drop = "not a MAME zip (" + (ext.Length > 0 ? ext : "no extension") + ") - Kronos reads ST-V sets as zips"; return; }
            if (string.Equals(name, KronosStv.BiosSet, StringComparison.OrdinalIgnoreCase)) { drop = "the ST-V BIOS, not a game - beside the games, or in Kronos's bios folder"; return; }
            if (!sets.TryGetValue(name, out set)) { drop = board == AnyArcade ? NotStv : UnknownName; return; }
            if (!set.Known)
            {
                if (board == AnyArcade) drop = "an ST-V set Kronos 2.7.5 does not run";
                else unknown = true;
            }
        }

        private const string UnknownName = "not an ST-V set by that name";
        private const string NotStv = "not an ST-V set of Kronos's";

        /// <summary>The list sorted. True when it was changed - a line out, a title set.</summary>
        internal static bool Filter(IWin32Window owner, object gameList, string board)
        {
            var list = GetProperty(gameList, "Games") as IList;
            if (list == null || list.Count == 0) return false;
            var sets = KronosStv.Sets;
            if (sets.Count == 0) { Log.Warn("[import] no set table - the list is left as it is"); return false; }

            var findings = list.Cast<object>().Select(r => new Finding { Record = r, Path = LbIntegrations.Lbip.LbipImportWatch.Full(GetProperty(r, "ApplicationPath") as string) ?? "" }).ToList();
            foreach (var f in findings)
            {
                Judge(f.Path, board, sets, out var drop, out var set, out var unknown);
                f.Drop = drop; f.Set = set; f.Unknown = unknown;
            }

            // AN ST-V SET KRONOS DOES NOT RUN (Sega ST-V): asked, once for all of them - Demul's rule (Mehdi, 06/10).
            var unknowns = findings.Where(f => f.Drop == null && f.Unknown).ToList();
            var policy = KronosSettings.Value(KronosSettings.ImportUnknownSets);
            if (unknowns.Count > 0 && policy != "ask")
            {
                if (policy == "drop") foreach (var f in unknowns) f.Drop = "not in Kronos's list of games (left out, as the Nixx window says)";
                Log.Info("[import] " + unknowns.Count + " ST-V set(s) Kronos does not run: " + (policy == "drop" ? "left out" : "kept") + ", as the Nixx window says");
            }
            else if (unknowns.Count > 0 && !Quiet)
            {
                var answer = MessageBox.Show(owner,
                    unknowns.Count + " ST-V game(s) of this folder are not in Kronos's list of games, so Kronos will not run them:\n\n"
                    + string.Join("\n", unknowns.Take(20).Select(f => System.IO.Path.GetFileName(f.Path) + "  -  " + f.Set.Title))
                    + (unknowns.Count > 20 ? "\n(and " + (unknowns.Count - 20) + " more, in kronos.log)" : "")
                    + "\n\nImport them all the same? (Yes if another emulator of this platform runs them.)",
                    "Nixx-Kronos - import", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.No) foreach (var f in unknowns) f.Drop = "not in Kronos's list of games (left out on the user's answer)";
                Log.Info("[import] " + unknowns.Count + " ST-V set(s) Kronos does not run: " + (answer == DialogResult.No ? "left out" : "kept") + " on the user's answer");
            }

            // IN ONE GO, as Flycast's and Demul's (measured 30/09: line by line, a MAME folder took 27 s and flickered).
            int kept = 0, dropped = 0, titled = 0;
            bool silenced = SetProperty(list, "IsNotifying", false);
            try
            {
                // By reference, not by place (Codex review 17/10): another pass may have shifted the list since the snapshot.
                kept = LbIntegrations.Lbip.LbipListRows.CountIn(list, findings.Where(f => f.Drop == null).Select(f => f.Record));
                dropped = LbIntegrations.Lbip.LbipListRows.Remove(list, findings.Where(f => f.Drop != null).Select(f => f.Record));
                // A game LaunchBox could not name keeps its file's name ("airtrix"): MAME's title for its set.
                foreach (var f in findings.Where(x => x.Drop == null && x.Set?.Title?.Length > 0))
                {
                    var title = GetProperty(f.Record, "Title") as string ?? "";
                    if (Plain(title) != Plain(System.IO.Path.GetFileNameWithoutExtension(f.Path))) continue;
                    if (SetProperty(f.Record, "Title", f.Set.Title)) titled++;
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
            foreach (var g in findings.Where(f => f.Drop != null).GroupBy(f => Reason(f.Drop)).OrderByDescending(g => g.Count()))
            {
                lines.Add("[import]   " + g.Count() + " removed - " + g.Key);
                if (g.Key != UnknownName && g.Key != NotStv)
                    foreach (var f in g) lines.Add("[import]     " + System.IO.Path.GetFileName(f.Path) + " - " + f.Drop);
            }
            if (lines.Count > 0) Log.Info(string.Join(Environment.NewLine, lines));
            Log.Info("[import] " + board + ": " + kept + " game(s) kept, " + dropped + " removed, " + titled + " titled from MAME's names");
            return dropped > 0 || titled > 0;
        }

        private static string Reason(string drop)
        {
            if (drop.StartsWith("not a MAME zip")) return "not a MAME zip";
            return drop;
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
            if (!_dumped) { _dumped = true; Log.Info("[import] the chosen emulator was not found: " + where); }
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
