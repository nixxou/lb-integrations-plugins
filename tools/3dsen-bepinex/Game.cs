// 3dSen's own types and members, reached BY NAME (read in 3dSen 1.1.2's Assembly-CSharp, docs\3dsen.md): nothing of the game
// is referenced at build time, so a member that an update renamed is null here, said once in nixx-errors.log, and the feature
// that needs it does nothing - the rest goes on.

using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LbIntegrations.ThreeDSen.Mod
{
    internal static class Game
    {
        public static Type Info, SaveStateDB, SettingManager, Emulator, NotificationManager, ScreenRecorder, Engine;

        public static void Resolve(NixxLog log)
        {
            Info = Need(log, "Nes3D.Info");
            SaveStateDB = Need(log, "Nes3D.SaveStateDB");
            SettingManager = Need(log, "Nes3D.SettingManager");
            Emulator = Need(log, "Nes3D.Emulator");
            NotificationManager = Need(log, "Nes3D.UI.NotificationManager");
            ScreenRecorder = Need(log, "Nes3D.ScreenRecorder");
            Engine = Need(log, "Nes3D.Engine");
        }

        private static Type Need(NixxLog log, string name)
        {
            var t = AccessTools.TypeByName(name);
            if (t == null) log.Error("type " + name + " not found in 3dSen - what needs it is off");
            return t;
        }

        /// <summary>A static "Ins" of a type (field or property), or null.</summary>
        public static object Ins(Type t)
        {
            if (t == null) return null;
            try
            {
                var f = AccessTools.Field(t, "Ins");
                if (f != null && f.IsStatic) return f.GetValue(null);
                var p = AccessTools.Property(t, "Ins");
                return p?.GetValue(null, null);
            }
            catch { return null; }
        }

        public static object Get(object o, string member)
        {
            if (o == null) return null;
            var t = o as Type ?? o.GetType();
            var target = o is Type ? null : o;
            var p = AccessTools.Property(t, member);
            if (p != null) return p.GetValue(target, null);
            var f = AccessTools.Field(t, member);
            return f?.GetValue(target);
        }

        public static object Call(object o, string method, params object[] args)
        {
            if (o == null) return null;
            var t = o as Type ?? o.GetType();
            var m = AccessTools.Method(t, method);
            if (m == null) throw new MissingMethodException(t.FullName, method);
            return m.Invoke(o is Type ? null : o, Fill(m, args));
        }

        /// <summary>The arguments, the optional ones the caller left out given their defaults.</summary>
        private static object[] Fill(MethodBase m, object[] args)
        {
            var ps = m.GetParameters();
            if (args.Length >= ps.Length) return args;
            var all = new object[ps.Length];
            Array.Copy(args, all, args.Length);
            for (int i = args.Length; i < ps.Length; i++) all[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            return all;
        }

        // ── what the plugin uses ─────────────────────────────────────────────

        /// <summary>The plugin's on-screen message: its own white box (Toast) - 3dSen's notifications, white text on nothing,
        /// cannot be read over a black screen.</summary>
        public static void Notify(string text, bool warning = false, float seconds = 2.5f) => Toast.Show(text, warning, seconds);

        /// <summary>3dSen's own on-screen message (NotificationManager.Notify, Normal or Warning), else nothing.</summary>
        public static void Notify3dSen(string text, bool warning = false, float seconds = 2.5f)
        {
            try
            {
                var n = Ins(NotificationManager);
                if (n == null) return;
                var m = AccessTools.Method(NotificationManager, "Notify");
                var type = m.GetParameters()[1].ParameterType;
                m.Invoke(n, new[] { text, Enum.ToObject(type, warning ? 1 : 0), (object)seconds });
            }
            catch { }
        }

        /// <summary>The game being played (Emulator.CurrentGame), or null - a menu with no game.</summary>
        public static object CurrentGame() { try { return Get(Ins(Emulator), "CurrentGame"); } catch { return null; } }

        public static string IdOf(object game) { try { return Get(game, "Id") as string; } catch { return null; } }

        /// <summary>A slot of 3dSen's (SaveConfig): "quick" or "1".."36".</summary>
        public static object Slot(string id)
        {
            var db = Ins(SaveStateDB);
            if (db == null) return null;
            if (id == "quick") return Get(db, "QuickSave");
            var list = Get(db, "SaveList") as IList;
            int n = int.Parse(id);
            return list != null && n >= 1 && n <= list.Count ? list[n - 1] : null;
        }

        public static bool SlotEmpty(object slot) { try { return (bool)Get(slot, "Empty"); } catch { return true; } }

        public static string SlotGame(object slot) { try { return Get(slot, "gameId") as string; } catch { return null; } }

        /// <summary>A picture of the 3D scene as 3dSen takes it when it pauses (ScreenRecorder.Capture, 256 x 192), then
        /// <paramref name="then"/> with it - null when 3dSen's recorder is not there.</summary>
        public static IEnumerator Capture(Action<Texture2D> then)
        {
            Texture2D tex = null;
            IEnumerator run = null;
            try
            {
                var rec = ScreenRecorder == null ? null : UnityEngine.Object.FindObjectOfType(ScreenRecorder);
                if (rec != null)
                {
                    tex = new Texture2D(256, 192, TextureFormat.RGB24, false);
                    run = (IEnumerator)Call(rec, "Capture", tex);
                }
            }
            catch { tex = null; run = null; }
            if (run != null) yield return run;
            then(tex);
        }
    }
}
