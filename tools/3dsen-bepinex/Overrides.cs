// --nixx-set:<Name>=<value>: a property of 3dSen's SettingManager for this session - the per-game options of the Nixx window.
//
// 3dSen keeps its settings in SettingManager (one instance, SettingManager.Ins), read from setting.cfg by Load() at start and
// written back by Save() when it quits or a menu changes one. The properties (read in 3dSen 1.1.2): Fov (float, degrees),
// GameSpeed (float), Volume (float 0..1), RenderMode (RenderAlgo: GREEDY / MARCHING / GREEDY_MULTICOLOR), Shadow, Skybox
// (int, -1 = colour), GraphicQuality, VSync, FrameRate (60/90/120), AntiAlias, RenderMethod, ResScaleIndex, SkipFrame,
// BGMusic, TVMode (TVType), AutoTVMode, TVBox, SceneRatio, Mirroring, EmptyTile, SpriteTolerance, ScanDirection,
// SameBgPalette, SameSpPalette, Core, ControlMethod, MapEnable, LocalCam, BgColor, EmulatedMenuButton... Any public property
// of a simple type (bool, int, float, an enum by name or number) is accepted by name: the window offers the measured ones.
//
// SET THROUGH THE PROPERTY'S SETTER, after Load() (a postfix), so 3dSen applies it as its own menu would (its setters raise
// PropertyChanged, which its views follow). NOT KEPT (--nixx-persist off, the default): around every Save() the values 3dSen
// had are put back for the write and ours again after - setting.cfg never learns them, the next game starts from the user's.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal static class Overrides
    {
        private static readonly List<(PropertyInfo Prop, object Ours, object Theirs)> Applied = new List<(PropertyInfo, object, object)>();
        private static Args _args;
        private static NixxLog _log;
        private static bool _swapped;

        public static void Install(Harmony h, Args args, NixxLog log)
        {
            _args = args; _log = log;
            if (args.Sets.Count == 0 || Game.SettingManager == null) return;
            var load = AccessTools.Method(Game.SettingManager, "Load");
            var save = AccessTools.Method(Game.SettingManager, "Save");
            if (load == null) { log.Error("SettingManager.Load not found - the --nixx-set options are off"); return; }
            h.Patch(load, postfix: new HarmonyMethod(typeof(Overrides), nameof(AfterLoad)));
            if (save != null && !args.Persist)
                h.Patch(save, prefix: new HarmonyMethod(typeof(Overrides), nameof(BeforeSave)), postfix: new HarmonyMethod(typeof(Overrides), nameof(AfterSave)));
            else if (save == null) log.Error("SettingManager.Save not found - the --nixx-set options may be kept in setting.cfg");
        }

        private static void AfterLoad(object __instance)
        {
            Applied.Clear();
            foreach (var kv in _args.Sets)
            {
                try
                {
                    var p = AccessTools.Property(Game.SettingManager, kv.Key);
                    if (p == null || !p.CanWrite || !p.CanRead) { _log.Error("--nixx-set:" + kv.Key + ": no such setting in 3dSen"); continue; }
                    if (!TryConvert(kv.Value, p.PropertyType, out var value)) { _log.Error("--nixx-set:" + kv.Key + "=" + kv.Value + ": not a " + p.PropertyType.Name); continue; }
                    var theirs = p.GetValue(__instance, null);
                    p.SetValue(__instance, value, null);
                    Applied.Add((p, value, theirs));
                    _log.Info("setting " + kv.Key + " = " + value + " (3dSen had " + theirs + ")");
                }
                catch (Exception ex) { _log.Error("--nixx-set:" + kv.Key + ": " + (ex.InnerException ?? ex).Message); }
            }
        }

        private static void BeforeSave(object __instance)
        {
            if (Applied.Count == 0 || _swapped) return;
            for (int i = 0; i < Applied.Count; i++)
            {
                var a = Applied[i];
                try
                {
                    // Changed in 3dSen's own menu since: the user's choice now, written as it is and left alone after.
                    var now = a.Prop.GetValue(__instance, null);
                    if (!Equals(now, a.Ours)) { Applied[i] = (a.Prop, now, now); continue; }
                    a.Prop.SetValue(__instance, a.Theirs, null);
                }
                catch { }
            }
            _swapped = true;
        }

        private static void AfterSave(object __instance)
        {
            if (!_swapped) return;
            foreach (var a in Applied) try { a.Prop.SetValue(__instance, a.Ours, null); } catch { }
            _swapped = false;
        }

        internal static bool TryConvert(string text, Type type, out object value)
        {
            value = null;
            text = (text ?? "").Trim();
            if (type == typeof(bool)) { var b = text.ToLowerInvariant(); if (b == "1" || b == "on" || b == "true" || b == "yes") value = true; else if (b == "0" || b == "off" || b == "false" || b == "no") value = false; return value != null; }
            if (type == typeof(int)) { if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) value = i; return value != null; }
            if (type == typeof(float)) { if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) value = f; return value != null; }
            if (type == typeof(double)) { if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) value = d; return value != null; }
            if (type == typeof(string)) { value = text; return true; }
            if (type.IsEnum)
            {
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { value = Enum.ToObject(type, n); return true; }
                try { value = Enum.Parse(type, text, true); return true; } catch { return false; }
            }
            return false;
        }
    }
}
