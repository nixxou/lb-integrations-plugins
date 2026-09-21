# lb-integrations-plugins

Emulator integration plugins for LaunchBox and LiteBox, for emulators LaunchBox does not cover.

LaunchBox ships 11 official "\<Emulator\> LaunchBox Integration" plugins — RetroArch, Dolphin, MAME,
PCSX2, ScummVM, BigPEmu, Xemu, Azahar, CEMU, RPCS3, DuckStation. Its metadata database knows 35
emulators, so **24 have no integration at all**: no download and update, no BIOS checks, no
RetroAchievements, no save management. The plugin catalogue is closed to third parties (every entry
is published by Unbroken Software), so these are installed by hand.

| Plugin | Emulator | Platform | Status |
|---|---|---|---|
| `src/Ppsspp` | PPSSPP | Sony PSP | download / update, BIOS, RetroAchievements, launch, save management |
| `src/Xenia` | Xenia (canary) | Microsoft Xbox 360 | download / update, launch fixes, save management |
| `src/Flycast` | Flycast | Sega Dreamcast, Sega Naomi, Sega Naomi 2, Sammy Atomiswave | download / update, BIOS, RetroAchievements, launch, save management |

## Building

```
dotnet tool restore
dotnet build src\Ppsspp\Ppsspp.csproj -c Release
```

No LaunchBox installation is needed: the SDK is committed under `vendor\` (see `vendor\README.md`
for which version and why).

A Release build runs a merge step (ILRepack, pinned in `dotnet-tools.json`) that folds every
dependency into the assembly and marks its types `internal`, producing
**`bin\Release\merged\Ppsspp.dll`** — one file whose reference table holds exactly one
non-framework entry, `Unbroken.LaunchBox.Plugins`. That is the file to install; `deploy-dev.ps1`
prefers it automatically. See `THIRD-PARTY.md` for what is folded in and under which licences.

## Installing

```
.\deploy-dev.ps1                      # builds, copies into G:\LB1326, verifies by hash
.\deploy-dev.ps1 -Plugin Xenia
.\deploy-dev.ps1 -LbRoot 'G:\LB'
```

It deploys the DLL **and** `src\<Plugin>\manifest.json` into `Local\Plugins\`, and verifies both by
hash. The manifest is not optional there: LaunchBox 14 loads nothing without one and says nothing
about it - a plugin can sit in a plugin folder for a day, never run, write no log line, and simply
have no install option in the Add Emulator window.

Or by hand. LaunchBox 14 reads three plugin roots and the choice matters:

| root | for | our plugins |
|---|---|---|
| `System\Plugins\` | Unbroken's own, `SourceKind: "LaunchBox"` | **no** - the core refuses a manifest whose declared origin does not match the root, with *"The plugin manifest does not match its managed install location"* |
| `Local\Plugins\` | third-party, managed, `SourceKind: "Local"` | **yes**, with a `manifest.json` beside the DLL |
| `Plugins\` | the legacy root, no manifest needed | works, but is not the managed location |

So: `<LaunchBox>\Local\Plugins\<Name>\<Plugin>.dll` plus its `manifest.json`. Before LaunchBox 14,
`Plugins\` is the only option.

Plugins are loaded once at start-up, so restart the frontend. Under LiteBox, tick the plugin in
**Options ▸ Plugins** the first time; it is not auto-enabled, deliberately, because the name that
would auto-enable it (`... LaunchBox Integration`) impersonates Unbroken's own.

## Testing without a frontend

`src\Probe` is a host stand-in. It loads a plugin, finds its `EmulatorPlugin`, and calls the
read-only half of the contract, printing what comes back — seconds instead of a frontend restart.

```
dotnet run --project src\Probe -- src\Ppsspp\bin\Release\Ppsspp.dll --emu "D:\Emulators\PPSSPP\PPSSPPWindows64.exe"
```

It writes nothing unless you ask it to. These modes do — the last two only inside the temp folder:

```
--inject-ra-test
    writes RetroAchievements credentials into the emulator you named with --emu.

--save-unit <PSP\SAVEDATA> --disc-id ULUS10064 [--expect-hash <md5>]
    extracts that save the way a backup would, checks the layout, prints the hash RomM and
    Argosy compute for it, and - when --emu is given - restores it and re-extracts to prove
    the round trip is lossless. The restore DELETES and rewrites that disc id's folders.

--flycast
    the whole Flycast contract against a forged install: disc ids, the three save regimes,
    BIOS, and how an emulator entry gets its platforms.

