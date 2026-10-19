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
- Escape does not quit [M]. WM_CLOSE quits cleanly, the files are flushed [M].

## 6. Disc formats [S]

By extension (case-insensitive): `.cue` (+ its .bin), `.chd`, `.mds`, `.ccd` (+ .img), `.zip` (a bin/cue inside only).
Anything else is read as a plain `.iso`. No `.m3u` and no `.7z`: multi-disc sets and other archives are the plugin's job,
as for Ymir.

## 7. Open questions

- Non-ASCII paths (fopen narrow strings outside MDS).
- Why Ctrl+n does nothing (a menu action hidden in full screen, or the keyboard pad catching Ctrl).
- ST-V (arcade), second batch (Mehdi 19/10): LaunchBox has the platform "Sega ST-V" (LaunchBox.Metadata.db). The games
  are MAME sets (stv.cpp, BIOS stvbios.zip); Kronos's table has ~130 of them (ctrl/src/stv.c GameList, files matched by
  name and CRC). Kronos scans EVERY zip of its ROM folder and writes gamelistv2.save there: the plugin will give it a
  folder of its own per launch (the game's zip, its parent, stvbios.zip). No command-line switch: Cartridge\Type=11 +
  Cartridge\STVGame. Import: Flycast's filter (FlycastLbImport) - quick by the zip's name in Kronos's table, CRC by
  its files - so a whole MAME folder imported to Sega ST-V keeps only Kronos's games.
