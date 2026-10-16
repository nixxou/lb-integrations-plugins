// LAUNCHBOX (desktop): the "Launch With" submenu of a game's right-click menu.
//
// Same arrangement as litebox-parental's AdminGuard: a WPF class handler on MenuItem.SubmenuOpened fires for
// every submenu LaunchBox opens, whoever built it, so there is no protected builder to find and nothing to
// patch. The submenu's children are WPF MenuItems whose Header LaunchBox set to the emulator's title.
//
// Measured (16/10, LaunchBox 14.0.1): the desktop builds this submenu WITHOUT menu-action objects. Each line's
// Click handler is a compiler closure of ContentViewModelBase (<>c__DisplayClass96_3) holding the Emulator in
// a field; an emulator with several command lines (RetroArch and its cores) is a container whose children's
// closures (<>c__DisplayClass96_4) reach the same <>c__DisplayClass96_3. The closure names are build-local,
// so they are not relied on: Reach just walks from the Click handler to the first Emulator.
//
// A line is ours when an emulator is reachable from it AND it sits in the "Launch With" submenu, recognised by
// LaunchBox's own localized label (LaunchWithLabel, read from its resx like AdminGuard does), so it works in
// every language and never touches "Open <emulator>" or any other line that happens to hold an emulator.
//
// The game is taken from the closure when one is met on the way, else from the selection (read when the
// submenu opens - the right-click has selected the game by then). LaunchBox rebuilds the submenu on every
// open (measured: new MenuItems each time), so only MenuItem.Header changes and nothing needs undoing.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Menus.LaunchWith
{
    internal static class LaunchBoxMenus
    {
        private sealed class Seen { public string Original; public Link Link; public object Tip; public bool TipSeen; }
        private static readonly ConditionalWeakTable<MenuItem, Seen> Items = new ConditionalWeakTable<MenuItem, Seen>();

        public static void Install()
        {
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
            Log.Info("LaunchBox: listening to MenuItem.SubmenuOpened");
        }

        private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
        {
            // SubmenuOpened bubbles: handle the submenu that opened, not every ancestor it passes through.
            if (!ReferenceEquals(sender, e.OriginalSource) || !(sender is MenuItem menu)) return;
            try
            {
                Labels.Refresh();
                int labelled = 0, lines = 0;
                foreach (var child in menu.Items.OfType<MenuItem>())
                {
                    var seen = Items.GetValue(child, Inspect);
                    if (seen.Link?.Emulator == null) continue;
                    lines++;
                    if (Apply(child, seen)) labelled++;
                }
                if (lines > 0)
                    Log.Info("submenu \"" + menu.Header + "\": " + lines + " emulator line(s), " + labelled + " labelled");
            }
            catch (Exception ex) { Log.Warn("SubmenuOpened", ex); }
        }

        /// <summary>First sight of a MenuItem: its original header and what it launches. Logged - this is
        /// the probe pass, the evidence of how LaunchBox ties a line to its emulator.</summary>
        private static Seen Inspect(MenuItem mi)
        {
            var seen = new Seen { Original = mi.Header as string };
            var link = Reach.FromMenuItem(mi);
            if (link.Emulator == null && mi.HasItems)
            {
                // A container (an emulator with several command lines): its children's emulator, if they share one.
                var inner = mi.Items.OfType<MenuItem>().Select(Reach.FromMenuItem).Where(l => l.Emulator != null).ToList();
                if (inner.Count > 0 && inner.All(l => ReferenceEquals(l.Emulator, inner[0].Emulator)))
                {
                    link = inner[0];
                    link.Path = "submenu[" + inner.Count + "] " + link.Path;
                }
            }
            bool launchWith = (link.ActionName ?? "").Contains("LaunchWith") || LaunchWithLabel.Matches((mi.Parent as MenuItem)?.Header as string);
            if (link.Emulator != null && !launchWith)
            {
                Log.Info("  not a Launch With line, left alone: \"" + seen.Original + "\" action=" + (link.ActionName ?? "none")
                         + " emulator \"" + Safe(() => link.Emulator.Title) + "\" via " + link.Path
                         + " | handlers: " + string.Join(", ", Reach.ClickHandlers(mi).Select(d => Reach.Short(d.Method.DeclaringType) + "." + d.Method.Name))
                         + " | Command=" + Reach.Short(mi.Command?.GetType()) + " Tag=" + Reach.Short(mi.Tag?.GetType())
                         + " in \"" + (mi.Parent as MenuItem)?.Header + "\"");
                link = new Link();
            }
            seen.Link = link;
            if (link.Emulator != null)
                Log.Info("  line \"" + seen.Original + "\" -> emulator \"" + Safe(() => link.Emulator.Title) + "\" game \""
                         + Safe(() => link.Game?.Title) + "\" action=" + link.ActionName + "  via " + link.Path);
            if (seen.Original == null && link.Emulator != null)
                Log.Info("  its Header is a " + Reach.Short(mi.Header?.GetType()) + ", not text: left alone");
            return seen;
        }

        private static bool Apply(MenuItem mi, Seen seen)
        {
            if (seen.Original == null) return false;
            var link = seen.Link;
            var game = link.Game ?? SelectedGame();
            var verdict = Labels.For(game, link.Emulator);
            var raw = verdict?.Text;
            var badge = Brush(verdict?.Color);
            Tip(mi, seen, verdict?.Note);
            if (raw != null && badge != null)
            {
                mi.Header = Badged(seen.Original, raw, badge, Brush(Labels.BadgeText) ?? Brushes.Black);
                return true;
            }
            var wanted = WithLabel(seen.Original, raw == null ? null : Labels.Format.Replace("{label}", raw));
            if (!Equals(mi.Header, wanted)) mi.Header = wanted;
            return raw != null;
        }

        /// <summary>The line's tooltip: the plugin's note on the game (the list's comments, what is missing) - wrapped, shown
        /// long enough to read; LaunchBox's own tooltip back when there is none.</summary>
        private static void Tip(MenuItem mi, Seen seen, string note)
        {
            if (!seen.TipSeen) { seen.Tip = mi.ToolTip; seen.TipSeen = true; }
            if (string.IsNullOrEmpty(note)) { if (!ReferenceEquals(mi.ToolTip, seen.Tip)) mi.ToolTip = seen.Tip; return; }
            if (mi.ToolTip is ToolTip own && own.Tag as string == note) return;
            mi.ToolTip = new ToolTip
            {
                Tag = note,
                Content = new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
            };
            ToolTipService.SetShowDuration(mi, 60000);
            ToolTipService.SetInitialShowDelay(mi, 300);
        }

        /// <summary>"Nixx-bsnes" [Perfect] "...": the name as LaunchBox wrote it (AccessText keeps its access
        /// key), a rounded badge in the label's colour, then LaunchBox's trailing ellipsis. The badge colours are
        /// light and its text dark, measured against LaunchBox's dark menu (README, "Badge colours").</summary>
        private static object Badged(string original, string label, Brush background, Brush text)
        {
            var core = original.TrimEnd('.', '…');
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new AccessText { Text = core, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = label, Foreground = text, FontWeight = FontWeights.SemiBold },
            });
            if (core.Length < original.Length)
                panel.Children.Add(new TextBlock { Text = original.Substring(core.Length), VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private static Brush Brush(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) return null;
            try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
            catch { Log.Info("not a colour: \"" + color + "\""); return null; }
        }

        /// <summary>"Nixx-bsnes..." + "  [Perfect]" -> "Nixx-bsnes  [Perfect]...": the label goes before
        /// LaunchBox's trailing ellipsis, which says the line opens something.</summary>
        public static string WithLabel(string original, string label)
        {
            if (string.IsNullOrEmpty(label)) return original;
            var core = original.TrimEnd('.', '…');
            return core + label + original.Substring(core.Length);
        }

        private static IGame SelectedGame()
        {
            try { var g = PluginHelper.StateManager?.GetAllSelectedGames(); return g != null && g.Length == 1 ? g[0] : null; }
            catch { return null; }
        }

        private static string Safe(Func<string> f) { try { return f() ?? "null"; } catch { return "?"; } }
    }
}
