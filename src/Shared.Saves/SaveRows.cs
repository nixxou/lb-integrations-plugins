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
// a rom renamed (and updated in LaunchBox) is followed.
//
// REFUSED, NOT REDIRECTED (Codex review, 08/10): a row tied to a version that no longer exists, a game no
// longer in LaunchBox, or versions that could not be read. Sent to the game instead, a version's save would
// be written under the game's rom name over the game's live save - a save of ANOTHER card, which LaunchBox's
// precautionary backup (the restored card's own) never copies. LiteBox and the RomM sync restore with nobody
// watching. The caller answers the restore with the warning and writes nothing.
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
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Saves
{
    /// <summary>Where a restore writes: the rom to derive the save's name from.</summary>
    internal sealed class RestoreTarget
    {
        public string? RomPath { get; init; }
        /// <summary>The version the target belongs to; null for the game itself.</summary>
        public string? VersionId { get; init; }
        /// <summary>Set when the target is missing, or refused - for the log and the user.</summary>
        public string? Warning { get; init; }
        /// <summary>True when nothing must be written: the restore is answered with <see cref="Warning"/>.</summary>
        public bool Refused { get; init; }
    }

    internal static class SaveRows
    {
        /// <summary>True when <paramref name="saveGroupId"/> is our JSON written by <paramref name="emu"/>.</summary>
        public static bool IsOurs(string? saveGroupId, string emu)
        {
            var o = SaveOwner.Of(saveGroupId);
            return o.Kind == SaveOwnerKind.Nixx && string.Equals(o.Name, emu, StringComparison.Ordinal);
        }

        /// <summary>A game's web link (Mehdi, 17/10): LaunchBox keeps a game's links as additional applications whose path is
        /// a URL, and hands them to GetSaves with the real versions. A save name derived from "https://site/" is empty, and a
        /// plugin that looks for saves by name or prefix would take saves that are not its. A Windows path never holds "://".</summary>
        public static bool IsLink(string? path)
        {
            var p = (path ?? "").Trim();
            return p.Length > 0 && (p.Contains("://") || p.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                                    || p.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Documents, pictures and videos a game's additional applications open (Mehdi, 17/10: manuals, scans, comics, readmes,
        /// trailers); the extension's case does not matter
        /// - no emulator runs them, so no save is theirs. None of them is a game's file.</summary>
        private static readonly HashSet<string> DocumentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".rtf", ".txt", ".md", ".nfo", ".htm", ".html", ".url",
            ".cbz", ".cbr", ".cb7", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
            ".mp4", ".mkv", ".avi", ".webm", ".mov", ".wmv",
        };

        /// <summary>A document or picture by its extension (DocumentExtensions).</summary>
        public static bool IsDocument(string? path)
        {
            try { return DocumentExtensions.Contains(Path.GetExtension((path ?? "").Trim())); } catch { return false; }
        }

        /// <summary>An additional application that cannot hold a save: a web link or a document.</summary>
        public static bool IsNoGame(string? path) => IsLink(path) || IsDocument(path);

        /// <summary><paramref name="args"/> without the game's web links and documents among its versions (IsNoGame): the same
        /// object when there are none, else a copy - GetSavesArgs is Emulator, Games and AdditionalApplications, nothing else.</summary>
        public static GetSavesArgs WithoutLinks(GetSavesArgs args, Action<string>? say = null)
        {
            var apps = args?.AdditionalApplications;
            if (args == null || apps == null || apps.Count == 0) return args!;
            var kept = apps.Where(a => a == null || !IsNoGame(Safe(() => a.ApplicationPath))).ToList();
            if (kept.Count == apps.Count) return args;
            say?.Invoke("GetSaves: " + (apps.Count - kept.Count) + " web link(s) or document(s) among the versions left out - no save is theirs");
            return new GetSavesArgs { Emulator = args.Emulator, Games = args.Games, AdditionalApplications = kept };
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
                    return new RestoreTarget
                    {
                        Refused = true,
                        Warning = "The version this save belongs to is not in LaunchBox any more, so it was not restored (it would have gone over the game's own save). To put it on the game, use Import Save Game File.",
                    };
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
            if (game == null) return new RestoreTarget { Refused = true, Warning = "The game of this save is not in LaunchBox any more, so it was not restored." };

            IAdditionalApplication[] apps;
            try { apps = game.GetAllAdditionalApplications() ?? Array.Empty<IAdditionalApplication>(); }
            catch
            {
                // Unreadable is not the same as gone: a row tied to a version is refused, never sent to the game.
                if (!string.IsNullOrEmpty(row?.AdditionalApplicationId))
                    return new RestoreTarget { Refused = true, Warning = "The versions of this game could not be read, so this save was not restored." };
                apps = Array.Empty<IAdditionalApplication>();
            }
            var versions = apps.Where(a => a != null).Select(a => (Safe(() => a.Id) ?? "", Safe(() => a.ApplicationPath)));
            return ChooseTarget(row?.AdditionalApplicationId, Safe(() => game.ApplicationPath), versions, full);
        }

        /// <summary><paramref name="row"/> with <paramref name="exe"/> and <paramref name="core"/> written on it (the front desk,
        /// SaveRouter): the row itself when they are already there, else a copy - every field, Slot included. The type
        /// a plugin may have derived is not kept: LaunchBox rebuilds its rows as GameSaveGame / GameSaveState anyway.</summary>
        public static GameSaveBase With(GameSaveBase row, string? exe, string? core)
        {
            exe = string.IsNullOrEmpty(exe) ? row.EmulatorFileName : exe;
            core = string.IsNullOrEmpty(core) ? row.EmulatorCore : core;
            if (string.Equals(row.EmulatorFileName, exe, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.EmulatorCore, core, StringComparison.OrdinalIgnoreCase)) return row;
            if (row is GameSaveState s)
                return new GameSaveState
                {
                    Slot = s.Slot,
                    AdditionalApplicationId = row.AdditionalApplicationId, SaveGroupId = row.SaveGroupId, MatchLineageId = row.MatchLineageId,
                    MigrationFamilyId = row.MigrationFamilyId, EmulatorCore = core, EmulatorFileName = exe, FileLocation = row.FileLocation,
                    GameId = row.GameId, Title = row.Title, SaveGroupName = row.SaveGroupName, DisplayChipText = row.DisplayChipText,
                    OriginalFileName = row.OriginalFileName, ReportedFileSizeBytes = row.ReportedFileSizeBytes, IsDirectory = row.IsDirectory,
                    ReportedLastModifiedUtc = row.ReportedLastModifiedUtc,
                };
            return new GameSaveGame
            {
                AdditionalApplicationId = row.AdditionalApplicationId, SaveGroupId = row.SaveGroupId, MatchLineageId = row.MatchLineageId,
                MigrationFamilyId = row.MigrationFamilyId, EmulatorCore = core, EmulatorFileName = exe, FileLocation = row.FileLocation,
                GameId = row.GameId, Title = row.Title, SaveGroupName = row.SaveGroupName, DisplayChipText = row.DisplayChipText,
                OriginalFileName = row.OriginalFileName, ReportedFileSizeBytes = row.ReportedFileSizeBytes, IsDirectory = row.IsDirectory,
                ReportedLastModifiedUtc = row.ReportedLastModifiedUtc,
            };
        }

        /// <summary><paramref name="row"/> with other ids (lot 4, StockDesk: a LaunchBox integration's ids in our form and back):
        /// the row itself when they are already those, else a copy - every field, Slot included. LaunchBox's own row is never
        /// changed.</summary>
        public static GameSaveBase WithIds(GameSaveBase row, string? groupId, string? lineage)
        {
            if (string.Equals(row.SaveGroupId, groupId, StringComparison.Ordinal) && string.Equals(row.MatchLineageId, lineage, StringComparison.Ordinal)) return row;
            if (row is GameSaveState s)
                return new GameSaveState
                {
                    Slot = s.Slot, AdditionalApplicationId = row.AdditionalApplicationId, SaveGroupId = groupId, MatchLineageId = lineage,
                    MigrationFamilyId = row.MigrationFamilyId, EmulatorCore = row.EmulatorCore, EmulatorFileName = row.EmulatorFileName, FileLocation = row.FileLocation,
                    GameId = row.GameId, Title = row.Title, SaveGroupName = row.SaveGroupName, DisplayChipText = row.DisplayChipText,
                    OriginalFileName = row.OriginalFileName, ReportedFileSizeBytes = row.ReportedFileSizeBytes, IsDirectory = row.IsDirectory, ReportedLastModifiedUtc = row.ReportedLastModifiedUtc,
                };
            return new GameSaveGame
            {
                AdditionalApplicationId = row.AdditionalApplicationId, SaveGroupId = groupId, MatchLineageId = lineage,
                MigrationFamilyId = row.MigrationFamilyId, EmulatorCore = row.EmulatorCore, EmulatorFileName = row.EmulatorFileName, FileLocation = row.FileLocation,
                GameId = row.GameId, Title = row.Title, SaveGroupName = row.SaveGroupName, DisplayChipText = row.DisplayChipText,
                OriginalFileName = row.OriginalFileName, ReportedFileSizeBytes = row.ReportedFileSizeBytes, IsDirectory = row.IsDirectory, ReportedLastModifiedUtc = row.ReportedLastModifiedUtc,
            };
        }

        /// <summary><paramref name="row"/> tied to the version <paramref name="versionId"/> (the front desk, SavePersist): the
        /// row itself when it already is, else a copy - every field, Slot included.</summary>
        public static GameSaveBase WithVersion(GameSaveBase row, string? versionId)
        {
            if (string.Equals(row.AdditionalApplicationId, versionId, StringComparison.OrdinalIgnoreCase)) return row;
            if (row is GameSaveState s)
                return new GameSaveState
                {
                    Slot = s.Slot, AdditionalApplicationId = versionId, SaveGroupId = row.SaveGroupId, MatchLineageId = row.MatchLineageId,
                    MigrationFamilyId = row.MigrationFamilyId, EmulatorCore = row.EmulatorCore, EmulatorFileName = row.EmulatorFileName, FileLocation = row.FileLocation,
                    GameId = row.GameId, Title = row.Title, SaveGroupName = row.SaveGroupName, DisplayChipText = row.DisplayChipText,
                    OriginalFileName = row.OriginalFileName, ReportedFileSizeBytes = row.ReportedFileSizeBytes, IsDirectory = row.IsDirectory, ReportedLastModifiedUtc = row.ReportedLastModifiedUtc,
                };
            return new GameSaveGame
            {
                AdditionalApplicationId = versionId, SaveGroupId = row.SaveGroupId, MatchLineageId = row.MatchLineageId,
                MigrationFamilyId = row.MigrationFamilyId, EmulatorCore = row.EmulatorCore, EmulatorFileName = row.EmulatorFileName, FileLocation = row.FileLocation,
                GameId = row.GameId, Title = row.Title, SaveGroupName = row.SaveGroupName, DisplayChipText = row.DisplayChipText,
                OriginalFileName = row.OriginalFileName, ReportedFileSizeBytes = row.ReportedFileSizeBytes, IsDirectory = row.IsDirectory, ReportedLastModifiedUtc = row.ReportedLastModifiedUtc,
            };
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
