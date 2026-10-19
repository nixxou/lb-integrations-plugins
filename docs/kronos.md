# Nixx-Kronos - research notes

Sources: [S] = the code of BenjaminSiskoo/Kronos_test (branch extui-align, read 19/10/2026 at 245dc96), [M] = measured on
the real Kronos 2.7.5 64-bit (G:\LB1326\lbip-kronos-lab\run, Atlantis - The Lost Tales (FR) disc 1, CHD).

## 1. Which Kronos [M]

- FCare/Kronos has slowed down since 01/2025 (2 commits in 2026) and publishes only an Ubuntu binary on GitHub.
- The Windows builds come from Tradu-France, Benjamin Siskoo's site (Kronos's tester, now its maintainer):
  `https://tradu-france.com/archives/Kronos?arch=64-bits` lists every version with its size. Download links are
  `include/pages/download.php?file=...` and need a browser User-Agent and the page as Referer (403 otherwise). No hash
  is published.
- 2.7.5 (22/08/2026): zip 51 516 897 bytes, sha256 672536ac...debbca. `kronos.exe` is unsigned, has no VersionInfo
  (the string "2.7.5" is in it, and the window title reads "Kronos v2.7.5"), sha256 F75441A4...26E12. Defender: clean.
  Qt 5.14, SDL2, its own vc_redist.x64.exe (not run).
- The live code is Siskoo's fork: its last commits before the release are dated 17-19/08.
- 2.7.5b, the pack's own (Mehdi 19/10): nixxou/Kronos_test, branch lbip-2.7.5b - 2.7.5's source (66bd0836d, the last commit
  before it; no stv.c change between it and the release) with the fork's fix of 23/08 (UIYabause calls threadInitialized()
  itself: the ST-V game list made at start, §8). Built by the branch's workflow (lbip-windows-release.yml: windows-latest,
  Qt 5.15.2 MSVC, SDL2 2.30.8, CMake 4 - CMAKE_POLICY_VERSION_MINIMUM=3.5 in the job's environment, libchdr/zlib/m68kmake ask
  for less), laid out as Tradu-France's zip (kronos.exe, Qt, SDL2, an empty kronos.ini, vc_redist.x64.exe); a tag v* makes
  the GitHub release, its asset Kronos_2.7.5b_-_64_Bits.zip. Version "2.7.5b" (YAB_VERSION_SUFFIX), title "Kronos v2.7.5b".
  The plugin installs the newer of Tradu-France's latest and this release (2.7.5 < 2.7.5b < 2.7.6), GitHub's size and
  SHA-256 checked; the stamp says where it came from. [M: Shienryu started by -a; a Saturn disc boots.]

## 2. Command line [S, M]

`kronos.exe -a -f -i "<disc>"` starts the game in full screen [M: window 1920x1080, title "ISO: <file>"].
Other switches: `--bios=<file>`, `-nb` (emulated BIOS), `-l <language>` (english, deutsch, french, spanish, italian,
japanese), `--autoload=<slot>` (starts and loads that state), `-ns` (no sound), `-v`.
They set VOLATILE settings: nothing is written to kronos.ini [M: no Fullscreen or CdRomISO key after the run]. A file
given without a switch is taken as `-a -i <file>`.

## 3. Settings: kronos.ini [S, M]

- On Windows, a `kronos.ini` beside the exe is used when it exists (the 2.7.5 zip ships an empty one = portable mode),
  otherwise `%AppData%\kronos\kronos.ini`.
- QSettings IniFormat, EVERYTHING under the `[1.0]` section with backslash groups: `General\Bios=...`. Keys written at
  the root are ignored [M].
- An almost empty ini fails with "Cannot initialize SH2" (a dialog, the emulator stays open) [M]:
  `Advanced\SH2Interpreter=8` (SH2 Kronos interpreter) is required. The settings dialog writes the full set.
- Kronos keeps the keys it finds and only adds `General\Geometry` at exit [M].
- Keys of interest: `General\Bios`, `General\EnableEmulatedBios`, `General\SaveStates`, `General\SystemLanguageID`,
  `General\SH2Cache`, `Memory\Path`, `Memory\ExtendMemory`, `Cartridge\Type`, `Cartridge\Path`, `Video\VideoCore`,
  `Video\AspectRatio`, `Video\resolution_mode`, `Video\upscale_type`, `Video\filter_type`, `Video\MeshMode`,
  `Video\BandingMode`, `Sound\SoundCore`, `Sound\Volume`, `Shortcuts\<action text>`.
- Defaults outside the profile: `Memory\Path` = `%AppData%\kronos\bkram.bin`, `General\SaveStates` = `%AppData%\kronos`
  [S]. Set both, and nothing reaches AppData [M: no %AppData%\kronos after 3 runs].

## 4. Saves [S, M]

- Internal backup RAM: by default an EXTENDED 8 MB memory that no Saturn has (`Memory\ExtendMemory`, true by default).
  A 32 KB file is detected and switches to the standard size by itself [S: BackupInit; M: Ymir's 32 KB file, with
  ExtendMemory=true, used and left byte-identical]. Its layout is 32 KB raw, header "BackUpRam Format", the same as
  Beetle Saturn's .bkr and Ymir's internal RAM. A wrong size is moved aside (`.bad`) and a `.bak` is tried [S].
