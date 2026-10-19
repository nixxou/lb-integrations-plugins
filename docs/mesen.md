# Nixx-Mesen: research notes (stage 1)

Branch `nes-mesen`, worktree `lb-integrations-plugins-nes-mesen`. Written on 18/10 by the Mesen team. This file is how the
next stage continues: every fact says how it was measured (**[M]** measured on the real exe in the lab, **[S]** read in
the emulator's source at the released tag, **[DB]** read in a copy of LaunchBox's metadata base). Nothing here comes
from a wiki.

Lab: `G:\LB1326\lbip-mesen-lab\` (downloads in `dl\`, sources in `src-2.1.1\` / `src-ce-2.2.1\` / `src-master\`, test
installs `inst1\` = 2.1.1 and `inst2\` = CE 2.2.1, copies of the test ROMs in `roms\`, decoded state frames in `thumbs\`,
a copy of the LaunchBox base in `db\`). The oracle script that started, observed and closed Mesen is
`mesen-oracle.ps1` (start with arguments, wait, list the process's windows, PrintWindow capture, keys POSTED to the
window - no focus taken from the desktop -, WM_CLOSE, wait for the exit). Only processes the script started were closed.

---

## 1. Which Mesen - read this first

| | SourMesen/Mesen2 | nesdev-org/MesenCE |
|---|---|---|
| Status | **archived**: its README (master, commit b9fa69d, 2026-06-04) says "This repository is an archive of Mesen as it was around version 2.1.1 in mid-2025. For the latest version of Mesen, see here: https://github.com/nesdev-org/MesenCE" [S] | the community continuation ("Mesen Community Edition", created 2026-02-13, GPL-3.0, still pushed 2026-10-07) [GitHub API] |
| Latest release | `2.1.1` (2025-07-06) | `2.2.1` (2026-06-05), `2.2.0` (2026-06-04) |
| Windows asset | `Mesen_2.1.1_Windows.zip`, 34 058 170 B, SHA-256 `23ccc2bc060b663c68dad3a8c5d6da7d23a50f872d04f135bafa2b04ff7d5cbe` (= the API's `digest`) | `Mesen_2.2.1_Windows.zip`, 34 259 023 B, SHA-256 `c19e10b3b3865eb0e86e4d01bf09a5027a4452081c04b5bd5b6cbbf76e346fd6` (= the API's `digest`); also `Mesen_2.2.1_Windows_7_And_8.zip` (a .NET 6 build) |
| URL | https://github.com/SourMesen/Mesen2/releases/download/2.1.1/Mesen_2.1.1_Windows.zip | https://github.com/nesdev-org/MesenCE/releases/download/2.2.1/Mesen_2.2.1_Windows.zip |
| Zip content [M] | one file, `Mesen.exe` 70 470 144 B, SHA-256 `d2eb03c2590c648bf329f127ebcfefd70130e7690a9e2ccdba8616faea1fe96b` | one file, `Mesen.exe` 70 753 792 B, SHA-256 `8ef403d6b9af32075416193914d7993ce8480b347fe1f20a0cfd9815374933e7` |
| Version resource [M] | FileVersion `2.1.1`, ProductVersion `2.1.1+137ae7ce3bf3f539d007e2c4ef3cb3b6c97672a1`, InternalName `Mesen.dll`, CompanyName `Mesen`, (C) 2014-2025 Sour | FileVersion `2.2.1`, ProductVersion `2.2.1+20ba206cef5ba207c21203176d02cb9f43dda9fb`, InternalName `Mesen.dll`, CompanyName `Mesen`, (C) 2014-2026 Sour |
| Window title [M] | `Mesen - <rom name without extension>` | `MesenCE - <rom name without extension>` |
| Source tag | `2.1.1` = 137ae7ce | `2.2.1` = 20ba206c |
| Since the tag | 19 commits on the archive's master, none in a release | 99 commits on master after 2.2.1 (07/10) |

**Proposal (to confirm with Mehdi): the plugin installs MesenCE**, the only maintained Mesen; it recognises and claims
an existing SourMesen 2.1.x too (same exe, same files, measured identical where it matters - §4-§11). Differences that
matter to the plugin, all [S] by diffing the two tags, the first two also [M]:

- CE runs **Pocket Challenge V2** (`.pc2`, `WsModel.PocketChallenge`, commit "WS: Initial Pocket Challenge V2 support
  (#95)"); `.pc2` is in CE's MesenCore.dll and not in 2.1.1's [M, strings].
- CE's Game Boy model enum gained `AutoFavorBest` (first value, CE's default); 2.1.1 does not know it: a switch
  `--gameboy.model=AutoFavorBest` on 2.1.1 is refused by `ConfigManager.ProcessSwitch` and the game still starts [M].
- CE's documents folder is `Documents\MesenCE` (with a fallback to `Documents\Mesen2` when that one has a
  `settings.json`); 2.1.1's is `Documents\Mesen2`. Irrelevant once the install is portable (§3).
- CE's single-instance guard has its own GUID (commit "UI: Set MesenCE-specific guid used for restricting to one
  instance (#153)"): a 2.1.1 and a CE can run side by side.
- A state written by CE says emulator version `0x20201`, 2.1.1's `0x20101` [M]; `SaveStateManager::GetSaveStatePreview` /
  `LoadState` refuse a state written by a NEWER version [S]: going back from CE to 2.1.1 loses the states.
- CE adds `.p2.srm` (second Game Boy on the local link cable) and `.turbofile_stf.sav` (SNES Turbo File Twin) to the
  battery names, `.rtc` for WonderSwan cartridges (S3511A RTC) [S].

**Development builds**: CE's README links `nightly.link/nesdev-org/MesenCE/workflows/build/master/Mesen (Windows - net8.0
- AoT).zip`, which now answers **404** [M, 18/10]; the workflow page lists `Mesen (Windows - net10.0 - AoT).zip` and
`Mesen (Windows - net10.0).zip` [M] (master's build.yml: .NET 10, AoT build self-contained, the other framework-dependent
single file [S]). Artifacts of GitHub Actions expire; the plugin installs releases only. The archive's README still
links its own nightly builds (net8.0).

## 2. Runtime: what Windows needs (the install flow must say it)

- The released `Mesen.exe` (2.1.1 and CE 2.2.1) is a **native AOT** executable: no CLR header in its PE data
  directories, no import of `hostfxr`/`coreclr`, the `NativeAOT` marker in its data [M, PE parsed]. Its imports are
  Windows system DLLs and the Universal CRT only (`advapi32, api-ms-win-crt-*, bcrypt, crypt32, iphlpapi, kernel32,
  ncrypt, ole32, oleaut32, user32, ws2_32`) [M]; `MesenCore.dll` imports `d3d11, dinput8, dsound, kernel32, ole32,
  oleaut32, user32, winmm, ws2_32, xinput9_1_0` [M]. **No .NET runtime and no Visual C++ redistributable are needed on
  Windows 10/11.** Their workflow publishes this asset with `PublishAot=true SelfContained=true` [S, build.yml].
- `Mesen_<v>_Windows_7_And_8.zip` needs the **.NET 6** runtime (CE README: "Windows 7 / 8 (requires .NET 6)"); the dev
  build "Windows - net10.0" needs **.NET 10** [S]. The plugin never installs those.
- Text for the install flow: "Mesen is a native build: nothing else to install (no .NET runtime). Windows 10 or 11."

## 3. Home folder, portable mode, first run

- The home folder is the exe's folder **if `settings.json` is there**, else `Documents\Mesen2` (2.1.1) /
  `Documents\MesenCE` (CE) (`ConfigManager.HomeFolder`) [S]. `Program.Main` sets the current directory to it.
- Without `settings.json` beside the exe, **Mesen created `Documents\Mesen2`, extracted its native files there and showed
  its setup wizard** (a 400x515 window titled `Mesen`), ignoring the ROM given; WM_CLOSE closed it, no `settings.json`
  written [M, 2.1.1; the folder was created by the test and removed after].
- With a `settings.json` beside the exe holding only
  `{"ConfigUpgrade":1,"Preferences":{"AutomaticallyCheckForUpdates":false}}` Mesen started portable, loaded the ROM and
  wrote its full configuration (230 KB, `ConfigUpgrade` 5, all default key mappings, `DefaultKeyMappings` "Xbox,
  ArrowKeys") [M, 2.1.1 and CE]. `ConfigUpgrade:1` is `ConfigUpgradeHint.FirstRun`: what the wizard writes
  (`Configuration.CreateConfig`) and what makes `InitializeDefaults` set every system's default keys [S]. An empty
  `{}` would leave the NES/SNES/GB/PCE keys unset (`ConfigUpgrade` 0: only `UpgradeConfig` runs) [S, not measured].
  **The plugin writes this bootstrap file at install when there is no `settings.json`** - never over an existing one.
- At each start Mesen extracts into its home folder [M]: `MesenCore.dll`, `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`,
  `MesenNesDB.txt`, `Satellaview\BSX012x-0.bin`, and creates `Cheats\ GameConfig\ HdPacks\ RecentGames\ Saves\`
  (`SaveStates\` at the first state). `Firmware\` holds the BIOS files (§9). Nothing was written under `%APPDATA%`,
  `%LOCALAPPDATA%` or the registry search done (`find` on both profiles' folders) [M]. File associations are written only
  when a `Preferences.Associate*` is true (all false by default) [S].

## 4. `settings.json`

- **Format [M]**: UTF-8 **with BOM**, **CRLF**, indented 2 spaces, enums as strings, written by `File.WriteAllText(path,
  text, Encoding.UTF8)` (`FileHelper.WriteAllText`) from System.Text.Json source generation (`WriteIndented`,
  `UseStringEnumConverter`) [S]. Mesen read a file we wrote with LF and no BOM and rewrote it as BOM + CRLF [M].
- **When Mesen writes it [S, M]**: `Configuration.Save()` writes only when the serialized text differs from the text it
  read (`_fileData`): after a game is loaded (`LoadRomHelper`: recent files), when a settings window closes, and when the
  main window closes (`CloseEmu`). A run that changed nothing left the file untouched (same mtime) [M]; a run with a
  config switch rewrote it at start [M]. **It is rewritten whole** - the plugin edits it before the launch, never while
  Mesen runs (and reads it back after).
- `--doNotSaveSettings` prevents every write of it for the session [M: hash unchanged after a run with a switch].
- Sections (`Configuration`): `Video Audio Input Emulation Snes Nes Gameboy PcEngine Sms Cv Gba Ws Preferences
  AudioPlayer Debug RecentFiles VideoRecord MovieRecord HdPackBuilder Cheats Netplay HistoryViewer MainWindow
  DefaultKeyMappings`; CE adds `EnableTestMode` [M].
- `Preferences.ConfirmExitResetPower` (default false): when true a WM_CLOSE asks Yes/No (`ValidateExit`) [S] - the plugin
  should leave it as the user set it but say so in the help.

## 5. Command line [S: `UI\Utilities\CommandLineHelper.cs`, identical in both tags]

- Every argument naming an existing file is loaded (`.lua` = script, anything else = a game); a relative path is taken
  from the current directory at start (`Program.OriginalFolder`). [M: absolute paths with spaces, quoted.]
- Switches (`-x`, `--x` or `/x`, case-insensitive): `--fullscreen`, `--doNotSaveSettings`, `--loadLastSession`,
  `--recordMovie=<file>`, `--enableStdout`, `--noVideo`, `--noAudio`, `--noInput`, `--testRunner`, `--timeout=<n>`.
- **Any configuration value**: `--<section>.<property>[.<property>]=<value>` (`ConfigManager.ProcessSwitch`, properties
  matched case-insensitively by reflection; bool, enum by name, int/uint/double within `[MinMax]`). The value must match
  `[a-z0-9_A-Z.\-]+`: **no path can be passed** (no `\`, `:` or space). Examples measured: `--nes.region=Pal`,
  `--preferences.showFps=true`, `--preferences.allowBackgroundInput=true` [M].
- **A switch is persisted**: it changes the configuration in memory and Mesen wrote it into `settings.json` (`Nes.Region`
  `Pal`, `ShowFps` true) [M]. Only `--doNotSaveSettings` avoids it - and it also drops every other change of the session
  (keys rebound, recent files). Consequence for the plugin: §13 (the session restore).
- `--fullscreen` toggles full screen after the game is loaded [S] - **not measured** (it would have taken over Mehdi's
  screen); to measure with him.
- **Single instance** (`Preferences.SingleInstance`, default true): a second `Mesen.exe` started while one runs hands its
  arguments to the first and **exits at once (0.1 s, code 0)**; the running one loaded the new game (its title changed,
  the first game's `.sav` written on the switch) [M]. Seen from LaunchBox the launch "ends" immediately:
  `LbipLaunchGate` must refuse a launch into a Mesen already running (as the other plugins do).
- **WM_CLOSE**: the window closed, the battery save and `RecentGames\<rom>.rgd` were written, the process exited in 0.1-0.2
  s with code 0, every time but one [M, 2.1.1 and CE, NES/SNES/GBC]. One exit took **16.7 s** (the run that persisted two
  switches, zip ROM); not reproduced in 2 retries. The plugin must wait for the process, never assume an instant exit.
- Mesen has its own `Exit` shortcut (unbound by default) = `_mainWindow.Close()`, the same path as WM_CLOSE [S].

## 6. What Mesen runs, and the LaunchBox platforms

**Systems [S]**: the core tries each console class in this order with the file's extension, then by file signature
(`Emulator::TryLoadRom`): NES, SNES, Game Boy, PC Engine, SMS, GBA, WonderSwan. Their extensions
(`GetSupportedExtensions`, same in both tags except `.pc2`):

| Core (`ConsoleType`) | Extensions | Notes |
|---|---|---|
| NES (2) | `.nes .fds .unif .unf .nsf .nsfe .studybox .qd` | FDS = `.fds/.qd` (BIOS required); StudyBox = `.studybox` (BIOS); NSF = music; Vs. System and PlayChoice games are `.nes` recognised by MesenNesDB / NES 2.0 header |
| SNES (0) | `.sfc .swc .fig .smc .bs .gb .gbc .gbx .spc .st` | `.bs` = Satellaview (needs `BS-X.bin`), `.st` = Sufami Turbo (needs `SufamiTurbo.sfc`), `.gb/.gbc/.gbx` here = **Super Game Boy** (only when the GB model resolves to SGB, else `UnknownType` and the GB core takes it - `Gameboy::LoadRom`), `.spc` = music. MSU-1 supported (`Msu1::Init`) |
| Game Boy (1) | `.gb .gbc .gbx .gbs` | model by `Gameboy.Model` (§6b); `.gbs` = music |
| PC Engine (3) | `.pce .cue .sgx .hes` | CD = **`.cue` only** (no `.chd`, no `.m3u`); SuperGrafx by `.sgx` or `PcEngine.ConsoleType`; `.hes` = music |
| SMS (4) | `.sms .gg .sg .col` | the model is the **extension**: `.gg` Game Gear, `.col` ColecoVision, `.sg` SG-1000, else Master System (`SmsConsole::LoadRom`) |
| GBA (5) | `.gba` | |
| WonderSwan (6) | `.ws .wsc` (+ `.pc2` in CE) | model by `Ws.Model` (Auto = by header) |

Archives: `.zip` and `.7z` opened by Mesen itself (§8).

**LaunchBox platform names** [DB, a copy of `G:\LB1326\Metadata\LaunchBox.Metadata.db` read 18/10]: all present in
`Platforms`. LiteBox's own extra names are the brief's (§2 of `nes-team-brief.md`).

| LaunchBox platform | Mesen system(s), default first | Extensions claimed | By extension | BIOS (§9) |
|---|---|---|---|---|
| `Nintendo Entertainment System` | NES | `.nes .unf .unif .nsf .nsfe .studybox` + `.fds .qd` | `.fds/.qd` -> FDS, `.studybox` -> StudyBox | FDS / StudyBox only for those files |
| `Nintendo Famicom Disk System` | FDS | `.fds .qd` | | `disksys.rom` **required** |
| `Super Nintendo Entertainment System` | SNES | `.sfc .smc .swc .fig .spc` + `.bs .st` | `.bs` -> Satellaview, `.st` -> Sufami Turbo | DSP/ST01x per game (optional entries) |
| `Nintendo Satellaview` | Satellaview | `.bs` (`.sfc` too: BS dumps are often named so) | | `BS-X.bin` **required** |
| `Nintendo Game Boy` | Game Boy (model per §6b) | `.gb .gbx .gbs` + `.gbc` | | boot ROMs optional; SGB needs `SGB1.sfc`/`SGB2.sfc` |
| `Nintendo Game Boy Color` | Game Boy Color | `.gbc .gb .gbx` | | boot ROM optional |
| `Nintendo Game Boy Advance` | GBA | `.gba` | | `gba_bios.bin` **required** (no HLE: a zeroed BIOS without it, `GbaConsole::LoadRom` [S]) |
| `NEC TurboGrafx-16` | PC Engine | `.pce .sgx .hes` | `.sgx` -> SuperGrafx | |
| `PC Engine SuperGrafx` | SuperGrafx | `.sgx .pce` | | |
| `NEC TurboGrafx-CD` | PC Engine CD | `.cue` | | `syscard3.pce` **required**; `gecard.pce` for Games Express discs |
| `Sega Master System` | SMS | `.sms` | | `bios.sms` optional |
| `Sega Game Gear` | Game Gear | `.gg` | | `bios.gg` optional |
| `Sega SG-1000` | SG-1000 | `.sg` | | |
| `ColecoVision` | ColecoVision | `.col` | | `bios.col` **required** (an empty BIOS without it [S]) |
| `WonderSwan` | WonderSwan (mono) | `.ws` | | boot ROMs optional (`Ws.UseBootRom` false by default) |
| `WonderSwan Color` | WonderSwan Color | `.wsc .ws` | | idem |
| `Pocket Challenge V2` (LiteBox's name) | Pocket Challenge V2 - **CE only** | `.pc2` | | |
| `Nes - Super Mario Bros. Hacks` (LiteBox) | NES | as NES | | |
| `Snes - Super Mario World Hacks` (LiteBox) | SNES | as SNES | | |
| `Super Nintendo MSU-1` (LiteBox) | SNES (MSU-1: the `.msu`/`.pcm` files beside the ROM) | as SNES | | |

Not claimed: `Sega SC-3000` (Mesen's `.sg` is SG-1000 only: its file dialog says "SG-1000 ROM files" [S]); Vs.
System / PlayChoice-10 (no LaunchBox platform of their own - they are `.nes` games under Arcade in most libraries:
question for Mehdi); the music formats `.gbs .hes .spc .nsf` are listed with their platform because Mesen plays them,
nothing more. Every claimed platform also gets `.zip; .7z`.

### 6b. Picking the system: what the "line" means for Mesen

Unlike ares, Mesen needs no system on its line: the core comes from the extension. What a platform DOES decide is the
**model** inside a core, and those are configuration values a switch can set (§5):

| Platform | Switch on the platform row (proposal) | Mesen's own default |
|---|---|---|
| Nintendo Game Boy | `--gameboy.model=AutoFavorGb` | `AutoFavorGbc` (2.1.1) / `AutoFavorBest` (CE) |
| Nintendo Game Boy Color | `--gameboy.model=AutoFavorGbc` | idem |
| PC Engine SuperGrafx | `--pcEngine.consoleType=SuperGrafx` | `Auto` (`.sgx` -> SuperGrafx) |
| WonderSwan | `--ws.model=Monochrome` | `Auto` (header) |
| WonderSwan Color | `--ws.model=Color` | `Auto` |
| Pocket Challenge V2 | `--ws.model=PocketChallenge` (CE) | `Auto` |
| the others | nothing | |

The game's window offers the model (Game Boy / Game Boy Color / Super Game Boy (+ SGB2), PC Engine / SuperGrafx /
TurboGrafx, WonderSwan / Color / SwanCrystal / Pocket Challenge, NES region NTSC/PAL/Dendy, SMS/CV region...).
Priority at launch as ares (memory "ares-plugin-model"): the game's window > the platform row's line > the table +
extension. **These switches persist in `settings.json`** (§5): see §13 for how the plugin keeps Mesen's own settings
unchanged. Whether a platform line should carry a model at all (or leave Mesen's auto-detection, which reads the
cartridge header and is right for most games) is question Q2.

## 7. Options a game's window can pass (switches)

What `ProcessSwitch` can set [S, `ConfigManager.ApplySetting`]: a `bool`, an enum by name (only the `[ValidValues]` when
the property has them), an `int`/`uint`/`double` **only when it carries `[MinMax]`** (a `uint` without it, such as
`Video.ExclusiveFullscreenRefreshRateNtsc`, cannot be set from the line); no strings, arrays or paths. Candidates for the
per-game window, by group (all CE 2.2.1, same names in 2.1.1 unless said):

- Every system: `video.videoFilter`, `video.aspectRatio`, `video.useBilinearInterpolation`, `video.scanlineIntensity`,
  `video.integerFpsMode`, `video.verticalSync`, `emulation.runAheadFrames` (0-10), `emulation.emulationSpeed`; per-core
  overrides of filter and aspect exist (`<core>.configOverrides.overrideVideoFilter` / `.videoFilter` /
  `.overrideAspectRatio` / `.aspectRatio`, `Sms.GgConfigOverrides` for the Game Gear).
- NES: `nes.region` (Auto/Ntsc/Pal/Dendy), `nes.consoleType` (Nes001/Nes101/Hvc001/Hvc101), `nes.removeSpriteLimit`,
  `nes.fdsAutoLoadDisk`, `nes.fdsFastForwardOnLoad`, `nes.enableHdPacks`.
- SNES: `snes.region`. Game Boy: `gameboy.model`, `gameboy.useSgb2`, `gameboy.hideSgbBorders`. GBA: `gba.skipBootScreen`.
- PC Engine: `pcEngine.consoleType` (Auto/PcEngine/SuperGrafx/TurboGrafx), `pcEngine.cdRomType`
  (CdRom/SuperCdRom/Arcade), `pcEngine.removeSpriteLimit`.
- SMS / GG / CV: `sms.region`, `sms.gameGearRegion`, `sms.revision`, `cv.region`. WonderSwan: `ws.model`,
  `ws.useBootRom`, `ws.autoRotate`.

Each one persists (§5) and needs the session restore of §13. Mesen has no per-game configuration besides dip switches
and overscan (`GameConfig\<rom>.json` [S]).

## 8. Archives

`.zip` and `.7z` are opened by Mesen itself (`ArchiveReader::GetReader`: "PK" -> ZipReader, "7z" -> SZReader) [S];
measured with a zip [M]. **`AutoExtract = false`.** Two facts the saves depend on:

- The battery file and the states take the name of the file **inside** the archive, not the archive's: `ZeldaArchive.zip`
  holding `Inner Zelda.nes` wrote `Saves\Inner Zelda.sav` and `RecentGames\Inner Zelda.rgd`, title `Mesen - Inner Zelda`
  [M]. The plugin's listing must read the archive's entry names (`VirtualFile::GetFileName` = the inner file [S]).
- An archive with **several** ROMs opens a selection dialog (`SelectRomWindow.Show`, one ROM -> picked directly) [S]:
  the plugin should unpack such an archive under `<install>\Unpacked\<name>-<hash>\` and pass the chosen file, or refuse.
- `Preferences.AutoLoadPatches` (true): an `.ips/.ups/.bps` beside the ROM with the same name is applied [S].

## 9. Saves (battery)

- Folder: `<home>\Saves\` by default (**never beside the game**), or `Preferences.SaveDataFolder` when
  `Preferences.OverrideSaveDataFolder` is true [S]. A **relative** folder is resolved from the home folder (current
  directory) - `"Saves\\Nintendo Entertainment System"` wrote `inst1\Saves\Nintendo Entertainment System\Zelda II - The
  Adventure of Link (Europe).sav` [M]; the folder is created by Mesen [M]. One folder for every system: per-platform
  folders mean rewriting the setting before each launch (§13).
- Name: `<rom name without extension><ext>` (`BatteryManager::GetBasePath`) [S]. Extensions [S, grep of every
  `SaveBattery("..."` at both tags; M where marked]:

| System | Files |
|---|---|
| NES | `.sav` [M, Zelda II 8 192 B], `.chr.sav` (CHR battery RAM), `.eeprom128` / `.eeprom256` (Bandai), `.ips` + `.chr.ips` (flash-saving boards), `.turbofile.sav`, `.battlebox.sav` (accessories) |
| FDS | `.ips` - the disk changes as an IPS patch against the original image (`Fds::SaveBattery`), unless `Nes.OverwriteOriginalRom` (writes the game file itself!) |
| SNES | `.srm` [M, Secret of Mana 8 192 B], `.rtc` (SPC7110 RTC), `.bs` (BS-X memory pack), `.turbofile.sav`, CE `.turbofile_stf.sav` |
| Game Boy / Color | `.srm` + `.rtc` [M, Pokemon Cristal: 32 768 B + 13 B], CE `.p2.srm` (2nd console on the link cable) |
| GBA | `.sav` (+ `.rtc` GBA cartridge RTC) |
| PC Engine CD | `.sav` (the CD-ROM unit's backup RAM, written only when changed) |
| SMS / GG / SG / CV | `.sav` |
| WonderSwan | `.sav`, CE `.rtc` |

- Written: at WM_CLOSE / Exit, and when another game is loaded [M]. Not written while running (except a game switch).
- **Collisions**: one folder for all systems and `.sav` used by NES/GBA/SMS/WS/PCE -> two games with the same name on two
  systems share a file in Mesen's default layout. The plugin's ids carry the system (`SaveIdData.Sys`), and per-platform
  folders avoid the collision on disk.

## 10. States

- `<state folder>\<rom name>_<n>.mss` (`SaveStateManager::GetStateFilepath`) [S, M]: Shift+F1 wrote `SaveStates\Zelda II
  - The Adventure of Link (Europe)_1.mss` (8 477 B), Shift+F2 `Secret of Mana (USA)_2.mss` [M]. Slots **1-10**
  (`MaxIndex` 10), **11 = the automatic state** (`AutoSaveStateIndex`, `Preferences.EnableAutoSaveState` true, every
  `AutoSaveStateDelay` 5 min). Folder `<home>\SaveStates\` or `Preferences.SaveStateFolder` when
  `OverrideSaveStateFolder` (absolute path measured working [M]).
- `RecentGames\<rom name>.rgd` [M]: a zip of `Screenshot.png`, `Savestate.mss` and `RomInfo.txt` (rom name, path, patch,
  aspect ratio) written at every close - Mesen's "resume" (the start screen, `--loadLastSession`). Its folder is fixed
  (`<home>\RecentGames`, no override) [S]. Not a slot: proposal - not listed as a save (Q5).
- **The state embeds its own picture** [S, M]: header `"MSS"`, u32 emulator version, u32 format (4), u32 console type,
  then the frame (`SaveVideoData`): u32 buffer size, u32 width, u32 height, u32 scale x100, u32 compressed size, zlib of
  the raw frame (u16 per pixel), then the rom name. The pixel is the console's own: NES = palette index (6 bits + 3
  emphasis bits, the palette is `Nes.UserPalette` in settings.json), SNES / Game Boy / GBA / SMS = RGB555, WonderSwan =
  RGB444, PC Engine = 9-bit index through `PcEngine.Palette` (+ 0x200 grayscale). Decoded by a test script into PNG for
  the NES (256x240), Game Boy Color (160x144) and SNES (256x239) states: the NES and GBC pictures are the game's screen
  at the save [M, `thumbs\`]; the SNES one is black, as the game's screen was at that moment (window capture agrees) [M].
  So `<state>.png` comes from the state itself, window capture only as a fallback.
- Keys are ignored while Mesen's window is not the foreground one (`Preferences.AllowBackgroundInput` false): posted
  Shift+F1 saved nothing until `--preferences.allowBackgroundInput=true` was given [M]. The AHK scripts send keys to the
  focused emulator, as LaunchBox's pause screen does.

## 11. BIOS / firmware

Files in `<home>\Firmware\` with Mesen's names (`FirmwareHelper.h` [S]); size checked, an alternate name tried; the UI
knows each one's SHA-256 (`UI\Interop\FirmwareTypeExtensions.cs` [S], the names also found as strings in both
`MesenCore.dll` [M]). A missing required file makes Mesen show a modal "firmware not found" box with a file picker
(`FirmwareHelper.RequestFirmwareFile`) [S]: the plugin refuses the launch first (`LbipNotice`, as `AresPlugin.PrepareCore`).

| Firmware | File (alt) | Size | SHA-256 (Mesen's list) | Needed by |
|---|---|---|---|---|
| FDS | `disksys.rom` (`FdsBios.bin`) | 8 192 | `99C18490...4B0178`, `A0A9D57C...FF2BF` | `.fds .qd` - required |
| StudyBox | `StudyBox.bin` | 262 144 | `365F84C8...D514C` | `.studybox` |
| GBA | `gba_bios.bin` | 16 384 | `FD254772...36570` | `.gba` - required |
| GB boot | `dmg_boot.bin` (`gb_bios.bin`) | 256 | `CF053ECC...CE1C7`, `26E71CF0...6818E` | optional (built-in boot otherwise) |
| GBC boot | `cgb_boot.bin` (`gbc_bios.bin`) | 2 304 | `B4F2E416...C50C7`, `3A307A41...92E45` | optional |
| SGB boot | `sgb_boot.bin` / `sgb2_boot.bin` (`sgb_bios.bin`) | 256 | `0E4DDFF3...1E360` / `FD243C4F...C3988` | optional |
| SGB | `SGB1.sfc` / `SGB2.sfc` | 262 144 / 524 288 | 3 revisions / `C172498A...CCF2C6` | Super Game Boy mode - required then (`UseSgb2` true by default -> SGB2) |
| Satellaview | `BS-X.bin` | 1 048 576 | 4 hashes | `.bs` - required |
| Sufami Turbo | `SufamiTurbo.sfc` | 262 144 | `EDACB453...B7342` | `.st` - required |
| DSP1/1B/2/3/4 | `dsp1.rom` ... (`.program.rom` + `.data.rom` split) | 8 192 | one each | SNES games with that chip (Super Mario Kart, Pilotwings...), unless the ROM embeds it |
| ST010/ST011/ST018 | `st010.rom` `st011.rom` `st018.rom` | 53 248 / 53 248 / 163 840 | one each | those few SNES games |
| PCE CD | `[BIOS] Super CD-ROM System (Japan) (v3.0).pce` (`syscard3.pce`) | 262 144 | `E11527B3...3D9CE` | `.cue` - required |
| Games Express | `[BIOS] Games Express CD Card (Japan).pce` (`gecard.pce`) | 32 768 / 16 384 | two | Games Express discs |
| ColecoVision | `bios.col` | 8 192 | `990BF195...A09AD`, `FB4A898E...CFFD4` | `.col` - required |
| SMS | `bios.sms` | 8 K / 16 K / 128 K / 256 K | 12 hashes | optional |
| Game Gear | `bios.gg` | 1 024 | `8C8A2133...0EEE2` | optional |
| WonderSwan | `bootrom.ws` / `bootrom.wsc` / `bootrom_sc.wsc` | 4 096 / 8 192 / 8 192 | one each | only with `Ws.UseBootRom` |
| YMF288 | `ymf288_adpcm_rom.bin` | 8 192 | `53AFD0FA...` | NES EPSM expansion |

The complete hashes are in the source file named above; `MesenFirmware.cs` will copy them with the line it came from.
Import (`LbipBiosImport.Import`): RetroArch's `system\` and the other emulators' folders use the same names for most
(`disksys.rom`, `gba_bios.bin`, `syscard3.pce`, `bios.col`...) - only what is missing, never a move.

## 12. Hotkeys

- Stored in `Preferences.ShortcutKeys`: a list of `{ "Shortcut": "<EmulatorShortcut name>", "KeyCombination": {"Key1",
  "Key2", "Key3"}, "KeyCombination2": {...} }` (two bindings per action, up to 3 keys each) [M]. Key codes measured in
  the file Mesen wrote (identical in 2.1.1 and CE): Backspace 2, Tab 3, **Esc 13**, **F1 90 ... F12 101** (F2 91, F4 93,
  F6 95, F7 96, F11 100), Left Shift 116, Left Ctrl 118, Left Alt 120; pad buttons are >= 0x1000 (4113 "Pad1 L2", 4114
  "Pad1 R2") [M]. All 113 shortcuts are present in the file, unbound ones with zeros.
- Mesen's defaults (`PreferencesConfig.InitializeDefaultShortcuts` [S], M in the file): **F1-F7 = load slot 1-7**,
  **Shift+F1-F7 = save slot 1-7**, F8 = load the automatic state, F9 max speed, F10 FPS, **F11 full screen**, F12
  screenshot, **Esc = Pause**, Tab fast forward, Backspace rewind, Ctrl+R reset... `SaveState` / `LoadState` (current
  slot), `MoveToNextStateSlot` / `MoveToPreviousStateSlot` and `Exit` exist and are **unbound**.
- The pack's keys (brief §10, Mehdi 07/10): F2 save state, F4 load state, F6/F7 slot down/up, F11 full screen, Esc quit.
  In Mesen: F11 is already full screen; F2/F4/F6/F7 are Mesen's default **LoadStateSlot2/4/6/7** and Esc its **Pause**.
  The RaSnes9x rule applies (`RaSnes9xLaunch`: a default still on the key is unassigned when the pack takes the key):
  `SaveState`=F2, `LoadState`=F4, `MoveToPreviousStateSlot`=F6, `MoveToNextStateSlot`=F7, `Exit`=Esc - each only if the
  action is unbound and the key holds nothing but Mesen's own default for it (then that default is unbound). Q3 for Esc.
  (The computed task text said "F5/F7": that is the pack's superseded scheme - `BsnesAhk.Superseded` - not today's.)

## 13. Versions, claiming, the plan of the plugin

### Version and identity
- Exe name `Mesen.exe` for both 2.1.1 and CE - **and for the old Mesen 0.9.x (NES only, .NET Framework)**: the claim is by
  file name AND the version resource's major >= 2 (FileVersion `2.1.1` / `2.2.1` [M]). Mesen-S is `Mesen-S.exe`.
- `GetCurrentVersion`: the stamp `lbip-mesen-build.txt` the install writes beside the exe ("2.2.1"); none -> null (not
  ours). `IsUpdateAvailable` as `AresPlugin` (empty path -> true; not ours -> false; ours and older -> true).
- Latest: GitHub API `repos/nesdev-org/MesenCE/releases/latest` (asset `Mesen_<v>_Windows.zip`, check its `digest`);
  fallback when rate-limited: HEAD `https://github.com/nesdev-org/MesenCE/releases/latest` -> `Location:
  .../releases/tag/2.2.1` [M], then `.../releases/download/<tag>/Mesen_<tag>_Windows.zip`. The zip holds `Mesen.exe`
  alone at its root [M]: extract over the folder, nothing of the user's is in it.

### The session restore (because switches persist)
Options passed as switches (the game's window, the platform model, `--fullscreen` is not one) are written by Mesen into
`settings.json` [M]. Proposal: before the launch the plugin records, in a session file under `%LOCALAPPDATA%`, each
key it is about to override and its value in `settings.json`; once Mesen has quit (and at the next launch / listing if
LaunchBox died meanwhile, as `AresConsoleSession.Recover`), each key still holding the forced value gets its old value
back; a key the user changed during the session is left as he set it. `--doNotSaveSettings` is NOT used: it would lose
the user's own changes and the recent games.

### Saves and states folders (lot 9, "par plateforme")
Mesen never writes beside the game: its defaults are `<install>\Saves\` and `<install>\SaveStates\`, one for all systems.
Per platform: before each launch `OverrideSaveDataFolder`/`SaveDataFolder` = `Saves\<platform>` and
`OverrideSaveStateFolder`/`SaveStateFolder` = `Saves\<platform>\States` (relative, so a moved install keeps working)
- unless the user set his own folder (kept). What Mesen already wrote in its flat `Saves\` / `SaveStates\` for that game
is moved into the platform folder at its launch (Mesen's own files: moved, never over an existing one). Screenshots stay
in `<install>\Screenshots\` (Mesen's default, not beside the game). Q4.

### Files (mapped on Bsnes / RaSnes9x / Ares)

| File | Role | Model |
|---|---|---|
| `Mesen.csproj`, `manifest.json` | as Bsnes; PluginId reserved: `6823227e-f902-452b-a1b0-7591a7e55917`; Name `Nixx-Mesen`; help `docs\help\mesen.html` | Bsnes |
| `MesenPlugin.cs` | the `EmulatorPlugin` (directly), rows, claiming (`IsMesenExe` + version), `FillPlatformLines`, versions, install (bootstrap `settings.json` when absent, firmware import, save folders), `CreateEmulator` (one row per platform, IsDefault only where free), BIOS per platform, no RetroAchievements (Mesen has none [S: no RA code]) | Ares |
| `MesenSystems.cs` | the two tables of §6 (systems: core, extensions, battery extensions, firmware, model switch; platforms: name -> systems, ByExtension), `AllExtensions(p)`; CE-only rows flagged | `AresSystems` |
| `MesenBuild.cs` | §13 latest release, `Fetch`, stamp, `InstalledVersion`, `IsNewer`, the SHA-256 check | `AresBuild` |
| `Archives.cs` | the release zip extracted over; a multi-ROM game archive unpacked into `Unpacked\` | Bsnes/RaSnes9x |
| `MesenJson.cs` | edits `settings.json` in place (JsonNode keeps member order and unknown members), writes UTF-8 BOM + CRLF + 2 spaces only when changed, temp file + replace | `BsnesBml` / `RaSnes9xConf` |
| `MesenLaunch.cs` | exe identity, `Running`, `PrepareSettings` (folders, pack hotkeys), `SystemFor` (game window > line > table + extension), `CommandLine` (switches before the ROM, `--fullscreen`), archives | `AresLaunch` |
| `MesenSession.cs` | the session restore above, `Recover` | `AresConsoleSession` |
| `MesenFirmware.cs` | §11 table, `LbipBiosImport` into `Firmware\` | `AresFirmware` |
| `MesenAhk.cs` | Running/Exit (`WinClose, ahk_exe Mesen.exe`)/SaveState F2/LoadState F4, `Superseded` | `BsnesAhk` |
| `MesenSettings.cs`, `MesenSettingsPage.cs`, `MesenGameMenu.cs` | store, simple page (install, version, BIOS found, full screen), rich per-game window grouped by system (model, region, video filter/aspect...) with "As set in Mesen (value)" read from `settings.json` | Ares / SuperZsnes |
| `MesenSaveDesk.cs`, `MesenSaves.cs`, `MesenSavePaths.cs` | the desk; battery files of §9 (+ companions `.rtc`), one row per state slot 1-10 (+ 11 auto?), ids `{"emu":"mesen","kind":...,"sys":"nes|snes|gb|gba|pce|sms|ws","by":"name","id":<inner rom name>,"slot":n}`; folders per platform | RaSnes9x (slots) + Ares (sys) |
| `MesenStateThumbs.cs` | `<state>.mss.png` decoded from the state's frame per console (§10), window capture fallback | `RaSnes9xStateThumbs` |
| `Log.cs` | `%LOCALAPPDATA%\lb-integrations-plugins\mesen.log` | Bsnes |
| `src\Probe\MesenCheck.cs` | table vs the installed core (`MesenCore.dll` strings: firmware names, `.pc2` for CE), `settings.json` editing round trip on a copy, bootstrap, state frame decode on the lab's `.mss`, platform rows only added | `AresCheck` |

## 14. Open questions for Mehdi

1. **MesenCE instead of SourMesen 2.1.1?** (proposed: install CE 2.2.1, claim both).
2. **The model on the platform lines** (§6b): write `--gameboy.model=...` / `--ws.model=...` / `--pcEngine.consoleType=...`
   on the platform rows (ares-like), or leave them empty and let Mesen detect from the header, the game's window only
   overriding?
3. **Esc**: Mesen's Esc is Pause. Bind Esc to Mesen's `Exit` (and unbind Pause from Esc), or keep Pause and quit by the
   AHK Exit script only?
4. **Folders**: `Saves\<platform>` and `Saves\<platform>\States` rewritten before each launch (a Mesen started by hand then
   uses the last platform's folder), or Mesen's flat `Saves\` / `SaveStates\` (already never beside the game) with the
   system only in the ids?
5. `RecentGames\*.rgd` (Mesen's resume state, one per game, fixed folder): ignored, or listed?
6. `.chd` for TurboGrafx-CD: Mesen opens `.cue` only - convert through `Shared.Disc` into `Unpacked\`, or refuse with a
   message?
7. Vs. System / PlayChoice-10 `.nes` games filed under an Arcade platform: claim nothing (proposed).
8. LiteBox's other spellings seen in `Host\Romm\RommPlatformMap.cs` (`Nintendo Famicom`, `Super Famicom`, `Nintendo Sufami
   Turbo`, `Super Game Boy`, `NEC PC Engine`, `NEC PC Engine-CD`): claim them too?

**Mehdi's answers (19/10):** 1 - MesenCE 2.2.1. 2 - no model switch on any platform row: Mesen's own detection; a
game's window alone can force a system. 3 - the pack's keys: Esc = `Exit` (Pause unbound from Esc while it is still Mesen's
default), F2/F4 save/load the current slot, F6/F7 slot down/up, F11 full screen - a key is taken only from Mesen's default,
never from the user's. 4 - per platform: `Saves\<platform>`, `\States`, `\Screenshots` set before each launch and put back
after it, what Mesen wrote in its flat folders moved at the game's next launch. 5-8: the proposals stand (`.rgd` not
listed, `.chd` refused with a message, Vs./PlayChoice not claimed, LiteBox's spellings claimed, rows only when present).

## 15. Not measured yet (stage 2)

`--fullscreen` on the real exe; FDS/GBA/CV launch without BIOS (the modal box); a `.7z`; a multi-ROM zip; PCE CD; SGB;
the automatic state (slot 11) after 5 minutes; the state frames of PCE/SMS/WS/GBA; the per-platform folders with a game
switch inside Mesen; `ConfirmExitResetPower` true with WM_CLOSE.