--hotkeys
    writes Flycast's keyboard mapping on a forged install and checks that none of Flycast's
    own default keys were lost in the process.
```

Point the first two at a throwaway install and a throwaway account.

Two modes write nothing at all: `--disc-id-of <rom>` reads a disc id out of a `.pbp`, `.iso`, `.cso`
or `.zso`, and `--rows` confronts the metadata row injection with a real `Microsoft.Data.Sqlite`.

`--flycast-real --emu <flycast.exe> --rom <game>` is the one that needs a real Flycast; it reads and
compares, and writes nothing.

## Notes on PPSSPP

**It needs no BIOS.** PPSSPP high-level-emulates the BIOS and the PSP's operating system; its own FAQ
says so and LaunchBox's metadata database agrees. `assets\flash0\font\` holds 18 freely
redistributable replacement fonts shipped with the emulator, not firmware. `GetBiosFilesForPlatform`
returning nothing is correct — please do not "fix" it.

**Installs are portable, and that constrains updates.** The release zip contains no `installed.txt`,
and its absence beside the executable makes PPSSPP keep everything — saves, save states,
`ppsspp.ini`, the RetroAchievements token — under `memstick\` *inside the emulator folder*. So an
update extracts **over** the folder and never clears it first. The cost is that files dropped between
releases linger.

This plugin also never converts an existing installer-based install to portable mode: that would
move the user's saves without telling them.

**RetroAchievements credentials go to two places.** PPSSPP marks `AchievementsToken` as `DONT_SAVE`:
it reads the key from `ppsspp.ini` but never writes it back, so that a user can post their ini for
debugging without leaking a credential. The copy it maintains is
`<memstick>\PSP\SYSTEM\ppsspp_retroachievements.dat`, raw bytes. A token written only to the ini
survives exactly until PPSSPP next exits.

Also: the ini key for hardcore mode is `AchievementsChallengeMode`, and it defaults to *on*.

**A save is several folders, and the set is the unit.** PPSSPP writes one folder per purpose under
`PSP\SAVEDATA\`, all sharing the game's 9-character disc id: `ULUS10064DATA00`,
`ULUS10064SETTINGS`, `ULUS10064SYSTEM`. All of them together are one save. This matters beyond
tidiness: Argosy, the Android client that shares these saves through RomM, **deletes every folder
matching the disc id** before unpacking a restore, so handing it an incomplete set destroys the
folders left out.

Folders holding installed game data - a readable `PARAM.SFO` of at most 64 KiB that declares neither
`SAVEDATA_PARAMS` nor `SAVEDATA_FILE_LIST` - are excluded, the same way and by the same rule Argosy
excludes them.

**A save is matched to a game by disc id, read from the ROM.** `.pbp`, `.iso`, `.cso`, `.zso` and
`.chd` are all parsed. Anything else falls back to matching on the game title from `PARAM.SFO` —
which is `TITLE`, not `SAVEDATA_TITLE`: the former names the game, the latter names the save inside
it.

**The backup layout is an interop contract, not a preference.** A backup contains each folder under
its own name at the root, because that is what makes the content hash equal the one RomM and Argosy
compute for the same save. `--save-unit` asserts it against a value derived from the golden vectors
in `argosy-launcher/sigil/scripts/romm_hash_vectors.py`; if you change how saves are stored, that
assertion is what will tell you the two ends have stopped agreeing.

Save STATES are deliberately not managed. They never leave the machine that wrote them (a state
belongs to one core at one version, and the server refuses them), and LiteBox drops companion files
for every state, so a `.ppst`'s sibling `.jpg` thumbnail could not travel with it anyway.

**Downloads come from GitHub.** ppsspp.org publishes a different set of Windows packages — a zip with
both the 32- and 64-bit builds, plus an Inno installer and a paid Gold variant. This plugin installs
the GitHub x64 zip and does not offer the installer, but it recognises an install made either way.

## Notes on Flycast

**Portable, always.** On Windows `setupPath()` (core/windows/winmain.cpp) unconditionally puts
`emu.cfg` beside the executable and everything written under `<exe>\data\`. There is no `%APPDATA%`
branch and no installed/portable choice to interpret, unlike PPSSPP.

**One emulator, four platforms.** Dreamcast, Naomi, Naomi 2 and Atomiswave. The arcade three read
their ROMs straight out of `.zip`, so the emulator entry must NOT auto-extract — handing Flycast an
extracted folder gives it something it cannot load.

**The BIOS are optional, and declared per romset.** Dreamcast runs most games without one; the arcade
platforms do not run at all without theirs. Both are declared so the frontend can tell the user which
file is missing instead of letting a game fail silently.

**It ships with no save-state keys, so this plugin adds them.** Flycast's default keyboard mapping
binds Tab, Space and F12 and nothing else: `EMU_BTN_SAVESTATE`, `EMU_BTN_LOADSTATE` and even
`EMU_BTN_ESCAPE` work but are attached to no key. On install the plugin writes
`mappings\SDL_Keyboard.cfg` with **F2** save, **F4** load, **F6**/**F7** slot, **Escape** quit — F2 and
F4 because that is what LaunchBox's own scripts already assume for RetroArch, and Escape because a
frontend's Exit button means Escape and `dc_exit()` closes the VMU files properly where a killed
process does not.

Two things make that file delicate, and both are why it carries more than four lines. A mapping file
REPLACES Flycast's defaults rather than adding to them (`GamepadDevice::find_mapping` falls back to
`getDefaultMapping()` only when no file exists), so it must also carry all nineteen default bindings
or the user loses his controls — `--hotkeys` exists to check exactly that. And one file covers all
four platforms: with no `_arcade` file, Flycast clones the Dreamcast mapping for arcade.

A mapping file that already exists is never rewritten. An action the user has bound keeps his key, a
key he already uses is never taken, and the emulator's AutoHotkey fields quote whatever the file
really says — a script naming a key the emulator ignores would fail silently, which is worse than no
script. Those fields are used by LaunchBox's and BigBox's pause screen; LiteBox stores them but has
no pause screen yet.

## Notes on Xenia

**Canary, not master.** `xenia-project/xenia` has been frozen since February 2026 and xenia.jp marks
it inactive; `xenia-canary` ships almost daily. The plugin **claims** both (`xenia_canary.exe` and
`xenia.exe`) and **reads saves from both** content layouts, but only **installs** canary.

**There is no version to read.** Xenia has no Win32 version resource and no `--version` flag, and
`--help` opens a modal dialog in a windowed app - never invoke it. What exists is a build string
compiled into the binary (`canary_experimental@74c4e4a on Sep 21 2026`), which this plugin reads out
of the file. Canary's "version" is a 7-character git SHA, so releases are ordered by publication
date, never by tag.

**A save is the `00000001` folder, not the title folder.** Under one title id sit `00000001` (saved
games), `00000002` (downloadable content) and `000B0000` (title updates). Only the first is a save.
This is also where Argosy stops, and matching it is what lets a save sync between the two.

**Two content layouts, sometimes in the same install.** Canary keys content by profile before title
(`content\<XUID>\<TITLEID>\<TYPE>\`); master does not (`content\<TITLEID>\<TYPE>\`). Canary
migrates in place, so a real install can hold both at once - at the top of `content\`, a 16-hex name
is a profile and an 8-hex name is a legacy title id.

**The save's `.header` travels outside the fingerprint.** Display name, thumbnail and license mask
live in a file one level above the save, which Argosy does not send. Including it would move the
content hash and the two ends could never agree; leaving it behind would lose the name and the
picture. It goes into LiteBox's reserved `.litebox-plugin` folder inside the backup, which is
excluded from every hash and from the zip served to clients.

**A restore merges, and never invents a profile.** Argosy deletes nothing on an Xbox 360 restore, so
neither does this. And a save can only be restored into a profile that exists: constructing a XUID
would produce a directory the emulator never reads, so with no profile the plugin says so instead.

**Two launch defaults are corrected** on the command line, because Xenia regenerates its TOML on
every start and exit so writing there is pointless: `--license_mask=1` (the default `0` boots many
XBLA titles in trial mode and hides owned DLC - the most common Xenia support question) and
`--discord=false`. Anything you set yourself is left alone.

**No RetroAchievements.** Neither fork supports it. Canary has native Xbox 360 achievements written
into the profile's `.gpd` files, which is unrelated machinery.

**Title ids** are read from the content: STFS containers (`CON`/`LIVE`/`PIRS` - Games on Demand,
XBLA, DLC), `.xex` files, extracted folders, and disc images through XDVDFS. `.zar` is not supported
- it carries its magic in a footer and holds no metadata.

## License

MIT, see `LICENSE`. The shipped binary statically links third-party components, one of them
LGPL-2.1 — `THIRD-PARTY.md` lists them and explains how the relinking requirement is met.
