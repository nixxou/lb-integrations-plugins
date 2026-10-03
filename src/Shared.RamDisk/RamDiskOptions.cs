// How the pack's RAM disks and VHDX are mounted - ONE set of options for every plugin (Mehdi, 02/10: a section
// of its own in the NixxMenu, "RamDisk & VHDX", not a copy per plugin). Read by RamDrive at every mount,
// written by that tab.
//
//   %LOCALAPPDATA%\lb-integrations-plugins\ramdisk.ini
//     backend   = auto | aim | imdisk     RAM disks. auto (default): Arsenal Image Mounter when it is installed, ImDisk else
//     removable = 1 | 0                   removable media - what indexers leave alone (default 1, measured
//                                         30/09: a fixed drive is held open by Everything)
//     memory    = auto | vm | awe         virtual memory, or physical memory through AWEAlloc. auto (default):
//                                         AWE through AIM, virtual memory through ImDisk, whose AWE driver is seldom
//                                         installed
//     vhdx      = windows | aim           VHDX (Vita3K's VHDX mode, image attaches): Windows' own support (vhdmp,
//                                         the default) or AIM (DiscUtils)
//
// WHAT IS SAVED IS A WISH, WHAT IS USED IS DECIDED AT EACH MOUNT (Mehdi, 02/10: "gaffe si on désinstalle un
// driver entre deux lancements"): Effective() checks what is installed NOW and falls back - AIM asked and gone:
// ImDisk; ImDisk asked and gone: AIM; VHDX through AIM without AIM:
// Windows. Logged, never a failed launch for it.
//
// Everything past backend and removable needs the helper 1.6; RamDrive refuses the mount rather than let an
// older helper quietly do something else.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LbIntegrations.RamDisk
{
    internal sealed class RamDiskOptions
    {
        public string Backend = "auto";
        public bool Removable = true;
        public bool Awe;
        /// <summary>memory=auto: AWE when the mount goes through AIM, virtual memory through ImDisk.</summary>
        public bool AutoMemory = true;
        public bool AweFor(bool aim) => AutoMemory ? aim : Awe;
        public string Vhdx = "windows";

        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lb-integrations-plugins");
        public static string FilePath => Path.Combine(Dir, "ramdisk.ini");

        /// <summary>The options as saved, the defaults for anything missing or unreadable. LBIP_RAMDISK_BACKEND
        /// overrides the backend - for the probe.</summary>
        public static RamDiskOptions Load()
        {
            var o = new RamDiskOptions();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                        var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        var value = line.Substring(eq + 1).Trim();
                        switch (key)
                        {
                            case "backend": if (value == "aim" || value == "imdisk" || value == "auto") o.Backend = value; break;
                            case "removable": o.Removable = value != "0"; break;
                            case "memory":
                                o.AutoMemory = !(value.Equals("awe", StringComparison.OrdinalIgnoreCase) || value.Equals("vm", StringComparison.OrdinalIgnoreCase));
                                o.Awe = value.Equals("awe", StringComparison.OrdinalIgnoreCase);
                                break;
                            case "vhdx": if (value == "aim" || value == "windows") o.Vhdx = value; break;
                        }
                    }
            }
            catch (Exception ex) { RamDiskLog.Warn("could not read " + FilePath, ex); }
            var forced = Environment.GetEnvironmentVariable("LBIP_RAMDISK_BACKEND");
            if (forced == "aim" || forced == "imdisk" || forced == "auto") o.Backend = forced;
            return o;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(FilePath, new[]
            {
                "# RAM disks and VHDX of the Nixx integration plugins - see the NixxMenu's RamDisk & VHDX tab",
                "backend=" + Backend,
                "removable=" + (Removable ? "1" : "0"),
                "memory=" + (AutoMemory ? "auto" : Awe ? "awe" : "vm"),
                "vhdx=" + Vhdx,
            });
        }

        /// <summary>What a mount will really use, given what is installed NOW: the RAM disk driver resolved to "aim"
        /// or "imdisk" (null when neither is there), the VHDX way resolved
        /// to "aim" or "windows". <paramref name="notes"/> says what was changed and why, for the log.</summary>
        public RamDiskOptions Effective(bool aimThere, bool imdiskThere, out string notes)
        {
            var n = new List<string>();
            var e = (RamDiskOptions)MemberwiseClone();
            if (Backend == "aim" && !aimThere) { e.Backend = imdiskThere ? "imdisk" : null; n.Add("AIM was chosen but is not installed - " + (imdiskThere ? "ImDisk instead" : "no RAM disk")); }
            else if (Backend == "imdisk" && !imdiskThere) { e.Backend = aimThere ? "aim" : null; n.Add("ImDisk was chosen but is not installed - " + (aimThere ? "AIM instead" : "no RAM disk")); }
            else if (Backend == "auto") e.Backend = aimThere ? "aim" : imdiskThere ? "imdisk" : null;   // AIM first (Mehdi, 04/10 - ImDisk first since 02/10 until then)
            if (Vhdx == "aim" && !aimThere) { e.Vhdx = "windows"; n.Add("VHDX through AIM, which is not installed - Windows' own instead"); }
            notes = string.Join("; ", n);
            return e;
        }

        /// <summary>Anything an older helper would get wrong without a word?</summary>
        public bool NeedsProtocol16 => !AutoMemory && Awe;
    }
}