- Backup cartridge: `Cartridge\Type=2` (4 Mbit) and `Cartridge\Path`. The file is 512 KB raw (odd bytes of the bus), the
  same as Beetle's .bcr and Ymir's 4 Mbit cartridge [S]. [M: Beetle's .bcr with the LBIPTEST___ mark read and left
  byte-identical.] Types: 0 none, 2/3/4/5 backup 4/8/16/32 Mbit, 6/7 DRAM 8/32 Mbit, 9 ROM 16 Mbit, 11 ST-V ROM,
  12 DRAM 128 Mbit.
- Save states: `<General\SaveStates>\<product code>_<slot 000-009>.yss` (~13.6 MB) [S, M: `MK-8109109_001.yss`].
  Named by the product code only, so each game needs its own folder.

## 5. Keys [S, M]

- Defaults: Ctrl+0..9 save to slot n, Alt+0..9 load it, F1 run, F2 pause, F3 reset, F4 vsync, F5/F9 load/save from a
  file, F12 FPS, Alt+Return full screen, Ctrl+I eject/load an image (a dialog), Ctrl+Q quit, digits 1-7 toggle the VDP
  layers.
- Rebinding: `Shortcuts\<English action text>=<key>` in `[1.0]` [M: `Shortcuts\Save1=F8` -> F8 wrote slot 1].
- Ctrl+2 (default Save2) wrote nothing, twice, in full screen [M]. Rebind every slot key instead of relying on the
  defaults.
- Rebound keys with modifiers all work [M, 2nd run]: F8, Shift+F5, Ctrl+F6, Alt+F7, Ctrl+Shift+F9 each wrote its slot.
  Only the default digit shortcuts (Ctrl/Alt + 0-9) are dead.
- Escape does not quit [M]. WM_CLOSE quits cleanly, the files are flushed [M].

## 6. Disc formats [S]

By extension (case-insensitive): `.cue` (+ its .bin), `.chd`, `.mds`, `.ccd` (+ .img), `.zip` (a bin/cue inside only).
Anything else is read as a plain `.iso`. No `.m3u` and no `.7z`: multi-disc sets and other archives are the plugin's job,
as for Ymir.

