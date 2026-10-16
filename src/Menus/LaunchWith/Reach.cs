// FROM A UI OBJECT TO THE EMULATOR AND THE GAME IT STANDS FOR.
//
// LaunchBox's menu builders are protected (.NET Reactor NecroBit): nothing can be read from their code. What
// they leave behind can: a WPF MenuItem keeps its Click handlers, its Command, its Tag; a Big Box PopupOption
// keeps its value and its callback. Somewhere behind those sits the menu action (a *MenuAction object, whose
// public Game is callable) and the emulator (an IEmulator - LaunchBox's Emulator implements the SDK's).
//
// So this walks the object graph from those roots, breadth first, a few levels deep, through fields only
// (any name: they are obfuscated), stepping into delegates' targets, never into WPF elements (that would be
// the whole visual tree) nor into the emulator or the game themselves. It returns what it found and THE PATH
// it took, which the log prints: the path is the evidence of how LaunchBox ties a line to its emulator.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Menus.LaunchWith
{
    internal sealed class Link
    {
        public object Action;       // the *MenuAction behind the line, if one was met
        public IEmulator Emulator;  // the emulator the line launches with
        public IGame Game;          // the game, from the action (Game property), when it has one
        public string Path;         // how the emulator was reached, for the log

        public string ActionName => Action?.GetType().Name;
    }

    internal static class Reach
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int MaxDepth = 4;

        // ---- roots ------------------------------------------------------------------------------

        public static Link FromMenuItem(MenuItem mi)
        {
            var roots = new List<(string, object)>
            {
                ("Tag", mi.Tag),
                ("CommandParameter", mi.CommandParameter),
                ("Command", mi.Command),
            };
            // DataContext only when it is the item's own (a menu inherits its window's otherwise).
            var parent = mi.Parent as FrameworkElement;
            if (mi.DataContext != null && (parent == null || !ReferenceEquals(parent.DataContext, mi.DataContext)))
                roots.Add(("DataContext", mi.DataContext));
            foreach (var d in ClickHandlers(mi)) roots.Add(("Click:" + d.Method.Name, d));
            return Search(roots);
        }

        public static Link FromObject(string name, object root) => Search(new List<(string, object)> { (name, root) });

        /// <summary>The handlers attached to a MenuItem's Click: UIElement keeps them in an internal
        /// EventHandlersStore, whose GetRoutedEventHandlers is public.</summary>
        public static IEnumerable<Delegate> ClickHandlers(MenuItem mi)
        {
            var result = new List<Delegate>();
            try
            {
                var store = typeof(UIElement).GetProperty("EventHandlersStore", All)?.GetValue(mi);
                var get = store?.GetType().GetMethod("GetRoutedEventHandlers", new[] { typeof(RoutedEvent) });
                if (get?.Invoke(store, new object[] { MenuItem.ClickEvent }) is RoutedEventHandlerInfo[] infos)
                    foreach (var i in infos) if (i.Handler != null) result.Add(i.Handler);
            }
            catch (Exception ex) { Log.Warn("reading Click handlers", ex); }
            return result;
        }

        // ---- the walk ---------------------------------------------------------------------------

        private static Link Search(List<(string name, object value)> roots)
        {
            var link = new Link();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<(object obj, string path, int depth)>();
            foreach (var (name, value) in roots)
                if (value != null) queue.Enqueue((value, name, 0));

            while (queue.Count > 0 && (link.Emulator == null || link.Action == null))
            {
                var (obj, path, depth) = queue.Dequeue();
                if (obj == null || !seen.Add(obj)) continue;

                if (obj is IEmulator e)
                {
                    if (link.Emulator == null) { link.Emulator = e; link.Path = path; }
                    continue;                                   // a leaf: never walk into the emulator
                }
                if (obj is IGame g)                             // nor into a game - but a game met on the way is the line's
                {
                    if (link.Game == null) link.Game = g;
                    continue;
                }

                var type = obj.GetType();
                if (link.Action == null && type.Name.EndsWith("MenuAction", StringComparison.Ordinal))
                {
                    link.Action = obj;
                    try { link.Game = type.GetProperty("Game", BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj) as IGame; }
                    catch { }
                }

                if (depth >= MaxDepth) continue;
                if (obj is Delegate d)
                {
                    foreach (var one in d.GetInvocationList())
                        if (one.Target != null) queue.Enqueue((one.Target, path + "->target(" + Short(one.Target.GetType()) + ")", depth + 1));
                    continue;
                }
                if (!Walkable(type)) continue;

                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(All | BindingFlags.DeclaredOnly))
                    {
                        if (!Walkable(f.FieldType) && !typeof(Delegate).IsAssignableFrom(f.FieldType)
                            && !typeof(IEmulator).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(object)
                            && !f.FieldType.IsInterface) continue;
                        object v;
                        try { v = f.GetValue(obj); } catch { continue; }
                        if (v != null) queue.Enqueue((v, path + "." + f.Name + "(" + Short(v.GetType()) + ")", depth + 1));
                    }
                }
            }
            return link;
        }

        /// <summary>Objects worth stepping into: LaunchBox's own, compiler closures, and the MVVM helpers
        /// a command may be. Never WPF elements, strings, primitives or collections.</summary>
        private static bool Walkable(Type t)
        {
            if (t == null || t.IsPrimitive || t.IsEnum || t == typeof(string) || t.IsPointer) return false;
            if (typeof(DependencyObject).IsAssignableFrom(t)) return false;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return false;
            var ns = t.Namespace ?? "";
            return ns.StartsWith("Unbroken") || ns.StartsWith("Caliburn") || ns.Length == 0
                   || t.Name.Contains("<>") || t.Name.Contains("DisplayClass") || t.Name.Contains("Command");
        }

        public static string Short(Type t) => t == null ? "null" : (t.FullName ?? t.Name).Replace("Unbroken.LaunchBox.Windows.", "~.");
    }
}
