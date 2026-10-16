// LaunchBox's "Import ROM Files" wizard, its last page's game list sorted for Demul - the Flycast plugin's
// FlycastLbImport, taken over for Demul's boards with Mehdi's rules of 06/10:
//
// SORTED BY BOARD, NOT BY WHAT DEMUL RUNS. Importing to Nixx-Demul for one of its platforms (the platform
// picked, or its scrape-as), a romset of ANOTHER board leaves the list - a whole MAME folder is what gets
// imported for Naomi - and so do the BIOS sets and what is no romset at all. A set of THE RIGHT board that
// Demul does not know is not dropped on that account: Demul will rarely be the only emulator of a platform,
// so the user is asked, once for all of them, whether to keep them.
//
// BY THE FILE'S NAME ALONE: the one thing Demul goes by ("-rom=<romname>"), checked against demul-sets.tsv
// (DemulSets). No CRC check: it costs minutes on a MAME folder, too much to run by default (Mehdi, 06/10).
// A Dreamcast import keeps the discs Demul reads (.gdi, .cdi, .chd).
//
// AS FLYCAST'S, for the same reasons (see FlycastLbImport): "Force using MAME metadata" ticked once per
// wizard for an arcade platform, the MAME page's "Skip ..." boxes unticked and "Skip games unplayable in
// MAME" hidden, a window while LaunchBox fills the list, Back greyed once it is sorted, a game LaunchBox could
// not name titled from MAME's list.
//
// WHICH EMULATOR: the wizard's own choice, looked for among its pages. Not Demul, or not found: nothing is
// touched - so this and the Flycast plugin's never sort the same list.
//
// ONLY INSIDE LAUNCHBOX: Install() returns before touching anything in any other process. Lines are
// "[import] ..." in demul.log.

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

namespace LbIntegrations.Demul
{
    internal static class DemulLbImport
    {
        private const string WizardType = "WizardViewModel";
        private const string PlatformPage = "ImportWizardPlatformSelectViewModel";
        private const string GameListPage = "RomImportGameListViewModel";
        private const string OptionsPage = "RomImportCustomOptionsViewModel";
        private const string MameOptionsPage = "RomImportMameOptionsViewModel";

        private static readonly string[] Discs = { ".gdi", ".cdi", ".chd" };
        private static readonly string[] Archives = { ".zip", ".7z" };

        /// <summary>For the probe: no question, no window - the unknown sets of the right board are kept.</summary>
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
                { IsBackground = true, Name = "Demul import wizard" };
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

        /// <summary>The import's "board" for the catch-all arcade platform: any arcade board of Demul's.</summary>
        internal const string AnyArcade = "Arcade";

        private static bool IsArcadeImport()
            => DemulPlatforms.IsArcadeCatchAll(Get(_platformPage, "Platform")) || DemulPlatforms.IsArcadeCatchAll(Get(_platformPage, "ScrapeAs"));

        /// <summary>The board of the platform picked or scraped as, or null.</summary>
        private static string Board()
            => DemulPlatforms.BoardOf(Get(_platformPage, "Platform")) ?? DemulPlatforms.BoardOf(Get(_platformPage, "ScrapeAs"));

        private static bool ToDemul(object wizard) { var exe = ChosenEmulator(wizard, out _); return exe != null && IsDemul(exe); }

        private static bool IsDemul(string path)
        {
            try { return string.Equals(Path.GetFileName(path.Trim().Trim('"')), "demul.exe", StringComparison.OrdinalIgnoreCase); }
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

                // "Arcade" (Mehdi, 17/10): a MAME folder imported to the catch-all platform keeps the sets of every board Demul
                // runs. Only the list is sorted - the MAME pages keep LaunchBox's choices (Board(): null).
                var board = Board() ?? (IsArcadeImport() ? AnyArcade : null);
                if (board == null) return;
                if (!DemulSettings.SortImport(board)) { Log.Info("[import] " + board + " import: sorting turned off in the Nixx window - the list is left as it is"); return; }
                var exe = ChosenEmulator(wizard, out var where);
                if (exe == null || !IsDemul(exe))
                {
                    Log.Info("[import] " + board + " import, but " + (exe == null ? "no chosen emulator was found (" + where + ")" : "the emulator is not Demul (" + exe + ")")
                             + " - the list is left as it is");
                    return;
                }
                Log.Info("[import] " + board + " import to Demul (" + exe + ", found as " + where + ")");
                WhenFilled(window, page, board);
            }
            catch (Exception ex) { Log.Warn("[import] page change", ex); }
        }

