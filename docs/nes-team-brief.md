# NES plugins: the team brief

Read this before writing a line. Every rule here comes from a file of this repository, a measurement recorded in
`docs\`, or a decision of Mehdi's; the file is named each time so you can read the source of truth yourself. The two
plugins to copy are `src\Bsnes` (a rolling nightly from GitHub, no RetroAchievements) and `src\RaSnes9x` (a pinned build
with a vendored fallback, RetroAchievements). `src\Ares` is the model for an emulator that runs **several systems**.

Work only in this worktree (`lb-integrations-plugins-nes`, branch `nes`, from `main`). Never touch
`..\lb-integrations-plugins` (Mehdi's checkout, another branch). Each team works in `src\<Project>\` and
`src\Probe\<Project>Check.cs` only; the shared files (`src\Shared.*`, `src\Catalog`, `src\Menus`, `src\Installer`,
`deploy-dev.ps1`, `build-release.ps1`, `README.md`, `src\NixxSaves`) are changed **at integration, by the lead** (§17).
A change a team needs in shared code is asked for, not made.

---

## 0. THE RULE OF THIS ROUND: a multi-system emulator runs ALL its systems

Mehdi, for this round: an emulator that runs more than the NES must be integrated **for every platform it runs**, not
for the NES alone (his example: Mesen is not limited to the NES). Before coding, each team lists, **from the emulator's
own source or `--help`, at the exact version installed** (never from a wiki), every system it runs and maps each to a
LaunchBox platform name (§2). `src\Ares\AresSystems.cs` is the model, and the note at its top says why: "the plugin's one
list, checked by the probe against `ares --help`".

What "for every platform" means, concretely (all of it is in Ares):

- **One table** of systems (`AresSystem`: name the emulator understands, extensions, save extensions, firmware, save and
  state sub-folders) and one table of platforms (`AresPlatform`: LaunchBox name -> systems, the default first, plus
  `ByExtension`, e.g. `.fds` among NES games -> Famicom Disk System). Everything else reads these two tables: the catalogue
  rows, `IsPlatformSupported`, `CreateEmulator`, BIOS, launch, saves, the game window. No platform name spelled anywhere
  else.
- **The catalogue row** (§6) carries one `LbCatalogPlatform` per platform, each with **its own** extensions
  (`AresSystems.AllExtensions(p)`); the emulator row's `ApplicableFileExtensions` is their union.
- **`CreateEmulator`** adds one `IEmulatorPlatform` per platform (`IsDefault` only where no emulator of the library is
  already the default - §6).
- **The system on the command line**, when the emulator needs to be told (ares: `--system "<name>"`): written on each
  **platform row's** `CommandLine` (`IEmulatorPlatform.CommandLine`, public SDK, as RetroArch's cores are), at creation,
  and on a row whose line is still **empty** when the host hands the emulator over (`AresPlugin.FillPlatformLines`) - a
  line the user wrote is never rewritten. Priority at launch (memory "ares-plugin-model"): the game's own choice in its
  window > the platform row's line > the table + extension (`AresLaunch.SystemFor`). A game whose platform maps to
  nothing is refused with `LbipNotice` saying what to do (`AresPlugin.PrepareCore`), never launched as a guess.
- **The save ids carry the system** when two systems could give the same file name: `SaveIdData.Sys`
  (`src\Shared.Saves\SaveId.cs`, "The system, only for a plugin that runs several"; `AresSaves.cs` writes
  `Sys = system.Node`). Decide it BEFORE the first release: an id is kept for ever by LaunchBox, so adding `sys` later
  makes new cards beside the old ones.
- **Saves and states in per-system folders** of the emulator's install when the emulator allows it (memory
  "no-saves-beside-games": "par plateforme si possible").
- **BIOS per platform** (`AresPlugin.GetBiosFilesForPlatform` walks the platform's systems).
- **The game window** shows the options that apply to the game's system (`AresSettings.Groups`: "Super Famicom",
  "Game Boy Advance"... groups shown only for their system).
- The probe checks the table against the emulator (`src\Probe\AresCheck.cs`).

A system the Windows build does not carry is left out, with the measurement in the table's header (ares: Saturn and
Atari 5200 are in the source, not in `ares-windows-x64.zip`).

---

## 1. Ground rules of the house

- **Commits**: French message, **no AI attribution of any kind** (no Co-Authored-By, no "Claude", no "Codex") - memory
  "no-ai-attribution-in-commits". Commit on `nes` only; never push, never tag (Mehdi does it).
- **Line endings**: `.gitattributes` has `* text=auto`; the index is LF, the working tree CRLF (`git ls-files --eol`
  shows `i/lf w/crlf`), no BOM. The Write tool and Python scripts write bare LF: normalise every file you wrote to CRLF
  before committing (`sed -i 's/\r$//; s/$/\r/' <file>` in Git Bash) and check at byte level
  (`python -I -c "b=open(f,'rb').read(); print(b.count(b'\r\n'), b.count(b'\n'))"` - both numbers equal). A heredoc
  un-escapes backslashes: any script holding `\\` goes through the Write tool (memory "crlf-python-patching").
- **No account data** in the repository: no user names, ids, e-mails, numbers read off Mehdi's accounts (memory
  "no-account-data-in-repo").
- **Tests in LaunchBox are Mehdi's clicks.** Never start or drive LaunchBox (no UI Automation, no SendKeys); prepare the
  files, give short steps, verify hashes, logs, XML afterwards (memory "tests-split-lb-actions-vs-prep"). Never
  `Stop-Process` a host he is using (memory "no-kill-deploy-while-user-active").
- **Measure, don't deduce** (`docs\conception.md` §5). Every fact about an emulator in a comment carries where it was
  read (source file, version, date) or that it was measured. The code style is the existing one: a long header comment
  explaining WHY, every host entry point defensive (the host swallows exceptions silently: catch, log, degrade).
- **Only the public SDK** (`vendor\Unbroken.LaunchBox.Plugins.dll`, 14.0.1.2). LaunchBox's core is obfuscated; nothing
  of it is called by a plugin (`docs\conception.md` §2).
- Write in English in code and docs, as the rest of the repository.

---

## 2. Platform names (read in LaunchBox's own base)

Read on 18/10 in a COPY of `G:\LB1326\Metadata\LaunchBox.Metadata.db` (`python -I` + `sqlite3`). Tables:
`Emulators(Name, CommandLine, ApplicableFileExtensions, URL, BinaryFileName, NoQuotes, NoSpace, HideConsole,
FileNameOnly, AutoExtract)` and `EmulatorPlatforms(Emulator, Platform, CommandLine, ApplicableFileExtensions,
Recommended, RequiredBiosFile)`.

LaunchBox's own NES rows:

| Emulators.Name | CommandLine | ApplicableFileExtensions | URL | BinaryFileName | AutoExtract |
|---|---|---|---|---|---|
| `FCEUX` | null | `.7z; .fds; .gz; .nes; .nsf; .rar; .unf; .zip` | http://www.fceux.com/ | `fceux.exe` | 0 |
| `Nestopia` | null | `.7z; .fds; .ips; .nes; .nsf; .nst; .nsv; .rar; .sav; .unf; .unif; .ups; .xml; .zip` | http://nestopia.sourceforge.net/ | `nestopia.exe` | 0 |

| EmulatorPlatforms.Emulator | Platform | Recommended |
|---|---|---|
| `FCEUX` | `Nintendo Entertainment System` | 0 |
| `Nestopia` | `Nintendo Entertainment System` | 0 |

Both list `.fds` but **neither has a row for `Nintendo Famicom Disk System`**: ours must. No Mesen, puNES,
Nintendulator, BizHawk or Mednafen row exists (the 35 emulators of the base: 4DO, Azahar, BigPEmu, Cemu, Cxbx-Reloaded,
Demul, Dolphin, DuckStation, FCEUX, Higan, Kega Fusion, MAME, Nestopia, PCSX-Reloaded, PCSX2, PPSSPP, Project64,
Project64 1.6, RPCS3, Redream, Retroarch, Rocket Launcher, ScummVM, Sega Model 2 Emulator, Snes9x, Supermodel,
TeknoParrot, VisualBoyAdvance-M, Vita3k, Xemu, Xenia, bsnes, ePSXe, mupen64, nullDC). Our rows are published under
`Nixx-<emulator>` beside theirs, never over them (`Emulators.Name` is a primary key - `src\Catalog\LbCatalog.cs`).

Exact platform names (all verified present in `Platforms` on 18/10) - **spell them exactly**:

- `Nintendo Entertainment System` (alternates in `PlatformAlternateNames`: Famicom, FC, NES, NES/Famicom, Nintendo,
  Nintendo NES, Nintendo Entertainment System (NES))
- `Nintendo Famicom Disk System` (alternates: Famicom Disk System, Famicon Disk System, NES/Famicom...)
- For multi-system emulators: `Super Nintendo Entertainment System`, `Nintendo Satellaview`, `Nintendo Game Boy`,
  `Nintendo Game Boy Color`, `Nintendo Game Boy Advance`, `Nintendo Virtual Boy`, `Nintendo Pokemon Mini`,
  `Nintendo 64`, `Nintendo 64DD`, `Sega Master System`, `Sega Game Gear`, `Sega SG-1000`, `Sega Genesis`,
  `Sega Saturn`, `NEC TurboGrafx-16`, `NEC TurboGrafx-CD`, `PC Engine SuperGrafx`, `NEC PC-FX`, `WonderSwan`,
  `WonderSwan Color`, `ColecoVision`, `Atari Lynx`, `SNK Neo Geo Pocket`, `SNK Neo Geo Pocket Color`,
  `Sony Playstation`. Anything else: query the copy of the base, do not guess.
- LiteBox's own extra names, claimed by name too (Mehdi 17/10, `AresSystems.Platforms` and `BsnesPlugin.SnesPlatforms`):
  `Nes - Super Mario Bros. Hacks` (NES), `Snes - Super Mario World Hacks`, `Super Nintendo MSU-1` (SNES),
  `Pocket Challenge V2`, `Megadrive - Sonic The Hedgehog 2 Hacks`. The main name goes first in a list: it is the one the
  BIOS and defaults go by.

`IsPlatformSupported` compares trimmed and case-insensitive (`BsnesPlugin.IsSnes`, `AresSystems.Platform`).

The test install `G:\LB1326` has **no NES platform** in `Data\Platforms\` today: a real test needs one made by Mehdi.

---

## 3. Anatomy of a plugin

### `src\Bsnes` (2 222 lines)

| File | What it does |
|---|---|
| `Bsnes.csproj` | the project: net10.0-windows, WinForms + WPF, shared sources compiled in, Catalog referenced, the ILRepack merge step, the help page embedded (§4) |
| `manifest.json` | LaunchBox 14's manifest (§5) |
| `BsnesPlugin.cs` | the `EmulatorPlugin` class (`public partial class BsnesPlugin : EmulatorPlugin, ISystemEventsPlugin, ILbCatalogSource`): constants (platforms, extensions, `PackName`), constructor (save desk, log, row injection), `MetadataRows`/`EmulatorRows`, `OnEventRaised`, claiming (`GetApplicableEmulators`, `EnsureHotkeyScripts`, `Fill`), `IsPlatformSupported`, versions, `InstallEmulator`, `CreateEmulator`, BIOS, RetroAchievements (none), `PrepareEmulatorForLaunch` -> `PrepareCore`, path helpers (`LaunchBoxRoot`, `MakeRelativeToLaunchBox`, `ResolveFullPath`) |
| `BsnesBuild.cs` | the build installed: GitHub API of the `nightly` tag, the HEAD fallback on the direct download when the API answers 403, `Fetch` with progress and cancel, the stamp `lbip-bsnes-build.txt` beside the exe, `InstalledVersion`, `IsNewer` |
| `Archives.cs` | the emulator's zip extracted OVER the install, its single top folder stripped, path traversal refused, the user's `settings.bml` kept when present (`KeptIfPresent`) |
| `BsnesBml.cs` | editor of bsnes's `settings.bml` in place: nothing reordered, nothing dropped, new nodes after their parent; LF, no BOM, as bsnes writes it |
| `BsnesLaunch.cs` | exe name and `IsBsnesExe`, `Running(exe)` (by process main module path), `PrepareSettings` (the game's options + the pack's hotkeys into settings.bml before launch), `CommandLine` (`--fullscreen` added once / removed, Windows tokenisation), `Tokenize` |
| `BsnesAhk.cs` | the four AutoHotkey scripts (running note, Exit = `WinClose`, SaveState = F2, LoadState = F4) and `Superseded` (what earlier builds wrote, replaced) |
| `BsnesSettings.cs` | the options table (`BsnesOption`: Key, Label, Hint, Values, Texts, Default) and the ini store: every game's in `<Local\Plugins>\.data\<PluginId>\settings.ini`, a game's own in `games\<game id>.ini` beside it; `For(gameId, key)` = game's, else every game's, else default |
| `BsnesSettingsPage.cs` | the Nixx window's page: `public static class Settings` (contract by name, §12) + the `UserControl` |
| `BsnesGameMenu.cs` | the right-click entry and the per-game window (one game or a selection, `LbipSelection`, `OptionMarks`, `NixxShell.Dress`) + `public static class GameMenu` (contract by name) |
| `BsnesSaveDesk.cs` | "GENERATED" overrides handing every save call to the front desk (`SaveDesk`), and `IOwnSaves` defaults for the two calls with no code of its own |
| `BsnesSaves.cs` | the plugin's own save code (`IOwnSaves.Own*`): folders read from settings.bml, listing `.srm` (+ `.rtc`/`.psr` companions) and `.bsz` (all states in one zip), ids, restore, delete, `OwnExpectedDir`, `ExecutableOf` |
| `BsnesSavePaths.cs` | lot 9, nothing beside the game: bsnes's empty `Path/*` nodes set to `<bsnes>\Saves\`, `\States\`, `\Cheats\`, `\Screenshots\`; files already beside the game moved (own) or copied (generic) by `LbipBesideGame`'s rule |
| `BsnesStateThumbs.cs` | the `.bsz.png` thumbnail decoded from bsnes's own preview in the state (RLE<2>, 15-bit), refreshed at listing and after the session |
| `Log.cs` | the log (§15) |

### `src\RaSnes9x` (2 996 lines) - what differs

| File | What it does |
|---|---|
| `RaSnes9xBuild.cs` | ONE pinned build (1.2), two sources: retroachievements.org first (browser User-Agent), then the copy in `vendor\rasnes9x\` on GitHub; **SHA-256 checked before extraction**; the exe recognised by MD5 (its version resource says Snes9x's), plus the stamp |
| `Archives.cs` | flat zip extracted over; also a game's `.7z` unpacked (Snes9x cannot open it) |
| `RaSnes9xConf.cs` | editor of `snes9x.conf` (sections, aligned `Key:Name = VALUE # comment`, CRLF) in place |
| `RaSnes9xLaunch.cs` | options and the pack's free hotkeys into snes9x.conf (and unbinding a default that would fire on the same key), empty save folders forced to `.\Saves`, `.7z` -> `RomFor` / `SwapRom` |
| `RaSnes9xCheevos.cs` | RetroAchievements login written to `RAPrefs_RASnes9X.cfg` (other keys kept, token never logged), `HasToolset` |
| `RaSnes9xToolset.cs` | `RA_Integration.dll` fetched exactly as the emulator would (same User-Agent, same request), short timeouts, never fails nor holds a launch |
| `RaSnes9xLateLoad.cs` | measured race: the ROM loaded from the line before RA's login ends gets no achievements; so the ROM goes first in the recent games and is loaded by a menu command once RALog says the login ended |
| `RaSnes9xSaves.cs` | `.srm` (+ `.rtc`) and **one row per slot** `.000`-`.009` (`Slot` in the id, `GetPotentialSaveSlots` = 0..9, `SlotRestore.Choose`) |
| `RaSnes9xStateThumbs.cs` | `.000.png` from the `SHO` block of the gzip state |
| the rest | same roles as Bsnes's (`Ahk`, `GameMenu`, `SaveDesk`, `Settings`, `SettingsPage`, `Log`, `manifest.json`, `.csproj`) |

Your plugin has the same files, `<Project>` in place of `Bsnes`. A multi-system emulator adds `<Project>Systems.cs`
(Ares). A BIOS need adds `<Project>Firmware.cs` (Ares, using `LbipBiosImport`).

---

## 4. The `.csproj` and the merge

Copy `src\Bsnes\Bsnes.csproj` and change only `AssemblyName`, `RootNamespace` (`LbIntegrations.<AssemblyName>` - the
menus and settings contracts are found by that exact name, §12), the comments, and the help resource. What it holds and
must keep:

- `net10.0-windows` (LaunchBox 14 runs .NET 10; the vendored SDK references System.Runtime 10), `Nullable`/`ImplicitUsings`
  disabled, `AppendTargetFrameworkToOutputPath=false`, `CopyLocalLockFileAssemblies=true`, `MSB3277` as message,
  `UseWindowsForms` + `UseWPF` (Shared.Lbip's import watch is WPF).
- `<ProjectReference Include="..\Catalog\Catalog.csproj" />` - referenced and SHIPPED, never merged.
- `<Compile Include="..\Shared.Lbip\**\*.cs" />`, `Shared.Saves`, `Shared.Compat`, `Shared.Menu`, `Shared.Ui` -
  compiled in (each plugin has its own copy, so a static is NOT shared across the pack: memory
  "lb14-plugin-direct-base-only"). Add `Shared.Disc`/`Shared.RamDisk`/... only if needed, and say so.
- `Lib.Harmony 2.4.2` and `SharpCompress 0.41.0` - **the same versions as every other plugin**.
- The SDK as `<Reference ... HintPath="..\..\vendor\Unbroken.LaunchBox.Plugins.dll"><Private>false</Private>` - the host
  provides it.
- The `MergePlugin` target (Release only): `dotnet ilrepack /internalize` folds every dependency except
  `LbIntegrations.Catalog.dll` into `bin\Release\merged\<Assembly>.dll`. Needs `dotnet tool restore` once (done in this
  worktree). That merged file is what deploy and release ship (`docs\conception.md` §7, `README.md` "Building").
- `<EmbeddedResource Include="..\..\docs\help\<emu>.html" LogicalName="help.html" />` - the user guide shown from the
  Nixx window (`src\Menus\Settings.cs` looks for a resource named `help.html`). Start from `docs\help\_template.html`.

Build: `dotnet build src\<Project>\<Project>.csproj -c Release`. The Catalog project is built once already in this
worktree (`src\Catalog\bin\Release\LbIntegrations.Catalog.dll`).

**The assembly holds exactly ONE public type deriving from `EmulatorPlugin`, and it derives from it DIRECTLY**: with an
abstract base in between, LaunchBox 14 loaded the DLL and constructed nothing, silently (measured 08/10, memory
"lb14-plugin-direct-base-only"). Share code by composition (the SaveDesk pattern).

**Never declare a menu interface (`IGameMenuItemPlugin`...) in a plugin**: LaunchBox 14 gives a `Local\Plugins` plugin
its emulator role only, and LiteBox would show the entry twice (memory "lb14-local-plugins-emulator-role-only").

---

## 5. `manifest.json`

Copy `src\Bsnes\manifest.json`:

- `SchemaVersion` 1, `UserId` 0, `OperatingSystemFamily` "Windows", `SourceKind` **"Local"** (must match the root it sits
  in, `Local\Plugins`, or the core refuses it - `deploy-dev.ps1`).
- `PluginId`: **a new GUID per plugin**, never changed afterwards (LaunchBox 14 names the data folder after it; the
  settings live in `Local\Plugins\.data\<PluginId>\`). The same GUID goes into `<Project>Settings.PluginId`.
- `Name`: `Nixx-<emulator>` - the folder name, the catalogue row's `Name`, `EmulatorName` and the entry's `Title` are all
  this string (memory "lb-integrations-plugins").
- `Developer` "nixxou", `Description` (one sentence: "Download, update, launch and save integration for ..."), `Credits`
  (thank the emulator's authors), `HomePageUrl` the emulator's project, `SourceCodeUrl`/`SupportUrl` this repository,
  `TipJarUrl` "".
- `InstalledVersion`: the pack's version, today **"1.2.2"** (all 15 manifests carry it; a release bumps them all -
  memory "release-procedure").
- `Tags`: `["5aae585f-ffc5-42bc-9f95-e5e190ef99e2"]`, the same as every other plugin.

LaunchBox 14 loads nothing without a manifest and says nothing (`build-release.ps1`, step 2). Nothing else is ever
written into the plugin folder (it is checked against the manifest): logs and caches go to `%LOCALAPPDATA%` (§15).

---

## 6. The emulator row, the platform rows, claiming

### The catalogue row (`src\Catalog\LbCatalog.cs`, `src\Shared.Lbip\LbipRowInjection.cs`)

The plugin class implements `ILbCatalogSource` and builds ONE `LbCatalogEmulator` (`MetadataRows`, returned by
`EmulatorRows()` too): `Name` = PackName, `CommandLine` (the default; usually "" - the host appends the quoted game),
`ApplicableFileExtensions` (`".nes; .fds; .zip"` - each with its dot, `"; "`), `Url`, `BinaryFileName` (wildcards
allowed), `AutoExtract` (false when the emulator opens archives itself: the archive's name is then the save's name), and
one `LbCatalogPlatform { Platform, ApplicableFileExtensions, Recommended = false, RequiredBiosFile }` per platform.

Two ways it reaches a host:
- **LiteBox** sets `LbCatalog.HostWillAsk = true` before constructing any plugin and reads `EmulatorRows()` itself.
- **LaunchBox** has no door: `LbipRowInjection.Install("com.nixxou.lbip.<id>", MetadataRows())` - called in the
  constructor AND in `OnEventRaised` (`PluginInitialized`, `LaunchBoxStartupCompleted`, `BigBoxStartupCompleted`) -
  publishes the rows into an AppDomain-wide list; the first plugin of the pack to arrive installs ONE pair of Harmony
  postfixes on `SqliteCommand.ExecuteDbDataReader[Async]` that re-run LaunchBox's query on an in-memory base holding our
  rows and append the result (tables `Emulators`, `EmulatorPlatforms` - `LbipRows.TableNames`). Read the header of
  `LbipRowInjection.cs`: why both methods, why one patch for the pack, why reflection for the types.

The Catalog assembly is pinned at 1.0.0.0 and has **no behaviour**; its types are shared by the whole process
(`LbCatalog`, `LbEmulatorOpened`, `LbEmulatorRedirect`, `LbImportFinished`, `LbSaveOwners`, `LbPersistedSaves`). Never
change it from a team; a member is only ever ADDED, at integration.

### Recommended: never

`IsPlatformSupported` returns `new EmulatorSupportResponse(supported, false)` and every `LbCatalogPlatform` has
`Recommended = false` (Mehdi 17/10: LaunchBox pushes a recommended emulator, in the import wizard too, not installed
yet).

### The entry an install creates (`BsnesPlugin.CreateEmulator`, Compat "emulatorEntry")

`PluginHelper.DataManager.AddNewEmulator()`, `Title` = PackName, `ApplicationPath` relative to LaunchBox's root
(`MakeRelativeToLaunchBox`), `CommandLine`, hotkey scripts (`EnsureHotkeyScripts`), one `AddNewEmulatorPlatform()` per
platform with `IsDefault = !taken.Contains(name)` (taken = platforms some emulator is already default for). **Do NOT set
`DefaultPlatform`** (see `XeniaPlugin.CreateEmulator`). `dm.Save(false)` in a try. The pack never writes LaunchBox's base
nor its XML itself.

**Never remove or un-default an existing platform row** of an emulator entry, even an unknown or odd-looking one: only
ADD the missing ones of yours (Mehdi 30/09, memory "never-touch-existing-platform-rows": custom platforms exist). A probe
check asserts the others keep their tick.

### Claiming

`GetApplicableEmulators` claims an emulator **by its `ApplicationPath`'s file name** (`BsnesLaunch.IsBsnesExe`),
never by its title, and fills its empty fields (`EnsureHotkeyScripts`, ares's `FillPlatformLines`) only when
`Compat.On("emulatorFields")`. The `Fill` rule: set a field only when it is empty or is, word for word, one an earlier
build wrote (`BsnesAhk.Superseded`); a field the user changed is never touched. The claim log line is written only when
the claimed set changes (`_lastClaimed`) - the host asks often.

**Exe-name collisions**: if your emulator's exe name is shared by another emulator (another of the six, ares, bsnes, or
two major versions of the same project), the file name alone is not enough - tell them apart (version resource, MD5 as
RaSnes9x does, a file only one of them ships) and say how in the header. Two plugins claiming one entry is a defect.

---

## 7. Install, update, version

### The three calls

- `GetCurrentVersion(path)` -> the version **of an install of ours**, else null: the stamp `lbip-<emu>-build.txt` the
  install wrote beside the exe (`BsnesBuild.Stamp/Stamped`), or the exe's MD5 for a pinned build (`RaSnes9xBuild`).
- `GetInstallableVersions()` -> `new EmulatorControllerVersion(identifier, label, fileName)`; null when the source is
  unreachable (Bsnes).
- `IsUpdateAvailable(path, out version)` -> **three answers** (`DemulPlugin.IsUpdateAvailable`, "THE DOWNLOAD BUTTON OF
  ADD EMULATOR IS THIS", measured 24/09): an **empty path** is the Add Emulator window's Download button - **true**
  (the button shows only on true); installed but **not by us** (current version null) - **false**, it is the user's;
  installed by us and older - **true**. The SDK's default is wrong (declares an update whenever `GetCurrentVersion` is
  null - `docs\conception.md` §1): always override. Catch, log, false.

### Getting the build

- **GitHub's API allows 60 requests an hour per address** (measured 06/10: 403 "rate limit exceeded"). Always a fallback:
  - a rolling tag (Bsnes): HEAD on the stable direct download URL, its `Last-Modified` is the asset's date
    (`BsnesBuild.FromDownload`);
  - numbered releases (`AresBuild`, `ZincExBuild.FromRedirect`): HEAD on `/releases/latest` or
    `/releases/latest/download/<asset>` and read the tag from the redirect's `Location`; with the API, check the asset's
    `digest` (`sha256:...`).
  - Cache the answer (Bsnes: 10 minutes).
- **A pinned build** (RaSnes9x, Demul): the official URL first, the copy in `vendor\<emu>\` (with a `SOURCE.md`) second,
  SHA-256 checked before anything is extracted. `deploy-dev.ps1` copies vendored archives to
  `%LOCALAPPDATA%\lb-integrations-plugins\dev` so a branch can be tried before it is pushed (the `Demul`/`RaSnes9x` block)
  - a new vendored plugin is added there at integration.
- `HttpClient` with a User-Agent (`lb-integrations-plugins/1.0`), a timeout, redirects on; `Fetch` streams to a temp
  file with progress (`args.ReportProgressAction`) and cancel (`args.ShouldCancelFunc`), the temp file deleted in
  `finally`.

### `InstallEmulator`

Reinstall = `args.ExistingEmulator` set: install into its folder, update only its `ApplicationPath`. New install:
`<LaunchBox root>\Emulators\<PackName>`. Refuse while the emulator runs from there (`Running(exe)`). Extract **over**
the folder, never after clearing it, and keep the user's config files when present (`Archives.KeptIfPresent`). Check the
exe is there, stamp, set the save folders (lot 9, §11), then create the entry unless `!args.ShouldCreateEmulator`.
Every failure is an `EmulatorInstallResponse(message)` a user can read.

Paths: the plugin's root is found once from the DLL's folder (`LaunchBoxRoot`, walking up to a folder holding `Core`
and `Data`); a relative path from the library is ALWAYS resolved against it (`ResolveFullPath`), never against the
current directory (LaunchBox sets it to its root, the probe and LiteBox do not - memory "lb-cwd-is-root"). Emulators
are started with their exe's folder as current directory.

---

## 8. BIOS

Override both `GetBiosFilesForPlatform(platform)` and `(appPath, platform, commandLine)` (the host calls the second, with
the **platform's** command line). `new EmulatorBiosFile(folder, fileName, required, description, ...)` - argument order
as in `AresPlugin.GetBiosFilesForPlatform`. When the
emulator needs one (the Famicom Disk System needs its BIOS, `disksys.rom` in most emulators - check yours), find it
where the user already has it and copy it in: `LbipBiosImport.Import` (RetroArch's `system\`, the other emulators'
`bios`/`system`/`firmware` folders, by name, only what is missing, never a move) - `AresFirmware.cs` is the model. A
launch that cannot work without it is refused with `LbipNotice` naming the file (`AresPlugin.PrepareCore`).

## 9. RetroAchievements (only if the emulator has them)

`SupportsRetroAchievements` reports user, whether a token is there (never its value: `token: hasToken ? "" : null`),
hardcore; `InjectRetroAchievementsCredentials` writes the login where the emulator reads it, keeping its other keys;
`PrepareCore` writes `args.RetroAchievementCredentials` at each launch with `EmulatorBeingLaunched.EnableHardcoreAchievements`.
Never log a token. Model: `RaSnes9xCheevos.cs`; read the late-load race of `RaSnes9xLateLoad.cs` and measure whether your
emulator has it. No RA: `new RetroAchievementSupportResponse(isSupported: false)`.

---

## 10. Launch

- `PrepareEmulatorForLaunch(args) => LbipLaunchGate.Run(PackName, args, PrepareCore)`: one launch at a time per plugin,
  a double click refused silently, a launch into an emulator already open outside ours refused with a message
  (`src\Shared.Lbip\LbipLaunchGate.cs`).
- `PrepareCore`: read exe / game id / rom / platform / title each in its own try; not our exe -> `success: true` and
  nothing. Warn when the emulator already runs (what is written now is lost when it quits).
- **The emulator's own config file is written BEFORE the launch** when it rewrites it whole on quit (bsnes's
  settings.bml, snes9x.conf): an edited-in-place editor (`BsnesBml`, `RaSnes9xConf`) that reorders nothing and drops
  nothing, writes only when changed, in the file's own encoding and line endings (ares reads a CRLF settings.bml as
  empty - memory "ares-plugin-model"). Prefer per-game command-line switches when the emulator has them (ares
  `--setting`). Never write while it runs.
- **The command line**: start from `args.CurrentCommandLine` (the host's, platform line + quoted game), add the plugin's
  switches before the ROM, never twice, tokenised as Windows does (`BsnesLaunch.CommandLine/Tokenize`); return
  `new PrepareForLaunchResponse(success: true) { NewCommandLine = line }` only when it changed. Log the line.
- **Full screen** is an option of the plugin (`Fullscreen`, default full screen), passed by the emulator's switch.
- **Archives**: what the emulator opens itself is passed as is (`AutoExtract = false`). What it cannot open (`.7z` for
  Snes9x) is unpacked by the plugin under the archive's own name, into a STABLE folder of the install
  (`<emu>\Unpacked\<name>-<hash>\`, `AresLaunch.Unpacked` - never `%TEMP%`, the emulator writes its saves beside it), and
  the line pointed at it (`RaSnes9xLaunch.RomFor/SwapRom`). An archive that cannot be played refuses the launch
  (`LbipNotice.Show`, `success: false`). The Compat switch "autoExtract" forces LaunchBox's own option for a few
  emulators - say at integration if yours needs it.
- **AutoHotkey** (`<Project>Ahk.cs`, written by `EnsureHotkeyScripts` into `AutoHotkeyScript`, `ExitAutoHotkeyScript`,
  `SaveStateAutoHotkeyScript`, `LoadStateAutoHotkeyScript`): the pack's keys (Mehdi 07/10) - **F2 save state, F4 load
  state, F6/F7 slot down/up, F11 full screen, Esc quit** - bound in the emulator only when the binding is empty (or an
  earlier build's) and no other hotkey or pad button holds the key (`BsnesLaunch.PrepareSettings`, `RaSnes9xLaunch`).
  The Exit script closes the window (`WinClose, ahk_exe <exe>`, WM_CLOSE) so the emulator writes its save; never Alt+F4
  to whatever has the focus. An emulator that quits on another key than Esc uses `LbipAhk.EscapeScript` (Esc mapped only
  in the emulator's window, the script ends itself once the emulator is gone - memory "melonds-hotkeys-not-pack-standard"
  for a case where the pack's keys are impossible). Keep a `Superseded` list whenever you change a script.

---

## 11. Saves

Read first: `docs\saves-lb14-routing.md` (§1 what LaunchBox does, §3 the ids, §4b the plan), `docs\saves-lot9.md`
(nothing beside the game), `docs\saves-two-installs.md`.

### The front desk (`src\Shared.Saves\SaveDesk.cs`)

- Constructor, **first line**: `_desk = new SaveDesk(this, this, SaveEmu, SaveLog);` - it registers the plugin as the
  owner of its `emu` for the other plugins (`LbSaveOwners`).
- `<Project>SaveDesk.cs` is a copy of `BsnesSaveDesk.cs` with the names changed: every save override goes to `_desk`;
  the plugin's own code implements `IOwnSaves` explicitly (`OwnGetSaves`, `OwnIsSaveActive`, `OwnIsSaveContainer`,
  `OwnUseSaveGroupIdForPersistedMatch`, `OwnTryComputeSaveSignature`, `OwnTryBackupSave`, `OwnTryGetSaveGroupInfo`,
  `OwnIsSecondarySaveFile`, `OwnGetCompanionSaveFiles`, `OwnAddSaveFile`, `OwnRemoveSave`, `OwnExpectedDir`).
- Why: **LaunchBox 14 calls `GetSaves` only on the plugin of the game's emulator** (assigned, else the platform's
  default), at listing AND at close; Launch With changes nothing (measured 07/10, memory "lb14-saves-only-game-emulator").
  The desk routes rows that are not the plugin's to their owner and adds the other emulators' saves of the game.
- `SupportsSaveManagement() => true`.

### The ids (`src\Shared.Saves\SaveId.cs`)

`SaveId.Write(new SaveIdData { Emu = "<emu>", Kind = "save"|"state"|..., Sys = <system, multi-system only>, By = "name",
Id = <rom name without extension>, Slot = <n, per-slot states only> })` - JSON, fixed key order, no `#` nor `|`. An
**identity only**: stable forever, nothing that can change (not the game, not the version - the row carries `GameId` /
`AdditionalApplicationId`). `emu` is one lower-case value per plugin, never reused. **`inst` is added by the desk itself**
(`InstallIds.Apply`, CRC32 of the exe's folder, Compat "twoInstalls"): two installs of one plugin get two cards - do not
write it yourself. `SaveRows.IsOurs(id, Emu)` recognises your rows (cheap: `IsSaveActive` is called hundreds of times).

### Listing (`BsnesSaves.OwnGetSaves`)

- Walk `args.AdditionalApplications` (tied with `SaveRows.TieTo` - a version on its game's own file is not tied) then
  `args.Games`, dedupe by path + context.
- Folders from the emulator's config, relative ones resolved from the exe's folder (`ExpectedDir.OfConfig`).
- At LaunchBox's start ONE call carries all games (2 003 measured): use `SaveScan.Exists/Files/Memo` and
  `StampedCache.Get` for parsed config files; call `SaveScan.Touched(path)` after writing during a listing.
- Rows: `GameSaveGame` (cartridge) / `GameSaveState` with `FileLocation`, `IsDirectory = false`, `OriginalFileName`,
  `SaveGroupId`, `SaveGroupName`, `DisplayChipText`, size, last write UTC, and `Slot` for a slotted state.
- States: one row per slot when the emulator keeps one file per slot (RaSnes9x `.000`-`.009`: `GetPotentialSaveSlots`
  returns 0..9, the restore goes through `SlotRestore.Choose`); one file for all slots (bsnes `.bsz`): no own slots,
  `GetPotentialSaveSlots => SaveDesk.ForeignSlots()`.
- Companions: `OwnGetCompanionSaveFiles` names the files that travel with a save (`.rtc` clock, the state's `.png`);
  `OwnIsSecondarySaveFile` says a companion is not a save of its own. A whole-folder save (a console's memory) is a
  container (`IsSaveContainer`, memory "save-container-vs-file").

### Restore and delete (`OwnAddSaveFile`, `OwnRemoveSave`)

`SaveRows.TargetFor(save, dm, Full)` (the row's version or game, its CURRENT path; refused, never redirected, when the
version or game is gone), the folders set and the game's files migrated first (as a launch does), the name of the rom
TODAY + the backup's extension, refused while the emulator runs, `SlotRestore.MayOverwrite` (asks before replacing
another card's live file LaunchBox will not back up), `SaveCompanions.WhyNotWritable` then `RestoreSet` (save and
companions all or nothing - LaunchBox copies no companion back), thumbnail restored or rebuilt. The install a restore
targets: `SaveForward.ViaEmulator(IsXExe)` first, then the game's emulator, then the first install of ours
(`BsnesSaves.ExecutableOf`). Delete removes the file and its thumbnail; a row not ours -> `base.RemoveSave`.

### Nothing beside the game (lot 9, memory "no-saves-beside-games")

Mehdi: saves, states, cheats, screenshots in folders of the emulator's install (`<emu>\Saves\...`, per system if
possible), never beside the game. At install and at each launch (not while it runs): every EMPTY folder setting of the
emulator set to the install's folder (a folder the user chose is kept, except one the pack set that no longer points
into this install - `LbipBesideGame.IsStalePackPath`); **note the moment BEFORE changing the setting**
(`LbipBesideGame.Note`, only when the setting was present and empty), then bring the files already beside the game:
the emulator's OWN files moved, GENERIC names (`.srm`, `.sav`, `.cht`... written by other emulators too) COPIED only
when last written before that moment (`LbipBesideGame.WrittenBefore/Bring`), never over an existing file
(`BsnesSavePaths.Ensure/Migrate`). An emulator with no folder setting writes beside the game whatever we do: say it,
measure it, ask.

### Thumbnails (memory "state-thumbnails-model")

`<state file>.png` beside each state (`LbipStateThumbs.PathOf`), a companion of the state. The emulator's OWN picture
first (decoded from the state: `BsnesStateThumbs`, `RaSnes9xStateThumbs`), else a window capture while the emulator
runs (`LbipStateThumbs.Watch`, as ares, melonDS) - an all-black capture is dropped, no thumbnail rather than a wrong one.
Refreshed at listing and once the emulator has quit (`AfterSession`).

---

## 12. Settings page and the game's window

- **Store**: `<Project>Settings.cs` as Bsnes's - an options table, `settings.ini` for every game and
  `games\<game id>.ini` per game under `Local\Plugins\.data\<PluginId>\`, read at use (never cached), `PathOverride` for
  the probe. A new option defaults to "as the emulator has it" (nothing written or passed).
- **The Nixx window's page** - contract BY NAME (`src\Menus\Settings.cs`): `public static class
  LbIntegrations.<Assembly>.Settings { string Title; Control CreatePage(); string Save(Control page); string[]
  Emulators(); string OpenEmulator(string path) }` (the last two optional: `LbipOpenEmulator.Find(IsXExe)` / `.Open`).
  **Keep it simple** (Mehdi 06/10, memory "nixx-page-simple-per-game-rich"): the install and its version, BIOS, screen;
  general settings stay the emulator's own. `LbipHint.Attach/Note` for explanations.
- **The right-click entry** - contract BY NAME (`src\Menus\Menus.cs`, the relay `Nixx-Menus` in the classic `Plugins\`
  root finds it among loaded assemblies): `public static class LbIntegrations.<Assembly>.GameMenu { string[]
  Entries(IGame[] games); void Selected(string entry, IGame[] games); Image Icon { get; } }`. Caption
  `"Nixx-<emu> : Options..."`. `IsOurs(game)` in memory only: its emulator is ours, or its platform is ours and an
  install of ours exists (BsnesGameMenu.IsOurs). Not in BigBox.
- **The per-game window** (`BsnesGameMenu.cs`, rich model `SuperZsnesSettingsPage.cs`): one game or a selection - the
  pack's rule (memory "multi-game-window-rule", `src\Shared.Menu\LbipSelection.cs`): "All selected games" first, then
  groups of identical options; a value they disagree on shows "Various (n of N)" with the purple bar; OK confirms over
  several games and writes only what changed (`LbipSelection.MergeFor`); "As every game (<value>)" / "As set in
  <emulator> (<value>)" read from the emulator's config; `OptionMarks.Legend/ResetButton/Bottom` at the bottom (bar
  colours: blue set here, amber game's own config in the emulator, grey emulator, red set here but loses, purple
  various); `NixxShell.Dress(this)`. Short texts. When options go on the command line: show live at the bottom "N
  option(s) added to the command line, before the ROM path:" + the line (SuperZsnesSettingsPage). A multi-system
  emulator shows only the groups of the game's system.

## 13. Menu icon

`public static Image Icon => LbipMenuIcon.Of(IsXExe, p => p)` (`src\Shared.Menu\LbipMenuIcon.cs`): the emulator's own exe
icon, from the first install of ours in the library; never null. No picture shipped.

## 14. Help page

`docs\help\<emu>.html` from `_template.html`, embedded as `help.html` (§4): what the plugin does, keys, where saves go,
what is not supported. Written for a user, in English.

## 15. Logging

Copy `src\Bsnes\Log.cs` (change the prefix and the file name): `%LOCALAPPDATA%\lb-integrations-plugins\<emu>.log`,
truncated once past 512 KB, `Info`, `Warn(message, ex)`, `Verbose` only with a `trace` marker file beside the log,
`Disabled(marker)` for switches. NEVER beside the assembly (the plugin folder is checked against its manifest). Wire it
in the constructor: `LbipLog.Use(Log.Info, Log.Warn, Log.Disabled, () => Log.Tracing);`. First line of the constructor
after the desk: `"plugin constructed, assembly ..."` - the first thing to look for when a plugin "does nothing" under
LaunchBox. Never log a token or a password.

## 16. The probe (`src\Probe`)

The probe is a host stand-in: it loads a plugin DLL by path (no project reference), finds its `EmulatorPlugin`, and
calls the read-only half: `dotnet run --project src\Probe -- <path>\bin\Release\merged\<X>.dll --emu <exe> --platform
"<name>"` (versions and IsUpdateAvailable hit the network; nothing that installs or writes).

Each team adds `src\Probe\<Project>Check.cs` on the model of `BsnesCheck.cs` / `RaSnes9xCheck.cs` / `AresCheck.cs`:
static methods taking the plugin `Assembly`, reaching internals by reflection (`asm.GetType("LbIntegrations.<X>.<Type>",
true)`, `BindingFlags.NonPublic | Static`), `[ok]/[FAIL]` lines, return bool; working on COPIES under `%TEMP%`
(config editing, saves listing, extraction of the real archive, latest-version read, the systems table against the
emulator's `--help`, platform rows only added). The lead wires them into `Program.cs` (`if (Has(args, "--<emu>-...")) ...`,
near the `--bsnes-*` lines) at integration - give the exact lines in your report. `--e2e-launch <dll> <exe>`
(`SnesFamilyE2E.cs`) runs a launch as LaunchBox makes it; it special-cases ares, so a multi-system plugin may need a branch
there (ask). `--saves-selftest` must stay green.

---

## 17. Integration (the lead's part - so you know what your report must give)

For each plugin, in this order:

1. `deploy-dev.ps1`: a row in `$Pack` (`'<Project>' = @{ Folder = 'Nixx-<emu>'; Old = @() }`) and the name in the `-All`
   list; the vendored dev copy block if the plugin has a pinned archive.
2. `build-release.ps1`: a row in `$Stage`.
3. `src\Installer\Payload.cs`: a `const string` folder name, the `Folders` array, `Assemblies` (`"<Project>.dll"`), three
   `Files` lines (dll, manifest, `Contract`). `src\Installer\Installer.csproj`: two `EmbeddedResource` (dll and manifest,
   `LogicalName payload/Nixx-<emu>/...`). `src\Installer\InstallerForm.cs`: the `What()` line (the platforms in a few
   words).
4. `src\NixxSaves\CatchAllClaims.cs`: the exe names in `Names` (else the catch-all also claims the emulator).
5. `src\Probe\Program.cs`: the check lines.
6. `README.md`: the table row; `THIRD-PARTY.md` if anything new is merged; `docs\help\<emu>.html`.
7. `src\Shared.Compat\CompatRegistry.cs`: the `Does` texts that list plugins, when yours uses that switch.
8. Build all Release, `--saves-selftest`, deploy.

Deploy pitfalls: `deploy-dev.ps1` verifies the DLL BY HASH and never stops a host; it ALSO rebuilds and overwrites
`G:\LB1326\Plugins\Nixx-Menus\NixxMenus.dll` and realigns `LbIntegrations.Catalog.dll` in EVERY `Nixx-*` folder - from
this worktree that can regress what Mehdi's branch deployed: compare `src\Menus` and `src\Catalog` with his branch first
and restore his `NixxMenus.dll` after (memory "lbip-deploy-from-worktree"). Never pipe its output into `select -First`
(PowerShell stops the script - memory "deploy-verify-by-hash"). A deploy waits for LaunchBox to be closed by Mehdi.

---

## 18. Pitfalls recorded (read before you hit them)

- **Saves go to the game's emulator's plugin only** (LB14, measured 07/10); a version on its own path makes LaunchBox ask
  the VERSION's emulator at close (`docs\saves-lot5.md` §5).
- **A known file keeps its old id**: LaunchBox finds a listed file by its PATH first, by id only for a new file - so an id
  change hits only new cards (`docs\saves-two-installs.md` §1). Get the id right before the first release.
- **Two installs of one plugin** share ids without `inst`; the desk adds it - don't bypass the desk
  (`docs\saves-two-installs.md`). `SaveRouter.EmulatorFor` once took the first install at equal exe names: whatever
  finds "the install" must not stop at the exe's name.
- **LaunchBox writes the asked emulator's core on the rows it creates**: never identify a save by `EmulatorCore`
  (memory "lb-writes-asking-emu-core").
- **LaunchBox 14 Backup/Restore defects** ("Can't find the game" on a save tied to a version): memory
  "lb14-save-restore-bugs", `SaveRows.TieTo` avoids the known case.
- **Restore companions**: LaunchBox copies them into the vault but not back (`SaveCompanions.cs`).
- **A restored state into another slot loses the state there with no copy** (`SlotRestore.cs`, `docs\saves-lb14-routing.md`
  §5).
- **Harmony**: each plugin merges its own copy; two copies patching one method erase each other's hooks silently
  (memory "harmony-copies-overwrite"). A plugin of this round patches nothing; if one must, it is discussed first.
- **Statics are per plugin**, not per pack (shared sources are compiled into each).
- **Native DLLs** never sit in the plugin folder as `.dll` (LaunchBox loads every `.dll` as an assembly):
  `native\<name>.native` (`deploy-dev.ps1`, melonDS/Vita3K blocks).
- **Make Default of LaunchBox 14** overwrites the game with the version (memory "lb14-make-default-bug") - do not rely
  on a version's path surviving it.
- **LaunchBox 14.1** (betas, `docs\lb141-plan.md`) wraps v1 plugins; stay on the 14.0.1 public SDK, no core access.
- **CRLF** (§1) and **no AI attribution** (§1).

## 19. What each team returns

The project folder, its probe check, the help page, a short report: the emulator's version and exe name, the systems ->
platforms table with where each fact was read, the BIOS, what was measured on the real emulator and what was not, the
exact lines for §17, and the open questions for Mehdi.
