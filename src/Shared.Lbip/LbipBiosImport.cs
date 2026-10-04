// The user's BIOS files, COPIED into an emulator's own folder at install from where they already are (Mehdi, 04/10) -
// so LaunchBox's BIOS check finds them at once. Name-based: a file is the one asked for under any of its names. The DSi
// side adds the NAND dumps on top of this (Shared.Dsi\DsiBiosImport); Flycast uses it as it is (FlycastBios.Import).
//
// ONLY WHAT IS MISSING: a file already in the target under any of its names is never replaced. The sources are left as
// they are - a copy, not a move. A source is searched RECURSIVELY or not, as it says: RetroArch's system folder is
// (system\dc\, system\nds\...), a folder of arcade romsets is not - its BIOS sets sit beside the games (ArcadeRomDirs).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Lbip
{
    internal static class LbipBiosImport
    {
        /// <summary>One file wanted: the name it is copied under, and the names it may have in a source.</summary>
        internal sealed class Wanted
        {
            public string Name;
            public string[] Aliases = Array.Empty<string>();
            public IEnumerable<string> Names => new[] { Name }.Concat(Aliases ?? Array.Empty<string>());
        }

        /// <summary>Where to look: a folder, searched with its sub-folders or not, and - when <see cref="Only"/> is set -
        /// for those names alone (a romset folder is asked for the arcade BIOS sets, not for a console's BIOS).</summary>
        internal sealed class Source
        {
            public string Dir;
            public bool Recursive;
            public HashSet<string> Only;
        }

        /// <summary>Copy into <paramref name="target"/> what it is missing, from <paramref name="sources"/> in order: within
        /// a source, the declared name before an alias, then the nearest folder. What was copied, one line each. Never
        /// throws; a file that cannot be copied is logged and skipped.</summary>
        public static List<string> Import(string target, IEnumerable<Source> sources, IEnumerable<Wanted> files,
                                          Action<string> report = null, Func<bool> cancelled = null)
        {
            var copied = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(target)) return copied;
                var full = Path.GetFullPath(target);
                var wanted = (files ?? Enumerable.Empty<Wanted>()).ToList();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var source in sources ?? Enumerable.Empty<Source>())
                {
                    string dir;
                    try { dir = string.IsNullOrEmpty(source?.Dir) ? null : Path.GetFullPath(source.Dir); } catch { dir = null; }
                    if (dir == null || !seen.Add(dir) || string.Equals(dir, full, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dir)) continue;
                    var listed = Listing(dir, source.Recursive);
                    foreach (var file in wanted)
                    {
                        if (cancelled?.Invoke() == true) return copied;
                        if (source.Only != null && !source.Only.Contains(file.Name)) continue;
                        if (file.Names.Any(n => File.Exists(Path.Combine(full, n)))) continue;
                        var found = file.Names.Select(n => listed.FirstOrDefault(p => string.Equals(Path.GetFileName(p), n, StringComparison.OrdinalIgnoreCase)))
                                              .FirstOrDefault(p => p != null);
                        if (found == null) continue;
                        Directory.CreateDirectory(full);
                        if (Copy(found, Path.Combine(full, file.Name), report)) copied.Add(found + " -> " + file.Name);
                    }
                }
                if (copied.Count > 0) LbipLog.Info("bios: " + copied.Count + " file(s) copied into " + full + " - " + string.Join(", ", copied));
            }
            catch (Exception ex) { LbipLog.Warn("bios: could not copy the user's files into " + target, ex); }
            return copied;
        }

        /// <summary>Every file in <paramref name="dir"/> - with its sub-folders when <paramref name="recursive"/> - the
        /// shallowest first, then by path, so the order never drifts. A folder that cannot be read is skipped.</summary>
        public static List<string> Listing(string dir, bool recursive = true)
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                return Directory.EnumerateFiles(dir, "*", options)
                    .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
                    .ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) { LbipLog.Warn("bios: could not list " + dir, ex); return new List<string>(); }
        }

        /// <summary>Through a temporary file, so a copy cut short never leaves a half file under the name.</summary>
        internal static bool Copy(string from, string to, Action<string> report)
        {
            var tmp = to + ".copying";
            try
            {
                report?.Invoke("Copying " + Path.GetFileName(from) + "...");
                File.Copy(from, tmp, overwrite: true);
                File.Move(tmp, to);
                return true;
            }
            catch (Exception ex)
            {
                LbipLog.Warn("bios: could not copy " + from, ex);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }

        /// <summary>The folders of the library's ARCADE games - those run by MAME, or by RetroArch with an FBNeo, FB Alpha
        /// or MAME core (Mehdi, 04/10): a romset's BIOS sets (naomi.zip, awbios.zip...) sit beside it. A game's emulator is
        /// its own, or its platform's default when it has none; RetroArch counts only on the platforms whose command line
        /// loads one of those cores. Distinct and existing, in the library's order. Empty without a data manager.</summary>
        public static List<string> ArcadeRomDirs()
        {
            var dirs = new List<string>();
            try
            {
                var dm = PluginHelper.DataManager;
                if (dm == null) return dirs;

                // Emulator id -> the platforms it runs arcade games on (null: all of them, as MAME).
                var arcade = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var emu in dm.GetAllEmulators() ?? Array.Empty<IEmulator>())
                {
                    string id = null, exe = null;
                    try { id = emu.Id; exe = Path.GetFileName(LbipImportWatch.Full(emu.ApplicationPath) ?? ""); } catch { }
                    if (string.IsNullOrEmpty(id)) continue;
                    var rows = Array.Empty<IEmulatorPlatform>();
                    try { rows = emu.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>(); } catch { }
                    foreach (var row in rows)
                    {
                        try { if (row.IsDefault && !string.IsNullOrEmpty(row.Platform) && !defaults.ContainsKey(row.Platform)) defaults[row.Platform] = id; } catch { }
                    }

                    if (exe.StartsWith("mame", StringComparison.OrdinalIgnoreCase) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        arcade[id] = null;
                    else if (string.Equals(exe, "retroarch.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var row in rows)
                            try { if (IsArcadeCore(row.CommandLine) && !string.IsNullOrEmpty(row.Platform)) platforms.Add(row.Platform); } catch { }
                        if (platforms.Count > 0) arcade[id] = platforms;
                    }
                }
                if (arcade.Count == 0) return dirs;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var game in dm.GetAllGames() ?? Array.Empty<IGame>())
                {
                    try
                    {
                        var platform = game.Platform ?? "";
                        var emuId = game.EmulatorId;
                        if (string.IsNullOrWhiteSpace(emuId) || emuId == Guid.Empty.ToString()) defaults.TryGetValue(platform, out emuId);
                        if (emuId == null || !arcade.TryGetValue(emuId, out var platforms)) continue;
                        if (platforms != null && !platforms.Contains(platform)) continue;
                        var dir = Path.GetDirectoryName(LbipImportWatch.Full(game.ApplicationPath) ?? "");
                        if (!string.IsNullOrEmpty(dir) && seen.Add(dir) && Directory.Exists(dir)) dirs.Add(dir);
                    }
                    catch { }
                }
                LbipLog.Info("bios: " + dirs.Count + " folder(s) of arcade games (MAME, FBNeo, FB Alpha) to look in");
            }
            catch (Exception ex) { LbipLog.Warn("bios: could not list the arcade games' folders", ex); }
            return dirs;
        }

        /// <summary>A RetroArch command line loading an FBNeo, FB Alpha or MAME core: -L "cores\fbneo_libretro.dll".</summary>
        internal static bool IsArcadeCore(string commandLine)
        {
            var c = (commandLine ?? "").ToLowerInvariant();
            return c.Contains("fbneo_libretro") || c.Contains("fbalpha") || System.Text.RegularExpressions.Regex.IsMatch(c, @"mame[\w]*_libretro");
        }
    }
}