CHD CODECS [S, M 19/10]: Kronos's libchdr (tools\libchdr, an old copy - lzma 16.04, flac 1.3.2) decodes zlib, cdzl, cdlz and
cdfl only. A CHD made with `chdman createcd -c cdzs,cdfl` (Zstandard) leaves Kronos 2.7.5 on an empty window, no error said; the
same disc in cdlz,cdzl,cdfl boots, and so does it uncompressed (`-c none`). So the plugin reads the codecs in the CHD v5
header and hands such a disc over UNCOMPRESSED, Demul's model (KronosChd, from DemulChd): served where it is through the RAM
disk helper (view=chd on AIM), detached when Kronos has gone, else copied once into `<kronos>\lbip-chd\` (a 20 GB cache) -
every disc of the game, the next-disc key's too; every other CHD as it is [M 19/10: Atlantis cdzs,cdfl copied in 3 s,
484 MB, Kronos on the Saturn then SEGA screens - `--kronos-zstd`; served through AIM with the helper 1.14 of G:\LB1326
(`--lb`): Z:\game.chd, the same screens, Z: detached once Kronos had gone]. The next-disc key says each disc's own name
(lbip-discs.txt: path, a tab, the name), not "game". The other Saturn emulators, measured with the same file: Ymir 0.4.1 reads it (libchdr 47d7941 has
cdzs); RetroArch's Beetle Saturn v0.9.45.1 reads it (Game ID, region in its log, as with the reference); the yabause,
yabasanshiro and kronos libretro cores do not ("Cannot initialize ISO-File Virtual Drive" with it only). CHDSharp (the plugins'
disc reader) reads it: same product code and hash.

## 6b. A game's own options [S, M 19/10]

KronosGameOptions, Ymir's model: keys of kronos.ini written for the game's session, put back after it (KronosSession); changed
in Kronos during the game, the option becomes the game's own. Read where Kronos reads them at each start
(YabauseThread::reloadSettings), values as UISettings writes them: Video\resolution_mode (1 the default, 8, 128, 32, 256, 64 =
the window's), Video\AspectRatio 0-3, Video\filter_type, Video\upscale_type, Video\MeshMode, Video\BandingMode,
General\SH2Cache, General\SystemLanguageID "0"-"5", Cartridge\Type 0 / 6 (8 Mbit DRAM) / 7 (32 Mbit) / 9 (16 Mbit ROM +
Cartridge\Path, a file of <kronos>\cart). Left out on purpose: Video\VideoCore (forced to the compute-shader core whatever the
file says), the region (from the disc; STV\Region is ST-V's), the SH-2 interpreter (8 only). [M: filter 6 + 4x + 32 Mbit
DRAM forced, scanlines on the Saturn screen - `LBIP_PROBE_OPTS=... --kronos-zstd`.]

## 7. Open questions

- Non-ASCII paths: a CHD through a folder named "Jeux é" opened [M: its product code read, states written];
  .cue/.bin and .mds with accents not measured.
- Why the digit shortcuts do nothing (the digits 1-7 toggle the VDP layers, the keyboard pad?) - rebound, irrelevant.
- ST-V: done, §8.
- ST-V in full screen not measured (2.7.5: the plugin toggles Kronos's "Fullscreen" after Run; 2.7.5b: -a -f); nor a clone
  set whose files are in its parent's zip (MAME's split sets) - the non-merged sets of MAME 0.288 were measured.

## 8. Sega ST-V [S, M 19/10]

- PLATFORMS: "Sega ST-V" (LaunchBox.Metadata.db spells it so), and "Arcade" (Mehdi 19/10), never recommended; the
  emulator gets a Sega ST-V row, never the default (MAME is).
- THE SETS: kronos-stv.tsv, made by tools\kronos-stv\make-sets.py from stv.c's GameList (119 entries in the fork's source),
  LaunchBox's Metadata\MAME.xml (106 sets of sega/stv.cpp, titles, parents) and kronos.exe (a name is known only when the
  binary has it). Kronos 2.7.5 knows 106: all of MAME's but wwshin, plus tsuribor (not in this MAME.xml). The source has 13
  more (Print Club sets...) the binary lacks - the fork's source is past 2.7.5. Parsing traps: C comments inside an entry
  ("{ /* ADDED */"), a line commented out (a NO_DUMP file), a CRC written with 9 digits (cut to 32 bits like the compiler).
  [M: the 106 complete, every file by name and CRC, in MAME 0.288 non-merged.]
- HOW KRONOS LOADS ONE: Cartridge\Type=11, Cartridge\Path a FOLDER (and Cartridge\Path\STVRomgame - the settings window's
  own key, "Cartridge/Path/" + the cartridge's name without spaces), Cartridge\STVGame the set. It reads every zip of the
  folder, writes gamelistv2.save, and writes <set>.nv (EEPROM, 128 bytes) and <set>.coins there when it quits.
- THE BIOS FROM A FILE NAMED stvbios.zip ONLY [M 19/10]: updateGameList counts a BIOS once processBios has read one, and only a
  zip of that name goes to processBios - the BIOS files a MAME non-merged set carries in its own zip are never taken
  (shienryu.zip alone: an empty gamelistv2.save, "Cannot initialize STV emulation", in 2.7.5 as in 2.7.5b; with stvbios.zip
  beside it, listed and run). So without a stvbios.zip the plugin makes one in the launch's folder with the BIOS files of
  the game's zip (or its parent's) - KronosStv.MakeBiosZip, their bytes and CRCs unchanged.
- 2.7.5 CANNOT START ONE BY ITSELF [M]: with -a, "Cannot initialize STV emulation" twice - its game list is made only by its
  settings window (the start's call, UIYabause::threadInitialized, is never made in 2.7.5; the fork fixed it later, and
  2.7.5b has that fix: started by -a [-f] like a Saturn disc, KronosBuild.StvStartsItself - an install of ours from 2.7.5b
  on). For 2.7.5 (or a Kronos the plugin did not install) the plugin (KronosStvStart) starts Kronos with neither -a nor -f, then through UI Automation invokes the toolbar's "Select
  Cartridge" (the settings window, cartridge tab: the list made), posts Enter to that window (its controls are not exposed to
  UI Automation; a posted key needs neither the foreground nor the keyboard), invokes "Run", and toggles "Fullscreen" for a
  game in full screen. [M: Shienryu and Batman Forever started in 3.4 s, no key typed.] The settings window's OK writes
  Kronos's whole settings with the values it shows (the ones in effect - defaults where absent): harmless.
- EACH LAUNCH: <kronos>\lbip-stv emptied, the game's zip, its parent's when a file is there, stvbios.zip (or one made of
  the game's BIOS files) - hard links (the same volume), copies otherwise; Cartridge\STVGameName the set's title (the title
  bar's - only the settings window writes it); its .nv and .coins brought from Saves\<platform>\ and taken
  back after (lbip-stv.txt says which game, for a session cut short). Checked before, as Kronos would: the set known, every
  file by name and CRC, a BIOS - refused with what is wrong. States: "_NNN.yss" (no disc: cdip->itemnum is empty) in
  States\<set>. Screenshots of every game in Saves\<platform>\Screenshots (Kronos's default is %LocalAppData%).
- IMPORT: KronosLbImport (Demul's DemulLbImport): to Sega ST-V the ST-V sets stay (those Kronos does not run asked about,
  or per the Nixx window), to Arcade only Kronos's; BIOS and the rest out, MAME's titles. [M: MAME 0.288, 43974 files: 107
  kept for Sega ST-V, 106 for Arcade, in under 60 ms.]
- A GAME'S OWN OPTIONS: those of every game (picture, SH-2 cache, language) and the board's region (STV\Region E/U/J/T);
  no expansion cartridge (the ST-V cartridge is the game).
