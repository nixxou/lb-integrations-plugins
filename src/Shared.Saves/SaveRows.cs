// The shared part of a Nixx plugin's save handling: recognising its ids, tying a save to a version or
// not, and choosing where a restore writes. Ready in lot 1, wired into the plugins in lot 2
// (docs\saves-lb14-routing.md §4b).
//
// WHERE A SAVE CAME FROM IS THE ROW'S, NOT THE ID'S (Mehdi, 08/10). The row carries GameId and
// AdditionalApplicationId; our plugin fills them when it lists the save, LaunchBox keeps them, and
// LiteBox keeps them up to date (its Make Default moves a game's saves onto the version that keeps
// the old rom, its combine re-points them to the absorbing game). AddSaveFile hands the row over.
//
// THE RESTORE TARGET: the version the row is tied to, else the game - its CURRENT ApplicationPath, so
// a rom renamed (and updated in LaunchBox) is followed. A row tied to a version that no longer exists
// falls back to the game WITH a warning, never silently: written there, the save of one rom could
// overwrite the live save of another.
//
// A VERSION ON ITS GAME'S FILE is not tied to the version: same file, nothing lost, and it avoids
// LaunchBox's "Can't find the game for this save item" defect (test 9b, 07/10).
//
// IsSaveActive is called hundreds of times by LaunchBox: comparing two ids is a string comparison
// and nothing else - no disk, no parsing.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    /// <summary>Where a restore writes: the rom to derive the save's name from.</summary>
    internal sealed class RestoreTarget
    {
        public string? RomPath { get; init; }
        /// <summary>The version the target belongs to; null for the game itself.</summary>
        public string? VersionId { get; init; }
        /// <summary>Set when the target is not the one the row asked for, or is missing - for the log and the user.</summary>
        public string? Warning { get; init; }
    }

    internal static class SaveRows
    {
        /// <summary>True when <paramref name="saveGroupId"/> is our JSON written by <paramref name="emu"/>.</summary>
        public static bool IsOurs(string? saveGroupId, string emu)
        {
            var o = SaveOwner.Of(saveGroupId);
            return o.Kind == SaveOwnerKind.Nixx && string.Equals(o.Name, emu, StringComparison.Ordinal);
        }

        /// <summary>The cheap comparison behind IsSaveActive: the same string, nothing more.</summary>
        public static bool SameId(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>The AdditionalApplicationId a new row of a save made on <paramref name="versionId"/> gets:
        /// the version, unless it launches the game's own file (then null - see the header).</summary>
        public static string? TieTo(string? versionId, string? gamePath, string? versionPath, Func<string?, string?> full)
            => string.IsNullOrEmpty(versionId) || SamePath(full(gamePath), full(versionPath)) ? null : versionId;

        /// <summary>The restore target, from plain data. <paramref name="versions"/> are the game's additional
        /// versions as (Id, ApplicationPath).</summary>
        public static RestoreTarget ChooseTarget(string? rowVersionId, string? gamePath,
                                                 IEnumerable<(string Id, string? Path)> versions, Func<string?, string?> full)
        {
            string? game = full(gamePath);
            if (!string.IsNullOrEmpty(rowVersionId))
            {
                var v = (versions ?? Enumerable.Empty<(string, string?)>())
                        .FirstOrDefault(x => string.Equals(x.Id, rowVersionId, StringComparison.OrdinalIgnoreCase));
                if (v.Id == null)
                    return new RestoreTarget { RomPath = game, Warning = "The version this save belongs to no longer exists; it goes to the game." };
                var vp = full(v.Path);
                return new RestoreTarget { RomPath = vp, VersionId = v.Id, Warning = Exists(vp) ? null : "The file of this save's version is missing." };
            }
            return new RestoreTarget { RomPath = game, Warning = Exists(game) ? null : "The game's file is missing." };
        }

        /// <summary><see cref="ChooseTarget"/> for a real row: the game is looked up through the data manager.</summary>
        public static RestoreTarget TargetFor(GameSaveBase row, IDataManager dm, Func<string?, string?> full)
        {
            IGame? game = null;
            try { if (!string.IsNullOrEmpty(row?.GameId)) game = dm?.GetGameById(row!.GameId); } catch { }
            if (game == null) return new RestoreTarget { Warning = "The game of this save is not in LaunchBox any more." };

            IAdditionalApplication[] apps;
            try { apps = game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>(); }
            catch { apps = Array.Empty<IAdditionalApplication>(); }
            var versions = apps.Where(a => a != null).Select(a => (Safe(() => a.Id) ?? "", Safe(() => a.ApplicationPath)));
            return ChooseTarget(row?.AdditionalApplicationId, Safe(() => game.ApplicationPath), versions, full);
        }

        private static bool SamePath(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static bool Exists(string? p) { try { return !string.IsNullOrEmpty(p) && File.Exists(p); } catch { return false; } }

        private static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }
    }
}
