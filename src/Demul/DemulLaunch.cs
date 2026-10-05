// What a launch of Demul needs: Demul.ini made ready for the game, and the command line for it.
//
// DEMUL TAKES NO PATH FOR AN ARCADE GAME. Its help text: "-rom=<romname> run specified system rom",
// "-image=<fullpath> run specified system image (CD/GD media systems only)". A romset is looked up BY
// NAME in the folders Demul.ini lists - [files] romsPathsCount and roms0, roms1... (the same keys
// Batocera's launcher writes) - and the BIOS sets (dc, naomi, awbios, hikaru...) are looked up the same
// way. So before an arcade game starts, the game's folder has to be in that list, and so does the
// folder its BIOS sets live in: the emulator's own roms\, which the archive ships empty for that.
//
// THE LIST STAYS THE USER'S. Paths found there that this plugin did not put in are kept, in their
// order, after ours. What it puts in is written down beside demul.exe (lbip-demul-roms.txt) and taken
// out again at the next launch, so launching games from twenty folders does not leave twenty paths.
//
// THE VIDEO PLUGIN BY BOARD, as Batocera does: gpuDX11old.dll for Gaelco and CV1000 - "Gaelco won't
// work with the new DX11 plugin, and for cave3rd it crashes at start-up" - gpuDX11.dll for the rest.
// Both are in the archive, each built with its demul.exe.
//
// THE DISC PLUGIN BY FILE: gdrCHD.dll for a .chd, gdrImage.dll for a .gdi or .cdi - the two readers
// the archive carries (plugins\gdrCHD.dll, plugins\gdrImage.dll).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LbIntegrations.Demul
{
    internal static class DemulLaunch
    {
        public const string IniName = "Demul.ini";
        public const string RomsListName = "lbip-demul-roms.txt";
        public const string BiosFolder = "roms";

        /// <summary>Make Demul.ini ready for <paramref name="rom"/> on <paramref name="system"/>.
        /// Throws on an I/O failure - the caller turns that into a refused launch.</summary>
        public static void PrepareIni(string exe, string system, string rom)
        {
            var emuDir = Path.GetDirectoryName(Path.GetFullPath(exe));
            var ini = DemulIni.Load(Path.Combine(emuDir, IniName));

            // ── where the romsets are ──
            var own = Folder(Path.Combine(emuDir, BiosFolder));
            Directory.CreateDirectory(own);
            var ours = new List<string> { own };
            if (system != "dc")
            {
                var gameDir = Folder(Path.GetDirectoryName(Path.GetFullPath(rom)));
                if (!Same(gameDir, own)) ours.Add(gameDir);
            }

            var previous = ReadList(Path.Combine(emuDir, RomsListName));
            int.TryParse(ini.Get("files", "romsPathsCount"), out int count);
            var users = new List<string>();
            for (int i = 0; i < count; i++)
            {
                var p = ini.Get("files", "roms" + i);
                if (string.IsNullOrWhiteSpace(p)) continue;
                var f = Folder(p);
                if (previous.Any(x => Same(x, f)) || ours.Any(x => Same(x, f)) || users.Any(x => Same(x, f))) continue;
                users.Add(f);
            }
            var paths = ours.Concat(users).ToList();
            for (int i = 0; i < paths.Count; i++) ini.Set("files", "roms" + i, paths[i]);
            for (int i = paths.Count; i < count; i++) ini.Remove("files", "roms" + i);
            ini.Set("files", "romsPathsCount", paths.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WriteList(Path.Combine(emuDir, RomsListName), ours);

            // ── the plugins ──
            var pluginDir = Folder(Path.Combine(emuDir, "plugins"));
            var declared = ini.Get("plugins", "directory");
            if (string.IsNullOrWhiteSpace(declared) || !Directory.Exists(declared)) ini.Set("plugins", "directory", pluginDir);
            SetIfMissing(ini, "plugins", "spu", "spuDemul.dll");
            SetIfMissing(ini, "plugins", "pad", "padDemul.dll");
            SetIfMissing(ini, "plugins", "net", "netDemul.dll");
            ini.Set("plugins", "gpu", OldGpu(system) ? "gpuDX11old.dll" : "gpuDX11.dll");
            if (system == "dc")
                ini.Set("plugins", "gdr", rom.EndsWith(".chd", StringComparison.OrdinalIgnoreCase) ? "gdrCHD.dll" : "gdrImage.dll");
            else
                SetIfMissing(ini, "plugins", "gdr", "gdrImage.dll");

            bool changed = ini.Changed;
            ini.SaveIfChanged();
            Log.Info("Demul.ini " + (changed ? "updated" : "already right") + ": " + system + ", "
                     + paths.Count + " rom folder(s) (" + users.Count + " of the user's), gpu "
                     + (OldGpu(system) ? "gpuDX11old" : "gpuDX11"));
        }

        /// <summary>The boards the new DX11 plugin cannot run.</summary>
        public static bool OldGpu(string system) => system == "gaelco" || system == "cave3rd";

        /// <summary>The command line for the game: "-run=dc -image=&quot;path&quot;" for a disc, "-run=naomi
        /// -rom=set" for a romset. Switches the user put on the emulator's line that are not one of these
        /// three are kept, after them; the game itself, which the host appended, is not.</summary>
        public static string CommandLine(string current, string system, string rom)
        {
            var head = system == "dc"
                ? "-run=dc -image=\"" + rom + "\""
                : "-run=" + system + " -rom=" + Path.GetFileNameWithoutExtension(rom);
            var kept = new List<string>();
            foreach (var t in Tokenize(current ?? ""))
            {
                if (t.Length == 0 || IsTheGame(t, rom)) continue;
                var key = t.Split('=')[0];
                if (key.Equals("-run", StringComparison.OrdinalIgnoreCase) || key.Equals("-rom", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("-image", StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(t.Contains(' ') ? "\"" + t + "\"" : t);
            }
            return kept.Count == 0 ? head : head + " " + string.Join(" ", kept);
        }

        private static void SetIfMissing(DemulIni ini, string section, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(ini.Get(section, key))) ini.Set(section, key, value);
        }

        /// <summary>A folder as Demul.ini holds one: absolute, with its trailing backslash.</summary>
        private static string Folder(string path)
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return full.EndsWith("\\") ? full : full + "\\";
        }

        private static bool Same(string a, string b)
            => string.Equals(a?.TrimEnd('\\'), b?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        private static List<string> ReadList(string path)
        {
            try { return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(Folder).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        private static void WriteList(string path, List<string> paths)
        {
            try { File.WriteAllText(path, string.Join("\r\n", paths) + "\r\n", Encoding.UTF8); }
            catch (Exception ex) { Log.Info("could not write " + Path.GetFileName(path) + " - " + ex.Message); }
        }

        private static bool IsTheGame(string token, string rom)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(rom)) return false;
            try { if (string.Equals(Path.GetFullPath(token), Path.GetFullPath(rom), StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            return string.Equals(token, Path.GetFileName(rom), StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, Path.GetFileNameWithoutExtension(rom), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Split on spaces outside double quotes; the quotes go.</summary>
        internal static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            foreach (var c in line)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ' ' && !quoted) { if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); } continue; }
                current.Append(c);
            }
            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }
    }
}
