// The private statics of LaunchBox's RetroArch and Dolphin integrations the pack calls (StockRestore, lot 3/4) - their
// EXACT signatures, in one place: StockRestore finds them with these and the Compatibility page checks these. Until 16/10
// the page looked for the names only, and said "OK" on LaunchBox 14.1 b3 while the restore refused them (RetroArch's
// folder functions took a core name more, Dolphin's Restore* the v2 request first).
//
// A static whose parameter types are partly the integration's own (Dolphin's DolphinUserDataRoots) is matched by name,
// count and return type instead - exactly one must match.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Compat
{
    internal sealed class NativeStatic
    {
        public string Name = "";
        /// <summary>The exact parameter types; null: by <see cref="Count"/> and a return type assignable to AddSaveResponse.</summary>
        public Type[]? Parameters;
        public int Count;

        /// <summary>How the page names it: "GetGameSaveDirectory(IEmulator, IGame, IAdditionalApplication, Boolean)".</summary>
        public override string ToString()
            => Name + "(" + (Parameters == null ? Count + " arguments, returning AddSaveResponse" : string.Join(", ", Parameters.Select(p => p.IsByRef ? "out " + p.GetElementType()!.Name : p.Name))) + ")";
    }

    internal static class NativeStatics
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static NativeStatic Exact(string name, params Type[] parameters) => new NativeStatic { Name = name, Parameters = parameters, Count = parameters.Length };
        private static NativeStatic Returning(string name, int count) => new NativeStatic { Name = name, Count = count };

        // LaunchBox 14.0's RetroArch integration (read 08/10).
        public static readonly NativeStatic RaGameSaveDirectory = Exact("GetGameSaveDirectory", typeof(IEmulator), typeof(IGame), typeof(IAdditionalApplication), typeof(bool));
        public static readonly NativeStatic RaSaveStateDirectory = Exact("GetSaveStateDirectory", typeof(IEmulator), typeof(IGame), typeof(IAdditionalApplication), typeof(bool));
        public static readonly NativeStatic RaIsSaturn = Exact("IsSaturnSaveContext", typeof(string), typeof(IGame));
        public static readonly NativeStatic RaSaturnCompanions = Exact("CopySaturnCompanionFiles", typeof(string), typeof(string));
        public static readonly NativeStatic RaSaturnGroupId = Exact("GetSaturnGroupId", typeof(string));

        public static readonly NativeStatic[] RetroArch = { RaGameSaveDirectory, RaSaveStateDirectory, RaIsSaturn, RaSaturnCompanions, RaSaturnGroupId };

        // LaunchBox 14.0's Dolphin integration (read 08/10).
        public static readonly NativeStatic DolPlatformType = Exact("GetPlatformType", typeof(IGame), typeof(string));
        public static readonly NativeStatic DolUserRoots = Exact("BuildUserRoots", typeof(IEmulator));
        public static readonly NativeStatic DolDiscId = Exact("TryGetDiscId", typeof(string), typeof(IEmulator), typeof(string).MakeByRefType());
        public static readonly NativeStatic DolGameCube = Returning("RestoreGameCubeSave", 4);
        public static readonly NativeStatic DolWii = Returning("RestoreWiiSave", 4);
        public static readonly NativeStatic DolState = Returning("RestoreSaveState", 4);

        public static readonly NativeStatic[] Dolphin = { DolPlatformType, DolUserRoots, DolDiscId, DolGameCube, DolWii, DolState };

        /// <summary>The static on <paramref name="t"/> as described, or null (gone, changed, or more than one by count).</summary>
        public static MethodInfo? Find(Type? t, NativeStatic s)
        {
            if (t == null) return null;
            try
            {
                if (s.Parameters != null) return t.GetMethod(s.Name, Statics, null, s.Parameters, null);
                var all = t.GetMethods(Statics).Where(m => m.Name == s.Name && m.GetParameters().Length == s.Count && typeof(AddSaveResponse).IsAssignableFrom(m.ReturnType)).ToList();
                return all.Count == 1 ? all[0] : null;
            }
            catch { return null; }
        }

        /// <summary>Those of <paramref name="wanted"/> not found on <paramref name="t"/>, as the page names them.</summary>
        public static List<string> Missing(Type? t, IEnumerable<NativeStatic> wanted) => wanted.Where(s => Find(t, s) == null).Select(s => s.ToString()).ToList();
    }
}
