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
//      emulatorPath as LaunchBox stores it (relative to its root, or full). The answer is one of the keys of Scale - "missing"
//      followed by what is missing: "missing:CHD", "missing:BIOS", "missing:file" - or several, ';' between, each a badge
//      of its own: "missing:CHD;perfect" (Mehdi, 16/10: what keeps the game from starting, then how it runs once it does) -
//      or null:
//      "this emulator is not mine", or "nothing to say" - the line is then left as LaunchBox wrote it. Called on every
//      opening of the menu, for each line: it must answer from memory or from a cache.
//      Optional beside it (Mehdi, 16/10: "un petit tooltip avec les éventuels commentaires"):
//
//              string Note(IGame game, string emulatorPath)
//
//      what the line's tooltip says - the list's own comments on the game, or what is missing and where it goes; null
//      when there is nothing more to say. Asked only after a level, as cheaply.
//
// The scale is fixed and its words are the relay's, so every plugin's verdict reads alike. Its colours (Mehdi, 16/10): green
// for a game that can be played through, yellow only for faults that spoil it, red for one that does not get in game; cyan
// above green for perfect, blue for the plugin's own recommendation. Light badges under dark text, measured against
// LaunchBox's dark menu as the PoC's were (README, "Badge colours").
// What is NOT about how well the game runs is told apart (Mehdi, 16/10: "1942 sur Flycast, c'est pas que le jeu tourne pas,
// c'est que c'est pas un format supporté"): Not supported (lavender) - a file or set the emulator does not take at all;
// Other version (lavender too) - a game the emulator runs, but not this dump of it; Missing ... and Invalid file (orange) -
// something the user can put right. Won't run stays the list's own verdict.

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
        public string Text, Color, Source, Note;
        /// <summary>The badges after the first ("missing:CHD;perfect": Perfect), in order.</summary>
        public List<(string Text, string Color)> More = new List<(string, string)>();
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
            ("unsupported", "Not supported", "#B39DDB"),
            ("version", "Other version", "#B39DDB"),
            ("missing", "Missing", "#FFA726"),
            ("invalid", "Invalid file", "#FFA726"),
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
            var (level, who, note) = Providers.Ask(game, Safe(() => emulator.ApplicationPath));
            if (level == null) return null;
            Verdict verdict = null;
            foreach (var part in level.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var badge = Badge(part.Trim());
                if (badge.Text == null) { Log.Info(who + " answered \"" + part + "\", not a level of the scale: left alone"); continue; }
                if (verdict == null) verdict = new Verdict { Text = badge.Text, Color = badge.Color, Source = who, Note = note };
                else verdict.More.Add(badge);
            }
            return verdict;
        }

        /// <summary>"missing:CHD" -> ("Missing CHD", its colour): the level's word, then what the plugin says is missing (a word or
        /// two, kept short). (null, null) for a word that is not of the scale.</summary>
        private static (string Text, string Color) Badge(string level)
        {
            var colon = level.IndexOf(':');
            var key = colon < 0 ? level : level.Substring(0, colon);
            var what = colon < 0 ? null : level.Substring(colon + 1).Trim();
            if (what != null && what.Length > 16) what = what.Substring(0, 16);
            var step = Scale.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            return step.Key == null ? (null, null) : (string.IsNullOrEmpty(what) ? step.Text : step.Text + " " + what, step.Color);
        }

        private static string Safe(Func<string> f) { try { return f(); } catch { return null; } }

        /// <summary>The plugins' LaunchWith classes, bound once per assembly.</summary>
        private static class Providers
        {
            private sealed class Provider { public string Name; public MethodInfo Level, Note; }

            private static readonly Dictionary<Assembly, Provider> Seen = new Dictionary<Assembly, Provider>();

            public static (string Level, string Who, string Note) Ask(IGame game, string emulatorPath)
            {
                if (string.IsNullOrWhiteSpace(emulatorPath)) return (null, null, null);
                foreach (var p in All())
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var level = p.Level.Invoke(null, new object[] { game, emulatorPath }) as string;
                        if (watch.ElapsedMilliseconds > 200) Log.Info(p.Name + ".LaunchWith.Level took " + watch.ElapsedMilliseconds + " ms");
                        if (string.IsNullOrEmpty(level)) continue;
                        string note = null;
                        try { note = p.Note?.Invoke(null, new object[] { game, emulatorPath }) as string; }
                        catch (Exception ex) { Log.Warn(p.Name + ".LaunchWith.Note", ex.InnerException ?? ex); }
                        if (watch.ElapsedMilliseconds > 200) Log.Info(p.Name + ".LaunchWith.Level and Note took " + watch.ElapsedMilliseconds + " ms");
                        return (level, p.Name, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
                    }
                    catch (Exception ex) { Log.Warn(p.Name + ".LaunchWith.Level", ex.InnerException ?? ex); }
                }
                return (null, null, null);
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
                    var note = type.GetMethod("Note", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IGame), typeof(string) }, null);
                    if (note != null && note.ReturnType != typeof(string)) note = null;
                    Log.Info("labels of " + name + "'s emulators come from " + type.FullName + (note != null ? " (with notes)" : ""));
                    return new Provider { Name = name, Level = level, Note = note };
                }
                catch (Exception ex) { Log.Warn("looking at " + asm.FullName, ex); return null; }
            }
        }
    }
}
