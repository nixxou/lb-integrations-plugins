// What Mesen runs, and which LaunchBox platform each is - THE PLUGIN'S ONE LIST (the catalogue rows, IsPlatformSupported,
// CreateEmulator, the BIOS, the launch and the saves all read it; no platform name is spelt anywhere else).
//
// READ OFF MESEN'S OWN CODE at the released tags - SourMesen/Mesen2 2.1.1 (137ae7ce) and nesdev-org/MesenCE 2.2.1
// (20ba206c), 18/10 (docs\mesen.md §6):
//   Core\*\*Console.cpp GetSupportedExtensions   what each core opens: NES .nes .fds .unif .unf .nsf .nsfe .studybox .qd;
//                                                 SNES .sfc .swc .fig .smc .bs .gb .gbc .gbx .spc .st; Game Boy .gb .gbc
//                                                 .gbx .gbs; PC Engine .pce .cue .sgx .hes; SMS .sms .gg .sg .col; GBA .gba;
//                                                 WonderSwan .ws .wsc (+ .pc2 in CE only - its MesenCore.dll, measured)
//   Core\SMS\SmsConsole.cpp LoadRom               the Sega model IS the extension: .gg Game Gear, .col ColecoVision, .sg
//                                                 SG-1000, anything else Master System
//   Core\Shared\FirmwareHelper.h                  every firmware's file name, size and alternate name (MesenFirmware below)
//   UI\Interop\FirmwareTypeExtensions.cs          the SHA-256 Mesen knows for each
//   every SaveBattery("<ext>") of Core\           the battery files' extensions (MesenSystem.Battery)
//
// THE "SYSTEM" OF A GAME (the ares rule, Mehdi 06/10): the core comes from the extension - Mesen needs no --system - so what a
// platform decides is the MODEL inside a core: a Game Boy game on a Game Boy or a Game Boy Color, a SuperGrafx, a mono
// WonderSwan, the Super Game Boy (run by the SNES core). Those are configuration values a switch sets
// (--gameboy.model=..., --ws.model=..., --pcEngine.consoleType=... - UI\Config\*Config.cs enums, names exact): a platform
// row's line carries the platform's (MesenPlatform.Line), a game's window can force another (MesenSystem.Switch). The rest of
// the systems here (Famicom Disk System, Satellaview, Sufami Turbo...) are told apart by the extension alone, and matter for
// their BIOS. A switch PERSISTS in settings.json (measured 18/10): MesenSession puts the user's value back after the session.
//
// LAUNCHBOX'S NAMES were read in a copy of G:\LB1326\Metadata\LaunchBox.Metadata.db on 18/10 (Platforms.Name), LiteBox's own
// ones in its code (the hacks, MSU-1 and Pocket Challenge V2 as AresSystems; "Nintendo Famicom", "Super Famicom", "Nintendo
// Sufami Turbo", "Super Game Boy", "NEC PC Engine", "NEC PC Engine-CD" from Host\Romm\RommPlatformMap.cs / Ra / EmuMovies).
// Not claimed: Sega SC-3000 (Mesen's .sg is the SG-1000 only, its file dialog says so), Vs. System / PlayChoice-10 (no
// platform of their own: .nes games of an Arcade platform - question for Mehdi), the music formats as platforms.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LbIntegrations.Mesen
{
    /// <summary>A firmware file of Mesen's Firmware folder (FirmwareHelper.h): its name, the alternate name Mesen also tries,
    /// the sizes it accepts (it checks the size, never the content), and the SHA-256 its UI knows.</summary>
    internal sealed class MesenFirmware
    {
        public string File, Alt, Label;
        /// <summary>Other names the same dump goes by in the other emulators' folders - copied in under <see cref="File"/>.</summary>
        public string[] Aliases = Array.Empty<string>();
        /// <summary>Exact sizes Mesen loads; <see cref="MinSize"/> instead for BS-X.bin (any size from 32 KiB).</summary>
        public long[] Sizes = Array.Empty<long>();
        public long MinSize;
        public string[] Sha256 = Array.Empty<string>();
        /// <summary>Not needed to start: a boot ROM (Mesen has its own boot), a chip only a few games use.</summary>
        public bool Optional;

        public IEnumerable<string> Names => new[] { File, Alt }.Where(n => !string.IsNullOrEmpty(n)).Concat(Aliases).Distinct(StringComparer.OrdinalIgnoreCase);
        public bool SizeFits(long size) => MinSize > 0 ? size >= MinSize : Sizes.Length == 0 || Sizes.Contains(size);
    }

    internal sealed class MesenSystem
    {
        public string Key;                  // short, lower case: the plugin's own (settings, ids)
        public string Name;                 // what the windows show
        public string Core;                 // Mesen's console: Nes, Snes, Gameboy, PcEngine, Sms, Gba, Ws
        public string[] Extensions = Array.Empty<string>();
        public string[] Battery = Array.Empty<string>();
        public MesenFirmware[] Firmware = Array.Empty<MesenFirmware>();
        /// <summary>The switch that forces this model, when a game's window chooses it ("--gameboy.model=SuperGameboy").
        /// Null: the core of the extension is the system.</summary>
        public string Switch;
        /// <summary>Only in MesenCE (2.2 and later): Pocket Challenge V2.</summary>
        public bool CeOnly;
        /// <summary>A disc system: no archives.</summary>
        public bool Disc;
    }

    internal sealed class MesenPlatform
    {
        public string Platform;             // LaunchBox's name
        public string[] Systems;            // the default first; the others are what a game's window offers
        public Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The platform row's command line the plugin writes ("" for none): the model that platform means.</summary>
        public string Line = "";
        /// <summary>One of LiteBox's other spellings: claimed, but a row is made for it only when the library has the platform.</summary>
        public bool OnlyIfInLibrary;
        public string Default => Systems[0];
    }

    internal static class MesenSystems
    {
        private static MesenFirmware Fw(string file, string alt, string label, long[] sizes, params string[] sha)
            => new MesenFirmware { File = file, Alt = alt, Label = label, Sizes = sizes, Sha256 = sha };
        private static MesenFirmware Opt(MesenFirmware f) { f.Optional = true; return f; }
        private static long[] S(params long[] s) => s;

        // ── the firmware, FirmwareHelper.h + FirmwareTypeExtensions.cs (identical in 2.1.1 and CE 2.2.1, diffed 18/10) ──
        internal static readonly MesenFirmware Fds = new MesenFirmware { File = "disksys.rom", Alt = "FdsBios.bin", Label = "Famicom Disk System BIOS",
            Aliases = new[] { "fds_bios.bin" }, Sizes = S(0x2000),
            Sha256 = new[] { "99c18490ed9002d9c6d999b9d8d15be5c051bdfa7cc7e73318053c9a994b0178", "a0a9d57cbace21bf9c85c2b85e86656317f0768d7772acc90c7411ab1dbff2bf" } };
        internal static readonly MesenFirmware StudyBox = Fw("StudyBox.bin", null, "Study Box BIOS", S(0x40000), "365f84c86f7f7c3aaa2042d78494d41448e998ec5a89ac1b5fecb452951d514c");
        internal static readonly MesenFirmware Ymf288 = Opt(Fw("ymf288_adpcm_rom.bin", null, "YMF288 ADPCM ROM (EPSM expansion)", S(0x2000), "53afd0fa9c62eda3e2be939e23f3adf48a2af8ad37bb1640261726c5d5adeba8"));
        internal static readonly MesenFirmware Gba = Fw("gba_bios.bin", null, "Game Boy Advance BIOS", S(0x4000), "fd2547724b505f487e6dcb29ec2ecff3af35a841a77ab2e85fd87350abd36570");
        internal static readonly MesenFirmware DmgBoot = Opt(Fw("dmg_boot.bin", "gb_bios.bin", "Game Boy boot ROM", S(256),
            "cf053eccb4ccafff9e67339d4e78e98dce7d1ed59be819d2a1ba2232c6fce1c7", "26e71cf01e301e5dc40e987cd2ecbf6d0276245890ac829db2a25323da86818e"));
        internal static readonly MesenFirmware CgbBoot = Opt(Fw("cgb_boot.bin", "gbc_bios.bin", "Game Boy Color boot ROM", S(2304),
            "b4f2e416a35eef52cba161b159c7c8523a92594facb924b3ede0d722867c50c7", "3a307a41689bee99a9a32ea021bf45136906c86b2e4f06c806738398e4f92e45"));
        internal static readonly MesenFirmware SgbBoot = Opt(Fw("sgb_boot.bin", "sgb_bios.bin", "Super Game Boy boot ROM", S(256), "0e4ddff32fc9d1eeaae812a157dd246459b00c9e14f2f61751f661f32361e360"));
        internal static readonly MesenFirmware Sgb2Boot = Opt(Fw("sgb2_boot.bin", "sgb_bios.bin", "Super Game Boy 2 boot ROM", S(256), "fd243c4fb27008986316ce3df29e9cfbcdc0cd52704970555a8bb76edbec3988"));
        /// <summary>The SNES cartridge of the Super Game Boy: SGB2.sfc when Gameboy.UseSgb2 is true (Mesen's default), else
        /// SGB1.sfc (FirmwareHelper::LoadSgbFirmware) - MesenFirmwareFiles picks which one is required.</summary>
        internal static readonly MesenFirmware Sgb1 = Fw("SGB1.sfc", null, "Super Game Boy (SNES cartridge)", S(0x40000),
            "bba9c269273bedb9b38bd5eb23bfaa6e509b8decc7cb80bb5513905af04f4ceb", "c6c4daab5c899b69900c460787de6089edabe94b760f96d9f583d30cc0a5bb30", "a75160f7b89b1f0e20fd2f6441bb86285c7378db5035ef6885485eaff6059376");
        internal static readonly MesenFirmware Sgb2 = Fw("SGB2.sfc", null, "Super Game Boy 2 (SNES cartridge)", S(0x80000), "c172498a23d1176672931bab33b629c7d28f914a43dca9e540b8af1b37ccf2c6");
        internal static readonly MesenFirmware Bsx = new MesenFirmware { File = "BS-X.bin", Label = "Satellaview BIOS (BS-X cartridge)", MinSize = 0x8000,
            Sha256 = new[] { "27cfdb99f7e4252bf3740d420147b63c4c88616883bc5e7fe43f2f30bf8c8cbb", "a49827b45ff9ac9cf5b4658190e1428e59251bc82d8a63d8e9e0f71e439f008f",
                             "3ce321496edc5d77038de2034eb3fb354d7724afd0bc7fd0319f3eb5d57b984d", "77d94d64d745014bf8b51280a4204056cdeb9d41ea30eae80dbc006675bebef8" } };
        internal static readonly MesenFirmware Sufami = Fw("SufamiTurbo.sfc", null, "Sufami Turbo BIOS", S(0x40000), "edacb453da14f825f05d1134d6035f4bf034e55f7cfb97c70c4ee107eabc7342");
        /// <summary>DSP and Seta chips: only the few SNES games that carry one (Super Mario Kart, Pilotwings...) need theirs, and a
        /// ROM dump with the chip's program appended needs none. Copied when found, never asked for.</summary>
        internal static readonly MesenFirmware[] SnesChips =
        {
            Opt(Fw("dsp1.rom", null, "DSP-1", S(0x2000), "91e87d11e1c30d172556bed2211cce2efa94ba595f58c5d264809ef4d363a97b")),
            Opt(Fw("dsp1b.rom", null, "DSP-1B", S(0x2000), "d789cb3c36b05c0b23b6c6f23be7aa37c6e78b6ee9ceac8d2d2aa9d8c4d35fa9")),
            Opt(Fw("dsp2.rom", null, "DSP-2", S(0x2000), "03ef4ef26c9f701346708cb5d07847b5203cf1b0818bf2930acd34510ffdd717")),
            Opt(Fw("dsp3.rom", null, "DSP-3", S(0x2000), "0971b08f396c32e61989d1067dddf8e4b14649d548b2188f7c541b03d7c69e4e")),
            Opt(Fw("dsp4.rom", null, "DSP-4", S(0x2000), "752d03b2d74441e430b7f713001fa241f8bbcfc1a0d890ed4143f174dbe031da")),
            Opt(Fw("st010.rom", null, "ST010", S(0xD000), "fa9bced838fedea11c6f6ace33d1878024bdd0d02cc9485899d0bdd4015ec24c")),
            Opt(Fw("st011.rom", null, "ST011", S(0xD000), "8b2b3f3f3e6e29f4d21d8bc736b400bc988b7d2214ebee15643f01c1fee2f364")),
            Opt(Fw("st018.rom", null, "ST018", S(0x28000), "6df209ab5d2524d1839c038be400ae5eb20dafc14a3771a3239cd9e8acd53806")),
        };
        internal static readonly MesenFirmware PceCd = new MesenFirmware { File = "[BIOS] Super CD-ROM System (Japan) (v3.0).pce", Alt = "syscard3.pce",
            Label = "PC Engine Super CD-ROM System card 3.0", Aliases = new[] { "syscard3j.pce" }, Sizes = S(0x40000),
            Sha256 = new[] { "e11527b3b96ce112a037138988ca72fd117a6b0779c2480d9e03eaebece3d9ce" } };
        internal static readonly MesenFirmware GamesExpress = new MesenFirmware { File = "[BIOS] Games Express CD Card (Japan).pce", Alt = "gecard.pce",
            Label = "Games Express CD card (its few discs only)", Aliases = new[] { "gexpress.pce" }, Sizes = S(0x8000, 0x4000), Optional = true,
            Sha256 = new[] { "4b86bb96a48a4ca8375fc0109631d0b1d64f255a03b01de70594d40788ba6c3d", "da173b20694c2b52087b099b8c44e471d3ee08a666c90d4afd997f8e1382add8" } };
        internal static readonly MesenFirmware Coleco = new MesenFirmware { File = "bios.col", Label = "ColecoVision BIOS", Aliases = new[] { "colecovision.rom", "coleco.rom" },
            Sizes = S(0x2000), Sha256 = new[] { "990bf1956f10207d8781b619eb74f89b00d921c8d45c95c334c16c8cceca09ad", "fb4a898eb93b19b36773d87a6c70eb28f981b2686bebdd2d431b05dcdf9cffd4" } };
        /// <summary>Read whatever its size (FirmwareHelper::LoadSmsBios takes the file as it is).</summary>
        internal static readonly MesenFirmware SmsBoot = Opt(new MesenFirmware { File = "bios.sms", Label = "Master System BIOS", Aliases = new[] { "bios_U.sms", "bios_E.sms" },
            Sha256 = new[] { "477617917a12a30f9f43844909dc2de6e6a617430f5c9a36306c86414a670d50" } });
        internal static readonly MesenFirmware GgBoot = Opt(new MesenFirmware { File = "bios.gg", Label = "Game Gear BIOS", Aliases = new[] { "majesco.gg" },
            Sha256 = new[] { "8c8a21335038285cfa03dc076100c1f0bfadf3e4ff70796f11f3dfaaab60eee2" } });
        internal static readonly MesenFirmware WsBoot = Opt(Fw("bootrom.ws", null, "WonderSwan boot ROM (only with Mesen's \"use boot ROM\")", S(0x1000), "bf4480dbea1c47c8b54ce7be9382bc1006148f431fbe4277e136351fa74f635e"));
        internal static readonly MesenFirmware WscBoot = Opt(Fw("bootrom.wsc", null, "WonderSwan Color boot ROM (only with Mesen's \"use boot ROM\")", S(0x2000), "f5a5c044d84ce1681f94e9ef74287cb989784497be5bd5108df17908dfa55db2"));
        internal static readonly MesenFirmware ScBoot = Opt(Fw("bootrom_sc.wsc", null, "SwanCrystal boot ROM (only with Mesen's \"use boot ROM\")", S(0x2000), "82e96addf5ab1ce09a84b6eedaa904e4ca432756851f7e0cc0649006c183834d"));

        private static readonly string[] NesBattery = { ".sav", ".chr.sav", ".eeprom128", ".eeprom256", ".ips", ".chr.ips", ".turbofile.sav", ".battlebox.sav" };
        private static readonly string[] SnesBattery = { ".srm", ".rtc", ".bs", ".turbofile.sav", ".turbofile_stf.sav" };
        private static readonly string[] GbBattery = { ".srm", ".rtc", ".p2.srm" };

        public static readonly MesenSystem[] All =
        {
            new MesenSystem { Key = "nes", Name = "NES / Famicom", Core = "Nes", Extensions = new[] { ".nes", ".unf", ".unif", ".nsf", ".nsfe" }, Battery = NesBattery, Firmware = new[] { Ymf288 } },
            // The disk's changes are kept as an IPS patch against the image (Fds::SaveBattery), never in the image unless the
            // user turns on Nes.OverwriteOriginalRom.
            new MesenSystem { Key = "fds", Name = "Famicom Disk System", Core = "Nes", Extensions = new[] { ".fds", ".qd" }, Battery = new[] { ".ips" }, Firmware = new[] { Fds } },
            new MesenSystem { Key = "studybox", Name = "Study Box", Core = "Nes", Extensions = new[] { ".studybox" }, Battery = NesBattery, Firmware = new[] { StudyBox } },
            new MesenSystem { Key = "snes", Name = "Super Nintendo / Super Famicom", Core = "Snes", Extensions = new[] { ".sfc", ".smc", ".swc", ".fig", ".spc" }, Battery = SnesBattery, Firmware = SnesChips },
            new MesenSystem { Key = "bsx", Name = "Satellaview", Core = "Snes", Extensions = new[] { ".bs" }, Battery = SnesBattery, Firmware = new[] { Bsx } },
            new MesenSystem { Key = "sufami", Name = "Sufami Turbo", Core = "Snes", Extensions = new[] { ".st" }, Battery = SnesBattery, Firmware = new[] { Sufami } },
            new MesenSystem { Key = "gb", Name = "Game Boy", Core = "Gameboy", Extensions = new[] { ".gb", ".gbx", ".gbs" }, Battery = GbBattery, Firmware = new[] { DmgBoot }, Switch = "--gameboy.model=Gameboy" },
            new MesenSystem { Key = "gbc", Name = "Game Boy Color", Core = "Gameboy", Extensions = new[] { ".gbc", ".gb", ".gbx" }, Battery = GbBattery, Firmware = new[] { CgbBoot }, Switch = "--gameboy.model=GameboyColor" },
            // A Game Boy game on the SNES core: only when the model resolves to the Super Game Boy (Gameboy::LoadRom).
            new MesenSystem { Key = "sgb", Name = "Super Game Boy", Core = "Snes", Extensions = new[] { ".gb", ".gbc", ".gbx" }, Battery = GbBattery,
                Firmware = new[] { Sgb2, Sgb1, SgbBoot, Sgb2Boot }, Switch = "--gameboy.model=SuperGameboy" },
            // No HLE BIOS: a zeroed one without the file (GbaConsole::LoadRom) - required.
            new MesenSystem { Key = "gba", Name = "Game Boy Advance", Core = "Gba", Extensions = new[] { ".gba" }, Battery = new[] { ".sav", ".rtc" }, Firmware = new[] { Gba } },
            new MesenSystem { Key = "pce", Name = "PC Engine", Core = "PcEngine", Extensions = new[] { ".pce", ".hes" }, Switch = "--pcEngine.consoleType=PcEngine" },
            new MesenSystem { Key = "tg16", Name = "TurboGrafx-16", Core = "PcEngine", Extensions = new[] { ".pce" }, Switch = "--pcEngine.consoleType=TurboGrafx" },
            new MesenSystem { Key = "sgx", Name = "SuperGrafx", Core = "PcEngine", Extensions = new[] { ".sgx", ".pce" }, Switch = "--pcEngine.consoleType=SuperGrafx" },
            // A CD only through its .cue (PceConsole: no .chd, no playlist). The CD-ROM unit's backup RAM is the .sav.
            new MesenSystem { Key = "pcecd", Name = "PC Engine CD", Core = "PcEngine", Extensions = new[] { ".cue" }, Battery = new[] { ".sav" }, Firmware = new[] { PceCd, GamesExpress }, Disc = true },
            new MesenSystem { Key = "sms", Name = "Master System", Core = "Sms", Extensions = new[] { ".sms" }, Battery = new[] { ".sav" }, Firmware = new[] { SmsBoot } },
            new MesenSystem { Key = "gg", Name = "Game Gear", Core = "Sms", Extensions = new[] { ".gg" }, Battery = new[] { ".sav" }, Firmware = new[] { GgBoot } },
            new MesenSystem { Key = "sg", Name = "SG-1000", Core = "Sms", Extensions = new[] { ".sg" }, Battery = new[] { ".sav" } },
            // An empty BIOS without the file (ColecoVision code) - required.
            new MesenSystem { Key = "cv", Name = "ColecoVision", Core = "Sms", Extensions = new[] { ".col" }, Battery = new[] { ".sav" }, Firmware = new[] { Coleco } },
            new MesenSystem { Key = "ws", Name = "WonderSwan", Core = "Ws", Extensions = new[] { ".ws" }, Battery = new[] { ".sav", ".rtc" }, Firmware = new[] { WsBoot }, Switch = "--ws.model=Monochrome" },
            new MesenSystem { Key = "wsc", Name = "WonderSwan Color", Core = "Ws", Extensions = new[] { ".wsc", ".ws" }, Battery = new[] { ".sav", ".rtc" }, Firmware = new[] { WscBoot }, Switch = "--ws.model=Color" },
            new MesenSystem { Key = "swancrystal", Name = "SwanCrystal", Core = "Ws", Extensions = new[] { ".wsc", ".ws" }, Battery = new[] { ".sav", ".rtc" }, Firmware = new[] { ScBoot }, Switch = "--ws.model=SwanCrystal" },
            new MesenSystem { Key = "pc2", Name = "Pocket Challenge V2", Core = "Ws", Extensions = new[] { ".pc2" }, Battery = new[] { ".sav" }, Firmware = new[] { WsBoot }, Switch = "--ws.model=PocketChallenge", CeOnly = true },
        };

        private static MesenPlatform P(string platform, params string[] systems) => new MesenPlatform { Platform = platform, Systems = systems };

        private static Dictionary<string, string> NesByExtension() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [".fds"] = "fds", [".qd"] = "fds", [".studybox"] = "studybox" };
        private static Dictionary<string, string> SnesByExtension() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [".bs"] = "bsx", [".st"] = "sufami" };

        /// <summary>THE LINES (docs\mesen.md §6b, question 2 for Mehdi): the model a platform means. Mesen's own default,
        /// AutoFavorGbc (2.1.1) / AutoFavorBest (CE), would run a dual-mode Game Boy game of the Game Boy platform in colour;
        /// AutoFavorGb keeps it on the Game Boy and still runs a colour-only one on the Color. A platform with no line leaves
        /// the model as set in Mesen.</summary>
        public static readonly MesenPlatform[] Platforms =
        {
            new MesenPlatform { Platform = "Nintendo Entertainment System", Systems = new[] { "nes", "fds", "studybox" }, ByExtension = NesByExtension() },
            P("Nintendo Famicom Disk System", "fds"),
            new MesenPlatform { Platform = "Super Nintendo Entertainment System", Systems = new[] { "snes", "bsx", "sufami" }, ByExtension = SnesByExtension() },
            // Satellaview games are .bs dumps; a .sfc there is a cartridge of its own (the BS-X cartridge itself, a BS game
            // re-made as a cartridge) and runs as one.
            new MesenPlatform { Platform = "Nintendo Satellaview", Systems = new[] { "bsx", "snes" }, ByExtension = { [".sfc"] = "snes", [".smc"] = "snes" } },
            new MesenPlatform { Platform = "Nintendo Game Boy", Systems = new[] { "gb", "gbc", "sgb" }, ByExtension = { [".gbc"] = "gbc" }, Line = "--gameboy.model=AutoFavorGb" },
            new MesenPlatform { Platform = "Nintendo Game Boy Color", Systems = new[] { "gbc", "gb", "sgb" }, Line = "--gameboy.model=AutoFavorGbc" },
            P("Nintendo Game Boy Advance", "gba"),
            new MesenPlatform { Platform = "NEC TurboGrafx-16", Systems = new[] { "pce", "tg16", "sgx" }, ByExtension = { [".sgx"] = "sgx" } },
            new MesenPlatform { Platform = "PC Engine SuperGrafx", Systems = new[] { "sgx", "pce" }, Line = "--pcEngine.consoleType=SuperGrafx" },
            P("NEC TurboGrafx-CD", "pcecd"),
            P("Sega Master System", "sms"),
            P("Sega Game Gear", "gg"),
            P("Sega SG-1000", "sg"),
            P("ColecoVision", "cv"),
            new MesenPlatform { Platform = "WonderSwan", Systems = new[] { "ws", "wsc", "swancrystal" }, ByExtension = { [".wsc"] = "wsc" }, Line = "--ws.model=Monochrome" },
            new MesenPlatform { Platform = "WonderSwan Color", Systems = new[] { "wsc", "swancrystal", "ws" }, Line = "--ws.model=Color" },
            // LiteBox's own names (its metadata, Mehdi 17/10).
            new MesenPlatform { Platform = "Pocket Challenge V2", Systems = new[] { "pc2" }, Line = "--ws.model=PocketChallenge" },
            new MesenPlatform { Platform = "Nes - Super Mario Bros. Hacks", Systems = new[] { "nes", "fds", "studybox" }, ByExtension = NesByExtension() },
            P("Snes - Super Mario World Hacks", "snes"),
            // MSU-1: the .msu and .pcm files beside the ROM, read by Mesen itself (Msu1::Init).
            P("Super Nintendo MSU-1", "snes"),
            // LiteBox's other spellings (Host\Romm\RommPlatformMap.cs, Ra, EmuMovies): a row only when the library has them.
            new MesenPlatform { Platform = "Nintendo Famicom", Systems = new[] { "nes", "fds", "studybox" }, ByExtension = NesByExtension(), OnlyIfInLibrary = true },
            new MesenPlatform { Platform = "Super Famicom", Systems = new[] { "snes", "bsx", "sufami" }, ByExtension = SnesByExtension(), OnlyIfInLibrary = true },
            new MesenPlatform { Platform = "Nintendo Sufami Turbo", Systems = new[] { "sufami" }, OnlyIfInLibrary = true },
            new MesenPlatform { Platform = "Super Game Boy", Systems = new[] { "sgb", "gb", "gbc" }, Line = "--gameboy.model=SuperGameboy", OnlyIfInLibrary = true },
            new MesenPlatform { Platform = "NEC PC Engine", Systems = new[] { "pce", "tg16", "sgx" }, ByExtension = { [".sgx"] = "sgx" }, OnlyIfInLibrary = true },
            new MesenPlatform { Platform = "NEC PC Engine-CD", Systems = new[] { "pcecd" }, OnlyIfInLibrary = true },
        };

        public static MesenSystem System(string key)
            => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        public static MesenPlatform Platform(string platform)
            => Platforms.FirstOrDefault(p => string.Equals(p.Platform, (platform ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>The system a file's extension says by itself, whatever the platform (a .fds is a Famicom Disk System game
        /// everywhere, a .gb a Game Boy one); null when it is none of Mesen's.</summary>
        public static MesenSystem ByExtensionAlone(string ext)
        {
            var e = (ext ?? "").ToLowerInvariant();
            // The system whose FIRST extension it is, the first of the table (.gb: Game Boy, .pce: PC Engine, .wsc: WonderSwan
            // Color), else the one system that opens it at all.
            var primary = All.FirstOrDefault(s => s.Extensions.Length > 0 && s.Extensions[0] == e);
            if (primary != null) return primary;
            var hits = All.Where(s => s.Extensions.Contains(e)).ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        /// <summary>Mesen's core for an extension (NES, SNES...: Emulator::TryLoadRom by extension), or null.</summary>
        public static string CoreOf(string ext)
        {
            var e = (ext ?? "").ToLowerInvariant();
            // .gb/.gbc/.gbx belong to the Game Boy core unless the model is the Super Game Boy.
            var s = All.FirstOrDefault(x => x.Extensions.Contains(e) && x.Key != "sgb");
            return s?.Core;
        }

        /// <summary>Every extension of a platform, for the catalogue row: ".nes; .unf; ...; .zip; .7z".</summary>
        public static string AllExtensions(MesenPlatform p)
        {
            var systems = p.Systems.Select(System).Where(s => s != null).ToList();
            var exts = systems.SelectMany(s => s.Extensions).Concat(p.ByExtension.Keys)
                              .Concat(systems.Any(s => !s.Disc) ? new[] { ".zip", ".7z" } : Array.Empty<string>())
                              .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join("; ", exts);
        }

        /// <summary>Every extension any game of the plugin can have (the emulator row's).</summary>
        public static string EveryExtension()
            => string.Join("; ", Platforms.SelectMany(p => AllExtensions(p).Split(new[] { "; " }, StringSplitOptions.None)).Distinct(StringComparer.OrdinalIgnoreCase));

        /// <summary>The key a switch sets ("gameboy.model") and its value, from "--gameboy.model=AutoFavorGb"; null when it is
        /// not a configuration switch.</summary>
        public static (string Key, string Value)? SwitchParts(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var t = token.Trim();
            if (t.StartsWith("--")) t = t.Substring(2); else if (t.StartsWith("-") || t.StartsWith("/")) t = t.Substring(1); else return null;
            int eq = t.IndexOf('=');
            if (eq <= 0 || t.IndexOf('.') <= 0 || t.IndexOf('.') > eq) return null;
            return (t.Substring(0, eq).ToLowerInvariant(), t.Substring(eq + 1));
        }

        /// <summary>The system a model switch means for a game of <paramref name="core"/> ("--gameboy.model=AutoFavorGb" -> gb),
        /// or null when the switch says nothing of the system.</summary>
        public static MesenSystem SystemOfSwitch(string token, string core)
        {
            var parts = SwitchParts(token);
            if (parts == null) return null;
            var (key, value) = parts.Value;
            var v = value.ToLowerInvariant();
            if (key == "gameboy.model" && (core == "Gameboy" || core == null))
                return System(v == "supergameboy" || v == "autofavorsgb" ? "sgb" : v == "gameboy" || v == "autofavorgb" ? "gb" : "gbc");
            if (key == "ws.model" && (core == "Ws" || core == null))
                return System(v == "monochrome" ? "ws" : v == "color" ? "wsc" : v == "swancrystal" ? "swancrystal" : v == "pocketchallenge" ? "pc2" : null);
            if (key == "pcengine.consoletype" && (core == "PcEngine" || core == null))
                return System(v == "supergrafx" ? "sgx" : v == "turbografx" ? "tg16" : v == "pcengine" ? "pce" : null);
            return null;
        }
    }
}
