// BIG BOX: the "Launch With" popup.
//
// Measured with the Big Box probe (src\BbProbe, 16/10):
//   - "Launch With" is SelectEmulatorMenuAction.OnEnter building a SelectOptionPopupViewModel<Emulator>; its
//     Items are PopupOption objects holding Value (the emulator) and DisplayString (the line's text). Every
//     theme draws it with a ListBox bound to Items and a TextBlock bound to DisplayString (Default and Old
//     Default ship SelectOptionPopupView.xaml; CriticalZoneV2 falls back to the stock one).
//   - The same popup type serves other lists (Additional Versions, themes, views...): only T = Emulator is ours.
//   - The game is LAUNCHED from inside the popup, synchronously, while it is still on screen
//     (PopupOption.OnEnter -> SelectEmulatorMenuAction -> MainViewModel.PlayGame -> OnBeforeGameLaunching).
//     That is why the emulator itself must never carry a label: here only DisplayString changes, a string
//     nothing reads but the TextBlock, and the launch sees the emulator untouched.
//
// DisplayString is get-only, backed by a readonly field with an obfuscated name: the field is found by a
// sentinel (the string field that, once set, the getter returns) and written; the ListBox is then asked to
// redraw, because PopupOption does not notify. No Harmony: a class handler on Loaded sees the popup's ListBox.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.LaunchWithLabels
{
    internal static class BigBoxPopup
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly ConcurrentDictionary<Type, FieldInfo> DisplayFields = new ConcurrentDictionary<Type, FieldInfo>();
        private static readonly ConditionalWeakTable<object, string> Originals = new ConditionalWeakTable<object, string>();

        public static void Install()
        {
            EventManager.RegisterClassHandler(typeof(ItemsControl), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
            Log.Info("Big Box: listening to ItemsControl.Loaded");
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is ItemsControl ic) || ic.ItemsSource == null) return;
            var dc = ic.DataContext;
            if (!IsEmulatorPopup(dc)) return;
            try
            {
                LaunchWithLabelsPlugin.Table.Refresh();
                int lines = 0, labelled = 0;
                foreach (var option in ((IEnumerable)ic.ItemsSource).Cast<object>().ToList())
                {
                    var emulator = option.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(option) as IEmulator;
                    var display = option.GetType().GetProperty("DisplayString", BindingFlags.Instance | BindingFlags.Public);
                    if (emulator == null || display == null || display.PropertyType != typeof(string)) continue;
                    lines++;

                    var original = Originals.GetValue(option, o => (string)display.GetValue(o));
                    var link = Reach.FromObject("option", option);   // its callback -> SelectEmulatorMenuAction -> Game
                    var game = link.Game ?? SelectedGame();
                    if (lines == 1)
                        Log.Info("popup \"" + Safe(() => dc.GetType().GetProperty("PopupTitle")?.GetValue(dc) as string) + "\" game \""
                                 + Safe(() => game?.Title) + "\" (from " + (link.Game != null ? link.ActionName : "the selection") + ")");

                    var label = LaunchWithLabelsPlugin.Table.LabelFor(game, emulator, Safe(() => emulator.Title));
                    var wanted = string.IsNullOrEmpty(label) ? original : original + label;
                    if (Write(option, display, wanted) && label != null) labelled++;
                }
                if (lines > 0)
                {
                    Log.Info("  " + lines + " emulator line(s), " + labelled + " labelled");
                    ic.Items.Refresh();
                }
            }
            catch (Exception ex) { Log.Warn("Big Box popup", ex); }
        }

        private static bool IsEmulatorPopup(object dc)
        {
            var t = dc?.GetType();
            return t != null && t.IsGenericType && t.Name.StartsWith("SelectOptionPopupViewModel", StringComparison.Ordinal)
                   && typeof(IEmulator).IsAssignableFrom(t.GetGenericArguments()[0]);
        }

        private static bool Write(object option, PropertyInfo display, string value)
        {
            var f = DisplayFields.GetOrAdd(option.GetType(), _ => FindField(option, display));
            if (f == null) return false;
            if (!Equals(f.GetValue(option), value)) f.SetValue(option, value);
            return true;
        }

        /// <summary>The string field the getter returns, proven with a sentinel and put back at once.</summary>
        private static FieldInfo FindField(object item, PropertyInfo p)
        {
            for (var t = item.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(All | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(string)) continue;
                    var keep = f.GetValue(item);
                    var sentinel = "lwl-probe-" + Guid.NewGuid().ToString("N");
                    string seen;
                    try { f.SetValue(item, sentinel); seen = p.GetValue(item) as string; }
                    finally { f.SetValue(item, keep); }
                    if (seen == sentinel)
                    {
                        Log.Info("DisplayString is backed by " + Reach.Short(t) + "." + f.Name);
                        return f;
                    }
                }
            }
            Log.Info("no field behind " + Reach.Short(item.GetType()) + ".DisplayString: popup left alone");
            return null;
        }

        private static IGame SelectedGame()
        {
            try { var g = PluginHelper.StateManager?.GetAllSelectedGames(); return g != null && g.Length == 1 ? g[0] : null; }
            catch { return null; }
        }

        private static string Safe(Func<string> f) { try { return f() ?? "null"; } catch { return "?"; } }
    }
}
