// --nixx-dump-options: what the Nixx window's options are measured from. Five seconds after the start (3dSen's menus are
// built by then), <data>\nixx-options.txt gets every readable property of SettingManager.Ins with its value and type (an
// enum with its names), then every Dropdown, Slider and Toggle of the loaded scenes - its path in the hierarchy, its options'
// texts or its range: the labels 3dSen shows, which live in its scene, not in its code.

using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal static class Dump
    {
        public static IEnumerator Run(string folder, NixxLog log)
        {
            yield return new WaitForSecondsRealtime(5f);
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# 3dSen settings and menus, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("## SettingManager.Ins");
                var sm = Game.Ins(Game.SettingManager);
                if (sm != null)
                    foreach (var p in Game.SettingManager.GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name))
                    {
                        string v;
                        try { v = Convert.ToString(p.GetValue(sm, null), System.Globalization.CultureInfo.InvariantCulture); } catch (Exception ex) { v = "<" + ex.GetType().Name + ">"; }
                        var t = p.PropertyType;
                        sb.AppendLine(p.Name + " = " + v + "    [" + t.Name + (t.IsEnum ? ": " + string.Join(", ", Enum.GetNames(t)) : "") + (p.CanWrite ? "" : ", read-only") + "]");
                    }
                sb.AppendLine("## Dropdowns");
                foreach (var d in Resources.FindObjectsOfTypeAll<Dropdown>().Where(x => x.gameObject.scene.IsValid()))
                    sb.AppendLine(PathOf(d.transform) + " = " + d.value + "    [" + string.Join(" | ", d.options.Select(o => o.text)) + "]");
                sb.AppendLine("## Sliders");
                foreach (var s in Resources.FindObjectsOfTypeAll<Slider>().Where(x => x.gameObject.scene.IsValid()))
                    sb.AppendLine(PathOf(s.transform) + " = " + s.value + "    [" + s.minValue + " .. " + s.maxValue + (s.wholeNumbers ? ", whole" : "") + "]");
                sb.AppendLine("## Toggles");
                foreach (var t in Resources.FindObjectsOfTypeAll<Toggle>().Where(x => x.gameObject.scene.IsValid()))
                    sb.AppendLine(PathOf(t.transform) + " = " + t.isOn);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "nixx-options.txt"), sb.ToString());
                log.Info("options dumped to " + Path.Combine(folder, "nixx-options.txt"));
            }
            catch (Exception ex) { log.Error("dump of the options: " + ex.Message); }
        }

        private static string PathOf(Transform t)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var x = t; x != null; x = x.parent) parts.Insert(0, x.name);
            var label = t.GetComponentsInChildren<Text>(true).FirstOrDefault()?.text;
            return string.Join("/", parts) + (label != null ? " (\"" + label.Replace("\n", " ") + "\")" : "");
        }
    }
}
