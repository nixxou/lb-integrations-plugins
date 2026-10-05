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
//   Restore   the file put in place, the restore noted in the stamp - laid into the console at the next launch (Mehdi, 05/10:
//             the heavy work at a game's opening and closing only). A backup of the older kind - the console itself, a qcow2 -
//             is put back as the console, and the save captured from it.
//   Remove    the file deleted; its save taken out of the console at the next launch.
// The savestates the same: Restore and Remove place or delete the file, the next launch rewrites the console.

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
            Saves.XemuStates.ForgetWithoutConsole(exe, titleId);
            // THE STAMP BELONGS TO THE CONSOLE too: a console deleted by hand is not a console the game emptied - read so, its save
            // would be removed. Forgotten, the save is laid into the new console at the next launch.
            var consolePath = XemuPaths.GameHdd(exe, titleId);
            var stampPath = consolePath == null ? null : Path.ChangeExtension(consolePath, ".stamp");
            if (stampPath != null && !File.Exists(consolePath) && File.Exists(stampPath)) { try { File.Delete(stampPath); Log.Info("saves: " + titleId + " - its console is gone: its stamp forgotten, its save kept"); } catch { } }
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

        /// <summary>The game's savestates and their files in step (Saves\XemuStates) - never while xemu runs.</summary>
        public static void States(string exe, string titleId, bool rewrite)
        {
            if (XemuPaths.Running(exe)) return;
            try
            {
                foreach (var line in XemuStates.Mirror(exe, titleId, rewrite, () => StampKeys(exe, titleId))) Log.Info("savestates: " + titleId + " - " + line);
            }
            catch (Exception ex) { Log.Warn("savestates: " + titleId + " could not be put in step", ex); }
        }

        public static List<XemuStateFile> StateFiles(string exe, string titleId)
        {
            var dir = XemuStates.Dir(exe, titleId);
            if (dir == null || !Directory.Exists(dir)) return new List<XemuStateFile>();
            return Directory.GetFiles(dir, "*" + XemuStates.Extension).Select(XemuStates.Read).Where(f => f != null).OrderBy(f => f.Slot).ToList();
        }

        /// <summary>The keys the console is written with - its stamp's keys= line (XboxSaveSync).</summary>
        private static string StampKeys(string exe, string titleId)
        {
            try
            {
                var stamp = Path.ChangeExtension(XemuPaths.GameHdd(exe, titleId), ".stamp");
                return File.Exists(stamp) ? File.ReadAllLines(stamp).FirstOrDefault(l => l.StartsWith("keys="))?.Substring(5) : null;
            }
            catch { return null; }
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
                if (!seen.Add(titleId + "|" + gameId + "|" + appId)) return;
                if (pack != null && File.Exists(pack))
                {
                    var info = new FileInfo(pack);
                    into.Add(Row(gameId, appId, pack, titleId, title, info.Length, info.LastWriteTimeUtc));
                }
                // Its savestates (Saves\XemuStates): the snapshots of its console, mirrored as files - exported when new.
                XemuSaveFiles.States(exe, titleId, rewrite: false);
                foreach (var f in XemuSaveFiles.StateFiles(exe, titleId))
                    into.Add(StateRow(gameId, appId, f, titleId));
            }
            catch (Exception ex) { Log.Warn("could not collect the save of " + rom, ex); }
        }

        private static GameSaveGame Row(string gameId, string appId, string file, string titleId, string name, long size, DateTime when)
            => new GameSaveGame
            {
                // Whose it is, said (Mehdi, 05/10): left empty, LaunchBox names another emulator's (cxbxr-ldr.exe) and may hand it to that plugin.
                EmulatorFileName = XemuPaths.Exe,
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

        private const string StatePrefix = "xemustate:", StateChipText = "xemu savestate";

        private static GameSaveState StateRow(string gameId, string appId, XemuStateFile f, string titleId)
        {
            var info = new FileInfo(f.Path);
            return new GameSaveState
            {
                EmulatorFileName = XemuPaths.Exe,
                GameId = gameId,
                AdditionalApplicationId = appId,
                FileLocation = f.Path,
                IsDirectory = false,
                OriginalFileName = Path.GetFileName(f.Path),
                Slot = f.Slot,
                SaveGroupId = StatePrefix + titleId + ":" + f.Slot,
                SaveGroupName = "xemu savestates",
                DisplayChipText = StateChipText + (string.IsNullOrWhiteSpace(f.Name) ? "" : " - " + f.Name),
                ReportedFileSizeBytes = info.Length,
                ReportedLastModifiedUtc = f.DateSec > 0 ? DateTimeOffset.FromUnixTimeSeconds(f.DateSec).UtcDateTime : info.LastWriteTimeUtc,
            };
        }

        /// <summary>A savestate's title id and slot, from its group "xemustate:<title id>:<slot>".</summary>
        private static (string TitleId, int Slot)? StateOf(GameSaveBase save)
        {
            var g = Safe(() => save?.SaveGroupId);
            if (g == null || !g.StartsWith(StatePrefix, StringComparison.OrdinalIgnoreCase)) return null;
            var c = g.Substring(StatePrefix.Length).Split(':');
            return c.Length == 2 && c[0].Length == 8 && int.TryParse(c[1], out var slot) ? (c[0].ToLowerInvariant(), slot) : ((string, int)?)null;
        }

        public override bool IsSaveContainer(GameSaveBase save) => false;
        public override bool IsSecondarySaveFile(string filePath) => false;
        public override IReadOnlyList<string> GetCompanionSaveFiles(string primaryFilePath) => Array.Empty<string>();

        public override bool IsSaveActive(GameSaveBase save, string emulatorApplicationPath)
        {
            try
            {
                if (StateOf(save) is (string stateTitle, int stateSlot))
                {
                    var want = XemuStates.SlotPath(ResolveFullPath(emulatorApplicationPath), stateTitle, stateSlot);
                    var have = Safe(() => save?.FileLocation);
                    return want != null && have != null && File.Exists(have) && string.Equals(Path.GetFullPath(want), Path.GetFullPath(have), StringComparison.OrdinalIgnoreCase);
                }
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
                if (save is GameSaveState || StateOf(save) != null) return AddState(save, source);
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
                    // Into its console at the next launch, whatever the stamp says - the console's version kept apart when it changed since.
                    if (XemuSaveFiles.Side(exe, titleId) is XboxSaveSide side) XboxSaveSync.MarkRestored(side);
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
                if (StateOf(save) is (string stTitle, int _) && Safe(() => save?.FileLocation) is string stPath)
                {
                    var exeS = EmulatorFor(save);
                    if (exeS != null && XemuPaths.Running(exeS)) return new PluginResponse(false, "xemu is running - close it first.");
                    if (File.Exists(stPath)) File.Delete(stPath);
                    // Its snapshot out of the console at the next launch.
                    Log.Info("removed the savestate " + stPath);
                    return new PluginResponse(true);
                }
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
                // The active save gone: out of its console at the next launch.
                Log.Info("removed the save " + path);
                return new PluginResponse(true);
            }
            catch (Exception ex)
            {
                Log.Warn("RemoveSave", ex);
                return new PluginResponse(false, "Could not remove the save: " + ex.Message);
            }
        }

        /// <summary>A savestate restored: its file put at its slot only - the console takes it at the next launch.</summary>
        private AddSaveResponse AddState(GameSaveBase save, string source)
        {
            var meta = XemuStates.Read(source);
            if (meta == null) return new AddSaveResponse("That file is not an xemu savestate of this plugin.");
            var titleId = StateOf(save)?.TitleId ?? meta.Title ?? TitleIdOfGame(save);
            if (titleId == null) return new AddSaveResponse("This savestate does not say which Xbox game it belongs to.");
            var exe = EmulatorFor(save);
            if (exe == null) return new AddSaveResponse("Could not locate the xemu installation.");
            if (XemuPaths.Running(exe)) return new AddSaveResponse("xemu is running - close it first, then restore the savestate.");
            int slot = (save as GameSaveState)?.Slot ?? StateOf(save)?.Slot ?? meta.Slot;
            if (slot <= 0) slot = meta.Slot > 0 ? meta.Slot : 1;
            var target = XemuStates.SlotPath(exe, titleId, slot);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                var tmp = target + ".lbip-restore";
                File.Copy(source, tmp, overwrite: true);
                File.Move(tmp, target, overwrite: true);
            }
            // Into the console at the next launch.
            Log.Info("restored the savestate " + Path.GetFileName(source) + " -> " + target);
            var f = XemuStates.Read(target) ?? meta;
            f.Path = target;
            return new AddSaveResponse(StateRow(Safe(() => save.GameId), Safe(() => save.AdditionalApplicationId), f, titleId));
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
