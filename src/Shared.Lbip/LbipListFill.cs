// When LaunchBox's import wizard has finished filling its game list - for every plugin that sorts that list (Flycast,
// Demul, ZiNc EX, PPSSPP, Vita3K, Xenia, Cxbx).
//
// 500 MS WITHOUT A NEW LINE IS NOT ENOUGH (measured 16/10, LaunchBox 14.1 b3, a MAME folder imported to Naomi): 14.1
// fills the list in batches with pauses between them - 600 lines in 0.8 s, a pause, then the rest up to 15 081. The
// sort ran on the first 600, the rest came in after it untouched. 14.0 filled in one stream, Finish greyed meanwhile.
//
// SO "FILLED" IS: 500 ms without a new line AND nothing saying LaunchBox is still at it - the wizard's Finish button
// greyed, or a true IsBusy / IsLoading on the wizard or the page. Whichever of them exists in this LaunchBox is used,
// and what was found is logged once. Past three minutes of quiet with Finish still greyed, the list is taken as it is
// (a list Finish never allows, an empty one, would otherwise keep the waiting window up for ever).
//
// ONE PASS AT A TIME, LINES TAKEN OUT BY REFERENCE (Codex review 17/10): a pass is never started while the previous one
// is still in its callback (Flycast's check window pumps messages, LaunchBox may add lines meanwhile) - the wait starts
// again instead. A plugin whose sort finishes later (ZiNc EX reads the romsets off the window's thread) can still overlap
// the next one: every sort takes its lines out by reference (LbipListRows.Remove), not by their place in a snapshot, so a pass that
// runs after another has shifted the list still takes all of its own lines out.
//
// AND LINES THAT COME AFTER THE SORT ARE SORTED TOO: the list stays watched until the wizard window closes; a line
// added after the sort starts the same wait again, and the plugin's sort runs again over the whole list - every sort
// here only takes lines out or retitles them, so a second pass leaves the lines kept by the first one as they are.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;

namespace LbIntegrations.Lbip
{
    internal static class LbipListFill
    {
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(3);
        private static readonly string[] BusyNames = { "IsBusy", "IsLoading" };

        /// <param name="listing">every 300 ms while LaunchBox fills the list, its count so far (the waiting window)</param>
        /// <param name="filled">on the window's thread, once the list is complete: the first time with
        /// <c>again</c> false, then once more for each batch LaunchBox adds after it</param>
        public static void Watch(System.Windows.Window window, object gameList,
                                 Action<int> listing, Action<int, TimeSpan, bool> filled)
        {
            object games = null;
            try { games = gameList.GetType().GetProperty("Games")?.GetValue(gameList); } catch { }
            if (!(games is INotifyCollectionChanged changes)) return;
            int Count() => (games as ICollection)?.Count ?? 0;
            var wizard = window.DataContext;

            var since = System.Diagnostics.Stopwatch.StartNew();
            var quietSince = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = Quiet };
            var count = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(300) };
            bool sorted = false, said = false, saidBusy = false, grewSinceSort = false, inPass = false;
            int last = Count();

            count.Tick += (_, _) => { if (!sorted) listing?.Invoke(Count()); };
            if (listing != null) count.Start();

            // Only a list that GROWS is LaunchBox filling it: lines taken out are a sort's - ours, or a plugin's that takes
            // them out later from its own work - and must not start another one. Counted, not read from the event: a
            // BindableCollection's AddRange and our sorts' one-go removal both arrive as Reset.
            NotifyCollectionChangedEventHandler restart = (_, _) =>
            {
                int c = Count();
                bool grew = c > last;
                last = c;
                if (!grew) return;
                if (sorted) grewSinceSort = true;
                quietSince.Restart();
                timer.Stop();
                timer.Start();
            };
            changes.CollectionChanged += restart;

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (inPass) { timer.Start(); return; }       // the previous pass is still in its callback: later
                if (!said) { said = true; LbipLog.Info("[import] list-filled signals here: " + Signals(window, wizard, gameList)); }
                var busy = Busy(window, wizard, gameList);
                if (busy != null && quietSince.Elapsed < GiveUp)
                {
                    if (!saidBusy) { saidBusy = true; LbipLog.Info("[import] " + Count() + " line(s) so far, quiet but LaunchBox is still at it (" + busy + ") - waiting"); }
                    timer.Start();
                    return;
                }
                if (busy != null) LbipLog.Info("[import] still \"" + busy + "\" after " + GiveUp.TotalMinutes + " min of quiet - the list taken as it is");
                saidBusy = false;
                bool again = sorted;
                if (again && !grewSinceSort) return;
                sorted = true;
                grewSinceSort = false;
                count.Stop();
                inPass = true;
                try { filled(Count(), since.Elapsed, again); }
                catch (Exception ex) { LbipLog.Warn("[import] the sort", ex); }
                finally { inPass = false; }
                last = Count();
            };

            EventHandler closed = null;
            closed = (_, _) =>
            {
                window.Closed -= closed;
                changes.CollectionChanged -= restart;
                timer.Stop();
                count.Stop();
            };
            window.Closed += closed;
            timer.Start();
        }

        /// <summary>What says LaunchBox is still filling, or null.</summary>
        private static string Busy(System.Windows.Window window, object wizard, object page)
        {
            foreach (var o in new[] { wizard, page })
                foreach (var n in BusyNames)
                    if (Bool(o, n) == true) return o.GetType().Name + "." + n;
            var finish = Finish(window);
            if (finish != null && !finish.IsEnabled) return "Finish greyed";
            return null;
        }

        private static string Signals(System.Windows.Window window, object wizard, object page)
        {
            var found = new List<string>();
            foreach (var o in new[] { wizard, page })
                foreach (var n in BusyNames)
                {
                    var v = Bool(o, n);
                    if (v != null) found.Add(o.GetType().Name + "." + n + "=" + v);
                }
            var finish = Finish(window);
            found.Add(finish == null ? "no Finish button found" : "Finish " + (finish.IsEnabled ? "enabled" : "greyed"));
            return string.Join(", ", found);
        }

        private static bool? Bool(object o, string name)
        {
            try { return o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o) as bool?; }
            catch { return null; }
        }

        private static System.Windows.Controls.Primitives.ButtonBase Finish(System.Windows.Window window) =>
            Descendants<System.Windows.Controls.Primitives.ButtonBase>(window)
                .FirstOrDefault(b => b.IsVisible && (b.Name == "Finish" || string.Equals((b.Content as string)?.Trim(), "Finish", StringComparison.OrdinalIgnoreCase)));

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
    }
}
