// What ares runs, and which LaunchBox platform each is - read off ares v148's own code (06/10):
//
//   desktop-ui\emulator\*.cpp   each system's name (what --system matches, EXACTLY, case and all), its firmware
//                               ({type, region, SHA-256} - the SHA-256 empty for Saturn and Neo Geo)
//   desktop-ui\settings\settings.cpp:225-236   a system's settings node: its name without spaces nor parentheses
//                               ("LaserActive (SEGA PAC)" -> "LaserActiveSEGAPAC"), a firmware's key
//                               "<node>/Firmware/<type>.<region>" with the type's spaces as '-'
//   mia\medium\*.cpp            the files each opens, and the files of its saves
//
// THE NAMES OTHER FRONTENDS USE THAT ARE NOT ARES'S ANY MORE (06/10): "Mega LD" (ES-DE; LaserActive (SEGA PAC) since
// 2025-09-19), "Neo Geo" and "Sufami Turbo" (Playnite) - with them ares says "Unrecognized argument for --system" and
// loads nothing. This table is the plugin's one list, checked by the probe against `ares --help`.
//
// THE FIRMWARE FILE NAMES are not ares's - it asks for none, a firmware is a path in settings.bml - but the names the
// dumps go by in the other emulators' folders (RetroArch's system\, RetroBat's bios\): what AresFirmware looks for.
//
// A PLATFORM'S SYSTEMS: the first is its default; the others are what a game's window offers ("Sega CD": Mega CD, Mega
// CD 32X). An extension can pick one on its own - a .32x among Genesis games is a 32X game.
//
// NOT IN THE WINDOWS BUILD, measured 06/10 on v148's own "ares --help": Saturn and Atari 5200 - their code is in the source,
// their cores are not built into ares-windows-x64.zip - so they are not offered.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Ares
{
    internal sealed class AresFirmwareSpec
    {
        public string Type, Region, Sha256;
        public string[] Names;
        public bool Optional;
        /// <summary>Another entry of the same system given in its place when it has no file of its own - said as another dump
        /// (the US System Card: the Japanese one, which the other emulators use for both).</summary>
        public string FallbackKey;

        /// <summary>"BIOS.US", "System-Card-3.0.US" - the end of its settings key.</summary>
        public string Key => (Type + "." + Region).Replace(' ', '-');
        public string Label => Type + " (" + Region + ")";
    }

    internal sealed class AresSystem
    {
        public string Name;                 // what --system takes
        public string[] Extensions = Array.Empty<string>();
        public string[] SaveExtensions = Array.Empty<string>();
        public bool Disc;                   // cue / chd: no .7z, a .m3u reduced to its first disc
        public AresFirmwareSpec[] Firmware = Array.Empty<AresFirmwareSpec>();
        /// <summary>The system whose firmware keys are used, when not its own (Mega CD 32X starts on the Mega CD's BIOS).</summary>
        public string FirmwareOf;
        /// <summary>The sub-folder of Paths/Saves for its saves and for its states, when they are not its name.</summary>
        public string SaveFolder, StateFolder;

        /// <summary>Its settings node: the name without spaces nor parentheses.</summary>
        public string Node => Name.Replace(" ", "").Replace("(", "").Replace(")", "");
    }

    internal sealed class AresPlatform
    {
        public string Platform;             // LaunchBox's name
        public string[] Systems;            // the default first
        public Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Default => Systems[0];
    }

    internal static class AresSystems
    {
        private static AresFirmwareSpec F(string type, string region, string sha, params string[] names)
            => new AresFirmwareSpec { Type = type, Region = region, Sha256 = sha, Names = names };

        private static readonly string[] Cart = { ".ram", ".eeprom", ".flash", ".rtc" };

        public static readonly AresSystem[] All =
        {
            new AresSystem { Name = "Atari 2600", Extensions = new[] { ".a26", ".bin" } },
            new AresSystem { Name = "ColecoVision", Extensions = new[] { ".col" },
                Firmware = new[] { F("BIOS", "World", "990bf1956f10207d8781b619eb74f89b00d921c8d45c95c334c16c8cceca09ad", "colecovision.rom", "coleco.rom", "bios.col") } },
            new AresSystem { Name = "Famicom", Extensions = new[] { ".nes", ".fc", ".unf", ".unif", ".unh" }, SaveExtensions = new[] { ".ram", ".eeprom", ".flash" } },
            new AresSystem { Name = "Dendy", Extensions = new[] { ".nes", ".fc" }, SaveExtensions = new[] { ".ram", ".eeprom", ".flash" }, SaveFolder = "Famicom", StateFolder = "Famicom" },
            new AresSystem { Name = "Famicom Disk System", Extensions = new[] { ".fds" }, SaveExtensions = new[] { ".d1a", ".d1b", ".d2a", ".d2b" }, StateFolder = "Famicom",
                Firmware = new[] { F("BIOS", "Japan", "fdc1a76e654feea993fcb38366e05ee5f4eb641f86fe6bebaeefd412e112dd72", "disksys.rom", "fds_bios.bin") } },
            new AresSystem { Name = "Super Famicom", Extensions = new[] { ".sfc", ".smc", ".swc", ".fig" }, SaveExtensions = new[] { ".ram", ".iram", ".bsx", ".rtc", ".dram" } },
            new AresSystem { Name = "Nintendo 64", Extensions = new[] { ".z64", ".n64", ".v64" }, SaveExtensions = new[] { ".ram", ".eeprom", ".flash", ".rtc", ".pak" } },
            new AresSystem { Name = "Nintendo 64DD", Extensions = new[] { ".ndd", ".n64dd", ".d64" }, SaveExtensions = new[] { ".disk", ".rtc", ".pak" }, StateFolder = "Nintendo 64",
                Firmware = new[] {
                    F("BIOS", "Japan", "806400ec0df94b0755de6c5b8249d6b6a9866124c5ddbdac198bde22499bfb8b", "IPL_JAP.n64", "64DD_IPL.bin", "IPL.n64"),
                    F("BIOS", "US", "e9fec87a45fba02399e88064b9e2f8cf0f2106e351c58279a87f05da5bc984ad", "IPL_USA.n64", "64DD_IPL_US.bin"),
                    F("BIOS", "DEV", "9c2962a8b994a29e4cd04b3a6e4ed730a751414655ab6a9799ebf5fc08b79d44", "IPL_DEV.n64", "64DD_IPL_DEV.bin") } },
            new AresSystem { Name = "Game Boy", Extensions = new[] { ".gb" }, SaveExtensions = Cart },
            new AresSystem { Name = "Game Boy Color", Extensions = new[] { ".gbc", ".gb" }, SaveExtensions = Cart, SaveFolder = "Game Boy", StateFolder = "Game Boy" },
            new AresSystem { Name = "Game Boy Advance", Extensions = new[] { ".gba" }, SaveExtensions = Cart,
                Firmware = new[] { F("BIOS", "World", "fd2547724b505f487e6dcb29ec2ecff3af35a841a77ab2e85fd87350abd36570", "gba_bios.bin", "gbabios.bin") } },
            new AresSystem { Name = "SG-1000", Extensions = new[] { ".sg", ".sg1000" }, SaveExtensions = new[] { ".ram" } },
            new AresSystem { Name = "SC-3000", Extensions = new[] { ".sc", ".sg" } },
            new AresSystem { Name = "Master System", Extensions = new[] { ".sms", ".ms" }, SaveExtensions = new[] { ".ram" },
                Firmware = new[] {
                    new AresFirmwareSpec { Type = "BIOS", Region = "US", Sha256 = "477617917a12a30f9f43844909dc2de6e6a617430f5c9a36306c86414a670d50", Names = new[] { "bios_U.sms", "bios.sms" }, Optional = true },
                    new AresFirmwareSpec { Type = "BIOS", Region = "Japan", Sha256 = "67846e26764bd862f19179294347f7353a4166b62ac4198a5ec32933b7da486e", Names = new[] { "bios_J.sms" }, Optional = true },
                    new AresFirmwareSpec { Type = "BIOS", Region = "Europe", Sha256 = "477617917a12a30f9f43844909dc2de6e6a617430f5c9a36306c86414a670d50", Names = new[] { "bios_E.sms", "bios_U.sms", "bios.sms" }, Optional = true } } },
            new AresSystem { Name = "Game Gear", Extensions = new[] { ".gg" }, SaveExtensions = new[] { ".ram" },
                Firmware = new[] { new AresFirmwareSpec { Type = "BIOS", Region = "World", Sha256 = "8c8a21335038285cfa03dc076100c1f0bfadf3e4ff70796f11f3dfaaab60eee2", Names = new[] { "bios.gg", "majesco.gg" }, Optional = true } } },
            new AresSystem { Name = "Mega Drive", Extensions = new[] { ".md", ".gen", ".bin" }, SaveExtensions = new[] { ".ram", ".eeprom" } },
            new AresSystem { Name = "Mega 32X", Extensions = new[] { ".32x" }, SaveExtensions = new[] { ".ram", ".eeprom" }, StateFolder = "Mega Drive" },
            new AresSystem { Name = "Mega CD", Extensions = new[] { ".cue", ".chd", ".iso", ".bin", ".img" }, SaveExtensions = new[] { ".bram" }, Disc = true, StateFolder = "Mega Drive",
                Firmware = new[] {
                    F("BIOS", "US", "fb477cdbf94c84424c2feca4fe40656d85393fe7b7b401911b45ad2eb991258c", "bios_CD_U.bin", "us_scd2_9306.bin", "segacd_bios_us.bin"),
                    F("BIOS", "Japan", "7133fc2dd2fe5b7d0acd53a5f10f3d00b5d31270239ad20d74ef32393e24af88", "bios_CD_J.bin", "jp_mcd2_9306.bin", "megacd_bios_jp.bin"),
                    F("BIOS", "Europe", "fe608a2a07676a23ab5fd5eee2f53c9e2526d69a28aa16ccd85c0ec42e6933cb", "bios_CD_E.bin", "eu_mcd2_9306.bin", "megacd_bios_eu.bin") } },
            new AresSystem { Name = "Mega CD 32X", Extensions = new[] { ".cue", ".chd", ".iso", ".bin", ".img" }, SaveExtensions = new[] { ".bram" }, Disc = true, FirmwareOf = "Mega CD", StateFolder = "Mega Drive" },
            new AresSystem { Name = "LaserActive (SEGA PAC)", Extensions = new[] { ".mmi" }, SaveExtensions = new[] { ".bram" }, Disc = true, StateFolder = "Mega Drive",
                Firmware = new[] {
                    F("BIOS", "US", "e89b5a319f66406611ec82fe5c4aa6827c175a05135bd7bd177366cba0465021", "[BIOS] LaserActive PAC-S10 (USA) (v1.04).bin"),
                    F("BIOS", "Japan", "dca942d977217f703d8d1c6eb1aeb6b32c78ecc421486bbb46c459d385161c94", "[BIOS] LaserActive PAC-S1 (Japan) (v1.02).bin") } },
            new AresSystem { Name = "PC Engine", Extensions = new[] { ".pce" }, SaveExtensions = new[] { ".bram" } },
            new AresSystem { Name = "SuperGrafx", Extensions = new[] { ".sgx" }, SaveExtensions = new[] { ".bram" }, StateFolder = "PC Engine" },
            new AresSystem { Name = "PC Engine CD", Extensions = new[] { ".cue", ".chd", ".iso", ".bin", ".img" }, SaveExtensions = new[] { ".bram" }, Disc = true, StateFolder = "PC Engine",
                Firmware = new[] {
                    new AresFirmwareSpec { Type = "System-Card 1.0", Region = "Japan", Sha256 = "afe9f27f91ac918348555b86298b4f984643eafa2773196f2c5441ea84f0c3bb", Names = new[] { "syscard1.pce" }, Optional = true },
                    F("System Card 3.0", "Japan", "e11527b3b96ce112a037138988ca72fd117a6b0779c2480d9e03eaebece3d9ce", "syscard3.pce", "syscard3j.pce"),
                    new AresFirmwareSpec { Type = "System Card 3.0", Region = "US", Sha256 = "cadac2725711b3c442bcf237b02f5a5210c96f17625c35fa58f009e0ed39e4db", Names = new[] { "syscard3u.pce" }, FallbackKey = "System-Card-3.0.Japan" },
                    new AresFirmwareSpec { Type = "Games Express", Region = "Japan", Sha256 = "4b86bb96a48a4ca8375fc0109631d0b1d64f255a03b01de70594d40788ba6c3d", Names = new[] { "gexpress.pce" }, Optional = true } } },
            new AresSystem { Name = "SuperGrafx CD", Extensions = new[] { ".cue", ".chd", ".iso", ".bin", ".img" }, SaveExtensions = new[] { ".bram" }, Disc = true, StateFolder = "PC Engine",
                Firmware = new[] { F("Arcade Card", "Japan", "e11527b3b96ce112a037138988ca72fd117a6b0779c2480d9e03eaebece3d9ce", "syscard3.pce", "syscard3j.pce") } },
            new AresSystem { Name = "LaserActive (NEC PAC)", Extensions = new[] { ".mmi" }, SaveExtensions = new[] { ".bram" }, Disc = true, StateFolder = "PC Engine",
                Firmware = new[] {
                    F("PAC-N10", "US", "0e87a3385a27b3a4cac51934819b7eefa5b3d690768d2495633838488cd0e2e4", "[BIOS] LaserActive PAC-N10 (USA) (v1.02).bin"),
                    F("PAC-N1", "Japan", "459325690a458baebd77495c91e37c4dddfdd542ba13a821ce954e5bb245627f", "[BIOS] LaserActive PAC-N1 (Japan) (v1.02).bin") } },
            new AresSystem { Name = "Neo Geo AES", Extensions = new[] { ".zip", ".ng" }, StateFolder = "Neo Geo",
                Firmware = new[] { F("BIOS", "World", null, "neogeo.zip") } },
            new AresSystem { Name = "Neo Geo MVS", Extensions = new[] { ".zip", ".ng" }, StateFolder = "Neo Geo",
                Firmware = new[] { F("BIOS", "World", null, "neogeo.zip") } },
            new AresSystem { Name = "Neo Geo Pocket", Extensions = new[] { ".ngp" }, SaveExtensions = new[] { ".sav", ".cram", ".aram" },
                Firmware = new[] { F("BIOS", "World", "0293555b21c4fac516d25199df7809b26beeae150e1d4504a050db32264a6ad7", "[BIOS] SNK Neo Geo Pocket (Japan, Europe) (En,Ja).ngp", "ngp_bios.ngp", "neopop.rom") } },
            new AresSystem { Name = "Neo Geo Pocket Color", Extensions = new[] { ".ngc", ".ngpc" }, SaveExtensions = new[] { ".sav", ".cram", ".aram" }, StateFolder = "Neo Geo Pocket",
                Firmware = new[] { F("BIOS", "World", "8fb845a2f71514cec20728e2f0fecfade69444f8d50898b92c2259f1ba63e10d", "[BIOS] SNK Neo Geo Pocket Color (World) (En,Ja).ngc", "ngpc_bios.ngc", "neopopc.rom") } },
            new AresSystem { Name = "WonderSwan", Extensions = new[] { ".ws" }, SaveExtensions = new[] { ".ram", ".eeprom", ".rtc", ".flash" } },
            new AresSystem { Name = "WonderSwan Color", Extensions = new[] { ".wsc" }, SaveExtensions = new[] { ".ram", ".eeprom", ".rtc", ".flash" }, StateFolder = "WonderSwan" },
            new AresSystem { Name = "Pocket Challenge V2", Extensions = new[] { ".pc2", ".pcv2" }, StateFolder = "WonderSwan" },
            new AresSystem { Name = "PlayStation", Extensions = new[] { ".cue", ".chd", ".iso", ".bin", ".img", ".exe", ".ps-exe" }, SaveExtensions = new[] { ".card" }, Disc = true,
                Firmware = new[] {
                    F("BIOS", "US", "11052b6499e466bbf0a709b1f9cb6834a9418e66680387912451e971cf8a1fef", "scph5501.bin", "scph1001.bin", "scph7001.bin", "scph7003.bin", "scph101.bin"),
                    F("BIOS", "Japan", "9c0421858e217805f4abe18698afea8d5aa36ff0727eb8484944e00eb5e7eadb", "scph5500.bin", "scph1000.bin", "scph7000.bin"),
                    F("BIOS", "Europe", "1faaa18fa820a0225e488d9f086296b8e6c46df739666093987ff7d8fd352c09", "scph5502.bin", "scph1002.bin", "scph7002.bin", "scph7502.bin") } },
            new AresSystem { Name = "MSX", Extensions = new[] { ".rom", ".msx", ".cas", ".wav", ".tzx", ".tsx" },
                Firmware = new[] { F("BIOS", "Japan", "413a2b601a94b3792e054be2439cc77a1819cceadbfa9542f88d51c7480f2ef0", "MSX.ROM", "msx.rom") } },
            new AresSystem { Name = "MSX2", Extensions = new[] { ".rom", ".msx2", ".cas", ".wav", ".tzx", ".tsx" }, StateFolder = "MSX",
                Firmware = new[] {
                    F("MAIN", "Japan", "0c672d86ead61a97f49a583b88b7c1905da120645cd44f0c9f2baf4f4631e0b1", "MSX2.ROM", "msx2.rom"),
                    F("SUB", "Japan", "6c6f421a10c428d960b7ecc990f99af1c638147f747bddca7b0bf0e2ab738300", "MSX2EXT.ROM", "msx2ext.rom") } },
            new AresSystem { Name = "ZX Spectrum", Extensions = new[] { ".tzx", ".tap", ".wav" }, SaveExtensions = new[] { ".sav" } },
            new AresSystem { Name = "ZX Spectrum 128", Extensions = new[] { ".tzx", ".tap", ".wav" }, SaveExtensions = new[] { ".sav" }, SaveFolder = "ZX Spectrum", StateFolder = "ZX Spectrum" },
        };

        private static AresPlatform P(string platform, params string[] systems) => new AresPlatform { Platform = platform, Systems = systems };

        public static readonly AresPlatform[] Platforms =
        {
            P("Atari 2600", "Atari 2600"),
            P("ColecoVision", "ColecoVision"),
            new AresPlatform { Platform = "Nintendo Entertainment System", Systems = new[] { "Famicom", "Dendy" }, ByExtension = { [".fds"] = "Famicom Disk System" } },
            P("Nintendo Famicom Disk System", "Famicom Disk System"),
            P("Super Nintendo Entertainment System", "Super Famicom"),
            new AresPlatform { Platform = "Nintendo 64", Systems = new[] { "Nintendo 64" }, ByExtension = { [".ndd"] = "Nintendo 64DD", [".n64dd"] = "Nintendo 64DD", [".d64"] = "Nintendo 64DD" } },
            P("Nintendo 64DD", "Nintendo 64DD"),
            new AresPlatform { Platform = "Nintendo Game Boy", Systems = new[] { "Game Boy", "Game Boy Color" }, ByExtension = { [".gbc"] = "Game Boy Color" } },
            P("Nintendo Game Boy Color", "Game Boy Color"),
            P("Nintendo Game Boy Advance", "Game Boy Advance"),
            P("Sega SG-1000", "SG-1000", "SC-3000"),
            P("Sega SC-3000", "SC-3000"),
            P("Sega Master System", "Master System"),
            P("Sega Game Gear", "Game Gear"),
            new AresPlatform { Platform = "Sega Genesis", Systems = new[] { "Mega Drive" }, ByExtension = { [".32x"] = "Mega 32X" } },
            P("Sega 32X", "Mega 32X"),
            P("Sega CD", "Mega CD", "Mega CD 32X"),
            P("Sega CD 32X", "Mega CD 32X"),
            P("Pioneer LaserActive", "LaserActive (SEGA PAC)", "LaserActive (NEC PAC)"),
            new AresPlatform { Platform = "NEC TurboGrafx-16", Systems = new[] { "PC Engine" }, ByExtension = { [".sgx"] = "SuperGrafx" } },
            P("NEC TurboGrafx-CD", "PC Engine CD", "SuperGrafx CD"),
            P("PC Engine SuperGrafx", "SuperGrafx"),
            P("SNK Neo Geo AES", "Neo Geo AES", "Neo Geo MVS"),
            P("SNK Neo Geo MVS", "Neo Geo MVS", "Neo Geo AES"),
            P("SNK Neo Geo Pocket", "Neo Geo Pocket"),
            P("SNK Neo Geo Pocket Color", "Neo Geo Pocket Color"),
            P("WonderSwan", "WonderSwan", "Pocket Challenge V2"),
            P("WonderSwan Color", "WonderSwan Color"),
            P("Sony Playstation", "PlayStation"),
            P("Microsoft MSX", "MSX", "MSX2"),
            P("Microsoft MSX2", "MSX2", "MSX"),
            P("Sinclair ZX Spectrum", "ZX Spectrum", "ZX Spectrum 128"),
        };

        public static AresSystem System(string name)
            => All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

        public static AresPlatform Platform(string platform)
            => Platforms.FirstOrDefault(p => string.Equals(p.Platform, (platform ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>The firmware a system starts on: its own, or the one it borrows (Mega CD 32X: the Mega CD's).</summary>
        public static (AresSystem Owner, AresFirmwareSpec[] Specs) FirmwareFor(AresSystem system)
        {
            var owner = system?.FirmwareOf != null ? System(system.FirmwareOf) : system;
            return (owner, owner?.Firmware ?? Array.Empty<AresFirmwareSpec>());
        }

        /// <summary>Every extension of every platform, for the catalogue row: ".sfc; .smc; ...".</summary>
        public static string AllExtensions(AresPlatform p)
        {
            var exts = p.Systems.Select(System).Where(s => s != null).SelectMany(s => s.Extensions).Concat(p.ByExtension.Keys)
                        .Concat(new[] { ".zip" }).Concat(p.Systems.Select(System).Any(s => s != null && !s.Disc) ? new[] { ".7z" } : new string[0])
                        .Concat(p.Systems.Select(System).Any(s => s != null && s.Disc) ? new[] { ".m3u" } : new string[0])
                        .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join("; ", exts);
        }
    }
}
