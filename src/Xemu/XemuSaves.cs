// A game's save, for LaunchBox's Backup / Restore - IN CXBX'S FORMAT (Mehdi, 04/10): the folder E:\UDATA\<title id> of the
// game's console (hdd\games\<title id>.qcow2) packed as Cxbx's plugin packs it (Saves\XemuSaveStore), so a save moves
// between the two emulators as one file.
//
// WHAT THE HOST SEES IS ONE FILE, <xemu>\lbip-saves\<title id>.cxbxsave - a file and not a folder (IsSaveContainer false:
// the host's folder arm is the one every save defect of this pack came from).
// THE CONSOLE IS THE TRUTH, the file follows it: captured after each session (XemuSession) and whenever the host lists saves
// and the console has changed (a stamp: its size and date). A console without the title's folder takes the file with it.
//   Backup    the host copies the file.
//   Restore   the file laid into the game's console at once - the folder's content REPLACED, not merged, as Cxbx's restore
//             does - never while xemu runs. A save of either emulator: the group "xemu:<title id>" or "cxbx:<title id>". A
//             backup of the older kind - the console itself, a qcow2 - is put back as the console.
//   Remove    the title's folder taken out of the console; its cache and the rest of it kept.
// NOT HANDLED: saves a game signs with the console's own key (a few games) - moved between consoles, such a game may call them
// damaged. To come: one HDD key for every console of the pack.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LbIntegrations.Xemu.Saves;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Xemu
{
    internal static class XemuSaveFiles
    {
        public const string GroupPrefix = "xemu:", CxbxGroupPrefix = "cxbx:";
        private static readonly object Gate = new object();

        public static string PackPath(string exe, string titleId)
            => XemuPaths.Dir(exe) is string d ? Path.Combine(d, "lbip-saves", titleId + XemuSaveStore.Extension) : null;

        /// <summary>The packed save, brought up to date with the game's console - or null when the game has none. Not captured
        /// while xemu runs: the game may be writing.</summary>
        public static string Capture(string exe, string titleId)
        {
            lock (Gate)
            {
                var pack = PackPath(exe, titleId);
                var console = XemuPaths.GameHdd(exe, titleId);
                if (pack == null || console == null) return null;
                var stampFile = Path.ChangeExtension(pack, ".stamp");
                if (!File.Exists(console))
                {
                    if (File.Exists(pack) && !XemuPaths.Running(exe)) Drop(pack, stampFile, titleId, "it has no console any more");
                    return null;
                }
                var info = new FileInfo(console);
                var now = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                try { if (File.Exists(pack) && File.Exists(stampFile) && File.ReadAllText(stampFile) == now) return pack; } catch { }
                if (XemuPaths.Running(exe)) return File.Exists(pack) ? pack : null;
                try
                {
                    if (!XemuSaveStore.Capture(console, titleId, pack))
                    {
                        if (File.Exists(pack)) Drop(pack, stampFile, titleId, "its console holds no save");
                        return null;
                    }
                    File.WriteAllText(stampFile, now);
                    Log.Info("saves: " + titleId + " captured -> " + pack);
                    return pack;
                }
                catch (Exception ex)
                {
                    Log.Warn("saves: could not capture the save of " + titleId + " from " + console, ex);
                    return File.Exists(pack) ? pack : null;
                }
            }
        }

        private static void Drop(string pack, string stampFile, string titleId, string why)
        {
            try { File.Delete(pack); File.Delete(stampFile); Log.Info("saves: " + titleId + " - " + why + ": its file removed"); } catch { }
        }

        public static void Forget(string exe, string titleId)
        {
            var pack = PackPath(exe, titleId);
            if (pack != null) try { File.Delete(Path.ChangeExtension(pack, ".stamp")); } catch { }
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
                    XemuSaveFiles.Forget(exe, titleId);
                    Log.Info("restored the console of " + titleId + " -> " + hdd);
                }
                else
                {
                    if (!XemuSaveFiles.IsZip(source)) return new AddSaveResponse("That file is not an Xbox save of this plugin or of Cxbx-Reloaded's.");
                    var baseDisk = XemuPaths.BaseHdd(exe);
                    if (!File.Exists(hdd) && (baseDisk == null || !File.Exists(baseDisk)))
                        return new AddSaveResponse("xemu's console disk is missing. Update xemu from LaunchBox to download it again, then restore the save.");
                    var pack = XemuSaveFiles.PackPath(exe, titleId);
                    Directory.CreateDirectory(Path.GetDirectoryName(pack));
                    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(pack), StringComparison.OrdinalIgnoreCase)) File.Copy(source, pack, overwrite: true);
                    XemuSaveStore.Insert(hdd, baseDisk, titleId, pack);
                    XemuSaveFiles.Forget(exe, titleId);
                    Log.Info("restored the save of " + titleId + " (" + Path.GetFileName(source) + ") into " + hdd);
                }
                var refreshed = XemuSaveFiles.Capture(exe, titleId);
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
                    var hdd = XemuPaths.GameHdd(exe, titleId);
                    if (XemuSaveStore.Remove(hdd, titleId)) Log.Info("removed the save of " + titleId + " from its console");
                    XemuSaveFiles.Forget(exe, titleId);
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

        /// <summary>The title id of a save of this plugin ("xemu:") or of Cxbx's ("cxbx:") - the same format either way.</summary>
        private static string TitleIdFrom(GameSaveBase save)
        {
            var g = Safe(() => save?.SaveGroupId);
            if (g == null) return null;
            string id = null;
            if (g.StartsWith(XemuSaveFiles.GroupPrefix, StringComparison.OrdinalIgnoreCase)) id = g.Substring(XemuSaveFiles.GroupPrefix.Length);
            else if (g.StartsWith(XemuSaveFiles.CxbxGroupPrefix, StringComparison.OrdinalIgnoreCase)) id = g.Substring(XemuSaveFiles.CxbxGroupPrefix.Length);
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
