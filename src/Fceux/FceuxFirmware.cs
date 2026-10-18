// The Famicom Disk System's BIOS, found where the user already keeps it and given to FCEUX.
//
// FCEUX ASKS FOR disksys.rom (file.cpp:751 FCEUMKF_FDSROM): in the "odfdsrom" folder when it is set, else beside the
// executable. It checks the SIZE ALONE - 8 192 bytes (fds.cpp:869) - never the content. MEASURED 18/10 without it: a .fds
// start shows the MODAL error "FDS BIOS ROM image missing: <exe folder>\disksys.rom", which WM_CLOSE does not close - so a
// disk game is never launched without it: the launch is refused with a notice naming the file and the folder.
//
// SO THE PLUGIN, at install and at each disk game's launch: a disksys.rom of 8 192 bytes where FCEUX reads it is kept as
// it is; else it is COPIED there from RetroArch's system folder and the bios / system / firmware folders of the other
// emulators beside this one (LbipBiosImport: by name - disksys.rom, or fds_bios.bin as other emulators name it - only
// what is missing, never a move). A file there of another size is the user's and is not replaced: the launch says so.
// The SHA-256 ares knows for the dump (FceuxSystems.Fds) is said in the log when it differs, never a refusal - FCEUX
// does not check it either.
//
// No FDS game and no disksys.rom exist on the machine this was written on: the loading of a disk game is read in the
// source, not measured (docs\fceux.md §7).

using System;
using System.Collections.Generic;
using System.IO;
using LbIntegrations.Lbip;

namespace LbIntegrations.Fceux
{
    internal sealed class FceuxBiosState
    {
        public string Folder, Path;
        public bool Present, RightSize, KnownDump;
    }

    internal static class FceuxFirmware
    {
        /// <summary>Where FCEUX reads disksys.rom: "odfdsrom" (relative ones from the exe's folder), else the exe's folder.</summary>
        public static string FolderOf(string exe, FceuxCfg cfg = null)
        {
            var exeDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe));
            try
            {
                cfg ??= FceuxCfg.Load(FceuxCfg.Of(exe));
                var v = cfg.Get(FceuxCfg.FdsRomDir);
                if (!string.IsNullOrWhiteSpace(v)) return LbIntegrations.Saves.ExpectedDir.OfConfig(v, exeDir) ?? exeDir;
            }
            catch { }
            return exeDir;
        }

        public static FceuxBiosState State(string exe, FceuxCfg cfg = null)
        {
            var s = new FceuxBiosState { Folder = FolderOf(exe, cfg) };
            s.Path = System.IO.Path.Combine(s.Folder, FceuxSystems.Fds.Bios);
            try
            {
                var info = new FileInfo(s.Path);
                s.Present = info.Exists;
                s.RightSize = info.Exists && info.Length == FceuxSystems.Fds.BiosSize;
                if (s.RightSize) s.KnownDump = string.Equals(FceuxBuild.Sha256Of(s.Path), FceuxSystems.Fds.BiosSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return s;
        }

        /// <summary>disksys.rom copied where FCEUX reads it when it is not there. Never throws; the state after.</summary>
        public static FceuxBiosState Ensure(string exe, Action<string> report = null)
        {
            var before = State(exe);
            if (before.Present)
            {
                if (before.RightSize && !before.KnownDump) Log.Info("BIOS: " + before.Path + " is 8 192 bytes but not the dump ares knows - FCEUX takes it as it is");
                return before;
            }
            try
            {
                var wanted = new[] { new LbipBiosImport.Wanted { Name = FceuxSystems.Fds.Bios, Aliases = FceuxSystems.Fds.BiosAliases } };
                var copied = LbipBiosImport.Import(before.Folder, Sources(exe), wanted, report);
                if (copied.Count > 0) Log.Info("BIOS: copied " + string.Join(", ", copied));
                else Log.Info("BIOS: no " + FceuxSystems.Fds.Bios + " found in the other emulators' folders");
            }
            catch (Exception ex) { Log.Warn("BIOS: could not look for " + FceuxSystems.Fds.Bios, ex); }
            var after = State(exe);
            if (after.Present && !after.RightSize) Log.Info("BIOS: " + after.Path + " is not 8 192 bytes - FCEUX would refuse it");
            return after;
        }

        /// <summary>RetroArch's system folder and the bios / system / firmware folders of the emulators beside this one.</summary>
        private static List<LbipBiosImport.Source> Sources(string exe)
        {
            var sources = new List<LbipBiosImport.Source>();
            try
            {
                var own = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exe));
                var emulators = System.IO.Path.GetDirectoryName(own);
                var names = new HashSet<string>(new[] { FceuxSystems.Fds.Bios }, StringComparer.OrdinalIgnoreCase);
                foreach (var a in FceuxSystems.Fds.BiosAliases) names.Add(a);
                if (emulators == null || !Directory.Exists(emulators)) return sources;
                foreach (var emu in Directory.EnumerateDirectories(emulators))
                {
                    if (string.Equals(System.IO.Path.GetFullPath(emu).TrimEnd('\\'), own.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    // Another FCEUX keeps it beside its exe.
                    sources.Add(new LbipBiosImport.Source { Dir = emu, Recursive = false, Only = names });
                    foreach (var sub in new[] { "system", "bios", "BIOS", "Bios", "firmware", "Firmware", "data" })
                    {
                        var dir = System.IO.Path.Combine(emu, sub);
                        if (Directory.Exists(dir)) sources.Add(new LbipBiosImport.Source { Dir = dir, Recursive = true, Only = names });
                    }
                }
            }
            catch (Exception ex) { Log.Warn("BIOS: could not list the other emulators", ex); }
            return sources;
        }
    }
}
