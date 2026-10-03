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
// THE FOLDER IS THE TRUTH, the file follows it: packed again after each session (CxbxSession) and whenever the host
// lists saves and the folder has changed (a stamp: files, bytes, newest date). A folder gone takes its file with it.
// RESTORE writes the file and lays it out as the folder at once - the folder's content REPLACED, not merged: a save
// slot the backup does not hold must not survive it.
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

        /// <summary>The packed save, brought up to date with its folder - or null when the game has none. Not packed while
        /// a loader runs: the game may be writing.</summary>
        public static string Capture(string exe, string titleId)
        {
            lock (Gate)
            {
                var pack = PackPath(exe, titleId);
                if (pack == null) return null;
                var live = FindLive(exe, titleId);
                var stampFile = Path.ChangeExtension(pack, ".stamp");
                if (live == null || !Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories).Any())
                {
                    if (File.Exists(pack) && !CxbxPaths.LoaderRunning())
                    {
                        try { File.Delete(pack); File.Delete(stampFile); Log.Info("saves: " + titleId + " has no save any more - its file removed"); } catch { }
                    }
                    return null;
                }
                var now = StampOf(live);
                try { if (File.Exists(pack) && File.Exists(stampFile) && File.ReadAllText(stampFile) == now) return pack; } catch { }
                if (CxbxPaths.LoaderRunning()) return File.Exists(pack) ? pack : null;
                if (!Pack(live, pack, out var error)) { Log.Warn("saves: could not pack " + live + ": " + error); return File.Exists(pack) ? pack : null; }
                File.WriteAllText(stampFile, now);
                Log.Info("saves: " + titleId + " packed -> " + pack);
                return pack;
            }
        }

        private static string StampOf(string dir)
        {
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
            return files.Count + "|" + files.Sum(f => f.Length) + "|" + (files.Count == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks));
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
                        if (e.IsDirectory || string.IsNullOrEmpty(e.Key)) continue;
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
                var titleId = TitleIdFrom(save);
                if (titleId == null) return new AddSaveResponse("This backup does not say which Xbox game it belongs to.");
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
                if (!CxbxSaves.Unpack(pack, live, out var error)) return new AddSaveResponse("Could not lay the save out: " + error);
                var refreshed = CxbxSaves.Capture(exe, titleId) ?? pack;
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
                    var live = CxbxSaves.LiveFolder(exe, titleId);
                    if (live != null && Directory.Exists(live)) Directory.Delete(live, recursive: true);
                    try { File.Delete(Path.ChangeExtension(pack, ".stamp")); } catch { }
                }
                if (File.Exists(path)) File.Delete(path);
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
            if (g == null || !g.StartsWith(CxbxSaves.GroupPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            var id = g.Substring(CxbxSaves.GroupPrefix.Length).Trim().ToLowerInvariant();
            return id.Length == 8 && uint.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? id : null;
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
