// The probe's two listening posts, both on code that is NOT protected:
//
//  1. Caliburn.Micro.PropertyChangedBase..ctor (non-generic): who builds each Big Box menu view model -
//     the frames that called it (LaunchBox's names are intact even where its bodies are protected).
//     Never a generic collection method: shared generic code patched by Harmony broke WPF at start-up.
//
//  2. FrameworkElement.Loaded / Unloaded, as class handlers (no patch): the view of each Big Box view
//     model appearing and going away - whatever theme drew it, since the view model is Big Box's - and
//     every list an ItemsControl shows, scanned for the items that carry an emulator.
//
// And one experiment, display only: an item that carries an emulator gets "  [sonde]" appended to its
// text through its public setter. Nothing it touches is saved anywhere (a view model, not the emulator).
// A file named bb-probe-norelabel beside the log turns it off.

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HarmonyLib;

namespace LbIntegrations.BbProbe
{
    internal static class Hooks
    {
        private const string Suffix = "  [sonde]";
        private static bool _relabel;

        public static void Install()
        {
            _relabel = !File.Exists(ProbeLog.Beside("bb-probe-norelabel"));
            ProbeLog.Info("relabel experiment: " + (_relabel ? "ON" : "off"));

            // NOT ObservableCollection<T>.InsertItem: its code is shared by every reference type, and Harmony's
            // replacement of shared generic code broke WPF's own resource loading at start-up (measured,
            // 16/10: EntryPointNotFoundException in IList<T>.Insert). A non-generic constructor is safe.
            try
            {
                var h = new Harmony("nixx.bb-probe");
                var pcb = Type.GetType("Caliburn.Micro.PropertyChangedBase, Caliburn.Micro.Core", true);
                var ctor = pcb.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                h.Patch(ctor, postfix: new HarmonyMethod(typeof(Hooks).GetMethod(nameof(AfterCreate), BindingFlags.Static | BindingFlags.NonPublic)));
                ProbeLog.Info("hooked " + pcb.FullName + "..ctor");
            }
            catch (Exception ex) { ProbeLog.Warn("PropertyChangedBase hook", ex); }

            try
            {
                EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
                EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded), true);
                ProbeLog.Info("listening to Loaded / Unloaded");
            }
            catch (Exception ex) { ProbeLog.Warn("Loaded/Unloaded handlers", ex); }
        }

        // ---- 1. items -------------------------------------------------------------------------------

        private static readonly string[] Watched = { "MenuItem", "Popup", "Option", "GameDetails", "Emulator" };

        /// <summary>A Big Box view model being built: who builds it. Its own fields are not set yet (this
        /// runs in the base constructor), so only the type and the stack are told here.</summary>
        private static void AfterCreate(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                if (!(t.Namespace ?? "").StartsWith("Unbroken.LaunchBox.Windows")) return;
                if (!Watched.Any(w => t.Name.Contains(w))) return;
                ProbeLog.Info("CREATE " + Describe.Short(t));
                Describe.Stack("create " + t.Name);
            }
            catch (Exception ex) { ProbeLog.Warn("AfterCreate", ex); }
        }

        private static readonly ConditionalWeakTable<object, object> Scanned = new ConditionalWeakTable<object, object>();

        /// <summary>A list a view shows: each Big Box item in it now, and each one added later.</summary>
        private static void ScanItems(ItemsControl ic, IEnumerable items, string where)
        {
            if (items == null || items is string) return;
            lock (Scanned)
            {
                if (Scanned.TryGetValue(items, out _)) return;
                Scanned.Add(items, null);
            }
            bool changed = false;
            foreach (var i in items.Cast<object>().Take(500).ToList()) changed |= Consider(i, where);
            if (changed) Refresh(ic);
            if (items is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += (s, e) =>
                {
                    bool more = false;
                    if (e.NewItems != null) foreach (var i in e.NewItems) more |= Consider(i, where + " (added later)");
                    if (more) Refresh(ic);
                };
        }

        /// <summary>PopupOption does not notify its changes: the list is asked to draw its rows again.</summary>
        private static void Refresh(ItemsControl ic)
        {
            try
            {
                ic.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ic.Items.Refresh(); ProbeLog.Info("    list refreshed: " + Describe.Short(ic.GetType())); }
                    catch (Exception ex) { ProbeLog.Warn("refresh", ex); }
                }));
            }
            catch (Exception ex) { ProbeLog.Warn("refresh", ex); }
        }

        /// <summary>True when the item's text was changed.</summary>
        private static bool Consider(object item, string where)
        {
            try
            {
                if (item == null) return false;
                var ns = item.GetType().Namespace ?? "";
                if (!ns.StartsWith("Unbroken.LaunchBox.Windows.BigBox") && !ns.StartsWith("Unbroken.LaunchBox.Windows.MenuActions")) return false;
                var text = Describe.Item(item);
                if (!(text.Contains(" emulators=[") || text.Contains(" carries=["))) return false;
                ProbeLog.Info("EMULATOR ITEM in " + where + ": " + text);
                return _relabel && Relabel(item);
            }
            catch (Exception ex) { ProbeLog.Warn("Consider", ex); return false; }
        }

        /// <summary>A read-only text (PopupOption.DisplayString) is written through the field behind it,
        /// found the way EmuLabels finds Emulator.Title's: the string field that, given a sentinel, makes
        /// the getter return it.</summary>
        private static bool RelabelReadOnly(object item, PropertyInfo p)
        {
            var before = (string)p.GetValue(item);
            if (before != null && before.EndsWith(Suffix)) return false;
            for (var t = item.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(string)) continue;
                    var keep = (string)f.GetValue(item);
                    var sentinel = "bb-probe-" + Guid.NewGuid().ToString("N");
                    f.SetValue(item, sentinel);
                    var seen = (string)p.GetValue(item);
                    if (seen != sentinel) { f.SetValue(item, keep); continue; }
                    f.SetValue(item, before + Suffix);
                    ProbeLog.Info("    relabel " + p.Name + " via field " + f.Name + ": \"" + before + "\" -> \"" + p.GetValue(item) + "\"");
                    return true;
                }
            }
            ProbeLog.Info("    relabel: no field behind " + p.Name);
            return false;
        }

        private static bool Relabel(object item)
        {
            var display = item.GetType().GetProperty("DisplayString", BindingFlags.Instance | BindingFlags.Public);
            if (display != null && display.PropertyType == typeof(string))
            {
                try { return RelabelReadOnly(item, display); }
                catch (Exception ex) { ProbeLog.Warn("relabel DisplayString", ex); return false; }
            }
            foreach (var n in new[] { "Text", "Title", "Name", "DisplayText", "Label", "Header" })
            {
                var p = item.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public);
                if (p == null || p.PropertyType != typeof(string) || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                try
                {
                    var before = (string)p.GetValue(item);
                    if (before != null && before.EndsWith(Suffix)) return false;
                    p.SetValue(item, before + Suffix);
                    ProbeLog.Info("    relabel " + n + ": \"" + before + "\" -> \"" + p.GetValue(item) + "\"");
                    return true;
                }
                catch (Exception ex) { ProbeLog.Warn("relabel " + n, ex); }
                return false;
            }
            ProbeLog.Info("    relabel: no writable text property on " + Describe.Short(item.GetType()));
            return false;
        }

        // ---- 2. views -------------------------------------------------------------------------------

        private static void OnLoaded(object sender, RoutedEventArgs e) => View("VIEW LOADED  ", sender);
        private static void OnUnloaded(object sender, RoutedEventArgs e) => View("VIEW UNLOADED", sender);

        private static void View(string what, object sender)
        {
            try
            {
                if (!(sender is FrameworkElement fe)) return;

                // Any list on screen, whatever the theme drew it with: its Big Box items, now and later.
                if (what.StartsWith("VIEW LOADED") && fe is ItemsControl ic && ic.ItemsSource != null)
                    ScanItems(ic, ic.ItemsSource, Describe.Short(ic.GetType()) + " (dc " + Describe.Short(ic.DataContext?.GetType()) + ")");

                var dc = fe.DataContext;
                if (dc == null) return;
                var ns = dc.GetType().Namespace ?? "";
                if (!ns.StartsWith("Unbroken.LaunchBox.Windows.BigBox.ViewModels")) return;
                if (dc.GetType().Name == "MenuItemViewModel") return;               // one per row: the list says enough
                if (!(fe is UserControl || fe is Window || fe is Page)) return;    // the view itself, not its insides
                var parent = VisualTreeHelper.GetParent(fe) as FrameworkElement ?? fe.Parent as FrameworkElement;
                if (parent != null && ReferenceEquals(parent.DataContext, dc)) return;

                var line = what + " view=" + Describe.Short(fe.GetType()) + " dc=" + Describe.Item(dc);
                var items = dc.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.Public)?.GetValue(dc) as IEnumerable;
                if (items != null)
                {
                    var list = items.Cast<object>().Take(12).ToList();
                    line += " items:" + string.Concat(list.Select(i => "\n        - " + Describe.Item(i)));
                }
                ProbeLog.Info(line);
            }
            catch (Exception ex) { ProbeLog.Warn("View", ex); }
        }
    }
}
