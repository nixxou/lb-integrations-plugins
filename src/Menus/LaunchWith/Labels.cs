// WHAT AN EMULATOR'S LINE OF "LAUNCH WITH" SAYS FOR A GAME - the PoC's labels (src\LaunchWithLabels\README.md), taken into
// the relay so the plugins of the pack can say it themselves.
//
// Asked in this order, the first answer wins:
//   1. emu-labels.json, the PoC's hand-written table (CompatTable) - kept as the user's override, and for the emulators no
//      plugin of the pack drives (MAME, RetroArch...);
//   2. the plugin of the pack that drives the emulator, by a NAME CONTRACT like the game menus (Menus.cs):
//
//          public static class LbIntegrations.<assembly name>.LaunchWith
//              string Level(IGame game, string emulatorPath)
//
//      emulatorPath as LaunchBox stores it (relative to its root, or full). The answer is one of the keys of Scale, or null:
//      "this emulator is not mine", or "nothing to say" - the line is then left as LaunchBox wrote it. Called on every
//      opening of the menu, for each line: it must answer from memory or from a cache.
//
// The scale is fixed and its words are the relay's, so every plugin's verdict reads alike. Its colours (Mehdi, 16/10): green
// for a game that can be played through, yellow only for faults that spoil it, red for one that does not get in game; cyan
// above green for perfect, blue for the plugin's own recommendation. Light badges under dark text, measured against
// LaunchBox's dark menu as the PoC's were (README, "Badge colours").

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Menus.LaunchWith
{
    internal sealed class Verdict
    {
        public string Text, Color, Source;
    }

    internal static class Labels
    {
        public static readonly CompatTable Table = new CompatTable(Log.Beside("emu-labels.json"));

        /// <summary>The levels a plugin may answer, their words and badge colours.</summary>
        public static readonly IReadOnlyList<(string Key, string Text, string Color)> Scale = new[]
        {
            ("perfect", "Perfect", "#4DD0E1"),
            ("recommended", "Recommended", "#64B5F6"),
            ("playable", "Playable", "#66BB6A"),
            ("glitches", "Major issues", "#FFD54F"),
            ("nostart", "Won't run", "#FF7961"),
            ("untested", "Untested", "#BDBDBD"),
        };

        public static string Format => Table.Format;
        public static string BadgeText => Table.BadgeText;

        public static void Refresh() => Table.Refresh();

        /// <summary>The label of <paramref name="emulator"/>'s line for <paramref name="game"/>, or null: none.</summary>
        public static Verdict For(IGame game, IEmulator emulator)
        {
            if (game == null || emulator == null) return null;
            var title = Safe(() => emulator.Title);
            var raw = Table.RawLabelFor(game, emulator, title);
            if (raw != null) return new Verdict { Text = raw, Color = Table.ColorFor(raw) ?? Scale.FirstOrDefault(s => s.Text.Equals(raw, StringComparison.OrdinalIgnoreCase)).Color, Source = "emu-labels.json" };
            var (level, who) = Providers.Ask(game, Safe(() => emulator.ApplicationPath));
            if (level == null) return null;
            var step = Scale.FirstOrDefault(s => s.Key.Equals(level, StringComparison.OrdinalIgnoreCase));
            if (step.Key == null) { Log.Info(who + " answered \"" + level + "\", not a level of the scale: left alone"); return null; }
            return new Verdict { Text = step.Text, Color = step.Color, Source = who };
        }

        private static string Safe(Func<string> f) { try { return f(); } catch { return null; } }

        /// <summary>The plugins' LaunchWith classes, bound once per assembly.</summary>
        private static class Providers
        {
            private sealed class Provider { public string Name; public MethodInfo Level; }

            private static readonly Dictionary<Assembly, Provider> Seen = new Dictionary<Assembly, Provider>();

            public static (string Level, string Who) Ask(IGame game, string emulatorPath)
            {
                if (string.IsNullOrWhiteSpace(emulatorPath)) return (null, null);
                foreach (var p in All())
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var level = p.Level.Invoke(null, new object[] { game, emulatorPath }) as string;
                        if (watch.ElapsedMilliseconds > 200) Log.Info(p.Name + ".LaunchWith.Level took " + watch.ElapsedMilliseconds + " ms");
                        if (!string.IsNullOrEmpty(level)) return (level, p.Name);
                    }
                    catch (Exception ex) { Log.Warn(p.Name + ".LaunchWith.Level", ex.InnerException ?? ex); }
                }
                return (null, null);
            }

            private static List<Provider> All()
            {
                var found = new List<Provider>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Provider p;
                    lock (Seen) if (!Seen.TryGetValue(asm, out p)) Seen[asm] = p = Bind(asm);
                    if (p != null) found.Add(p);
                }
                return found;
            }

            private static Provider Bind(Assembly asm)
            {
                try
                {
                    if (asm.IsDynamic) return null;
                    var name = asm.GetName().Name;
                    if (string.IsNullOrEmpty(name)) return null;
                    var type = asm.GetType("LbIntegrations." + name + ".LaunchWith", throwOnError: false);
                    if (type == null) return null;
                    var level = type.GetMethod("Level", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IGame), typeof(string) }, null);
                    if (level == null || level.ReturnType != typeof(string))
                    {
                        Log.Info(type.FullName + " is there but not as expected (string Level(IGame, string))");
                        return null;
                    }
                    Log.Info("labels of " + name + "'s emulators come from " + type.FullName);
                    return new Provider { Name = name, Level = level };
                }
                catch (Exception ex) { Log.Warn("looking at " + asm.FullName, ex); return null; }
            }
        }
    }
}
