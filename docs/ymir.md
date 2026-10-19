# Nixx-Ymir: research notes (stage 1)

Facts tagged [M] measured in the lab (`G:\LB1326\lbip-ymir-lab`), [S] read in Ymir's sources at the v0.4.1 tag
(`lab\src-041`), [L] read in the official compatibility list. Plan and open questions at the end.

## 1. Which Ymir

- The project moved from `StrikerX3/Ymir` to **`github.com/ymir-emu/Ymir`** (the old URL redirects) [M]. GPL-3.0, C++,
  very active: 0.3.1 (05/2026), 0.3.2 / 0.3.3 (06/2026), **0.4.0 (2026-10-17), 0.4.1 (2026-10-18)**, plus a rolling
  `latest-nightly` pre-release [M, GitHub API].
- Windows assets per release: `ymir-windows-x86_64-AVX2-<v>.zip`, `ymir-windows-x86_64-SSE2-<v>.zip`,
  `ymir-windows-ARM64-<v>.zip`; the API gives each asset's SHA-256 (`digest`) [M]. The plugin takes the latest STABLE
  release, the AVX2 build when the CPU has AVX2 (`System.Runtime.Intrinsics.X86.Avx2.IsSupported`), else SSE2; never the
  nightly. Downloaded 0.4.1 AVX2: SHA-256 `7a62443b...0e8` = GitHub's digest [M].
