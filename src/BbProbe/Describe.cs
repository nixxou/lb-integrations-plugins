// What the probe says about an object: its type, its texts, the emulators it carries - read by reflection,
// because Big Box's types are obfuscated in their private members and protected in their bodies (their
// accessors can be CALLED, which is all this needs).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.BbProbe
{
    internal static class Describe
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] TextNames = { "Text", "Title", "Name", "DisplayText", "DisplayString", "Label", "Header", "PopupTitle" };
        private static readonly Dictionary<string, int> Stacks = new Dictionary<string, int>();

        public static string Short(Type t) => t == null ? "null" : t.FullName?.Replace("Unbroken.LaunchBox.Windows.", "~.") ?? t.Name;

        /// <summary>Type, readable texts, the action and its emulators.</summary>
        public static string Item(object o)
        {
            if (o == null) return "null";
            var sb = new StringBuilder(Short(o.GetType()));
            foreach (var n in TextNames)
            {
                var p = o.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public);
                if (p == null || p.PropertyType != typeof(string) || p.GetIndexParameters().Length > 0) continue;
                sb.Append(" ").Append(n).Append("=\"").Append(Safe(() => (string)p.GetValue(o))).Append("\"");
            }
            var action = o.GetType().GetProperty("Action", BindingFlags.Instance | BindingFlags.Public)?.GetValue(o);
            if (action != null)
            {
                sb.Append(" action=").Append(Short(action.GetType()));
                var emus = Emulators(action);
                if (emus.Count > 0) sb.Append(" emulators=[").Append(string.Join(", ", emus.Select(Title))).Append("]");
            }
            var own = Emulators(o);
            if (own.Count > 0) sb.Append(" carries=[").Append(string.Join(", ", own.Select(Title))).Append("]");
            return sb.ToString();
        }

        /// <summary>Every IEmulator held in a field of o (directly, or in an enumerable / dictionary keys).</summary>
        public static List<IEmulator> Emulators(object o)
        {
            var found = new List<IEmulator>();
            if (o == null) return found;
            for (var t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(All | BindingFlags.DeclaredOnly))
                {
                    object v;
                    try { v = f.GetValue(o); } catch { continue; }
                    if (v is IEmulator e) found.Add(e);
                    else if (v is IDictionary d) { foreach (var k in d.Keys) if (k is IEmulator ke) found.Add(ke); }
                    else if (v is IEnumerable en && !(v is string))
                    {
                        int n = 0;
                        try { foreach (var x in en) { if (x is IEmulator xe) found.Add(xe); if (++n > 200) break; } } catch { }
                    }
                }
            }
            return found.Distinct().ToList();
        }

        public static string Title(IEmulator e) => Safe(() => e.Title);

        /// <summary>The calling frames worth reading - LaunchBox's (names intact even when bodies are protected),
        /// Caliburn's and WPF's - logged in full the first time, by count afterwards.</summary>
        public static void Stack(string tag)
        {
            try
            {
                var frames = new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>();
                var names = new List<string>();
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    var t = m?.DeclaringType;
                    var ns = t?.Namespace ?? "";
                    if (t == null || ns.StartsWith("LbIntegrations") || ns.StartsWith("HarmonyLib")) continue;
                    if (!(ns.StartsWith("Unbroken") || ns.StartsWith("Caliburn") || ns.StartsWith("BigBox")
                          || ns.StartsWith("System.Windows.Controls") || ns.StartsWith("System.Windows.Threading"))) continue;
                    names.Add(Short(t) + "." + m.Name);
                    if (names.Count >= 28) break;
                }
                var key = tag + "|" + string.Join("<", names);
                int seen;
                lock (Stacks) { Stacks.TryGetValue(key, out seen); Stacks[key] = seen + 1; }
                if (seen == 0) ProbeLog.Info("    stack[" + tag + "]:\n        " + string.Join("\n        ", names));
                else ProbeLog.Info("    stack[" + tag + "]: same as before (#" + (seen + 1) + ")");
            }
            catch (Exception ex) { ProbeLog.Warn("stack", ex); }
        }

        public static string Safe(Func<string> f) { try { return f() ?? "null"; } catch (Exception ex) { return "<" + ex.GetType().Name + ">"; } }
    }
}
