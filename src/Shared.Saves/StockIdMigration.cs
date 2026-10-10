// The cards LaunchBox already persisted for its own integrations' saves, given our ids (lot 4, docs\saves-lot4.md) - and
// given their own back when our ids are turned off (the `stockIds` switch; lot 7 does the same at uninstall, on the XML).
//
// A card is the persisted rows of one game sharing a SaveGroupId (Game.Saves and each version's Saves, where SavePersist
// found them, measured 09/10). Changed only when its owner is certain:
//   - a native form that names it (pcsx2:, saturn-);
//   - a GUID (LaunchBox's own, made for a row listed with no id) whose rows' exes are all driven by ONE of LaunchBox's
//     integrations and by none of the pack - otherwise left as it is, and logged;
//   - already one of our ids: left as it is (the pass can run any number of times).
// The new id keeps the old one in `native` - two cards of one file (Pokemon Cristal: the game's and its version's) stay
// two. A row's MatchLineageId follows the card it names: its own, or another card of the game migrated in the same pass.
//
// In memory only: LaunchBox writes its XML when it saves its data, as for any change made in it. Before the first pass that
// changes anything, the platform XML files are copied aside (three copies kept).

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    /// <summary>A persisted row as the migration sees it.</summary>
    internal sealed class PersistedRow
    {
        public string GameId = "";
        public string? SaveGroupId;
        public string? MatchLineageId;
        public string? EmulatorFileName;
        public string? FilePath;
        /// <summary>The version it is persisted on (null: the game).</summary>
        public string? AppId;
        public bool IsState;
        /// <summary>The LaunchBox object, for the adapter; the plan never reads it.</summary>
        public object? Source;
    }

    internal static class StockIdMigration
    {
        public sealed class Change
        {
            public Change(PersistedRow row, string? groupId, string? lineage) { Row = row; GroupId = groupId; Lineage = lineage; }
            public PersistedRow Row { get; }
            public string? GroupId { get; }
            public string? Lineage { get; }
        }

        /// <summary>What to change, forward (to our ids) or back (to theirs). <paramref name="stockOfExe"/>: the one LaunchBox
        /// integration ("lb.retroarch") that drives every emulator of the library with that exe name, when no pack plugin
        /// does; null otherwise. <paramref name="freshGuid"/>: a new id for a card that had none (back only).</summary>
        public static IReadOnlyList<Change> Plan(IReadOnlyList<PersistedRow> rows, Func<string?, string?> stockOfExe, bool forward,
                                                 Func<string> freshGuid, Action<string>? say = null, Func<string?, bool>? only = null)
        {
            var changes = new List<Change>();
            foreach (var game in rows.GroupBy(r => r.GameId, StringComparer.OrdinalIgnoreCase))
            {
                // Old id → new id, for every card of the game that changes.
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var card in game.GroupBy(r => r.SaveGroupId ?? "", StringComparer.Ordinal))
                {
                    var id = card.Key;
                    if (id.Length == 0) continue;
                    var next = forward ? Forward(id, card.ToList(), stockOfExe, say, only) : Back(id, freshGuid, only);
                    if (next != null && next != id) map[id] = next;
                }
                if (map.Count == 0) continue;
                foreach (var r in game)
                {
                    var id = r.SaveGroupId ?? "";
                    // An empty lineage stays empty: the way back is then exact.
                    string? lineage = r.MatchLineageId;
                    if (!string.IsNullOrEmpty(lineage) && map.TryGetValue(lineage!, out var mapped)) lineage = mapped;
                    var group = map.TryGetValue(id, out var g) ? g : r.SaveGroupId;
                    if (group != r.SaveGroupId || lineage != r.MatchLineageId) changes.Add(new Change(r, group, lineage));
                }
            }
            return changes;
        }

        private static string? Forward(string id, List<PersistedRow> card, Func<string?, string?> stockOfExe, Action<string>? say, Func<string?, bool>? only)
        {
            var owner = SaveOwner.Of(id);
            string? emu;
            switch (owner.Kind)
            {
                case SaveOwnerKind.LaunchBox:
                    if (id[0] == '{') return null;           // already ours
                    emu = owner.Name;
                    break;
                case SaveOwnerKind.Unknown:
                    var emus = card.Select(r => stockOfExe(r.EmulatorFileName)).Distinct().ToList();
                    if (emus.Count != 1 || emus[0] == null)
                    {
                        say?.Invoke("ids: card " + id + " of game " + card[0].GameId + " left as it is - its owner is not certain ("
                                    + string.Join(", ", card.Select(r => r.EmulatorFileName ?? "no exe").Distinct()) + ")");
                        return null;
                    }
                    emu = emus[0];
                    break;
                default:
                    return null;                             // a pack plugin's, LiteBox's
            }
            if (only != null && !only(emu)) return null;   // an integration left in its own form
            return StockIds.Encode(emu!, id, card.All(r => r.IsState), null, null);
        }

        private static string? Back(string id, Func<string> freshGuid, Func<string?, bool>? only)
        {
            var owner = SaveOwner.Of(id);
            if (owner.Kind != SaveOwnerKind.LaunchBox || id[0] != '{') return null;
            if (only != null && !only(owner.Name)) return null;
            return StockIds.Reverse(id, freshGuid);
        }

        // ── the adapter: LaunchBox's objects, by reflection ──────────────────

        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly object Gate = new object();

        /// <summary>One pass over <paramref name="games"/> (every game of the library when null): the changes made, or -1 when
        /// nothing could be read. Nothing written on the disk but the copies of the platform XML, made before the first change
        /// of the process.</summary>
        public static int Run(IEnumerable<IGame>? games, bool forward, Action<string> say, Func<string?, bool>? only = null)
        {
            lock (Gate)
            {
                try
                {
                    var all = (games ?? PluginHelper.DataManager?.GetAllGames() ?? Array.Empty<IGame>()).Where(g => g != null).ToList();
                    var rows = new List<PersistedRow>();
                    foreach (var g in all) rows.AddRange(Read(g));
                    var changes = Plan(rows, StockOfExe, forward, () => Guid.NewGuid().ToString("N"), say, only);
                    if (changes.Count == 0) return 0;
                    BackUpXml(say);
                    int done = 0;
                    foreach (var c in changes)
                    {
                        if (c.Row.Source is not { } s) continue;
                        if (Set(s, "SaveGroupId", c.GroupId) && Set(s, "MatchLineageId", c.Lineage)) done++;
                    }
                    say("ids: " + done + " persisted row(s) given " + (forward ? "our ids" : "their own ids back")
                        + " (" + changes.Select(c => c.Row.GameId).Distinct().Count() + " game(s))");
                    return done;
                }
                catch (Exception ex) { say("ids: pass failed, nothing more changed: " + (ex.InnerException ?? ex).Message); return -1; }
            }
        }

        /// <summary>The persisted rows of a game: Game.Saves and each version's Saves.</summary>
        internal static IEnumerable<PersistedRow> Read(IGame game)
        {
            foreach (var (holder, appId) in PersistedRows.Holders(game))
            {
                if (holder.GetType().GetProperty("Saves", Inst)?.GetValue(holder) is not IEnumerable saves) continue;
                foreach (var s in saves.Cast<object>().ToList())
                {
                    var t = s.GetType();
                    string? P(string n) { try { return t.GetProperty(n)?.GetValue(s) as string; } catch { return null; } }
                    object? slot = null;
                    try { slot = t.GetProperty("Slot")?.GetValue(s); } catch { }
                    yield return new PersistedRow
                    {
                        GameId = P("GameId") ?? game.Id ?? "", SaveGroupId = P("SaveGroupId"), MatchLineageId = P("MatchLineageId"),
                        EmulatorFileName = P("EmulatorFileName"), FilePath = P("FilePath"), AppId = appId, IsState = slot != null, Source = s,
                    };
                }
            }
        }

        private static bool Set(object o, string name, string? value)
        {
            try
            {
                var p = o.GetType().GetProperty(name);
                if (p == null || !p.CanWrite) return false;
                if (!Equals(p.GetValue(o), value)) p.SetValue(o, value);
                return true;
            }
            catch { return false; }
        }

        /// <summary>The LaunchBox integration that drives every emulator of the library with this exe name - when one does,
        /// and no plugin of the pack drives any of them.</summary>
        private static string? StockOfExe(string? exe)
        {
            if (string.IsNullOrWhiteSpace(exe)) return null;
            string? found = null;
            foreach (var e in SaveRouter.LibraryEmulators())
            {
                string? path = null;
                try { path = e.ApplicationPath; } catch { }
                if (!string.Equals(Path.GetFileName(path ?? ""), exe, StringComparison.OrdinalIgnoreCase)) continue;
                var d = SaveRouter.DriverOf(e)?.Emu;
                if (d == null || !d.StartsWith(SaveOwner.LaunchBoxPrefix, StringComparison.Ordinal)) return null;
                if (found != null && found != d) return null;
                found = d;
            }
            return found;
        }

        private static bool _backedUp;

        /// <summary>The platform XML files copied to Local\Plugins\.data\nixx-saves-backup\&lt;time&gt;\ once per process, before
        /// the first change; the three newest copies kept.</summary>
        private static void BackUpXml(Action<string> say)
        {
            if (_backedUp) return;
            _backedUp = true;
            try
            {
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                var source = Path.Combine(root, "Data", "Platforms");
                if (!Directory.Exists(source)) return;
                var store = Path.Combine(root, "Local", "Plugins", ".data", "nixx-saves-backup");
                var target = Path.Combine(store, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                Directory.CreateDirectory(target);
                foreach (var f in Directory.GetFiles(source, "*.xml")) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
                foreach (var old in Directory.GetDirectories(store).OrderByDescending(d => d, StringComparer.Ordinal).Skip(3))
                    try { Directory.Delete(old, true); } catch { }
                say("ids: the platform XML files copied to " + target + " before the first change");
            }
            catch (Exception ex) { say("ids: the platform XML could not be copied aside: " + ex.Message); }
        }
    }

    /// <summary>Where LaunchBox keeps a game's persisted save rows: the game itself and each of its versions.</summary>
    internal static class PersistedRows
    {
        public static IEnumerable<(object Holder, string? AppId)> Holders(IGame game)
        {
            // LaunchBox's own objects: a game handed through a view (SaveGameView) has no Saves of its own.
            game = SaveGameView.Real(game);
            yield return (game, null);
            foreach (var a in game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                if (a != null) yield return (a, a.Id);
        }
    }
}
