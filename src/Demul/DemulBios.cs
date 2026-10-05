// The user's BIOS sets, copied into Demul's roms\ from where they already are - as the Flycast plugin does into its
// data\ (FlycastBios.Import, Mehdi 04/10), so LaunchBox's BIOS check finds them at once and a game starts.
//
// THE SETS: the BIOS romsets demul.exe names - dc, naomi, naomi2, naomigd, awbios, hikaru, segasp - each a zip under
// its MAME name. Copied as they are: whether a set holds the revision Demul wants is said by Demul at launch.
//
// FROM, in this order, only what roms\ is missing (a copy, never a move - LbipBiosImport):
//   - the Flycast plugin's data\ beside this install (Emulators\Nixx-Flycast\data), where naomi.zip, naomi2.zip and
//     awbios.zip already are for a user of both;
//   - RetroArch's system folder beside this install, with its sub-folders;
//   - the folders of the library's arcade games run by MAME or RetroArch's arcade cores (LbipBiosImport.ArcadeRomDirs),
//     not their sub-folders - the BIOS sets sit beside the romsets.
//
// WHEN: at install, all of them; and at a launch whose BIOS is not in roms\ yet, that one - a set added to the library
// after the install is then found without a reinstall.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LbIntegrations.Lbip;

namespace LbIntegrations.Demul
{
    internal static class DemulBios
    {
        public static readonly string[] Sets = { "dc.zip", "naomi.zip", "naomi2.zip", "naomigd.zip", "awbios.zip", "hikaru.zip", "segasp.zip" };

        /// <summary>The set a system cannot start without, or null (CV1000 and Gaelco need none).</summary>
        public static string For(string system, string board)
        {
            if (board == "Naomi 2") return "naomi2.zip";
            if (board == "System SP") return "segasp.zip";
            return system switch
            {
                "dc" => "dc.zip",
                "naomi" => "naomi.zip",
                "awave" => "awbios.zip",
                "hikaru" => "hikaru.zip",
                _ => null,
            };
        }

        /// <summary>Copy into <paramref name="emuDir"/>\roms what it is missing of <paramref name="only"/> (every set when
        /// null). What was copied, one line each. Never throws.</summary>
        public static List<string> Import(string emuDir, IEnumerable<string> only = null, Action<string> report = null, Func<bool> cancelled = null)
        {
            try
            {
                if (string.IsNullOrEmpty(emuDir)) return new List<string>();
                var target = Path.Combine(emuDir, DemulLaunch.BiosFolder);
                Directory.CreateDirectory(target);
                var names = (only ?? Sets).Where(n => n != null).ToList();
                if (names.All(n => File.Exists(Path.Combine(target, n)))) return new List<string>();
                var wanted = names.Select(n => new LbipBiosImport.Wanted { Name = n }).ToList();
                var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

                var sources = new List<LbipBiosImport.Source>();
                try { sources.Add(new LbipBiosImport.Source { Dir = Path.GetFullPath(Path.Combine(emuDir, "..", "Nixx-Flycast", "data")), Recursive = false, Only = set }); } catch { }
                try { sources.Add(new LbipBiosImport.Source { Dir = Path.GetFullPath(Path.Combine(emuDir, "..", "RetroArch", "system")), Recursive = true, Only = set }); } catch { }
                foreach (var dir in LbipBiosImport.ArcadeRomDirs())
                    sources.Add(new LbipBiosImport.Source { Dir = dir, Recursive = false, Only = set });

                var copied = LbipBiosImport.Import(target, sources, wanted, report, cancelled);
                var still = names.Where(n => !File.Exists(Path.Combine(target, n))).ToList();
                Log.Info("BIOS: " + (copied.Count == 0 ? "nothing copied" : copied.Count + " copied") + " into " + target
                         + (still.Count == 0 ? "" : ", still missing: " + string.Join(", ", still)));
                return copied;
            }
            catch (Exception ex) { Log.Warn("BIOS import", ex); return new List<string>(); }
        }
    }
}
