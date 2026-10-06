// What a launch writes: the game's options into snes9x.conf, and a .7z game unpacked for Snes9x.
//
// THE OPTIONS (RaSnes9xSettings.All) go into [Display\Win] before every launch - Snes9x reads the file when it starts
// and writes it back whole when it quits (RaSnes9xConf), so a key the plugin manages is the plugin's: changed in
// Snes9x's own menus, it comes back to the Nixx window's value at the next launch. Every other key - the pads, the
// hotkeys, the sound, the folders - is the user's and is never touched.
//
//   Fullscreen  Fullscreen:Enabled, and Fullscreen:EmulateFullscreen = TRUE (a borderless window over the screen)
//   Aspect      Stretch:Enabled = TRUE; Stretch:MaintainAspectRatio; Stretch:AspectRatioBaseWidth 299 (4:3) or 256 (8:7)
//   Integer     Stretch:IntegerScaling        Smooth  Stretch:BilinearFilter
//   VSync       Vsync                         Output  OutputMethod 1 Direct3D, 2 OpenGL, 0 DirectDraw
//
// A .7z GAME: Snes9x opens .zip and .jma itself (memmap.cpp), not .7z. Its ROM is unpacked to
// %TEMP%\lbip-rasnes9x\<archive>\ UNDER THE ARCHIVE'S NAME (Lylat.7z holding "Star Fox (USA).sfc" runs as Lylat.sfc),
// so its .srm and its states carry the same name a zip would give them, and a game moved from .7z to .zip keeps them.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpCompress.Archives;

namespace LbIntegrations.RaSnes9x
{
    internal static class RaSnes9xLaunch
    {
        public const string ExecutableName = "RASnes9x.exe";
        private const string Display = "Display\\Win";

        /// <summary>The extensions of a SNES ROM inside an archive.</summary>
        internal static readonly string[] RomExtensions = { ".sfc", ".smc", ".swc", ".fig", ".bs", ".st", ".bin" };

        public static bool IsRaSnes9xExe(string path)
        {
            try { return string.Equals(Path.GetFileName((path ?? "").Trim().Trim('"')), ExecutableName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>A RASnes9x running from that folder: an edit of snes9x.conf now would be lost when it quits.</summary>
        public static bool Running(string exe)
        {
            try
            {
                var full = Path.GetFullPath(exe);
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExecutableName)))
                    using (p)
                        try { if (string.Equals(p.MainModule?.FileName, full, StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            }
            catch { }
            return false;
        }

        /// <summary>The game's options written into snes9x.conf. Never throws: a launch with the file as it was beats none.</summary>
        public static void PrepareConf(string exe, string gameId)
        {
            try
            {
                var conf = RaSnes9xConf.Load(RaSnes9xConf.Of(exe));
                bool full = RaSnes9xSettings.BoolFor(gameId, "Fullscreen");
                conf.SetBool(Display, "Fullscreen:Enabled", full);
                if (full) conf.SetBool(Display, "Fullscreen:EmulateFullscreen", true);

                var aspect = RaSnes9xSettings.For(gameId, "Aspect");
                conf.SetBool(Display, "Stretch:Enabled", true);
                conf.SetBool(Display, "Stretch:MaintainAspectRatio", aspect != "stretch");
                if (aspect != "stretch") conf.Set(Display, "Stretch:AspectRatioBaseWidth", aspect == "8:7" ? "256" : "299");
                conf.SetBool(Display, "Stretch:IntegerScaling", RaSnes9xSettings.BoolFor(gameId, "IntegerScaling"));
                conf.SetBool(Display, "Stretch:BilinearFilter", RaSnes9xSettings.BoolFor(gameId, "Smooth"));
                conf.SetBool(Display, "Vsync", RaSnes9xSettings.BoolFor(gameId, "VSync"));
                var output = RaSnes9xSettings.For(gameId, "Output");
                conf.Set(Display, "OutputMethod", output == "opengl" ? "2" : output == "ddraw" ? "0" : "1");

                bool changed = conf.Changed;
                conf.SaveIfChanged();
                Log.Info("snes9x.conf: " + (changed ? "written" : "already as wanted") + " - " + (full ? "full screen" : "windowed") + ", " + aspect
                         + (RaSnes9xSettings.BoolFor(gameId, "IntegerScaling") ? ", integer" : "") + ", " + output);
            }
            catch (Exception ex) { Log.Warn("snes9x.conf could not be written - the game starts with it as it was", ex); }
        }

        /// <summary>For a .7z: the ROM inside, unpacked under the archive's name, or null with <paramref name="problem"/>
        /// said. Any other file comes back as it is.</summary>
        public static string RomFor(string rom, out string problem)
        {
            problem = null;
            if (!string.Equals(Path.GetExtension(rom ?? ""), ".7z", StringComparison.OrdinalIgnoreCase)) return rom;
            try
            {
                using var archive = ArchiveFactory.Open(rom);
                var roms = archive.Entries.Where(e => !e.IsDirectory && RomExtensions.Contains(Path.GetExtension(e.Key ?? "").ToLowerInvariant())).ToList();
                var name = Path.GetFileNameWithoutExtension(rom);
                var pick = roms.Count == 1 ? roms[0]
                         : roms.FirstOrDefault(e => string.Equals(Path.GetFileNameWithoutExtension(e.Key), name, StringComparison.OrdinalIgnoreCase));
                if (pick == null)
                {
                    problem = roms.Count == 0 ? "there is no Super Nintendo ROM in " + Path.GetFileName(rom)
                            : Path.GetFileName(rom) + " holds " + roms.Count + " ROMs, and none is named after the archive - Snes9x cannot tell which to play. "
                              + "Keep one ROM per archive, or name the one to play \"" + name + Path.GetExtension(roms[0].Key) + "\".";
                    return null;
                }
                // One folder per archive PATH: two archives of the same name never share an unpacked ROM.
                string hash;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    hash = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(rom).ToLowerInvariant()))).Substring(0, 8).ToLowerInvariant();
                var dir = Path.Combine(Path.GetTempPath(), "lbip-rasnes9x", name + "-" + hash);
                var target = Path.Combine(dir, name + Path.GetExtension(pick.Key).ToLowerInvariant());
                if (File.Exists(target) && new FileInfo(target).Length == pick.Size && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(rom))
                    return target;
                Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir)) try { File.Delete(old); } catch { }
                using (var src = pick.OpenEntryStream())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    src.CopyTo(dst);
                Log.Info("unpacked " + pick.Key + " from " + Path.GetFileName(rom) + " -> " + target);
                return target;
            }
            catch (Exception ex)
            {
                Log.Warn("could not unpack " + rom, ex);
                problem = Path.GetFileName(rom) + " could not be opened: " + ex.Message;
                return null;
            }
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
            // Spelt otherwise by the host (relative, forward slashes): the last quoted token, else the last token - never a
            // second game beside the first.
            var m = System.Text.RegularExpressions.Regex.Match(line, "\"[^\"]*\"\\s*$");
            if (m.Success) return line.Substring(0, m.Index) + "\"" + newRom + "\"";
            var cut = line.TrimEnd().LastIndexOf(' ');
            return cut < 0 ? "\"" + newRom + "\"" : line.Substring(0, cut) + " \"" + newRom + "\"";
        }
    }
}
