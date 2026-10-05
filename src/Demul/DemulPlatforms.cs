// The platforms Demul runs, and the system each one is launched as.
//
// FIVE come from LaunchBox's metadata database and are spelled as it spells them (LaunchBox.Metadata.db,
// table Platforms, read 2026-10-05): Sega Dreamcast, Sega Naomi, Sega Naomi 2, Sammy Atomiswave and
// Sega Hikaru. THREE have no platform there - Sega System SP, Cave CV1000 and Gaelco's PowerVR board -
// and none in LiteBox's extended database either (Core\litebox\LaunchBox.Extended.Metadata.db files
// their games under "Arcade"). So, as the Flycast plugin does for System SP (Mehdi, 29/09 and 05/10):
// ANY platform whose name holds "system sp", "cv1000" or "gaelco", whatever the case, is that board -
// a platform the user makes and scrapes as Arcade.
//
// THE SYSTEM is Demul's -run= value, read off its own help text: "run specified system (dc, naomi,
// awave, hikaru, gaelco, cave3rd)". Atomiswave is "awave", not "atomiswave". There is no naomi2 and no
// segasp: the binary's list of systems is exactly those six, and its menu says "Run Naomi/Naomi2" - so
// Naomi 2 and System SP games start under "naomi" and Demul finds the board from the romset.

using System;
using System.Collections.Generic;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Demul
{
    internal static class DemulPlatforms
    {
        public const string Dreamcast = "Sega Dreamcast";
        public const string Naomi = "Sega Naomi";
        public const string Naomi2 = "Sega Naomi 2";
        public const string Atomiswave = "Sammy Atomiswave";
        public const string Hikaru = "Sega Hikaru";

        /// <summary>The platforms of the metadata database, Dreamcast first.</summary>
        public static readonly string[] Metadata = { Dreamcast, Naomi, Naomi2, Atomiswave, Hikaru };

        public static bool IsSystemSp(string platform) => Holds(platform, "system sp");
        public static bool IsCv1000(string platform) => Holds(platform, "cv1000");
        public static bool IsGaelco(string platform) => Holds(platform, "gaelco");

        /// <summary>A platform of the user's that stands for a board with no platform in the metadata.</summary>
        public static bool IsNamedByUser(string platform) => IsSystemSp(platform) || IsCv1000(platform) || IsGaelco(platform);

        public static bool IsOurs(string platform)
            => Metadata.Any(p => Is(platform, p)) || IsNamedByUser(platform);

        public static bool IsDreamcast(string platform) => Is(platform, Dreamcast);

        /// <summary>Demul's -run= value for a platform, or null for a platform not ours.</summary>
        public static string SystemOf(string platform)
        {
            if (Is(platform, Dreamcast)) return "dc";
            if (Is(platform, Naomi) || Is(platform, Naomi2) || IsSystemSp(platform)) return "naomi";
            if (Is(platform, Atomiswave)) return "awave";
            if (Is(platform, Hikaru)) return "hikaru";
            if (IsCv1000(platform)) return "cave3rd";
            if (IsGaelco(platform)) return "gaelco";
            return null;
        }

        /// <summary>The board a platform stands for, as demul-sets.tsv names boards ("Naomi", "Naomi 2",
        /// "Atomiswave", "Hikaru", "System SP", "CV1000", "Gaelco"), "Dreamcast", or null for a platform not
        /// ours. Finer than SystemOf: Naomi 2 and System SP are launched as "naomi" but are boards of their own.</summary>
        public static string BoardOf(string platform)
        {
            if (Is(platform, Dreamcast)) return "Dreamcast";
            if (Is(platform, Naomi)) return "Naomi";
            if (Is(platform, Naomi2)) return "Naomi 2";
            if (Is(platform, Atomiswave)) return "Atomiswave";
            if (Is(platform, Hikaru)) return "Hikaru";
            if (IsSystemSp(platform)) return "System SP";
            if (IsCv1000(platform)) return "CV1000";
            if (IsGaelco(platform)) return "Gaelco";
            return null;
        }

        /// <summary>The platforms an emulator entry of ours carries: the five of the metadata, and every
        /// platform of the library named for one of the other three boards. Never throws.</summary>
        public static List<string> Wanted()
        {
            var names = new List<string>(Metadata);
            try
            {
                foreach (var p in PluginHelper.DataManager?.GetAllPlatforms() ?? Array.Empty<IPlatform>())
                {
                    string name = null;
                    try { name = p?.Name?.Trim(); } catch { }
                    if (!string.IsNullOrEmpty(name) && IsNamedByUser(name) && !names.Contains(name, StringComparer.InvariantCultureIgnoreCase))
                        names.Add(name);
                }
            }
            catch { }
            return names;
        }

        private static bool Is(string candidate, string platform)
            => string.Equals((candidate ?? "").Trim(), platform, StringComparison.InvariantCultureIgnoreCase);

        private static bool Holds(string platform, string word)
            => (platform ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
