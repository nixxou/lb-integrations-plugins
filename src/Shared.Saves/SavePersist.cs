// Plan A (09/10): every row the desk hands LaunchBox gets a persisted row at its own path, as LaunchBox itself writes one.
// A listed row found by path among the persisted rows keeps its own id and lineage; one not found there is given, by
// ApplyPersistedMetadata, the id and lineage of any persisted row of the game whose original name is its file name
// (measured 08-09/10: RASnes9x's Secret of Mana .srm taken into SUPER ZSNES's card).
//
// WHERE LAUNCHBOX KEEPS THEM (measured 09/10): Game.Saves and each version's AdditionalApplicationBase.Saves
// (BlockingCollection<GameSave>). The listed row and its persisted row must be on the SAME one, or they never meet: the
// persisted row is then counted as a backup, and the listed one, matched against the game's own rows (the vault copies), is
// taken into another card by its file name.
//
// LaunchBox moves the persisted row of a save of the game onto the version that runs the game's own file ("Play
// Version..."), though the row was listed with no version (measured 09/10, Secret of Mana). So the desk lists such a row on
// that version itself (TieLikeLaunchBox), as RetroArch's rows are listed: listed and persisted then meet.
// Listed untied instead, the manual Backup Save still failed with "Can't find the game for this save item!" - on
// RetroArch's own card too (09/10): a LaunchBox defect of a game with a version on its own file, whatever we list (test 9b's
// "untied avoids it" held for the Restore only; the tied Restore went through on 09/10 too).
// A persisted row of one of OUR saves found on another game or version than its listed row is moved to the listed one.
// Rows that are not ours (RetroArch's, LaunchBox's) are never touched.
//
// By reflection - the SDK has no save API. Missing or changed: nothing written, logged.

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
    internal static class SavePersist
    {
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private sealed class Owner
        {
            public Owner(object collection, MethodInfo add, MethodInfo? take, string? appId, string? appPath)
            { Collection = collection; Add = add; Take = take; AppId = appId; AppPath = appPath; }
            public object Collection { get; }
            public MethodInfo Add { get; }
            public MethodInfo? Take { get; }
            public string? AppId { get; }
            public string? AppPath { get; }
            public IEnumerable<object> Items => ((IEnumerable)Collection).Cast<object>().ToList();
        }

        /// <summary><paramref name="rows"/> with each row of the game tied to no version tied to the version that runs the
        /// game's own file, when there is one - where LaunchBox puts its persisted row (see the header).</summary>
        public static IReadOnlyList<GameSaveBase> TieLikeLaunchBox(string gameId, IReadOnlyList<GameSaveBase> rows, Action<string> say)
        {
            try
            {
                var game = PluginHelper.DataManager?.GetGameById(gameId);
                if (game == null) return rows;
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                string Full(string? p) { if (string.IsNullOrWhiteSpace(p)) return ""; try { return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(root, p)); } catch { return p!; } }
                var gamePath = Full(game.ApplicationPath);
                if (gamePath.Length == 0) return rows;
                var version = (game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                              .FirstOrDefault(a => a != null && string.Equals(Full(a.ApplicationPath), gamePath, StringComparison.OrdinalIgnoreCase))?.Id;
                if (string.IsNullOrEmpty(version)) return rows;
                return rows.Select(r => r != null && string.IsNullOrEmpty(r.AdditionalApplicationId)
                                        && (string.IsNullOrEmpty(r.GameId) || string.Equals(r.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                                        ? SaveRows.WithVersion(r, version) : r).ToList();
            }
            catch (Exception ex) { say("tie failed, rows left as listed: " + ex.Message); return rows; }
        }

        /// <summary>The live files of the card <paramref name="saveGroupId"/> of the game: the full paths of its persisted rows
        /// that are not in LaunchBox's vault (&lt;root&gt;\Saves). Read-only, by reflection; nothing when it cannot be read.
        /// A restore asks it which install a backup came from (SaveRouter.RestoreEmulator).</summary>
        public static IReadOnlyList<string> LivePathsOf(string? gameId, string? saveGroupId)
        {
            var found = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(saveGroupId)) return found;
                var game = PluginHelper.DataManager?.GetGameById(gameId);
                if (game == null) return found;
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                var vault = Path.Combine(root, "Saves") + Path.DirectorySeparatorChar;
                foreach (var (h, _) in PersistedRows.Holders(game))
                {
                    if (h.GetType().GetProperty("Saves", Inst)?.GetValue(h) is not IEnumerable rows) continue;
                    foreach (var s in rows.Cast<object>().ToList())
                    {
                        var t = s.GetType();
                        if (!string.Equals(t.GetProperty("SaveGroupId")?.GetValue(s) as string, saveGroupId, StringComparison.Ordinal)) continue;
                        var p = t.GetProperty("FilePath")?.GetValue(s) as string;
                        if (string.IsNullOrWhiteSpace(p)) continue;
                        string full;
                        try { full = Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(root, p)); } catch { continue; }
                        if (!full.StartsWith(vault, StringComparison.OrdinalIgnoreCase)) found.Add(full);
                    }
                }
            }
            catch { }
            return found;
        }

        /// <summary>Gives each of <paramref name="rows"/> (with an id) a persisted row at its path, on the game or version
        /// the row is listed on; moves there the persisted row of one of our saves found on another.</summary>
        public static void Ensure(string gameId, IEnumerable<GameSaveBase> rows, Action<string> say)
        {
            try
            {
                var game = PluginHelper.DataManager?.GetGameById(gameId);
                if (game == null) return;
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                string Full(string? p) { if (string.IsNullOrWhiteSpace(p)) return ""; try { return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(root, p)); } catch { return p!; } }

                Type? saveType = null;
                Owner? OwnerOf(object o, string? appId, string? appPath)
                {
                    var prop = o.GetType().GetProperty("Saves", Inst);
                    var coll = prop?.GetValue(o);
                    var item = prop?.PropertyType.IsGenericType == true ? prop.PropertyType.GetGenericArguments()[0] : null;
                    var add = item == null ? null : coll?.GetType().GetMethod("Add", new[] { item });
                    if (coll == null || add == null) return null;
                    var take = coll.GetType().GetMethod("TryTake", new[] { item!.MakeByRefType() });
                    saveType ??= item;
                    return new Owner(coll, add, take, appId, appPath);
                }

                var gameOwner = OwnerOf(game, null, Full(game.ApplicationPath));
                if (gameOwner == null) { say("persist: Game.Saves not found - nothing written"); return; }
                var owners = new List<Owner> { gameOwner };
                foreach (var app in game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>())
                {
                    if (app == null) continue;
                    var o = OwnerOf(app, app.Id, Full(app.ApplicationPath));
                    if (o != null) owners.Add(o);
                }
                string Where(Owner o) => o.AppId == null ? "the game" : "version " + o.AppId;

                string? P(object o, string n) { try { return saveType!.GetProperty(n)?.GetValue(o) as string; } catch { return null; } }
                void Set(object s, string n, object? v) { var pi = saveType!.GetProperty(n); if (pi != null && pi.CanWrite) try { pi.SetValue(s, v); } catch { } }

                // Takes the persisted row <s> out of <o>: the collection has no Remove, so it is emptied and filled again.
                bool TakeOut(Owner o, object s)
                {
                    if (o.Take == null) return false;
                    var kept = new List<object>();
                    var found = false;
                    var args = new object?[1];
                    bool done = false;
                    try
                    {
                        while ((bool)o.Take.Invoke(o.Collection, args)!)
                        {
                            if (!found && ReferenceEquals(args[0], s)) found = true; else kept.Add(args[0]!);
                            args[0] = null;
                        }
                        done = true;
                    }
                    finally
                    {
                        // Every row taken out goes back, whatever failed (review 10/10: a failure in between lost them) - the
                        // one being moved too when it failed (second review: it was lost, never added to its new owner).
                        if (!done && found) kept.Add(s);
                        foreach (var k in kept) try { o.Add.Invoke(o.Collection, new[] { k }); } catch (Exception ex) { say("persist: a row could not be put back: " + ex.Message); }
                    }
                    return found;
                }

                foreach (var r in rows)
                {
                    if (r == null || string.IsNullOrEmpty(r.SaveGroupId) || string.IsNullOrEmpty(r.FileLocation)) continue;
                    var full = Full(r.FileLocation);
                    if (full.Length == 0) continue;
                    var target = string.IsNullOrEmpty(r.AdditionalApplicationId) ? gameOwner
                                 : owners.FirstOrDefault(o => string.Equals(o.AppId, r.AdditionalApplicationId, StringComparison.OrdinalIgnoreCase));
                    if (target == null) continue;   // a version no longer there: nothing written

                    bool At(object s) => string.Equals(Full(P(s, "FilePath")), full, StringComparison.OrdinalIgnoreCase);
                    bool Mine(object s) => At(s) && string.Equals(P(s, "SaveGroupId"), r.SaveGroupId, StringComparison.Ordinal);
                    bool ours = SaveOwner.Of(r.SaveGroupId).Kind == SaveOwnerKind.Nixx;
                    if (target.Items.Any(At))
                    {
                        // Already there - and a twin of ours on another owner (left by LaunchBox re-persisting a row we
                        // had moved, measured 09/10: four on Secret of Mana) is taken away: one row per path.
                        if (ours)
                            foreach (var o in owners.Where(o => o != target).ToList())
                                foreach (var twin in o.Items.Where(Mine).ToList())
                                    say(TakeOut(o, twin) ? "persist: removed a twin row of " + full + " on " + Where(o)
                                                         : "persist: could not remove a twin row of " + full + " on " + Where(o));
                        continue;
                    }

                    // Ours, persisted on another game or version than the listed row: moved to the listed one.
                    var from = !ours ? null : owners.FirstOrDefault(o => o != target && o.Items.Any(Mine));
                    if (from != null)
                    {
                        var moved = from.Items.First(Mine);
                        if (TakeOut(from, moved))
                        {
                            Set(moved, "AdditionalApplicationId", target.AppId);
                            target.Add.Invoke(target.Collection, new[] { moved });
                            say("persist: moved the row of " + full + " (" + r.SaveGroupId + ") from " + Where(from) + " to " + Where(target));
                        }
                        else say("persist: could not take the row of " + full + " out of " + Where(from) + " - left there");
                        continue;
                    }
                    if (owners.Any(o => o.Items.Any(At))) continue;   // someone else's row at that path: never doubled

                    var n = Activator.CreateInstance(saveType!, true)!;
                    Set(n, "GameId", r.GameId); Set(n, "AdditionalApplicationId", target.AppId); Set(n, "FilePath", full);
                    Set(n, "SaveGroupId", r.SaveGroupId); Set(n, "MatchLineageId", string.IsNullOrEmpty(r.MatchLineageId) ? r.SaveGroupId : r.MatchLineageId);
                    Set(n, "EmulatorFileName", r.EmulatorFileName); Set(n, "EmulatorCore", r.EmulatorCore);
                    Set(n, "SaveGroupName", r.SaveGroupName); Set(n, "DisplayChipText", r.DisplayChipText);
                    Set(n, "OriginalFileName", string.IsNullOrEmpty(r.OriginalFileName) ? Path.GetFileName(full) : r.OriginalFileName);
                    if (r is GameSaveState st) { var sp = saveType!.GetProperty("Slot"); if (sp != null) try { sp.SetValue(n, st.Slot); } catch { } }
                    target.Add.Invoke(target.Collection, new[] { n });
                    say("persist: added a row at its own path for " + full + " (" + r.SaveGroupId + ") on " + Where(target));
                }
            }
            catch (Exception ex) { say("persist failed, nothing more written: " + (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }
    }
}
