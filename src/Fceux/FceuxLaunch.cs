// What a launch writes into fceux.cfg and FCEUX's folders, and what it hands FCEUX on its line.
//
// FCEUX (the Qt / SDL build, bin\qfceux.exe) READS fceux.cfg WHEN IT STARTS AND WRITES IT BACK WHOLE WHEN IT QUITS
// (FceuxCfg), so everything the plugin wants is written BEFORE the launch, in one pass, and only when FCEUX is not running
// from that folder:
//
//   the folders     its sav, fcs, snaps and cheats folders made junctions to the game's platform's (FceuxSavePaths) - the
//                   Qt build has no setting for them
//   the screen      "SDL.Fullscreen" 1 / 0, the plugin's option (FceuxSettings "Fullscreen"). Written in the file, not
//                   on the line: an option on the line is stored too (measured 18/10, "--pal 1" left "SDL.PAL = 1"), so the
//                   file is the one place it is said. Qt's full screen is a borderless window (QWidget::showFullScreen,
//                   drivers\Qt\ConsoleWindow.cpp): the display's mode is never changed.
//   the menu        "SDL.AutoHideMenuFullsreen" 1 (FCEUX's spelling): no menu bar over the game in full screen
//   the hotkeys     the pack's, where FCEUX lets them be (FceuxHotkeys)
//
// THE LINE: the host's (the game's quoted path), nothing added. Two things are checked before it goes:
//   - the path must be ANSI (FceuxCfg.Representable). Refused with a notice.
//   - an archive whose ROM is a .unf / .unif: FCEUX looks only at .nes, .fds and .nsf inside an archive (fceu.cpp:439) - the
//     ROM is unpacked under its own name into <FCEUX>\Unpacked\<archive>-<hash>\ (a stable folder of the install, never
//     %TEMP%) and the line pointed at it; its saves take the ROM's name as they would have from the archive.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LbIntegrations.Fceux
{
    internal static class FceuxLaunch
    {
        /// <summary>The Qt / SDL build (what the plugin installs, Mehdi 18/10). Not the classic Windows build's fceux.exe /
        /// fceux64.exe: another fceux.cfg (its "od*" folders, its numeric keys), another plugin's.</summary>
        public static readonly string[] ExecutableNames = { "qfceux.exe" };
        public const string ExecutableName = "qfceux.exe";
        public const string UnpackedFolder = "Unpacked";

        public static bool IsFceuxExe(string path)
        {
            try
            {
                var name = Path.GetFileName((path ?? "").Trim().Trim('"'));
                return ExecutableNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        /// <summary>An FCEUX running from that folder (either build): an edit of fceux.cfg now would be lost when it quits.</summary>
        public static bool Running(string exe)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(exe));
                foreach (var name in ExecutableNames)
                    foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                        using (p)
                            try { if (string.Equals(Path.GetDirectoryName(p.MainModule?.FileName ?? ""), dir, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        // ── fceux.cfg ────────────────────────────────────────────────────────

        /// <summary>The screen, the game's own options and the hotkeys written into fceux.cfg for this game, and FCEUX's
        /// folders pointed at its platform's. Never throws: a launch with things as they were beats none.</summary>
        public static void PrepareConfig(string exe, string gameId, string platform, string rom)
        {
            try
            {
                var cfg = FceuxCfg.Load(FceuxCfg.Of(exe));
                var fresh = !cfg.Existed;
                // A session whose end was not seen (LaunchBox closed first): FCEUX's own values back before anything.
                var back = PutBack(exe, cfg);
                var session = new List<(string Name, string Was)>();
                var said = new List<string>();
                foreach (var (o, name, value) in FceuxSettings.LinesFor(gameId))
                {
                    if (!o.OnPage && !session.Any(s => s.Name == name))
                        session.Add((name, cfg.Get(name) ?? (FceuxSettings.Defaults.TryGetValue(name, out var d) ? d : value)));
                    cfg.Set(name, value);
                    if (!o.OnPage) said.Add(name + " = " + value);
                }
                // The menu bar hidden in full screen (Mehdi 19/10): the plugin's, as the screen - always on.
                cfg.Set(FceuxCfg.HideMenuFullScreen, "1");
                foreach (var line in FceuxHotkeys.Bind(cfg)) Log.Info(line);
                bool changed = cfg.Changed;
                cfg.SaveIfChanged();
                WriteSession(exe, session);
                Log.Info("fceux.cfg: " + (changed ? (fresh ? "made" : "written") : "already as wanted") + " - "
                         + (FceuxSettings.BoolFor(gameId, "Fullscreen") ? "full screen" : "windowed")
                         + (said.Count > 0 ? ", for this game: " + string.Join(", ", said) + " (FCEUX's own put back when it quits)" : "")
                         + (back > 0 ? ", " + back + " value(s) of an earlier session put back first" : ""));
            }
            catch (Exception ex) { Log.Warn("fceux.cfg could not be written - the game starts with it as it was", ex); }
            Log.Info("folders: " + FceuxSavePaths.Ensure(exe, platform));
        }

        // ── a game's own options, for its session only ───────────────────────

        /// <summary>Beside fceux.cfg: FCEUX's own values of the lines a game's options changed, "name=value" - put back when
        /// FCEUX has quit (AfterSession), else at the next launch.</summary>
        public const string SessionFile = "lbip-fceux-session.txt";

        internal static string SessionPath(string exe) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exe)), SessionFile);

        private static void WriteSession(string exe, List<(string Name, string Was)> session)
        {
            var path = SessionPath(exe);
            if (session.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(string.Concat(session.Select(s => s.Name + "=" + s.Was + "\r\n"))));
        }

        /// <summary>The values noted in the session file set back into <paramref name="cfg"/> (in memory; the caller saves
        /// and then removes the file). Returns how many.</summary>
        private static int PutBack(string exe, FceuxCfg cfg)
        {
            var path = SessionPath(exe);
            if (!File.Exists(path)) return 0;
            int n = 0;
            foreach (var line in System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int at = line.IndexOf('=');
                if (at <= 0) continue;
                cfg.Set(line.Substring(0, at), line.Substring(at + 1));
                n++;
            }
            return n;
        }

        /// <summary>FCEUX's own values put back now, when a session file is there and FCEUX is not running. Never throws.</summary>
        public static void PutBackNow(string exe)
        {
            try
            {
                if (!File.Exists(SessionPath(exe)) || Running(exe)) return;
                var cfg = FceuxCfg.Load(FceuxCfg.Of(exe));
                int n = PutBack(exe, cfg);
                cfg.SaveIfChanged();
                File.Delete(SessionPath(exe));
                Log.Info("fceux.cfg: " + n + " value(s) of FCEUX's own put back after the session");
            }
            catch (Exception ex) { Log.Warn("fceux.cfg: FCEUX's own values could not be put back (they will be at the next launch)", ex); }
        }

        /// <summary>Once FCEUX has quit after a launch (a minute to appear): its own values back, then a thumbnail beside each
        /// state of the game.</summary>
        public static void AfterSession(string exe, string rom, string platform)
        {
            if (string.IsNullOrEmpty(exe)) return;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 120 && !Running(exe); i++) await System.Threading.Tasks.Task.Delay(500).ConfigureAwait(false);
                    while (Running(exe)) await System.Threading.Tasks.Task.Delay(1000).ConfigureAwait(false);
                    PutBackNow(exe);
                    if (!string.IsNullOrWhiteSpace(rom)) FceuxStateThumbs.RefreshAll(exe, rom, platform);
                }
                catch (Exception ex) { Log.Warn("after the session", ex); }
            });
        }

        // ── the line ─────────────────────────────────────────────────────────

        /// <summary>The file FCEUX is given for this game - the game itself, or its .unf unpacked from an archive (see the
        /// header) - or null with <paramref name="problem"/> said.</summary>
        public static string RomFor(string rom, string exe, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(rom) || !Archives.IsArchive(rom)) return rom;
            var (seen, unif) = Archives.RomEntries(rom);
            if (seen == null || seen.Count > 0 || unif == null || unif.Count == 0) return rom;     // FCEUX's to open (or to say what is wrong)
            if (unif.Count > 1)
            {
                problem = Path.GetFileName(rom) + " holds " + unif.Count + " UNIF ROMs and FCEUX looks only at .nes, .fds and .nsf files inside an archive. "
                        + "Keep one ROM per archive.";
                return null;
            }
            try
            {
                var entry = unif[0];
                var target = Path.Combine(UnpackDir(exe, rom), Path.GetFileName(entry.Replace('\\', '/')));
                if (!Archives.Unpack(rom, entry, target)) { problem = Path.GetFileName(rom) + " could not be read."; return null; }
                Log.Info("unpacked " + entry + " from " + Path.GetFileName(rom) + " -> " + target + " (FCEUX does not look for UNIF ROMs inside an archive)");
                return target;
            }
            catch (Exception ex)
            {
                Log.Warn("could not unpack " + rom, ex);
                problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message;
                return null;
            }
        }

        /// <summary>One folder per archive PATH under the install: two archives of the same name never share it.</summary>
        internal static string UnpackDir(string exe, string rom)
        {
            string hash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                hash = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
            return Path.Combine(FceuxSavePaths.InstallDir(exe), UnpackedFolder, Path.GetFileNameWithoutExtension(rom) + "-" + hash);
        }

        /// <summary>The host's line with its game path swapped for <paramref name="newRom"/>.</summary>
        public static string SwapRom(string line, string oldRom, string newRom)
        {
            line = line ?? "";
            foreach (var form in new[] { "\"" + oldRom + "\"", oldRom })
            {
                int at = line.LastIndexOf(form, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) return line.Substring(0, at) + "\"" + newRom + "\"" + line.Substring(at + form.Length);
            }
            var m = System.Text.RegularExpressions.Regex.Match(line, "\"[^\"]*\"\\s*$");
            if (m.Success) return line.Substring(0, m.Index) + "\"" + newRom + "\"";
            var cut = line.TrimEnd().LastIndexOf(' ');
            return cut < 0 ? "\"" + newRom + "\"" : line.Substring(0, cut) + " \"" + newRom + "\"";
        }
    }
}
