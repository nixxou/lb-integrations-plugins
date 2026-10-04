// The DSP-1 ROM: the one coprocessor firmware SUPER ZSNES asks for - Super Mario Kart, Pilotwings and the other
// DSP-1 games stop at "Need DSP ROM" without it (Mehdi, 04/10).
//
// NEVER SHIPPED. It is Nintendo's code, read out of a chip: the user provides it, the pack keeps it and puts it back.
//
// WHAT THE EMULATOR WANTS, read off the build (04/10): the name "dsp1b.rom" is the one file name of the kind in its code
// (global-metadata.dat), looked for beside the game, beside the executable or in its data folder (<exe>\portable with the
// pack's plugin), and the content is checked ("DSP ROM MISMATCH"). The CRC32 of a good dump, 465C4E1C, is a constant in
// GameAssembly.dll - once, and the wrong dump tried that day (a DSP-1, E359F184) is nowhere in it. So a file is taken
// on its CRC, whatever it is called.
//
// KEPT IN THE PLUGIN'S DATA (<plugin root>\.data\<PluginId>\bios\dsp1b.rom), where the pack's uninstaller and a
// reinstall of the emulator do not reach, and PUT BESIDE THE EXECUTABLE at every install and launch when it is missing
// or different there - an emulator folder deleted and installed again has it back at its first game. A good one found
// in the emulator's folder (put there by hand) is adopted into the plugin's data the first time it is seen.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.SuperZsnes
{
    internal static class SuperZsnesBios
    {
        public const string FileName = "dsp1b.rom";
        public const uint Crc = 0x465C4E1C;
        public const string Md5 = "332273cc0df5775d3803f2fd88e95d18";
        public const int Size = 8192;

        public static string KeptPath => Path.Combine(Path.GetDirectoryName(SuperZsnesSettings.SettingsPath), "bios", FileName);

        public static bool IsKept => File.Exists(KeptPath);

        /// <summary>Null when <paramref name="path"/> is a good DSP-1B dump; otherwise why not.</summary>
        public static string Check(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "the file is not there";
                var info = new FileInfo(path);
                if (info.Length != Size) return "it is " + info.Length + " bytes; a DSP-1B ROM is " + Size;
                var crc = Crc32(File.ReadAllBytes(path));
                return crc == Crc ? null : "its CRC32 is " + crc.ToString("X8") + ", not the DSP-1B's " + Crc.ToString("X8") + " - another dump (a DSP-1, DSP-1A...) SUPER ZSNES refuses";
            }
            catch (Exception ex) { return "it could not be read: " + ex.Message; }
        }

        /// <summary>Take <paramref name="path"/> into the plugin's data. Null when done; otherwise why not.</summary>
        public static string Keep(string path)
        {
            var why = Check(path);
            if (why != null) return why;
            try
            {
                var kept = KeptPath;
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase)) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(kept));
                var tmp = kept + ".tmp";
                File.Copy(path, tmp, overwrite: true);
                File.Move(tmp, kept, overwrite: true);
                Log.Info("dsp1b.rom kept from " + path + " -> " + kept);
                return null;
            }
            catch (Exception ex) { Log.Warn("could not keep dsp1b.rom", ex); return "it could not be copied: " + ex.Message; }
        }

        /// <summary>None kept yet: a good one in the emulator's folder or its portable\ is taken. Never throws.</summary>
        public static void Adopt(string exeDir)
        {
            try
            {
                if (IsKept || string.IsNullOrEmpty(exeDir)) return;
                foreach (var candidate in new[] { Path.Combine(exeDir, FileName), Path.Combine(exeDir, "portable", FileName) })
                    if (File.Exists(candidate) && Check(candidate) == null && Keep(candidate) == null) { Log.Info("dsp1b.rom adopted from the emulator's folder"); return; }
            }
            catch (Exception ex) { Log.Warn("dsp1b.rom: adopt", ex); }
        }

        /// <summary>The kept one beside the executable, when it is missing or different there. What was done, or null.
        /// Never throws.</summary>
        public static string PutInPlace(string exeDir)
        {
            try
            {
                if (!IsKept || string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir)) return null;
                var target = Path.Combine(exeDir, FileName);
                var kept = File.ReadAllBytes(KeptPath);
                if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(kept)) return null;
                var tmp = target + ".tmp";
                File.WriteAllBytes(tmp, kept);
                File.Move(tmp, target, overwrite: true);
                Log.Info("dsp1b.rom put beside " + exeDir);
                return "dsp1b.rom put in place";
            }
            catch (Exception ex) { Log.Warn("dsp1b.rom: could not put it beside " + exeDir, ex); return null; }
        }

        /// <summary>For the NixxMenu tab.</summary>
        public static string Status()
            => IsKept ? "Kept by the pack, put beside every SUPER ZSNES at install and launch." : "None yet: Super Mario Kart, Pilotwings and the other DSP-1 games stop at \"Need DSP ROM\".";

        private static uint[] _table;

        internal static uint Crc32(byte[] data)
        {
            var t = _table;
            if (t == null)
            {
                t = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[i] = c;
                }
                _table = t;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = t[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
