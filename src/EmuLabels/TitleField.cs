// THE FIELD BEHIND Emulator.Title.
//
// Emulator's accessors are protected by .NET Reactor (NecroBit): their IL on disk is a decoy, the
// real body is supplied at JIT time, so Harmony cannot patch them - but they can be CALLED. The
// getter is called as is. The setter is not: it is LaunchBox's, it may flag the emulator as
// changed (DataManager.EmulatorsChanged) and so schedule a save of a title we only meant to show.
// What is written instead is the private field the getter reads.
//
// Its name is obfuscated and changes from one build to the next, so it is FOUND, not named: every
// instance string field of the runtime type is given a unique sentinel in turn, and the one that
// makes the getter return that sentinel is the field. Each field is put back at once; nothing is
// left changed, nothing else is called. Found once per runtime type.
//
// If no field qualifies, the setter is the fallback, and the log says so: the label still shows,
// and SaveGuard still keeps it off the disk.

using System;
using System.Collections.Concurrent;
using System.Reflection;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.EmuLabels
{
    internal static class TitleField
    {
        private static readonly ConcurrentDictionary<Type, FieldInfo> Found = new ConcurrentDictionary<Type, FieldInfo>();

        public static string Get(IEmulator e) => e.Title;

        public static void Set(IEmulator e, string value)
        {
            var f = Resolve(e);
            if (f != null) f.SetValue(e, value);
            else e.Title = value;
        }

        /// <summary>The field, or null when none could be proven (the caller then uses the setter).</summary>
        public static FieldInfo Resolve(IEmulator e)
            => Found.GetOrAdd(e.GetType(), _ => Find(e));

        private static FieldInfo Find(IEmulator e)
        {
            try
            {
                var type = e.GetType();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                  | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType != typeof(string) || f.IsInitOnly) continue;
                        var before = (string)f.GetValue(e);
                        var sentinel = "emu-labels-probe-" + Guid.NewGuid().ToString("N");
                        string seen;
                        try
                        {
                            f.SetValue(e, sentinel);
                            seen = e.Title;
                        }
                        finally { f.SetValue(e, before); }

                        if (ReferenceEquals(seen, sentinel) || seen == sentinel)
                        {
                            Log.Info("Title is backed by " + t.FullName + "." + f.Name);
                            return f;
                        }
                    }
                }
                Log.Info("no field backs " + type.FullName + ".Title; falling back to its setter");
            }
            catch (Exception ex) { Log.Warn("looking for the field behind Title", ex); }
            return null;
        }
    }
}
