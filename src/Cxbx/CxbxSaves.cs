// A game's save, for LaunchBox's Backup / Restore (Mehdi, 02/10: one save per game).
//
// ON THE XBOX a game's saves are a folder of its own: E:\UDATA\<title id>\ - one subfolder per save slot, each with its
// SaveMeta.xbx and the game's files, and TitleMeta.xbx / TitleImage.xbx beside them. Cxbx-Reloaded keeps E: at
// <data>\EmuDisk\Partition1. The title id is the XBE certificate's (Xbe.cs).
//
// WHAT THE HOST SEES IS ONE FILE, <data>\lbip-saves\<title id>.cxbxsave: that folder packed into a zip, the same bytes
// for the same content (sorted ordinal, every entry stamped 1980-01-01, stored, no zip64 - Shared.Snapshot's SnapFile
// rules, with subfolders). A file and not the folder because the host's folder arm is the one every save defect of
// this pack came from (memory: save-container-vs-file) - IsSaveContainer answers false, the file is copied as it is.
//
// THE FILE IS THE ACTIVE SAVE (Mehdi, 04/10: "dans lbip-saves\ je veux avoir les saves actives", for both Xbox emulators),
// kept in step with the folder by Shared.Xbox\XboxSaveSync (Mehdi, 05/10): every comparison of whole contents, the stamp of
// the last agreement in <data>\lbip-stamps\<title id>.stamp - with the console, not beside the save. At a launch the save is
// laid out (its folder's content REPLACED, not merged: a save slot the backup does not hold must not survive it), or the
// folder captured, or both kept in a conflict; at a session's end and when LaunchBox lists saves, the folder captured when
// that is safe; a Restore or a Remove only places or deletes the file (Mehdi, 05/10), the folder reached at the next launch
// - a Restore noted in the stamp so that launch lays it in whatever the stamp says.
// TDATA (E:\TDATA\<title id>, the game's own cache and settings) is NOT part of the save: to be measured on real games
// before it is.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Cxbx
{
    internal static class CxbxSaves
    {
        public const string GroupPrefix = "cxbx:";
        public const string Extension = ".cxbxsave";
        private static readonly DateTime Stamp = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly object Gate = new object();

        public static string LiveFolder(string exe, string titleId)
        {
            var udata = CxbxPaths.UdataDir(exe);
            return udata == null ? null : Path.Combine(udata, titleId);
        }

        public static string PackPath(string exe, string titleId)
        {
            var data = CxbxPaths.DataDir(exe);
            return data == null ? null : Path.Combine(data, "lbip-saves", titleId + Extension);
        }

        /// <summary>The live folder of <paramref name="titleId"/>, whatever case the game wrote its name in.</summary>
        private static string FindLive(string exe, string titleId)
        {
            var udata = CxbxPaths.UdataDir(exe);
            if (udata == null || !Directory.Exists(udata)) return null;
            try { return Directory.GetDirectories(udata).FirstOrDefault(d => string.Equals(Path.GetFileName(d), titleId, StringComparison.OrdinalIgnoreCase)); }
            catch { return null; }
        }

        /// <summary>The game's save on this Cxbx-Reloaded, for Shared.Xbox\XboxSaveSync: its console is the folder
        /// E:\UDATA\<title id> (whatever case the game wrote its name in), its stamp <data>\lbip-stamps\<title id>.stamp.</summary>
        public static LbIntegrations.Xbox.XboxSaveSide Side(string exe, string titleId)
        {
            var data = CxbxPaths.DataDir(exe);
            var pack = PackPath(exe, titleId);
            var live = LiveFolder(exe, titleId);
            if (data == null || pack == null || live == null) return null;
            return new LbIntegrations.Xbox.XboxSaveSide
            {
                TitleId = titleId,
                Pack = pack,
                StampPath = Path.Combine(data, "lbip-stamps", titleId + ".stamp"),
                ConflictDir = Path.Combine(data, "lbip-conflicts"),
                ReadConsole = () => FolderFiles(FindLive(exe, titleId)),
                LayIn = () => { var folder = FindLive(exe, titleId) ?? live; if (!Unpack(pack, folder, out var error)) throw new IOException("the save file could not be laid out: " + error); },
                RemoveFromConsole = () => { var folder = FindLive(exe, titleId); if (folder == null || !Directory.Exists(folder)) return false; Directory.Delete(folder, recursive: true); return true; },
                NaturalKeys = () => NaturalKeys(exe),
                Log = m => Log.Info("saves: " + titleId + " - " + m),
            };
        }

        /// <summary>A folder's files by their path in it ('/' separated) - empty when there is none.</summary>
        private static List<(string Name, byte[] Data)> FolderFiles(string folder)
        {
            var files = new List<(string, byte[])>();
            if (folder == null || !Directory.Exists(folder)) return files;
            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                files.Add((Path.GetFullPath(f).Substring(root.Length).Replace('\\', '/'), File.ReadAllBytes(f)));
            return files;
        }

        /// <summary>The save and the console in step (XboxSaveSync) - never while a loader runs. What it did, or null.</summary>
        public static string Sync(string exe, string titleId, LbIntegrations.Xbox.XboxSyncMode mode)
        {
            if (CxbxPaths.LoaderRunning()) return null;
            var side = Side(exe, titleId);
            if (side == null) return null;
            var done = LbIntegrations.Xbox.XboxSaveSync.Sync(side, mode);
            if (done != null) Log.Info("saves: " + titleId + " - " + done);
            return done;
        }

        /// <summary>The active save, brought up to date when that is safe (a listing) - its path, or null when there is none.</summary>
        public static string Capture(string exe, string titleId)
        {
            try { Sync(exe, titleId, LbIntegrations.Xbox.XboxSyncMode.Listing); }
            catch (Exception ex) { Log.Warn("saves: could not bring the save of " + titleId + " up to date", ex); }
            var pack = PackPath(exe, titleId);
            return pack != null && File.Exists(pack) ? pack : null;
        }

        /// <summary>The keys Cxbx-Reloaded runs with as its files are now: EEPROM.bin's HDD key (kept in clear), keys.bin's
        /// certificate key or zero.</summary>
        internal static LbIntegrations.Xbox.SaveKeys NaturalKeys(string exe)
        {
            try
            {
                var data = CxbxPaths.DataDir(exe);
                var eeprom = data == null ? null : Path.Combine(data, "EEPROM.bin");
                if (eeprom == null || !File.Exists(eeprom)) return null;
                var b = File.ReadAllBytes(eeprom);
                if (b.Length != 256) return null;
                return new LbIntegrations.Xbox.SaveKeys { Hdd = b.Skip(0x1C).Take(16).ToArray(), Cert = CxbxOptions.CertificateKey(data) };
            }
            catch { return null; }
        }

        /// <summary>A folder into one zip, deterministic, through a .part file moved into place.</summary>
        public static bool Pack(string folder, string target, out string error)
        {
            error = null;
            string partial = target + ".part";
            try
            {
                var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
                var names = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                                     .Select(f => Path.GetFullPath(f).Substring(root.Length).Replace('\\', '/'))
                                     .OrderBy(n => n, StringComparer.Ordinal).ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new ZipWriter(output, new ZipWriterOptions(CompressionType.None)))
                    foreach (var name in names)
                    {
                        using var source = File.OpenRead(Path.Combine(root, name.Replace('/', '\\')));
                        writer.Write(name, source, new ZipWriterEntryOptions { CompressionType = CompressionType.None, ModificationDateTime = Stamp, EnableZip64 = false });
                    }
                File.Move(partial, target, overwrite: true);
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; try { File.Delete(partial); } catch { } return false; }
        }

        /// <summary>A packed save laid out as <paramref name="folder"/> - its content REPLACED. Through a sibling folder
        /// swapped in, so a broken file leaves the old save where it was.</summary>
        public static bool Unpack(string pack, string folder, out string error)
        {
            error = null;
            var fresh = folder + ".lbip-restore";
            try
            {
                if (Directory.Exists(fresh)) Directory.Delete(fresh, recursive: true);
                Directory.CreateDirectory(fresh);
                var root = Path.GetFullPath(fresh) + "\\";
                using (var archive = ZipArchive.Open(pack))
                    foreach (var e in archive.Entries)
                    {
                        if (e.IsDirectory || string.IsNullOrEmpty(e.Key) || LbIntegrations.Xbox.XboxSaveKeys.IsKeysEntry(e.Key)) continue;
                        var path = Path.GetFullPath(Path.Combine(root, e.Key.Replace('/', '\\')));
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { error = "an entry escapes the save's folder: " + e.Key; return false; }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using var s = e.OpenEntryStream();
                        using var f = File.Create(path);
                        s.CopyTo(f);
                    }
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                Directory.CreateDirectory(Path.GetDirectoryName(folder));
                Directory.Move(fresh, folder);
                return true;
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
            finally { try { if (Directory.Exists(fresh)) Directory.Delete(fresh, recursive: true); } catch { } }
        }

        public static bool IsZip(string path)
        {
            try { using var a = ZipArchive.Open(path); return a.Entries != null; } catch { return false; }
        }
    }

    public partial class CxbxPlugin
    {
        private const string ChipText = "Xbox save";

        public override bool SupportsSaveManagement() => true;

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!CxbxPaths.IsCxbx(exe)) return new GetSavesResponse("This emulator is not Cxbx-Reloaded's loader.");

                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                    Collect(exe, Safe(() => app.ApplicationPath), Safe(() => app.GameId), Safe(() => app.Id), Safe(() => PluginHelper.DataManager?.GetGameById(app.GameId)?.Title), found, seen);
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    Collect(exe, Safe(() => game.ApplicationPath), Safe(() => game.Id), null, Safe(() => game.Title), found, seen);
                Log.Verbose("found " + found.Count + " save(s)");
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves", ex);
                return new GetSavesResponse("Could not read the Cxbx-Reloaded saves: " + ex.Message);
            }
        }

        private static void Collect(string exe, string rom, string gameId, string appId, string title, List<GameSaveBase> into, HashSet<string> seen)
        {
            try
            {
                var titleId = CxbxGame.TitleIdOf(ResolveFullPath(rom));
                if (titleId == null) return;
                var pack = CxbxSaves.Capture(exe, titleId);
                if (pack == null || !File.Exists(pack)) return;
                if (!seen.Add(titleId + "|" + gameId + "|" + appId)) return;
                var info = new FileInfo(pack);
                into.Add(Row(gameId, appId, pack, titleId, title, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) { Log.Warn("could not collect the save of " + rom, ex); }
        }

        private static GameSaveGame Row(string gameId, string appId, string pack, string titleId, string name, long size, DateTime when)
            => new GameSaveGame
            {
                // Whose it is, said (Mehdi, 05/10): left empty, LaunchBox names whichever emulator played last - xemu's saves share the group.
                EmulatorFileName = CxbxPaths.Loader,
                GameId = gameId,
                AdditionalApplicationId = appId,
                FileLocation = pack,
                IsDirectory = false,
                OriginalFileName = Path.GetFileName(pack),
                SaveGroupId = CxbxSaves.GroupPrefix + titleId,
                SaveGroupName = string.IsNullOrWhiteSpace(name) ? "Xbox save " + titleId : name,
                DisplayChipText = ChipText,
                ReportedFileSizeBytes = size > 0 ? size : (long?)null,
                ReportedLastModifiedUtc = when == default ? (DateTime?)null : when,
            };

        public override bool IsSaveContainer(GameSaveBase save) => false;
        public override bool IsSecondarySaveFile(string filePath) => false;
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                var titleId = TitleIdFrom(save);
                if (titleId == null) return false;
                var expected = CxbxSaves.PackPath(ResolveFullPath(emulatorApplicationPath), titleId);
                var actual = Safe(() => save?.FileLocation);
                return expected != null && actual != null && File.Exists(actual)
                       && string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public override AddSaveResponse AddSaveFile(AddSaveArgs args)
        {
            try
            {
                var save = args?.SaveToAdd;
                if (save == null) return new AddSaveResponse("No save was supplied.");
                var source = Safe(() => save.FileLocation);
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return new AddSaveResponse("This Xbox backup is not a file: " + source);
                // An xemu savestate is xemu's plugin's, never a save of this one: its zip would be laid out as the game's save.
                if (save is GameSaveState || (Safe(() => save.SaveGroupId) ?? "").StartsWith("xemustate:", StringComparison.OrdinalIgnoreCase))
                    return new AddSaveResponse("This is an xemu savestate - restore it on the game's xemu (Nixx-Xemu), not on Cxbx-Reloaded.");
                // A backup of a save group says its game; a file imported by hand (Import Save Game File...) may not - then the
                // game it is imported for says it, by its disc. A save of xemu's plugin ("xemu:") is the same format.
                var titleId = TitleIdFrom(save) ?? TitleIdOfGame(save);
                if (titleId == null) return new AddSaveResponse("This backup does not say which Xbox game it belongs to, and the game's disc could not be read.");
                if (!CxbxSaves.IsZip(source)) return new AddSaveResponse("That file is not an Xbox save of this plugin.");
                if (CxbxPaths.LoaderRunning()) return new AddSaveResponse("Cxbx-Reloaded is running - close it first, then restore the save.");

                var exe = EmulatorFor(save);
                if (exe == null) return new AddSaveResponse("Could not locate the Cxbx-Reloaded installation.");
                var pack = CxbxSaves.PackPath(exe, titleId);
                var live = CxbxSaves.LiveFolder(exe, titleId);
                if (pack == null || live == null) return new AddSaveResponse("Cxbx-Reloaded has not been set up yet: start it once, then restore the save.");

                bool same = string.Equals(Path.GetFullPath(source), Path.GetFullPath(pack), StringComparison.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.GetDirectoryName(pack));
                if (!same) File.Copy(source, pack, overwrite: true);
                // Into its console at the next launch (XboxSaveSync): laid out, or - both changed - the console's version kept apart first.
                if (CxbxSaves.Side(exe, titleId) is LbIntegrations.Xbox.XboxSaveSide side) LbIntegrations.Xbox.XboxSaveSync.MarkRestored(side);
                var refreshed = pack;
                Log.Info("restored the save of " + titleId + " -> " + live);
                var info = new FileInfo(refreshed);
                return new AddSaveResponse(Row(Safe(() => save.GameId), Safe(() => save.AdditionalApplicationId), refreshed, titleId,
                                               Safe(() => save.SaveGroupName), info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex)
            {
                Log.Warn("AddSaveFile", ex);
                return new AddSaveResponse("Could not restore the save: " + ex.Message);
            }
        }

        public override PluginResponse RemoveSave(GameSaveBase save)
        {
            try
            {
                var titleId = TitleIdFrom(save);
                var path = Safe(() => save?.FileLocation);
                if (titleId == null || path == null) return base.RemoveSave(save);
                var exe = EmulatorFor(save);
                var pack = exe == null ? null : CxbxSaves.PackPath(exe, titleId);
                // Only the live save takes its folder with it - a copy elsewhere is just a file.
                if (pack != null && string.Equals(Path.GetFullPath(pack), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    if (CxbxPaths.LoaderRunning()) return new PluginResponse(false, "Cxbx-Reloaded is running - close it first.");
                }
                if (File.Exists(path)) File.Delete(path);
                // The active save gone: out of its console at the next launch (XboxSaveSync).
                Log.Info("removed the save " + path);
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave", ex);
                return new PluginResponse(false, "Could not remove the save: " + ex.Message);
            }
        }

        private static string TitleIdFrom(GameSaveBase save)
        {
            var g = Safe(() => save?.SaveGroupId);
            if (g == null) return null;
            string id = null;
            if (g.StartsWith(CxbxSaves.GroupPrefix, StringComparison.OrdinalIgnoreCase)) id = g.Substring(CxbxSaves.GroupPrefix.Length);
            else if (g.StartsWith("xemu:", StringComparison.OrdinalIgnoreCase)) id = g.Substring("xemu:".Length);
            id = id?.Trim().ToLowerInvariant();
            return id != null && id.Length == 8 && uint.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? id : null;
        }

        /// <summary>The title id of the game a save is added for, read from its disc - for a file imported without its group.</summary>
        private static string TitleIdOfGame(GameSaveBase save)
        {
            try
            {
                var game = PluginHelper.DataManager?.GetGameById(Safe(() => save?.GameId));
                var rom = ResolveFullPath(Safe(() => game?.ApplicationPath));
                return rom == null ? null : CxbxGame.TitleIdOf(rom);
            }
            catch { return null; }
        }

        /// <summary>The loader a save belongs to: the game's emulator, else any of ours. AddSaveArgs names neither.</summary>
        private static string EmulatorFor(GameSaveBase save)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(Safe(() => save?.GameId));
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm.GetEmulatorById(emuId);
                var path = ResolveFullPath(Safe(() => emu?.ApplicationPath));
                if (CxbxPaths.IsCxbx(path)) return path;
                var any = dm?.GetAllEmulators()?.FirstOrDefault(e => CxbxPaths.IsCxbx(Safe(() => e.ApplicationPath)));
                var anyPath = ResolveFullPath(Safe(() => any?.ApplicationPath));
                return CxbxPaths.IsCxbx(anyPath) ? anyPath : null;
            }
            catch { return null; }
        }
    }
}
