// Two installs of one pack plugin, at listing time (docs\saves-two-installs.md §4, decided with Mehdi 12/10).
//
// The pack's ids name a save by its file name only, so two installs of ares listing Zelda's .ram gave ONE id: LaunchBox
// filed both files in one card, the second install's live file taken for a backup of the first (measured 12/10). A card
// made from now on carries "inst", a hint of the install it was made on - CRC32 of the folder of the emulator's exe -,
// so two installs give two ids, two cards.
//
// HOW LAUNCHBOX FILES A LISTED FILE (measured 08/10, tests 10-12): first by its PATH among the persisted rows (then the old
// id stays, whatever the plugin gives), else by its id (the same id joins that card, a new one makes a new card). So the
// listed id is given here so that a known file keeps its card and only a NEW card gets the hint:
//   1. a persisted row of the game at the file's path: its id (whatever it is - an old card stays as it is, no migration);
//   2. else the ONE card of the same save (its id without "inst") whose live files are all gone, or that has none (a
//      renamed or moved emulator folder, a plugin that persists only its vault copies): adopted, its history kept;
//   3. else the id with this install's hint.
// The hint is never corrected afterwards, never used as a destination: it only breaks a tie at a restore (SaveRouter).

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    internal static class InstallIds
    {
        /// <summary>For the probe: the persisted rows of a game, instead of LaunchBox's data.</summary>
        internal static Func<string?, IReadOnlyList<SavePersist.Persisted>>? RowsForTests;

        /// <summary>The hint of an install: CRC32 of its exe's folder - full, lower case, relative to LaunchBox's root when it
        /// is under it (moving the whole LaunchBox changes nothing) - as 8 hex digits. Null for no path.</summary>
        public static string? Hint(string? exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return null;
            try
            {
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")).TrimEnd(Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.IsPathRooted(exePath) ? exePath! : Path.Combine(root, exePath!));
                var dir = (Path.GetDirectoryName(full) ?? "").TrimEnd(Path.DirectorySeparatorChar);
                if (dir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) dir = dir.Substring(root.Length + 1);
                return Crc32(dir.Replace('/', '\\').ToLowerInvariant()).ToString("x8");
            }
            catch { return null; }
        }

        /// <summary>The id without its hint - what two installs' cards of one save have in common. Null when not our JSON.</summary>
        public static string? Base(string? id)
        {
            if (!SaveId.TryRead(id, out var d) || d == null) return null;
            return SaveId.Write(new SaveIdData { Emu = d.Emu, Kind = d.Kind, Sys = d.Sys, By = d.By, Id = d.Id, Slot = d.Slot, Native = d.Native, Dir = d.Dir });
        }

        /// <summary>The hint an id carries, or null.</summary>
        public static string? HintOf(string? id) => SaveId.TryRead(id, out var d) ? d?.Inst : null;

        private static string WithHint(string id, string hint)
        {
            SaveId.TryRead(id, out var d);
            return SaveId.Write(new SaveIdData { Emu = d!.Emu, Kind = d.Kind, Sys = d.Sys, By = d.By, Id = d.Id, Slot = d.Slot, Native = d.Native, Dir = d.Dir, Inst = hint });
        }

        /// <summary>The rows of <paramref name="emu"/> listed by the install <paramref name="exePath"/>, each with the id the
        /// rules above give it. Rows of anyone else, and rows not in our JSON, are left as they are.</summary>
        public static List<GameSaveBase> Apply(string emu, string? exePath, IReadOnlyCollection<GameSaveBase> rows, Action<string>? say)
        {
            var list = rows.ToList();
            try
            {
                if (!LbIntegrations.Compat.Compat.On("twoInstalls")) return list;
                var hint = Hint(exePath);
                if (hint == null) return list;
                var byGame = new Dictionary<string, IReadOnlyList<SavePersist.Persisted>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < list.Count; i++)
                {
                    var row = list[i];
                    if (row == null || string.IsNullOrEmpty(row.GameId) || !SaveId.TryRead(row.SaveGroupId, out var d) || d == null) continue;
                    if (!string.Equals(d.Emu, emu, StringComparison.Ordinal) || d.Inst != null) continue;
                    if (!byGame.TryGetValue(row.GameId!, out var persisted))
                        byGame[row.GameId!] = persisted = (RowsForTests ?? SavePersist.RowsOf)(row.GameId);
                    var id = IdFor(row, row.SaveGroupId!, hint, persisted, out var why);
                    if (id == row.SaveGroupId) continue;
                    list[i] = SaveRows.WithIds(row, id, row.MatchLineageId);
                    say?.Invoke("install " + hint + ": " + Path.GetFileName(row.FileLocation ?? "") + " " + why);
                }
            }
            catch (Exception ex) { say?.Invoke("install hints not given: " + ex.Message); }
            return list;
        }

        /// <summary>The id one listed row takes (rules 1-3 of the header).</summary>
        internal static string IdFor(GameSaveBase row, string listed, string hint, IReadOnlyList<SavePersist.Persisted> persisted, out string why)
        {
            var path = Full(row.FileLocation);
            // 1. The file is known: LaunchBox keeps its card whatever we say - say the same.
            var atPath = persisted.FirstOrDefault(p => !p.Vault && path != null && string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            if (atPath.SaveGroupId != null) { why = "known at its path, its card's id kept"; return atPath.SaveGroupId; }
            // 2. The one card of this save left without a live file: this file is its new place.
            var key = Base(listed);
            var orphans = persisted.Where(p => p.SaveGroupId != null && Base(p.SaveGroupId) == key)
                                   .GroupBy(p => p.SaveGroupId!, StringComparer.Ordinal)
                                   .Where(g => g.Where(p => !p.Vault).All(p => !Exists(p.Path)))
                                   .Select(g => g.Key).ToList();
            if (orphans.Count == 1) { why = "adopted the card whose file is gone"; return orphans[0]; }
            // 3. A card of its own, with this install's hint.
            why = orphans.Count > 1 ? "a new card (" + orphans.Count + " cards without a file, none chosen)" : "a new card";
            return WithHint(listed, hint);
        }

        private static bool Exists(string path) { try { return File.Exists(path) || Directory.Exists(path); } catch { return true; } }

        private static string? Full(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
                return Path.GetFullPath(Path.IsPathRooted(path) ? path! : Path.Combine(root, path!)).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch { return null; }
        }

        private static readonly uint[] Table = MakeTable();
        private static uint[] MakeTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
            return t;
        }

        /// <summary>CRC-32 (IEEE) of the string's UTF-8 bytes.</summary>
        internal static uint Crc32(string s)
        {
            uint c = 0xFFFFFFFFu;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(s)) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
