// THE GUARD: NO LABEL EVER REACHES THE DISK.
//
// The label lives in Emulator.Title, so anything LaunchBox serialises while it is there would carry
// it: Emulators.xml, a cloud sync, an edited copy of the emulator saved from its dialog. LaunchBox's
// own save code is protected (NecroBit) and cannot be patched, but what it hands the value TO is
// .NET's, and that is ordinary IL:
//
//   - Emulators.xml is a DataSet written with WriteXml (standalone="yes", root <LaunchBox>): each
//     emulator fills a DataRow (DataItem.AddToTable / AddChangedToTable), then System.Xml writes it.
//     Both ends are guarded - the row, and the writer - so a path that skips one still meets the other.
//   - Newtonsoft.Json and System.Text.Json, for whatever goes out as JSON.
//
// Each guard is a Harmony prefix that removes every U+2063 ... U+2064 span from the string on its way
// in (see Label). A string without U+2063 costs one IndexOf; nothing else is ever changed.
//
// The labels are only applied once the row AND the XML guards are in place (Installed): a guard that
// failed to install means no label, never a label without its guard.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;

namespace LbIntegrations.EmuLabels
{
    internal static class SaveGuard
    {
        public const string HarmonyId = "nixx.emu-labels.save-guard";

        private static readonly object Gate = new object();
        private static bool _tried;

        /// <summary>True once the DataRow and the XmlWriter guards are both patched.</summary>
        public static bool Installed { get; private set; }

        /// <summary>What was patched, for the log and the test bench.</summary>
        public static readonly List<string> Patched = new List<string>();

        public static bool Install()
        {
            lock (Gate)
            {
                if (_tried) return Installed;
                _tried = true;
                try
                {
                    var h = new Harmony(HarmonyId);
                    int rows = PatchRows(h);
                    int xml = PatchXml(h);
                    int json = PatchJson(h);
                    Installed = rows > 0 && xml > 0;
                    Log.Info("save guard: " + rows + " row, " + xml + " xml, " + json + " json method(s) patched"
                             + (Installed ? "" : " - INCOMPLETE, labels stay off"));
                }
                catch (Exception ex)
                {
                    Installed = false;
                    Log.Warn("save guard failed, labels stay off", ex);
                }
                return Installed;
            }
        }

        // ---- System.Data ------------------------------------------------------------------------

        private static int PatchRows(Harmony h)
        {
            int n = 0;
            var row = typeof(DataRow);
            n += Patch(h, row.GetMethod("set_Item", new[] { typeof(string), typeof(object) }), nameof(ValueAt1));
            n += Patch(h, row.GetMethod("set_Item", new[] { typeof(int), typeof(object) }), nameof(ValueAt1));
            n += Patch(h, row.GetMethod("set_Item", new[] { typeof(DataColumn), typeof(object) }), nameof(ValueAt1));
            n += Patch(h, row.GetMethod("set_ItemArray", new[] { typeof(object[]) }), nameof(ValuesAt0));
            n += Patch(h, typeof(DataRowCollection).GetMethod("Add", new[] { typeof(object[]) }), nameof(ValuesAt0));
            n += Patch(h, typeof(DataTable).GetMethod("LoadDataRow", new[] { typeof(object[]), typeof(bool) }), nameof(ValuesAt0));
            n += Patch(h, typeof(DataTable).GetMethod("LoadDataRow", new[] { typeof(object[]), typeof(LoadOption) }), nameof(ValuesAt0));
            return n;
        }

        // ---- System.Xml -------------------------------------------------------------------------

        /// <summary>Every concrete XmlWriter of System.Xml that declares its own WriteString or
        /// WriteValue(string): XmlTextWriter (DataSet.WriteXml), the well-formed and encoded writers
        /// behind XmlWriter.Create, and the rest. Patching all of them, rather than guessing which one
        /// LaunchBox gets, is what makes this independent of how it writes.</summary>
        private static int PatchXml(Harmony h)
        {
            int n = 0;
            Type[] types;
            try { types = typeof(XmlWriter).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types.Where(t => typeof(XmlWriter).IsAssignableFrom(t)))
            {
                foreach (var name in new[] { "WriteString", "WriteValue" })
                {
                    var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                              | BindingFlags.DeclaredOnly, null, new[] { typeof(string) }, null);
                    if (m == null || m.IsAbstract) continue;
                    n += Patch(h, m, nameof(StringAt0));
                }
            }
            return n;
        }

        // ---- JSON -------------------------------------------------------------------------------

        private static int PatchJson(Harmony h)
        {
            int n = 0;
            try
            {
                var newtonsoft = Type.GetType("Newtonsoft.Json.JsonTextWriter, Newtonsoft.Json", false);
                n += Patch(h, newtonsoft?.GetMethod("WriteValue", new[] { typeof(string) }), nameof(StringAt0));
            }
            catch (Exception ex) { Log.Warn("Newtonsoft guard", ex); }
            n += Patch(h, typeof(System.Text.Json.Utf8JsonWriter).GetMethod("WriteStringValue", new[] { typeof(string) }),
                       nameof(StringAt0));
            return n;
        }

        // ---- the prefixes -----------------------------------------------------------------------

        private static void StringAt0(ref string __0)
        {
            if (Label.Has(__0)) __0 = Label.Strip(__0);
        }

        private static void ValueAt1(ref object __1)
        {
            if (__1 is string s && Label.Has(s)) __1 = Label.Strip(s);
        }

        /// <summary>A copy, never the caller's array changed in place.</summary>
        private static void ValuesAt0(ref object[] __0)
        {
            var values = __0;
            if (values == null) return;
            object[] copy = null;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is string s && Label.Has(s))
                {
                    copy ??= (object[])values.Clone();
                    copy[i] = Label.Strip(s);
                }
            }
            if (copy != null) __0 = copy;
        }

        private static int Patch(Harmony h, MethodBase target, string prefix)
        {
            if (target == null) return 0;
            try
            {
                h.Patch(target, prefix: new HarmonyMethod(typeof(SaveGuard).GetMethod(prefix,
                    BindingFlags.Static | BindingFlags.NonPublic)));
                lock (Patched) Patched.Add(target.DeclaringType?.FullName + "." + target.Name);
                return 1;
            }
            catch (Exception ex)
            {
                Log.Warn("could not guard " + target.DeclaringType?.FullName + "." + target.Name, ex);
                return 0;
            }
        }
    }
}