- The zip: `ymir-sdl3.exe`, `dxcompiler.dll`, `dxil.dll` (Direct3D 12 shader compiler, for the hardware renderer),
  `gamecontrollerdb.txt`, `licenses\`, README / CHANGELOG / TROUBLESHOOTING [M].
- Save states are guaranteed compatible only between stable releases (the README; nightly warning) [S].

## 2. Command line [S: `apps/ymir-sdl3/src/main.cpp`]

    ymir-sdl3.exe [-d] <disc> [-p <profile dir>] [-u] [-f] [-P] [-F]

`-d/--disc` (also positional) the disc image; `-p/--profile` a profile folder; `-u` force the user profile; `-f`
full screen; `-P` start paused; `-F` start in fast forward; `-h` help. No config switches (unlike Mesen).

## 3. The profile (where everything lives) [S: `app.cpp` 341-420, `profile.hpp`]

Priority: `-p` > `-u` > current folder if `Ymir.toml` is there > the exe's folder if `Ymir.toml` is there > the user
profile (`SDL_GetPrefPath`) if it has one > a first-run dialog ("Installed" / "Portable"). LaunchBox starts emulators
with ITS ROOT as current folder (memory "lb-cwd-is-root"), so the plugin always passes **`-p <install>`**: portable, no
dialog, whatever the current folder.

Inside the profile: `Ymir.toml`; `roms\ipl\` (BIOS), `roms\cdb\` (CD block ROM, optional LLE), `roms\cart\`;
`backup\` (backup RAM), `backup\exported\`; `state\` (persistent state); `savestates\`; `dumps\`; `screenshots\`.
Every one of those can be moved by `[General.PathOverrides]` in Ymir.toml (`IPLROMImages`, `BackupMemory`,
`PersistentState`, `SaveStates`, `Screenshots`...).

## 4. Disc formats [S: `libs/ymir-core/media/loader`]

Ymir opens itself: **.chd, .cue (BIN/CUE), .mds (MDF/MDS), .ccd (IMG/CCD/SUB), .iso** - and a physical drive. Not:
.m3u, .zip / .7z, a lone .bin / .img, anything else ("Not a valid disc image format...").
So for LaunchBox: a .m3u is the plugin's (no playlist in Ymir), an archive is unpacked by the plugin (AutoExtract off,
as Mesen, or on), a .chd is native - no conversion needed. Multi-disc: no disc list in Ymir; Load Disc (Ctrl+O, a file
dialog), Eject (Ctrl+W), Open/Close tray (Ctrl+T) [S settings.cpp defaults].

## 5. The disc hash and the Saturn header [S, M]

- Ymir's **disc hash** (`media/filesystem.cpp`): XXH3-128 of the 2048-byte user data of FAD 150-165 (the system area,
  LBA 0-15), then of each volume descriptor from FAD 166 up to and including the terminator; canonical (big-endian)
  digest, written in upper-case hex. It names the save-state folder and identifies a game in the compatibility list.
- **Measured** on the two Atlantis CHDs (CHDSharp, `MODE1_RAW` track 1, user data at +16 of each 2448-byte unit):
  `05ED3C66A53212C9DFD7F86B625C723D` and `6CBE114C153C7F5A5A0F821CE73D12AE` - exactly the list's hashes of
  "Atlantis - The Lost Tales (France) (Disc 1 / 2)" [M, L]. The throwaway checker: scratchpad `ymirhash`.
- The header (LBA 0): "SEGA SEGASATURN ", product number at 0x20 (`MK-8109150`), version at 0x2A, area at 0x40, title
  at 0x60 [M]. NB the lab's files are swapped: "Atlantis (FR) (Disc 1).chd" holds "ATLANTIS CD2" [M].
- The product code alone is NOT unique: the French and European Atlantis share `MK-8109150` [L].

## 6. BIOS [S: `db/ipl_db.cpp`, `services/rom_service.cpp`]

Any file in `roms\ipl\` (or the IPLROMImages override), recognised by its XXH128 - names do not matter. Known: JP
1.00 / 1.01, US-EU 1.00a / 1.01a (`mpr-17933.bin` = 1.01a), KR 1.02a, Hi-Saturn 1.01-1.03, V-Saturn 1.01, the dev kit,
and region-free patched variants. Per game, Ymir picks the ROM of the disc's area (J / U-E / K...), else a region-free
one; `[System.IPL] Override` forces a file. Lab: `G:\LB1326\Emulators\RetroArch\system\mpr-17933.bin` [M].

## 7. Saves [S: `shared_context.cpp`, `settings.cpp`, `services/save_state_service.cpp`]

- **Internal backup RAM** (the console's own, 32 KB): by default ONE file for every game,
  `state\bup-int.bin` (`[System] InternalBackupRAMImagePath`) - as a real Saturn. With `InternalBackupRAMPerGame =
  true`: `backup\games\bup-int-<title> [<product>].bin`, title and product from the disc header - so the two discs of
  a game get TWO files (different titles and products).
- **Backup RAM cartridge** (4-32 Mbit): per game `backup\games\bup-ext-<N>M-<title> [<product>].bin`, or one image
  (`[Cartridge.BackupRAM] ImagePath`).
- **Save states**: `savestates\<disc hash>\<slot>.savestate` (slots 1-10) and `<slot>-1.savestate` (the previous one,
  for "undo save"). No picture file beside them (to be checked in the state format).
- `state\smpc-<region>.bin`: the console's clock / language, per BIOS region.

## 8. Hotkeys [S: `settings.hpp` Hotkeys, `settings.cpp` defaults 2335-2425]

Defaults: **F2 quick save, F3 quick load**, 1-0 select slot 1-10, Ctrl+1..0 load slot, Shift+1..0 save slot,
Shift+F2 / Shift+F3 undo save / load, **Alt+Enter full screen**, F9 windowed output, F10 settings, F11 debug trace (!),
F12 screenshot, Space / Pause pause, Tab turbo, Backspace rewind, F8 rewind buffer, **ExitApp unbound** (Alt+F4 always).
No next / previous slot action. Stored in Ymir.toml `[Hotkeys]` and `[Hotkeys.SaveStates]` (`QuickSaveState`,
`QuickLoadState`, `SelectState1`..., `ExitApp`, `ToggleFullScreen`) - exact value format to measure.

## 9. Ymir.toml [S]

`ConfigVersion`; `[General]` (PreloadDiscImagesToRAM, RememberLastLoadedDisc, PauseWhenUnfocused, CheckForUpdates...,
`[General.PathOverrides]`), `[GUI]`, `[System]` (VideoStandard, AutoDetectRegion, PreferredRegionOrder, EmulateSH2Cache,
SH2ClockFactor, InternalBackupRAMImagePath, InternalBackupRAMPerGame, `[System.IPL]`, `[System.RTC]`), `[Hotkeys]`,
`[Input]`, `[Video]` (FullScreen, ...), `[Audio]`, `[Cartridge]`, `[CDBlock]`. Written by toml++ on change - edited
line by line as XemuToml does. Defaults written at the first run: to measure.

## 10. Compatibility list [L]

The official list (README): a Google Sheet, `1SLZzL9LelSlpEmTKy8cjaQnE7mew2uW1rfCgcekO58Q`, readable without an
account as CSV per tab (`/gviz/tq?tqx=out:csv&sheet=<tab>`). Tabs NTSC-U (324 games), NTSC-J (1684), NTSC-A (14), PAL
(359), ROM Hacks, Homebrew. Columns: Game name (Redump), Product code, **Disc hash**, Compatibility (Unknown, Nothing,
Intro, Menus, In-game, Playable, Perfect), Ymir version, BIOS, Tester, Additional notes. Matching: the disc hash
(exact), then the product code + title. Precedents: Vita3kCompat, XeniaCompat.

## 11. Not measured yet

The first-run Ymir.toml and the hotkey value format; full screen and exit with posted keys; WM_CLOSE flushing the backup
RAM; the save-state format (a picture inside?); a .cue / .iso / .mds / .ccd launch; the AVX2 / SSE2 choice; disc swap
through the Load Disc dialog.