        /// <summary>"Force using MAME metadata" ticked, once, for an arcade platform imported to Demul.</summary>
        private static void MameMetadata(object wizard, object page)
        {
            if (_mameDone) return;
            var board = Board();
            if (board == null || board == "Dreamcast" || !ToDemul(wizard)) return;
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
                Log.Info("[import] " + board + " to Demul: " + mame[0].Name + " ticked (MAME's names for MAME sets) - the user may untick it");
            }
            catch (Exception ex) { Log.Warn("[import] could not tick " + mame[0].Name, ex); }
        }

        /// <summary>Every "Skip ..." checkbox of the MAME page unticked, "Skip games unplayable in MAME" hidden and kept
        /// false - once per wizard, for an arcade platform imported to Demul. See FlycastLbImport.MameChoices.</summary>
        private static void MameChoices(System.Windows.Window window, object wizard, object page)
        {
            if (_mameChoicesDone) return;
            var board = Board();
            if (board == null || board == "Dreamcast" || !ToDemul(wizard)) return;
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
                    Log.Info("[import] " + board + " to Demul: " + boxes.Count + " Skip box(es) on the page, "
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
                    Text = "Nixx-Demul - sorting the games", StartPosition = FormStartPosition.Manual,
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
            public DemulSet Set;
            /// <summary>A set of the right board that Demul does not know: kept or not on the user's answer.</summary>
            public bool Unknown;
        }

        /// <summary>The verdict on one line, by its name or extension.</summary>
        internal static void Judge(string path, string board, Dictionary<string, DemulSet> sets, out string drop, out DemulSet set, out bool unknown)
        {
            drop = null; set = null; unknown = false;
            var ext = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (board == "Dreamcast")
            {
                if (!Discs.Contains(ext)) drop = "not a Dreamcast disc Demul reads (" + (ext.Length > 0 ? ext : "no extension") + ")";
                return;
            }
            if (!Archives.Contains(ext)) { drop = "not an arcade set (" + (ext.Length > 0 ? ext : "no extension") + ")"; return; }
            if (!sets.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(path), out set)) { drop = UnknownName; return; }
            if (set.Board == DemulSets.Bios) { drop = "a BIOS, not a game - it goes in Demul's roms folder"; return; }
            if (board != AnyArcade && !string.Equals(set.Board, board, StringComparison.OrdinalIgnoreCase))
            { drop = (set.Board.StartsWith("A") ? "an " : "a ") + set.Board + " set, not " + board; return; }
            unknown = !set.Known;
        }

        private const string UnknownName = "not a set of Demul's boards by that name";

        /// <summary>The list sorted. True when it was changed - a line out, a title set.</summary>
        internal static bool Filter(IWin32Window owner, object gameList, string board)
        {
            var list = GetProperty(gameList, "Games") as IList;
            if (list == null || list.Count == 0) return false;
            var sets = DemulSets.All;
            if (board != "Dreamcast" && sets.Count == 0) { Log.Warn("[import] no set table - the list is left as it is"); return false; }

            var findings = list.Cast<object>().Select(r => new Finding { Record = r, Path = LbIntegrations.Lbip.LbipImportWatch.Full(GetProperty(r, "ApplicationPath") as string) ?? "" }).ToList();
            foreach (var f in findings)
            {
                Judge(f.Path, board, sets, out var drop, out var set, out var unknown);
                f.Drop = drop; f.Set = set; f.Unknown = unknown;
            }

            // THE RIGHT BOARD, UNKNOWN TO DEMUL: asked, once for all of them (Mehdi, 06/10).
            var unknowns = findings.Where(f => f.Drop == null && f.Unknown).ToList();
            var policy = DemulSettings.UnknownSets;
            if (unknowns.Count > 0 && policy != "ask")
            {
                if (policy == "drop") foreach (var f in unknowns) f.Drop = "not in Demul's list of games (left out, as the Nixx window says)";
                Log.Info("[import] " + unknowns.Count + " set(s) of the board unknown to Demul: " + (policy == "drop" ? "left out" : "kept") + ", as the Nixx window says");
            }
            else if (unknowns.Count > 0 && !Quiet)
            {
                var answer = MessageBox.Show(owner,
                    unknowns.Count + " " + board + " game(s) of this folder are not in Demul's list of games, so Demul will most likely not run them:\n\n"
                    + string.Join("\n", unknowns.Take(20).Select(f => System.IO.Path.GetFileName(f.Path) + "  -  " + f.Set.Title))
                    + (unknowns.Count > 20 ? "\n(and " + (unknowns.Count - 20) + " more, in demul.log)" : "")
                    + "\n\nImport them all the same? (Yes if another emulator of this platform runs them.)",
                    "Nixx-Demul - import", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.No) foreach (var f in unknowns) f.Drop = "not in Demul's list of games (left out on the user's answer)";
                Log.Info("[import] " + unknowns.Count + " set(s) of the board unknown to Demul: " + (answer == DialogResult.No ? "left out" : "kept") + " on the user's answer");
            }

            // IN ONE GO, as Flycast's (measured 30/09: line by line, a MAME folder took 27 s and flickered).
            int kept = 0, dropped = 0, titled = 0;
            bool silenced = SetProperty(list, "IsNotifying", false);
            try
            {
                for (int i = findings.Count - 1; i >= 0; i--)
                {
                    if (i >= list.Count || !ReferenceEquals(list[i], findings[i].Record)) continue;
                    if (findings[i].Drop == null) { kept++; continue; }
                    list.RemoveAt(i);
                    dropped++;
                }
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
                if (g.Key != UnknownName)
                    foreach (var f in g) lines.Add("[import]     " + System.IO.Path.GetFileName(f.Path) + " - " + f.Drop);
            }
            if (lines.Count > 0) Log.Info(string.Join(Environment.NewLine, lines));
            Log.Info("[import] " + board + ": " + kept + " game(s) kept, " + dropped + " removed, " + titled + " titled from MAME's names");
            return dropped > 0 || titled > 0;
        }

        private static string Reason(string drop)
        {
            if (drop.StartsWith("not an arcade set") || drop.StartsWith("not a Dreamcast disc")) return drop.Substring(0, drop.IndexOf(" (") > 0 ? drop.IndexOf(" (") : drop.Length);
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
