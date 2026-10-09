// LaunchBox's RetroArch integration reads its retroarch.cfg again for EVERY game it is asked about (MEASURED 09/10, its
// code decompiled): GetGameSaveDirectory and GetSaveStateDirectory each call the private static LoadConfiguration - a
// File.ReadAllLines of the whole file, split on '=', no cache. Nothing in it depends on the game: no per-game or per-core
// override is looked at. With a 111 KB / 3,400-line retroarch.cfg, 2,000 games listed by the desk cost ~6 s per RetroArch.
//
// A Harmony prefix answers from a cache instead: one parsed copy per file, kept while the file's write time and length
// are the same (a setting changed in RetroArch is read again on the next call). Each caller gets its own copy of the
// dictionary - its two callers only read it today, a later one might not. Measured outside LaunchBox on its real dll:
// 4,000 calls 3.9 s → 0.36 s, the same folders answered, the cache refreshed after an edit of the file.
//
// Patched ONCE per process, by whichever plugin of this pack comes first (a mark in the AppDomain's data says it is in), when
// RetroArch's integration is loaded - now, or when it loads later. Off from the Compatibility page ("raConfigCache").

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LbIntegrations.Saves
{
    internal static class RaConfigCache
    {
        public const string HarmonyId = "com.nixxou.lbip.ra-config-cache";
        private const string Marker = "LbIntegrations.RaConfigCache.Patched";
        private const string TypeName = "Unbroken.LaunchBox.Windows.RetroArch.RetroArchPlugin";
        private static readonly object Gate = new object();
        private static bool _watching;

        private static readonly Dictionary<string, (DateTime Written, long Length, Dictionary<string, string?> Values)> Cache
            = new Dictionary<string, (DateTime, long, Dictionary<string, string?>)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Put the patch in when RetroArch's integration is there, or as soon as it loads.</summary>
        public static void Install()
        {
            try
            {
                if (!LbIntegrations.Compat.Compat.On("raConfigCache")) return;
                lock (Gate)
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        if (TryPatch(a)) return;
                    if (_watching) return;
                    _watching = true;
                    AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
                }
            }
            catch (Exception ex) { SaveRouter.Say("RetroArch config cache not installed: " + ex.Message); }
        }

        private static void OnLoad(object? sender, AssemblyLoadEventArgs e)
        {
            try
            {
                lock (Gate)
                    if (TryPatch(e.LoadedAssembly)) AppDomain.CurrentDomain.AssemblyLoad -= OnLoad;
            }
            catch { }
        }

        /// <summary>The method patched, or null when RetroArch's integration (or the method) is not there.</summary>
        internal static MethodInfo? Target(Assembly a)
        {
            if (a.IsDynamic || (a.GetName().Name ?? "").IndexOf("RetroArch", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var m = a.GetType(TypeName, false)?.GetMethod("LoadConfiguration", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            return m != null && typeof(Dictionary<string, string>).IsAssignableFrom(m.ReturnType) ? m : null;
        }

        /// <summary>True once the patch is in (by this plugin or another one of the pack).</summary>
        private static bool TryPatch(Assembly a)
        {
            var m = Target(a);
            if (m == null) return false;
            // Every plugin has its own copy of this class AND of Harmony (merged into its dll): neither a lock on a type nor
            // Harmony's patch info sees the others (MEASURED 09/10: twelve prefixes stacked). An interned string and the
            // AppDomain's data are the same object for all of them.
            lock (string.Intern(Marker))
            {
                if (AppDomain.CurrentDomain.GetData(Marker) != null) return true;
                new Harmony(HarmonyId).Patch(m,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(RaConfigCache), nameof(Before))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(RaConfigCache), nameof(After))));
                AppDomain.CurrentDomain.SetData(Marker, HarmonyId);
            }
            SaveRouter.Say("RetroArch's retroarch.cfg is read once and kept (until the file changes)");
            return true;
        }

        private static bool Before(string filePath, ref Dictionary<string, string?> __result, out (string Path, DateTime Written, long Length)? __state)
        {
            __state = null;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return true;
                var fi = new FileInfo(filePath);
                if (!fi.Exists) return true;
                lock (Cache)
                {
                    if (!Cache.TryGetValue(fi.FullName, out var c) || c.Written != fi.LastWriteTimeUtc || c.Length != fi.Length)
                    {
                        // Taken BEFORE the read: a file changed while it is read is read again next time.
                        __state = (fi.FullName, fi.LastWriteTimeUtc, fi.Length);
                        return true;
                    }
                    __result = new Dictionary<string, string?>(c.Values);
                    return false;
                }
            }
            catch { return true; }
        }

        private static void After(Dictionary<string, string?> __result, bool __runOriginal, (string Path, DateTime Written, long Length)? __state)
        {
            if (!__runOriginal || __result == null || __state is not { } s) return;
            try { lock (Cache) Cache[s.Path] = (s.Written, s.Length, new Dictionary<string, string?>(__result)); }
            catch { }
        }
    }
}
