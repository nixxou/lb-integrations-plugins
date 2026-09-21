// Save management for Xenia.
//
// Presented as a CONTAINER, the same shape the PPSSPP plugin uses: FileLocation points at something
// holding several saves, and the save is extracted from it. Here the container is the content-type
// folder 00000001 under a title, and everything about its boundaries comes from the other end of
// the wire - Argosy - because the save's fingerprint is computed over ZIP ENTRY NAMES and the two
// ends have to agree on them exactly:
//
//   * the unit is the 00000001 folder, NOT the title folder. 00000002 (DLC) and 000B0000 (title
//     updates) sit under the same title id and are excluded; syncing them as progress would push
//     gigabytes of add-ons around.
//   * the archive is rooted at the literal string "00000001", identical for every game, so hashed
//     entries read "00000001/<relative path>". The archive therefore carries NO identity of its own.
//     Argosy unpacks any archive rooted this way into any Xbox 360 destination; validating the root
//     against a title id would reject every archive they produce. We accept that rather than fix it.
//   * a restore MERGES. Argosy deletes nothing here (unlike its PSP handler), so neither do we -
//     otherwise the same upload would settle differently on the two sides.
//
// The `.header` beside the unit, which carries the display name, thumbnail and license mask, is not
// part of what travels. It goes into LiteBox's reserved `.litebox-plugin` folder inside the vault
// copy, which SaveVault excludes from every hash and from the zip served to clients - so the backup
// is complete on disk while the fingerprint still names only the save.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xenia
{
    public partial class XeniaPlugin
    {
        private const string GroupPrefix = "xenia:";
        private const string ChipText = "Saved Game";

        /// <summary>The folder LiteBox keeps out of every hash and out of the zip it serves. Mirrors
        /// SaveVault.PluginMetaDirName on the host side; a mismatch would silently put the header back
        /// into the fingerprint and break the agreement with Argosy.</summary>
        private const string MetaDirName = ".litebox-plugin";

        public override bool SupportsSaveManagement() => true;

        // ── listing ──────────────────────────────────────────────────────────

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");

                string appPath = Safe(() => args.Emulator.ApplicationPath);
                if (!XeniaPaths.IsXeniaExecutable(appPath))
                    return new GetSavesResponse("This emulator is not Xenia.");

                var layout = XeniaPaths.Resolve(appPath, Safe(() => args.Emulator.CommandLine));
                if (!Directory.Exists(layout.ContentRoot))
                {
                    Log.Info("no content directory at " + layout.ContentRoot + " (" + layout.Reason + ")");
                    return new GetSavesResponse(new List<GameSaveBase>());
                }

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var app in args.AdditionalApplications
                                    ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                {
                    var game = SafeGame(Safe(() => app.GameId));
                    var save = SaveFor(layout, Safe(() => app.ApplicationPath), game, app, seen);
                    if (save != null) found.Add(save);
                }
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                {
                    var save = SaveFor(layout, Safe(() => game.ApplicationPath), game, null, seen);
                    if (save != null) found.Add(save);
                }

                Log.Info("found " + found.Count + " save(s) under " + layout.ContentRoot);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves failed", ex);
                return new GetSavesResponse("Could not read Xenia saves: " + ex.Message);
            }
        }

        private GameSaveGame SaveFor(XeniaLayout layout, string romPath, IGame game,
                                     IAdditionalApplication app, HashSet<string> seen)
        {
            if (game == null) return null;

            var titleId = XeniaTitleId.Of(romPath);
            if (titleId == null) return null;                    // .zar and anything unreadable

            var unit = XeniaContent.ForTitleId(layout.ContentRoot, titleId);
            if (unit == null) return null;

            string gameId = Safe(() => game.Id) ?? "";
            string appId = app != null ? Safe(() => app.Id) : null;
            if (!seen.Add(unit.TitleId + "|" + (appId ?? gameId))) return null;

            return new GameSaveGame
            {
                GameId = gameId,
                AdditionalApplicationId = appId,
                // The content-type folder itself. Pointing at the title folder would drag DLC and title
                // updates into every size and freshness reading.
                FileLocation = unit.UnitPath,
                OriginalFileName = XeniaContent.SavedGameType,
                SaveGroupId = GroupPrefix + unit.TitleId,
                SaveGroupName = SaveNameOf(unit, game),
                DisplayChipText = ChipText,
                ReportedFileSizeBytes = unit.SizeBytes > 0 ? unit.SizeBytes : (long?)null,
                ReportedLastModifiedUtc = unit.LastWriteUtc == default ? (DateTime?)null : unit.LastWriteUtc,
            };
        }

        /// <summary>A readable label. The package folders inside the unit are the names the game chose,
        /// so one of those beats a bare title id; several means several saves in one unit.</summary>
        private static string SaveNameOf(XeniaSaveUnit unit, IGame game)
        {
            if (unit.Packages.Count == 1)
            {
                var name = Path.GetFileName(unit.Packages[0]);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            if (unit.Packages.Count > 1) return unit.Packages.Count + " saves";
            return Safe(() => game.Title) ?? unit.TitleId;
        }

        // ── the container contract ───────────────────────────────────────────

        public override bool IsSaveContainer(GameSaveBase save) => IsOurs(save);

        public override bool UseSaveGroupIdForPersistedMatch(GameSaveBase save) => IsOurs(save);

        /// <summary>Defensive only: a file inside a save must not become a save of its own.</summary>
        public override bool IsSecondarySaveFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;
                foreach (var segment in filePath.Split('\\', '/'))
                    if (string.Equals(segment, XeniaContent.SavedGameType, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>Nothing. The unit is a directory; companions are the sibling-FILES mechanism.</summary>
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath)
            => Array.Empty<string>();

        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (!IsOurs(save) || string.IsNullOrWhiteSpace(emulatorApplicationPath)) return true;
                var layout = XeniaPaths.Resolve(emulatorApplicationPath);
                var loc = save.FileLocation;
                if (string.IsNullOrWhiteSpace(loc) || string.IsNullOrWhiteSpace(layout.ContentRoot)) return true;
                return Path.GetFullPath(loc).StartsWith(Path.GetFullPath(layout.ContentRoot),
                                                        StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        // ── backup ───────────────────────────────────────────────────────────

        /// <summary>Extract the save into <paramref name="destinationFolder"/>.
        ///
        /// THE storage decision: the unit goes in under the literal name "00000001", because that is
        /// what Argosy roots its archive at and the hash is computed over entry names. The headers go
        /// under `.litebox-plugin`, which LiteBox excludes from the hash and from the served zip - so
        /// they survive a backup without changing what the save fingerprints as.</summary>
        public override bool TryBackupSave(GameSaveBase save, string emulatorApplicationPath,
                                           string destinationFolder, out string error)
        {
            error = null;
            try
            {
                var unit = LiveUnit(save, emulatorApplicationPath);
                if (unit == null) { error = "This Xenia save is no longer on disk."; return false; }

                if (!XeniaContent.CopyInto(unit, destinationFolder, MetaDirName, out error)) return false;
                Log.Info("extracted " + unit.TitleId + ": " + unit.Packages.Count + " package(s)"
                         + (unit.HeadersPath != null ? " + headers" : "") + " -> " + destinationFolder);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("TryBackupSave failed", ex);
                error = ex.Message;
                return false;
            }
        }

        // ── restore ──────────────────────────────────────────────────────────

        /// <summary>Put a vault copy back.
        ///
        /// This MERGES rather than replaces, matching Argosy: their restore deletes nothing for Xbox
        /// 360, so a wipe-then-extract here would make the same upload settle differently on the two
        /// devices. Headers are put back from `.litebox-plugin` when the backup carries them.</summary>
        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");

                string source = save.FileLocation;
                if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                    return new AddSaveResponse("This Xenia backup is not a folder: " + (source ?? "(none)"));

                string unitSource = Path.Combine(source, XeniaContent.SavedGameType);
                if (!Directory.Exists(unitSource))
                    return new AddSaveResponse("This Xenia backup holds no " + XeniaContent.SavedGameType + " folder.");

                string titleId = TitleIdOf(save);
                if (titleId == null)
                    return new AddSaveResponse("Could not work out which game this Xenia backup belongs to.");

                var destination = ResolveRestoreTarget(save, titleId, out string why);
                if (destination == null) return new AddSaveResponse(why);

                bool overwrite = true;
                if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                {
                    try { overwrite = args.ShouldOverwriteFunc?.Invoke() ?? true; } catch { }
                    if (!overwrite) return new AddSaveResponse("Restore cancelled.");
                }

                XeniaContent.CopyDirectory(unitSource, destination);

                // Headers sit one level up from the unit, beside the content-type folders.
                string headerSource = Path.Combine(source, MetaDirName, XeniaContent.HeadersDirName,
                                                   XeniaContent.SavedGameType);
                if (Directory.Exists(headerSource))
                {
                    var titleDir = Path.GetDirectoryName(destination);
                    if (titleDir != null)
                    {
                        XeniaContent.CopyDirectory(headerSource,
                            Path.Combine(titleDir, XeniaContent.HeadersDirName, XeniaContent.SavedGameType));
                        Log.Info("restored headers for " + titleId);
                    }
                }

                Log.Info("restored " + titleId + " -> " + destination);
                return new AddSaveResponse(new GameSaveGame
                {
                    GameId = save.GameId,
                    AdditionalApplicationId = save.AdditionalApplicationId,
                    FileLocation = destination,
                    OriginalFileName = XeniaContent.SavedGameType,
                    SaveGroupId = GroupPrefix + titleId,
                    SaveGroupName = save.SaveGroupName,
                    DisplayChipText = ChipText,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile failed", ex);
                return new AddSaveResponse("Could not restore this Xenia save: " + ex.Message);
            }
        }

        /// <summary>Where a restore lands. An existing unit for this title wins. Otherwise a profile is
        /// CHOSEN, never invented: a made-up XUID produces a directory Xenia never reads. With no
        /// profile at all, say so instead of guessing.</summary>
        private static string ResolveRestoreTarget(GameSaveBase save, string titleId, out string why)
        {
            why = null;
            string contentRoot = ContentRootFor(save);
            if (contentRoot == null)
            {
                why = "Could not locate Xenia's content folder for this game.";
                return null;
            }

            var existing = XeniaContent.ForTitleId(contentRoot, titleId);
            if (existing != null) return existing.UnitPath;

            // No save for this title yet: put it under the most recently used profile.
            var profiles = SafeDirectories(contentRoot)
                .Where(d => XeniaContent.IsHex(Path.GetFileName(d), 16)
                            && !string.Equals(Path.GetFileName(d), XeniaContent.MachineXuid,
                                              StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(NewestWriteUtc)
                .ToList();

            if (profiles.Count == 0)
            {
                why = "Xenia has no profile to restore this save into. Sign in to a profile in Xenia "
                      + "once, then try again.";
                return null;
            }
            return Path.Combine(profiles[0], titleId.ToUpperInvariant(), XeniaContent.SavedGameType);
        }

        // ── delete ───────────────────────────────────────────────────────────

        /// <summary>Must be overridden: the SDK default deletes the single path in FileLocation, which
        /// here is a directory it cannot remove.</summary>
        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                if (!IsOurs(save)) return base.RemoveSave(save);

                string loc = save?.FileLocation;
                if (string.IsNullOrWhiteSpace(loc)) return new PluginResponse(false, "This save has no location.");
                if (!Directory.Exists(loc)) return new PluginResponse(true);          // already gone

                Directory.Delete(loc, recursive: true);

                // The headers describe a save that no longer exists.
                var titleDir = Path.GetDirectoryName(loc);
                if (titleDir != null)
                {
                    var headers = Path.Combine(titleDir, XeniaContent.HeadersDirName, XeniaContent.SavedGameType);
                    try { if (Directory.Exists(headers)) Directory.Delete(headers, recursive: true); }
                    catch (Exception ex) { Log.Warn("could not remove " + headers, ex); }
                }
                Log.Info("deleted " + (TitleIdOf(save) ?? "save") + " at " + loc);
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave failed", ex);
                return new PluginResponse(false, "Could not delete this Xenia save: " + ex.Message);
            }
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static bool IsOurs(GameSaveBase save)
            => save?.SaveGroupId != null
               && save.SaveGroupId.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase);

        private static string TitleIdOf(GameSaveBase save)
        {
            var id = save?.SaveGroupId;
            if (id == null || !id.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            var rest = id.Substring(GroupPrefix.Length).Trim();
            return XeniaContent.IsHex(rest, 8) ? rest.ToUpperInvariant() : null;
        }

        private static XeniaSaveUnit LiveUnit(GameSaveBase save, string emulatorApplicationPath)
        {
            string titleId = TitleIdOf(save);
            if (titleId == null) return null;

            string contentRoot = !string.IsNullOrWhiteSpace(emulatorApplicationPath)
                ? XeniaPaths.Resolve(emulatorApplicationPath).ContentRoot
                : ContentRootFromUnitPath(save?.FileLocation);
            if (contentRoot == null) return null;

            return XeniaContent.ForTitleId(contentRoot, titleId);
        }

        /// <summary>...\content\&lt;XUID&gt;\&lt;TITLEID&gt;\00000001 -> ...\content. Used when no emulator
        /// was supplied, which is how the probe drives a backup.</summary>
        private static string ContentRootFromUnitPath(string unitPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(unitPath)) return null;
                var titleDir = Path.GetDirectoryName(unitPath);          // <TITLEID>
                var xuidDir = Path.GetDirectoryName(titleDir);           // <XUID> or content
                if (xuidDir == null) return null;
                // A master-style tree has no XUID level, so the title's parent is already content.
                return XeniaContent.IsHex(Path.GetFileName(xuidDir), 16)
                    ? Path.GetDirectoryName(xuidDir)
                    : xuidDir;
            }
            catch { return null; }
        }

        /// <summary>Xenia's content root for this save's game, for a RESTORE.
        ///
        /// Deliberately does not fall back to deriving it from the save's own FileLocation: on the way
        /// in, that path is the BACKUP COPY, not a place in Xenia's tree, and deriving a content root
        /// from it would produce a plausible-looking directory that the emulator never reads. With no
        /// data manager there is no answer, and saying so beats inventing one.</summary>
        private static string ContentRootFor(GameSaveBase save)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(save.GameId);
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm.GetEmulatorById(emuId);
                var appPath = Safe(() => emu?.ApplicationPath);
                if (XeniaPaths.IsXeniaExecutable(appPath))
                    return XeniaPaths.Resolve(appPath, Safe(() => emu.CommandLine)).ContentRoot;

                var any = dm?.GetAllEmulators()?
                    .FirstOrDefault(e => XeniaPaths.IsXeniaExecutable(Safe(() => e.ApplicationPath)));
                var anyPath = Safe(() => any?.ApplicationPath);
                if (anyPath != null) return XeniaPaths.Resolve(anyPath).ContentRoot;
            }
            catch (Exception ex) { Log.Warn("could not resolve the content folder", ex); }
            return null;
        }

        private static IEnumerable<string> SafeDirectories(string p)
        {
            try { return Directory.EnumerateDirectories(p).ToList(); } catch { return Array.Empty<string>(); }
        }

        private static DateTime NewestWriteUtc(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                                .Select(File.GetLastWriteTimeUtc)
                                .DefaultIfEmpty(DateTime.MinValue).Max();
            }
            catch { return DateTime.MinValue; }
        }

        private static IGame SafeGame(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            try { return PluginHelper.DataManager?.GetGameById(gameId); }
            catch (Exception ex) { Log.Warn("could not resolve game " + gameId, ex); return null; }
        }
    }
}
