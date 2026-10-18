// The Famicom Disk System's BIOS, found where the user already keeps it and copied where puNES looks.
//
// WHERE puNES LOOKS (read in src\core\fds.c:255-330, v0.111), the first that is there:
//   1. the "fds bios file=" path of puNES.cfg ([system]);
//   2. disksys.rom in the CURRENT folder - the exe's, as LaunchBox starts an emulator;
//   3. disksys.rom in the GAME's folder;
//   4. <data folder>\bios\disksys.rom - the exe's folder when portable (PunesCfg.DataDir).
// Only its first 8 KB are read and NO HASH is checked (fds.c:322-328). WITHOUT IT (measured 18/10 on a one-side .fds made
// for the test): no message, no error - the window stays on an empty screen. So a disk game is refused at the launch,
// with the file named (PunesPlugin.PrepareCore), rather than started for nothing.
//
// THE PLUGIN puts it in place 4, bios\disksys.rom, never beside a game: copied (LbipBiosImport: by name, only what is
// missing, never a move) from RetroArch's system folder and the bios / system / firmware folders of the other emulators -
// those beside this install (Emulators\*\) and those of the library - under any of the names the dump goes by
// (PunesSystems). A dump whose SHA-256 is not the known one is still given (puNES checks nothing) and said in the log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LbIntegrations.Lbip;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Punes
{
    internal static class PunesFirmware
    {
        public const string Folder = "bios";

        /// <summary>For the probe: the library's emulators are not asked (no LaunchBox).</summary>
        internal static bool NoLibrary;

        /// <summary>bios\ of the install: where the plugin puts the dumps.</summary>
        public static string FolderOf(string exe) => Path.Combine(PunesCfg.DataDir(exe), Folder);

        /// <summary>The file puNES will read for <paramref name="bios"/> when it starts <paramref name="rom"/>, in its own
        /// order (fds.c) - or null: there is none.</summary>
        public static string Found(string exe, PunesBios bios, string rom = null)
        {
            try
            {
                if (bios.Name == "disksys.rom")
                {
                    var cfg = PunesCfg.Load(PunesCfg.MainOf(exe));
                    var given = cfg.Get("system", "fds bios file");
                    if (!string.IsNullOrWhiteSpace(given))
                    {
                        var p = Path.IsPathRooted(given) ? given : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), given);
                        if (File.Exists(p)) return Path.GetFullPath(p);
                    }
                }
                var exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
                var beside = Path.Combine(exeDir, bios.Name);
                if (File.Exists(beside)) return beside;
                if (!string.IsNullOrWhiteSpace(rom))
                {
                    var game = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rom)), bios.Name);
                    if (File.Exists(game)) return game;
                }
                var mine = Path.Combine(FolderOf(exe), bios.Name);
                return File.Exists(mine) ? mine : null;
            }
            catch (Exception ex) { Log.Warn("bios: could not look for " + bios.Name, ex); return null; }
        }

        /// <summary>The BIOS of <paramref name="systems"/> put into bios\ when they are not where puNES looks. Never throws;
        /// what is there afterwards, one entry per file. Nothing is copied while puNES runs from there.</summary>
        public static List<(PunesBios Bios, string Path)> Ensure(string exe, IEnumerable<PunesSystem> systems, Action<string> report = null, string rom = null)
        {
            var states = new List<(PunesBios, string)>();
            try
            {
                var wanted = systems.Where(s => s != null).SelectMany(s => s.Bios).Distinct().ToList();
                if (wanted.Count == 0) return states;
                var target = FolderOf(exe);
                var missing = wanted.Where(b => Found(exe, b, rom) == null).ToList();
                if (missing.Count > 0 && !PunesLaunch.Running(exe))
                {
                    // A dump already in bios\ under another of its names: puNES asks for its own name alone.
                    foreach (var b in missing)
                        foreach (var alias in b.Aliases)
                        {
                            var have = Path.Combine(target, alias);
                            if (File.Exists(have) && !File.Exists(Path.Combine(target, b.Name)))
                                try { File.Copy(have, Path.Combine(target, b.Name)); Log.Info("bios: " + alias + " in bios\\ copied as " + b.Name); } catch (Exception ex) { Log.Warn("bios: " + alias, ex); }
                        }
                    var still = missing.Where(b => Found(exe, b, rom) == null).ToList();
                    if (still.Count > 0)
                    {
                        var names = new HashSet<string>(still.SelectMany(b => b.Names), StringComparer.OrdinalIgnoreCase);
                        // LbipBiosImport logs what it copies.
                        LbipBiosImport.Import(target, Sources(exe, names), still.Select(b => new LbipBiosImport.Wanted { Name = b.Name, Aliases = b.Aliases }), report);
                    }
                }
                foreach (var b in wanted)
                {
                    var path = Found(exe, b, rom);
                    if (path != null && b.Sha256 != null)
                    {
                        var sha = Sha256Of(path);
                        if (sha != null && !string.Equals(sha, b.Sha256, StringComparison.OrdinalIgnoreCase))
                            Log.Info("bios: " + path + " has the SHA-256 " + sha + ", not the dump puNES's users know - puNES checks nothing and is given it");
                    }
                    states.Add((b, path));
                }
            }
            catch (Exception ex) { Log.Warn("bios", ex); }
            return states;
        }

        /// <summary>Where the user's dumps may be: the system, bios and firmware folders of the other emulators beside this
        /// install (Emulators\*\, RetroArch's system\ among them) and of the library's, with their sub-folders.</summary>
        private static List<LbipBiosImport.Source> Sources(string exe, HashSet<string> names)
        {
            var dirs = new List<string>();
            var own = Path.GetDirectoryName(Path.GetFullPath(exe)).TrimEnd('\\');
            try
            {
                var emulators = Path.GetDirectoryName(own);
                if (emulators != null && Directory.Exists(emulators)) dirs.AddRange(Directory.EnumerateDirectories(emulators));
            }
            catch (Exception ex) { Log.Warn("bios: could not list the other emulators", ex); }
            if (!NoLibrary)
                try
                {
                    foreach (var e in PluginHelper.DataManager?.GetAllEmulators() ?? Array.Empty<IEmulator>())
                    {
                        string app = null;
                        try { app = e?.ApplicationPath; } catch { }
                        if (string.IsNullOrWhiteSpace(app)) continue;
                        var full = PunesPlugin.ResolveFullPathForUi(app);
                        var dir = string.IsNullOrEmpty(full) ? null : Path.GetDirectoryName(full);
                        if (dir != null) dirs.Add(dir);
                    }
                }
                catch (Exception ex) { Log.Warn("bios: could not list the library's emulators", ex); }

            var sources = new List<LbipBiosImport.Source>();
            foreach (var emu in dirs.Select(d => Path.GetFullPath(d).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(emu, own, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var sub in new[] { "system", "bios", "BIOS", "Bios", "firmware", "Firmware", "data" })
                {
                    var dir = Path.Combine(emu, sub);
                    if (Directory.Exists(dir)) sources.Add(new LbipBiosImport.Source { Dir = dir, Recursive = true, Only = names });
                }
            }
            return sources;
        }

        internal static string Sha256Of(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length >= 1L << 20) return null;
                using var sha = SHA256.Create();
                using var s = File.OpenRead(info.FullName);
                return Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
            }
            catch { return null; }
        }
    }
}
