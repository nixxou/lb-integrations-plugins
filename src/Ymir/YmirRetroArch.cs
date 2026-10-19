// A game's backup RAM carried between Ymir and RetroArch, from the game's window (Mehdi 20/10: "export/import la live save
// avec l'émulateur RetroArch dont le core est configuré pour cette plateforme").
//
// THE SAME BYTES (measured 20/10): Beetle Saturn's <rom>.bkr and Ymir's backup RAM are both the Saturn's internal backup RAM,
// 32 KiB, starting "BackUpRam Format" (Ymir: sys\memory_defs.hpp kInternalBackupRAMSize, backup_ram.cpp kHeader; Beetle:
// Atlantis' .bkr). A copy is all it takes - no conversion. Only Beetle Saturn (mednafen_saturn_libretro) is offered: the
// other Saturn cores (Yabause, YabaSanshiro, Kronos) keep their saves in formats not measured here, and are said so.
//
// WHICH RETROARCH: the one in LaunchBox whose row for the game's platform names a core (-L cores\<core>.dll), else whose
// default line does - LaunchBox's own choice for a game of that platform; the game's own emulator first if it is a RetroArch.
// WHERE ITS FILE IS: as RetroArch decides it (and as LaunchBox's RetroArch integration and LiteBox read it - SaveManager
// ResolveRetroArchSavesDir): retroarch.cfg's savefiles_in_content_dir / savefile_directory (":\" = beside the exe),
// sort_savefiles_by_content_enable (+ the game's folder name), sort_savefiles_enable (+ the core's display name, its .info's
// corename) - then <the game's file name>.bkr.
//
// NOTHING IS LOST: a copy over an existing file first keeps that file in <Ymir>\Saves\<platform>\replaced\ (dated). Refused
// while Ymir or that RetroArch runs - each writes its RAM when it quits.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Ymir
{
    internal sealed class YmirRaLink
    {
        public string RetroArchExe, Core, CoreName, SaveFile;
        /// <summary>Why there is nothing to carry, said to the user; null when the link is usable.</summary>
        public string Problem;
    }

    internal static class YmirRetroArch
    {
        public const string BeetleSaturn = "mednafen_saturn_libretro";
        private static readonly string[] OtherSaturnCores = { "yabause_libretro", "yabasanshiro_libretro", "kronos_libretro" };
        public const int RamSize = 32 * 1024;

        private static bool IsRetroArch(string path) => string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), "retroarch.exe", StringComparison.OrdinalIgnoreCase);

        internal static string CoreOf(string commandLine)
        {
            var m = Regex.Match(commandLine ?? "", @"-L\s+""?[^""\s]*?cores[\\/](?<core>[^\\/""]+)\.dll""?", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["core"].Value : "";
        }

        /// <summary>The RetroArch of the game's platform and the file its core keeps the game's RAM in. Never throws.</summary>
        public static YmirRaLink Find(IGame game)
        {
            var link = new YmirRaLink();
            try
            {
                var dm = PluginHelper.DataManager;
                var platform = Safe(() => game?.Platform) ?? YmirPlugin.Platform;
                var emus = (dm?.GetAllEmulators() ?? Array.Empty<IEmulator>()).Where(e => e != null && IsRetroArch(Safe(() => e.ApplicationPath))).ToList();
                if (emus.Count == 0) { link.Problem = "No RetroArch in LaunchBox."; return link; }
                var own = Safe(() => game?.EmulatorId);
                (IEmulator Emu, string Core, bool Default) Pick(IEmulator e)
                {
                    var rows = Safe(() => e.GetAllEmulatorPlatforms()) ?? Array.Empty<IEmulatorPlatform>();
                    var row = rows.FirstOrDefault(r => string.Equals(Safe(() => r.Platform), platform, StringComparison.OrdinalIgnoreCase));
                    if (row == null) return (e, null, false);
                    var core = CoreOf(Safe(() => row.CommandLine));
                    if (core.Length == 0) core = CoreOf(Safe(() => e.CommandLine));
                    return (e, core, Safe(() => row.IsDefault));
                }
                // The game's own RetroArch, then the platform's default one, then the others; among them a Beetle Saturn first.
                var candidates = emus.Select(Pick).Where(c => c.Core != null)
                                     .OrderBy(c => Safe(() => c.Emu.Id) == own ? 0 : c.Default ? 1 : 2).ToList();
                if (candidates.Count == 0) { link.Problem = "No RetroArch in LaunchBox has a row for " + platform + "."; return link; }
                var chosen = candidates.FirstOrDefault(c => string.Equals(c.Core, BeetleSaturn, StringComparison.OrdinalIgnoreCase));
                if (chosen.Emu == null) chosen = candidates.FirstOrDefault(c => c.Core.Length > 0);
                if (chosen.Emu == null) { link.Problem = "RetroArch's row for " + platform + " names no core (-L cores\\<core>.dll)."; return link; }
                link.RetroArchExe = YmirPlugin.ResolveFullPathForUi(Safe(() => chosen.Emu.ApplicationPath));
                link.Core = chosen.Core;
                var exeDir = Path.GetDirectoryName(link.RetroArchExe) ?? "";
                link.CoreName = CoreDisplayName(Path.Combine(exeDir, "info", link.Core + ".info")) ?? link.Core;
                if (!string.Equals(link.Core, BeetleSaturn, StringComparison.OrdinalIgnoreCase))
                {
                    link.Problem = "RetroArch runs " + platform + " with " + link.CoreName + (OtherSaturnCores.Contains(link.Core, StringComparer.OrdinalIgnoreCase)
                        ? ": its saves are not in Beetle Saturn's format, which is the only one carried for now." : ", not a Saturn core this window knows.");
                    return link;
                }
                var rom = YmirPlugin.ResolveFullPathForUi(Safe(() => game?.ApplicationPath));
                var dir = SavesDir(link.RetroArchExe, rom, link.CoreName);
                if (dir == null) { link.Problem = "RetroArch's save folder could not be read from its retroarch.cfg."; return link; }
                link.SaveFile = Path.Combine(dir, Path.GetFileNameWithoutExtension(rom) + ".bkr");
            }
            catch (Exception ex) { link.Problem = "RetroArch could not be looked at: " + ex.Message; Log.Warn("retroarch link", ex); }
            return link;
        }

        /// <summary>RetroArch's save folder for a game (header); null when retroarch.cfg does not say.</summary>
        internal static string SavesDir(string exe, string rom, string coreName)
        {
            var exeDir = Path.GetDirectoryName(exe) ?? "";
            var cfg = ReadCfg(Path.Combine(exeDir, "retroarch.cfg"));
            if (cfg == null) return null;
            string dir;
            if (cfg.TryGetValue("savefiles_in_content_dir", out var inContent) && inContent == "true") dir = Path.GetDirectoryName(rom) ?? "";
            else
            {
                if (!cfg.TryGetValue("savefile_directory", out var raw) || string.IsNullOrEmpty(raw) || raw == "default") return null;
                dir = raw.StartsWith(":\\", StringComparison.Ordinal) ? Path.GetFullPath(raw.Substring(2), exeDir) : raw;
            }
            if (cfg.TryGetValue("sort_savefiles_by_content_enable", out var byContent) && byContent == "true")
                dir = Path.Combine(dir, Path.GetFileName(Path.GetDirectoryName(rom)) ?? "");
            if (cfg.TryGetValue("sort_savefiles_enable", out var byCore) && byCore == "true")
                dir = Path.Combine(dir, coreName);
            return dir;
        }

        private static Dictionary<string, string> ReadCfg(string path)
        {
            if (!File.Exists(path)) return null;
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                var m = Regex.Match(line, "^\\s*(\\w+)\\s*=\\s*\"?([^\"]*)\"?\\s*$");
                if (m.Success) found[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return found;
        }

        private static string CoreDisplayName(string info)
        {
            try
            {
                if (!File.Exists(info)) return null;
                foreach (var line in File.ReadLines(info))
                {
                    var m = Regex.Match(line, "^corename\\s*=\\s*\"([^\"]*)\"");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        // ── the copy ─────────────────────────────────────────────────────────

        /// <summary>A Saturn internal backup RAM: 32 KiB, its header first. Null when it is one, else why not.</summary>
        internal static string WhyNotRam(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Path.GetFileName(path) + " is not there.";
                if (info.Length != RamSize) return Path.GetFileName(path) + " is " + info.Length + " bytes, not a Saturn's 32 KB backup RAM.";
                var head = new byte[16];
                using (var s = info.OpenRead()) s.Read(head, 0, 16);
                return Encoding.ASCII.GetString(head) == "BackUpRam Format" ? null : Path.GetFileName(path) + " does not start as a Saturn backup RAM.";
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static bool Runs(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                    using (p) try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        /// <summary><paramref name="from"/> over <paramref name="to"/>, the file there first kept in <paramref name="replacedDir"/>.
        /// Null when done, else why not.</summary>
        public static string Copy(string from, string to, string replacedDir, string ymirExe, string raExe)
        {
            if (ymirExe != null && YmirLaunch.Running(ymirExe)) return "Ymir is running: it writes the game's RAM when it quits. Close it first.";
            if (raExe != null && Runs(raExe)) return "RetroArch is running: it writes the game's RAM when it quits. Close it first.";
            var bad = WhyNotRam(from);
            if (bad != null) return bad;
            try
            {
                if (File.Exists(to))
                {
                    Directory.CreateDirectory(replacedDir);
                    var kept = Path.Combine(replacedDir, Path.GetFileNameWithoutExtension(to) + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + Path.GetExtension(to));
                    File.Copy(to, kept);
                    Log.Info("retroarch link: " + to + " kept as " + kept);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to, overwrite: true);
                Log.Info("retroarch link: copied " + from + " -> " + to);
                return null;
            }
            catch (Exception ex) { Log.Warn("retroarch link: copy", ex); return ex.Message; }
        }

        private static T Safe<T>(Func<T> get) { try { return get(); } catch { return default; } }
    }
}
