// Can a group id be stored in OUR format while a native integration still gets exactly ITS own (Mehdi, 07/10)? Tried
// on RetroArch's Saturn sets, whose id is "saturn-<base>": its GetSaves hands out {"emu":"retroarch","kind":"saturn",
// "by":"name","id":"<base>"} instead - what LaunchBox then stores - and every save method of RetroArchPlugin is given
// "saturn-<base>" back on the way in, and our JSON again on the way out (the row LaunchBox holds, AddSaveFile's answer,
// TryGetSaveGroupInfo's out id). RetroArch never sees the JSON; LaunchBox never sees "saturn-".
// Off unless TranslateSaturn=true in Main's probe.ini (read at every call).

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.SaveProbeMain
{
    internal static class SaveProbeTranslator
    {
        private const string Native = "saturn-";
        private const string Head = "{\"emu\":\"retroarch\",\"kind\":\"saturn\",\"by\":\"name\",\"id\":";
        private static readonly PropertyInfo GroupProp = typeof(GameSaveBase).GetProperty(nameof(GameSaveBase.SaveGroupId));
        private static readonly PropertyInfo LineageProp = typeof(GameSaveBase).GetProperty(nameof(GameSaveBase.MatchLineageId));

        private static bool On => Probe.IniBool("TranslateSaturn", false);

        public static string ToOurs(string native) => native != null && native.StartsWith(Native, StringComparison.OrdinalIgnoreCase)
            ? Head + Probe.J(native.Substring(Native.Length)) + "}" : native;

        public static string ToNative(string ours)
        {
            if (ours == null || !ours.StartsWith(Head, StringComparison.Ordinal)) return ours;
            var m = Regex.Match(ours, "\"id\":\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Native + Unescape(m.Groups[1].Value) : ours;
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                var c = s[++i];
                if (c == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ── the patches ──────────────────────────────────────────────────────

        public static void Patch(Harmony harmony, Type retroArch)
        {
            var first = new HarmonyMethod(typeof(SaveProbeTranslator).GetMethod(nameof(In), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.First };
            var last = new HarmonyMethod(typeof(SaveProbeTranslator).GetMethod(nameof(Out), BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.Last };
            int n = 0;
            foreach (var m in retroArch.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                       .Where(m => m.GetParameters().Any(p => typeof(GameSaveBase).IsAssignableFrom(p.ParameterType) || p.ParameterType == typeof(AddSaveArgs))))
            {
                try { harmony.Patch(m, prefix: first, postfix: last); n++; }
                catch (Exception ex) { Probe.Log("translator: could not patch RetroArchPlugin." + m.Name + " - " + ex.Message); }
            }
            Patch1(harmony, retroArch, "GetSaves", nameof(OutGetSaves));
            Patch1(harmony, retroArch, "AddSaveFile", nameof(OutAddSave));
            Patch1(harmony, retroArch, "TryGetSaveGroupInfo", nameof(OutGroupInfo));
            Probe.Log("translator: " + n + " RetroArch method(s) translated in and out (TranslateSaturn=" + On + ")");
        }

        private static void Patch1(Harmony harmony, Type t, string method, string postfix)
        {
            try
            {
                var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m == null) { Probe.Log("translator: RetroArchPlugin." + method + " not found"); return; }
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(SaveProbeTranslator).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static)) { priority = Priority.Last });
            }
            catch (Exception ex) { Probe.Log("translator: could not patch RetroArchPlugin." + method + " - " + ex.Message); }
        }

        /// <summary>Before RetroArch: every row given to it carries its own id again. What was changed is put back after.</summary>
        private static void In(MethodBase __originalMethod, object[] __args, out List<(GameSaveBase Row, string Group, string Lineage)> __state)
        {
            __state = null;
            try
            {
                if (!On) return;
                foreach (var a in __args ?? Array.Empty<object>())
                {
                    var row = a as GameSaveBase ?? (a as AddSaveArgs)?.SaveToAdd;
                    if (row == null) continue;
                    var g = row.SaveGroupId;
                    var native = ToNative(g);
                    if (ReferenceEquals(native, g) || native == g) continue;
                    var l = row.MatchLineageId;
                    GroupProp.SetValue(row, native);
                    if (l == g) LineageProp.SetValue(row, native);
                    (__state ??= new List<(GameSaveBase, string, string)>()).Add((row, g, l));
                    Probe.Log("translator: RetroArchPlugin." + __originalMethod.Name + " gets \"" + native + "\" (stored: " + Probe.IdSummary(g) + ")");
                }
            }
            catch (Exception ex) { Probe.Log("translator in: " + ex.Message); }
        }

        /// <summary>After RetroArch: LaunchBox's rows carry our id again.</summary>
        private static void Out(List<(GameSaveBase Row, string Group, string Lineage)> __state)
        {
            if (__state == null) return;
            foreach (var (row, g, l) in __state)
                try { GroupProp.SetValue(row, g); LineageProp.SetValue(row, l); } catch { }
        }

        private static void OutGetSaves(GetSavesResponse __result)
        {
            try
            {
                if (!On || __result?.FoundSaves == null) return;
                foreach (var row in __result.FoundSaves) ToOursRow(row, "GetSaves");
            }
            catch (Exception ex) { Probe.Log("translator GetSaves: " + ex.Message); }
        }

        private static void OutAddSave(AddSaveResponse __result)
        {
            try { if (On && __result?.SaveAdded != null) ToOursRow(__result.SaveAdded, "AddSaveFile"); }
            catch (Exception ex) { Probe.Log("translator AddSaveFile: " + ex.Message); }
        }

        private static void OutGroupInfo(ref string saveGroupId)
        {
            try
            {
                if (!On) return;
                var ours = ToOurs(saveGroupId);
                if (ours == saveGroupId) return;
                Probe.Log("translator: TryGetSaveGroupInfo answered \"" + saveGroupId + "\" -> LaunchBox gets " + Probe.IdSummary(ours));
                saveGroupId = ours;
            }
            catch (Exception ex) { Probe.Log("translator TryGetSaveGroupInfo: " + ex.Message); }
        }

        private static void ToOursRow(GameSaveBase row, string where)
        {
            var g = row.SaveGroupId;
            var ours = ToOurs(g);
            if (ours == g) return;
            var l = row.MatchLineageId;
            GroupProp.SetValue(row, ours);
            if (string.IsNullOrEmpty(l) || l == g) LineageProp.SetValue(row, ours);
            Probe.Log("translator: RetroArchPlugin." + where + " row \"" + g + "\" -> LaunchBox gets " + Probe.IdSummary(ours));
        }
    }
}
