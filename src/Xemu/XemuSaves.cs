// A game's save, for LaunchBox's Backup / Restore - IN CXBX'S FORMAT (Mehdi, 04/10): the folder E:\UDATA\<title id> of the
// game's console (hdd\games\<title id>.qcow2) packed as Cxbx's plugin packs it (Saves\XemuSaveStore), so a save moves
// between the two emulators as one file.
//
// WHAT THE HOST SEES IS ONE FILE, <xemu>\lbip-saves\<title id>.cxbxsave - a file and not a folder (IsSaveContainer false:
// the host's folder arm is the one every save defect of this pack came from) - in the group "cxbx:<title id>", the Cxbx
// plugin's: the same save, the same format, one group whichever emulator plays the game.
// THE FILE IS THE ACTIVE SAVE, kept in step with the console by Shared.Xbox\XboxSaveSync (Mehdi, 05/10) - its stamp beside
// the game's disk, hdd\games\<title id>.stamp:
//   Launch    the save laid into the console, or the console captured, or both kept in a conflict - XboxSaveSync's rule
//   Session's end, LaunchBox listing saves (its backups)    the console captured when that is safe
//   Restore   the file put in place and laid into the console at once (xemu not running). A backup of the older kind - the
//             console itself, a qcow2 - is put back as the console, and the save captured from it.
//   Remove    the file deleted, and its save taken out of the console at once; the console's cache and the rest kept.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Xemu.Saves;
using LbIntegrations.Xbox;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    internal static class XemuSaveFiles
    {
        /// <summary>The save group, shared with the Cxbx plugin (Mehdi, 04/10): the same save in the same format, so LaunchBox sees
        /// one group for the game whichever emulator plays it - a Cxbx backup made active for a game on xemu goes where it should.</summary>
        public const string GroupPrefix = "cxbx:", OldGroupPrefix = "xemu:";

        public static string PackPath(string exe, string titleId)
            => XemuPaths.Dir(exe) is string d ? Path.Combine(d, "lbip-saves", titleId + XemuSaveStore.Extension) : null;

        /// <summary>The game's save on this xemu, for XboxSaveSync: its console is hdd\games\<title id>.qcow2.</summary>
        public static XboxSaveSide Side(string exe, string titleId)
        {
            var console = XemuPaths.GameHdd(exe, titleId);
            var pack = PackPath(exe, titleId);
            if (console == null || pack == null) return null;
            return new XboxSaveSide
            {
                TitleId = titleId,
                Pack = pack,
                StampPath = Path.ChangeExtension(console, ".stamp"),
                ConflictDir = Path.Combine(XemuPaths.Dir(exe), "lbip-conflicts"),
                ReadConsole = () => File.Exists(console) ? XemuSaveStore.Extract(console, titleId) : new List<(string, byte[])>(),
                LayIn = () => XemuSaveStore.Insert(console, XemuPaths.BaseHdd(exe), titleId, pack),
                RemoveFromConsole = () => File.Exists(console) && XemuSaveStore.Remove(console, titleId),
                NaturalKeys = () => NaturalKeys(exe),
                Log = m => Log.Info("saves: " + titleId + " - " + m),
            };
        }

        /// <summary>The save and the console in step (XboxSaveSync) - never while xemu runs. What it did, or null.</summary>
        public static string Sync(string exe, string titleId, XboxSyncMode mode)
        {
            if (XemuPaths.Running(exe)) return null;
            var side = Side(exe, titleId);
            if (side == null) return null;
            var done = XboxSaveSync.Sync(side, mode);
            if (done != null) Log.Info("saves: " + titleId + " - " + done);
            return done;
        }

        /// <summary>The active save, brought up to date when that is safe (a listing) - its path, or null when there is none.</summary>
        public static string Capture(string exe, string titleId)
        {
            try { Sync(exe, titleId, XboxSyncMode.Listing); }
            catch (Exception ex) { Log.Warn("saves: could not bring the save of " + titleId + " up to date", ex); }
            var pack = PackPath(exe, titleId);
            return pack != null && File.Exists(pack) ? pack : null;
        }

        /// <summary>The keys xemu runs with on its own: eeprom.bin's HDD key, the flash BIOS's certificate key.</summary>
        internal static SaveKeys NaturalKeys(string exe)
        {
            try
            {
                var hdd = File.Exists(XemuPaths.Eeprom(exe)) ? Eeprom.XemuEeprom.Open(File.ReadAllBytes(XemuPaths.Eeprom(exe)))?.HddKey : null;
                var cert = XemuPaths.McpxPath(exe) is string m && XemuPaths.FlashPath(exe) is string f ? XboxKeys.CertificateKeyOf(m, f) : null;
                return hdd != null && cert != null ? new SaveKeys { Hdd = hdd, Cert = cert } : null;
            }
            catch { return null; }
        }

        public static bool IsZip(string path)
        {
            try { using var a = SharpCompress.Archives.Zip.ZipArchive.Open(path); return a.Entries != null; } catch { return false; }
        }
    }

    public partial class XemuPlugin
    {
        private const string ChipText = "Xbox save";

        public override bool SupportsSaveManagement() => true;

        public override GetSavesResponse GetSaves(GetSavesArgs args)
        {
            try
            {
                if (args?.Emulator == null) return new GetSavesResponse("No emulator was supplied.");
                var exe = ResolveFullPath(Safe(() => args.Emulator.ApplicationPath));
                if (!XemuPaths.IsOurs(exe)) return new GetSavesResponse("This emulator is not an xemu of this plugin.");
                var found = new List<GameSaveBase>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var app in args.AdditionalApplications ?? (IReadOnlyCollection<IAdditionalApplication>)Array.Empty<IAdditionalApplication>())
                    Collect(exe, Safe(() => app.ApplicationPath), Safe(() => app.GameId), Safe(() => app.Id), Safe(() => PluginHelper.DataManager?.GetGameById(app.GameId)?.Title), found, seen);
                foreach (var game in args.Games ?? (IReadOnlyCollection<IGame>)Array.Empty<IGame>())
                    Collect(exe, Safe(() => game.ApplicationPath), Safe(() => game.Id), null, Safe(() => game.Title), found, seen);
                return new GetSavesResponse(found);
            }
            catch (Exception ex)
            {
                Log.Warn("GetSaves", ex);
                return new GetSavesResponse("Could not read the xemu saves: " + ex.Message);
            }
        }

        private static void Collect(string exe, string rom, string gameId, string appId, string title, List<GameSaveBase> into, HashSet<string> seen)
        {
            try
            {
                var titleId = XemuDisc.TitleIdOf(ResolveFullPath(rom), exe);
                if (titleId == null) return;
                var pack = XemuSaveFiles.Capture(exe, titleId);
                if (pack == null || !File.Exists(pack)) return;
                if (!seen.Add(titleId + "|" + gameId + "|" + appId)) return;
                var info = new FileInfo(pack);
                into.Add(Row(gameId, appId, pack, titleId, title, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) { Log.Warn("could not collect the save of " + rom, ex); }
        }

        private static GameSaveGame Row(string gameId, string appId, string file, string titleId, string name, long size, DateTime when)
            => new GameSaveGame
            {
                GameId = gameId,
                AdditionalApplicationId = appId,
                FileLocation = file,
                IsDirectory = false,
                OriginalFileName = Path.GetFileName(file),
                SaveGroupId = XemuSaveFiles.GroupPrefix + titleId,
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
                var expected = XemuSaveFiles.PackPath(ResolveFullPath(emulatorApplicationPath), titleId);
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
                // A backup of a save group says its game; a file imported by hand (Import Save Game File...) may not - then the
                // game it is imported for says it, by its disc.
                var titleId = TitleIdFrom(save) ?? TitleIdOfGame(save);
                if (titleId == null) return new AddSaveResponse("This backup does not say which Xbox game it belongs to, and the game's disc could not be read.");
                var exe = EmulatorFor(save);
                if (exe == null) return new AddSaveResponse("Could not locate the xemu installation.");
                if (XemuPaths.Running(exe)) return new AddSaveResponse("xemu is running - close it first, then restore the save.");
                var hdd = XemuPaths.GameHdd(exe, titleId);

                // The older kind of backup: the console itself.
                if (Qcow2Overlay.VirtualSize(source) > 0 && Qcow2Overlay.BackingName(source) != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(hdd));
                    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(hdd), StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = hdd + ".lbip-restore";
                        File.Copy(source, tmp, overwrite: true);
                        File.Move(tmp, hdd, overwrite: true);
                    }
                    // The console is the backup: the save is made from it.
                    Log.Info("restored the console of " + titleId + " -> " + hdd);
                    if (XemuSaveFiles.Side(exe, titleId) is XboxSaveSide side) Log.Info("saves: " + titleId + " - " + XboxSaveSync.CaptureNow(side));
                }
                else
                {
                    if (!XemuSaveFiles.IsZip(source)) return new AddSaveResponse("That file is not an Xbox save of this plugin or of Cxbx-Reloaded's.");
                    // The file is what counts: put in place, it goes into the console at the game's next launch (SyncIn).
                    var pack = XemuSaveFiles.PackPath(exe, titleId);
                    Directory.CreateDirectory(Path.GetDirectoryName(pack));
                    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(pack), StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = pack + ".lbip-restore";
                        File.Copy(source, tmp, overwrite: true);
                        File.Move(tmp, pack, overwrite: true);
                    }
                    Log.Info("restored the save of " + titleId + " (" + Path.GetFileName(source) + ") -> " + pack);
                    // Into its console at once, whatever the stamp says - the console's version kept apart when it changed since.
                    XemuSaveFiles.Sync(exe, titleId, XboxSyncMode.Restore);
                }
                var refreshed = XemuSaveFiles.PackPath(exe, titleId) is string active && File.Exists(active) ? active : null;
                if (refreshed == null) return new AddSaveResponse("The save was restored, but the console holds none for this game.");
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
                var pack = exe == null ? null : XemuSaveFiles.PackPath(exe, titleId);
                // Only the live save takes the console's folder with it - a copy elsewhere is just a file.
                if (pack != null && string.Equals(Path.GetFullPath(pack), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    if (XemuPaths.Running(exe)) return new PluginResponse(false, "xemu is running - close it first.");
                }
                if (File.Exists(path)) File.Delete(path);
                // The active save gone: out of its console at once.
                if (pack != null && string.Equals(Path.GetFullPath(pack), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    XemuSaveFiles.Sync(exe, titleId, XboxSyncMode.Launch);
                Log.Info("removed the save " + path);
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave", ex);
                return new PluginResponse(false, "Could not remove the save: " + ex.Message);
            }
        }

        /// <summary>The title id of a save of the group "cxbx:" - this plugin's and Cxbx's, the same format - or of the older "xemu:".</summary>
        private static string TitleIdFrom(GameSaveBase save)
        {
            var g = Safe(() => save?.SaveGroupId);
            if (g == null) return null;
            string id = null;
            if (g.StartsWith(XemuSaveFiles.GroupPrefix, StringComparison.OrdinalIgnoreCase)) id = g.Substring(XemuSaveFiles.GroupPrefix.Length);
            else if (g.StartsWith(XemuSaveFiles.OldGroupPrefix, StringComparison.OrdinalIgnoreCase)) id = g.Substring(XemuSaveFiles.OldGroupPrefix.Length);
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
                return rom == null ? null : XemuDisc.TitleIdOf(rom);
            }
            catch { return null; }
        }

        /// <summary>The xemu a save belongs to: the game's emulator, else any of ours. AddSaveArgs names neither.</summary>
        private static string EmulatorFor(GameSaveBase save)
        {
            try
            {
                var dm = PluginHelper.DataManager;
                var game = dm?.GetGameById(Safe(() => save?.GameId));
                var emuId = Safe(() => game?.EmulatorId);
                var emu = string.IsNullOrEmpty(emuId) ? null : dm.GetEmulatorById(emuId);
                var path = ResolveFullPath(Safe(() => emu?.ApplicationPath));
                if (XemuPaths.IsOurs(path)) return path;
                foreach (var e in dm?.GetAllEmulators() ?? new IEmulator[0])
                {
                    var p = ResolveFullPath(Safe(() => e.ApplicationPath));
                    if (XemuPaths.IsOurs(p)) return p;
                }
            }
            catch { }
            return null;
        }
    }
}
