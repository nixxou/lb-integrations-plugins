// The shared part of a Nixx plugin's save handling: building an id, recognising one, and choosing
// where a restore writes. Ready in lot 1, wired into the plugins in lot 2 (docs\saves-lb14-routing.md §4b).
//
// THE RESTORE TARGET (Mehdi, 08/10), in this order:
//   1. the version the ROW is tied to today (AdditionalApplicationId) - the freshest fact: LiteBox's
//      Make Default moves a game's saves onto the version that keeps the old rom;
//   2. else the id's "ver";
//   3. else the game.
// Its CURRENT ApplicationPath is taken, so a renamed rom is followed. When that file's fingerprint is
// not the id's "rom", the game's file and every version's are fingerprinted and the one that matches
// wins (a version and the game whose files were swapped). None matching only warns: LaunchBox's own
// Make Default, for one, drops the old file altogether (measured 08/10) and there is nothing to find.
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
    /// <summary>Where a restore writes: the rom to derive the save's name from, and what was concluded.</summary>
    internal sealed class RestoreTarget
    {
        public string? RomPath { get; init; }
        /// <summary>The version the target belongs to; null for the game itself.</summary>
        public string? VersionId { get; init; }
        /// <summary>False when the id has a fingerprint and no file of the game matches it.</summary>
        public bool Matches { get; init; } = true;
        /// <summary>True when the target was found by fingerprint rather than by the ids.</summary>
        public bool FoundByPrint { get; init; }
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

        /// <summary>True when a save of <paramref name="version"/> must stay tied to the game: the version
        /// launches the game's own file.</summary>
        public static bool OnGamesFile(string? gamePath, string? versionPath, Func<string?, string?> full)
            => SamePath(full(gamePath), full(versionPath));

        /// <summary>The origin keys of a new row: the game, the version when it is on ANOTHER file (else null,
        /// see the header), and the fingerprint of the file that row's rom is.</summary>
        public static (string? Game, string? Ver, RomPrintData? Rom) Origin(
            string? gameId, string? gamePath, string? versionId, string? versionPath, Func<string?, string?> full)
        {
            bool tie = !string.IsNullOrEmpty(versionId) && !OnGamesFile(gamePath, versionPath, full);
            var rom = RomPrint.Of(full(tie ? versionPath : gamePath));
            return (NullIfEmpty(gameId), tie ? versionId : null, rom);
        }

        /// <summary>The restore target, from plain data - see the header for the order. <paramref name="versions"/>
        /// are the game's additional versions as (Id, ApplicationPath).</summary>
        public static RestoreTarget ChooseTarget(string? rowVersionId, SaveIdData? id, string? gamePath,
                                                 IEnumerable<(string Id, string? Path)> versions, Func<string?, string?> full)
        {
            var list = (versions ?? Enumerable.Empty<(string, string?)>()).ToList();
            string? wanted = NullIfEmpty(rowVersionId) ?? NullIfEmpty(id?.Ver);
            string? path = gamePath, verId = null;
            if (wanted != null)
            {
                var v = list.FirstOrDefault(x => string.Equals(x.Id, wanted, StringComparison.OrdinalIgnoreCase));
                if (v.Id != null) { path = v.Path; verId = v.Id; }
            }

            var print = id?.Rom;
            if (print == null) return new RestoreTarget { RomPath = full(path), VersionId = verId };
            if (print.SameAs(RomPrint.Of(full(path)))) return new RestoreTarget { RomPath = full(path), VersionId = verId };

            // Not the file it was made on: look for that file among the game's.
            if (print.SameAs(RomPrint.Of(full(gamePath))))
                return new RestoreTarget { RomPath = full(gamePath), FoundByPrint = true };
            foreach (var v in list)
                if (print.SameAs(RomPrint.Of(full(v.Path))))
                    return new RestoreTarget { RomPath = full(v.Path), VersionId = v.Id, FoundByPrint = true };

            return new RestoreTarget
            {
                RomPath = full(path), VersionId = verId, Matches = false,
                Warning = "This save comes from another file than the one the game launches now.",
            };
        }

        /// <summary><see cref="ChooseTarget"/> for a real row: the game is looked up through the data manager.</summary>
        public static RestoreTarget TargetFor(GameSaveBase row, IDataManager dm, Func<string?, string?> full)
        {
            SaveId.TryRead(row?.SaveGroupId, out var id);
            var gameId = NullIfEmpty(row?.GameId) ?? id?.Game;
            IGame? game = null;
            try { if (gameId != null) game = dm?.GetGameById(gameId); } catch { }
            if (game == null) return new RestoreTarget { Matches = false, Warning = "The game of this save is not in LaunchBox any more." };

            IAdditionalApplication[] apps;
            try { apps = game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>(); }
            catch { apps = Array.Empty<IAdditionalApplication>(); }
            var versions = apps.Where(a => a != null).Select(a => (Safe(() => a.Id) ?? "", Safe(() => a.ApplicationPath)));
            return ChooseTarget(row?.AdditionalApplicationId, id, Safe(() => game.ApplicationPath), versions, full);
        }

        private static bool SamePath(string? a, string? b)
            => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
               && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

        private static T? Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }
    }
}
