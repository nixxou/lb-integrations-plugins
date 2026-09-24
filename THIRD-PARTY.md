# Third-party components

Each plugin is built by merging its dependencies into a single assembly with ILRepack
`/internalize`. The components below are therefore **statically linked** into the shipped file, and
their licences apply to it.

The merge is not cosmetic. A LaunchBox plugin folder is a shared namespace: the host resolves a
dependency by simple name across every plugin's folder, so two plugins carrying different versions
of the same library is a real hazard. After the merge the assembly's reference table holds exactly
one non-framework entry — `Unbroken.LaunchBox.Plugins` — so there is nothing left to collide.

## Merged into `Xenia.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading the release archive - canary's Windows asset became a `.7z` in June 2026 |

## Under its own licence inside `Xenia.dll`

`src/Xenia/Xdvdfs.cs` is **MPL-2.0**, derived from `sigil/src/xdvdfs.c` in
[argosy-launcher](https://github.com/abduznik/Freegosy). Its structure, its two-magic validation and
its partition-base probing order are sigil's work; the C# is ours. MPL-2.0 is file-scoped copyleft:
that one file stays MPL and its source is in this repository, which is what the licence asks.

## Merged into `NoGba.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading the release archive, and reading a game archive's entry name - no$gba cannot open an archive itself |
| [Harmony](https://github.com/pardeike/Harmony) | MIT | describing no$gba to LaunchBox, whose metadata database has no row for it |

## Merged into `Flycast.dll`

| Component | Licence | Used for |
|---|---|---|
| [CHDSharp](https://github.com/purelogiccode/CHDSharp) | MIT | reading `.chd` disc images |
| VendoredLZMA (LZMA SDK) | public domain | a CHD codec |
| VendoredZLib | zlib | a CHD codec |
| VendoredZSTD | MIT | a CHD codec |
| **VendoredFlac** | **LGPL-2.1** | a CHD codec - see below |
| Microsoft.Extensions.Logging.Abstractions | MIT | a CHDSharp dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | idem |
| System.Diagnostics.DiagnosticSource | MIT | idem |
| System.IO.Hashing | MIT | idem |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the two postfixes that let LaunchBox read our emulator rows |

Dreamcast discs are distributed as `.chd` as often as `.gdi`, and the disc id lives inside the
image, so reading CHD is not optional here. Flycast's release archive is a plain `.zip`, so unlike
the Xenia plugin this one needs no SharpCompress - `System.IO.Compression` reads it.

## Merged into `MelonDs.dll`

| Component | Licence | Used for |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | MIT | reading a game archive - melonDS loads a DS ROM out of a `.zip`, `.7z` or `.rar`, and the save is named after the entry inside it |
| ZstdSharp | MIT | a SharpCompress dependency |
| [Lib.Harmony](https://github.com/pardeike/Harmony) | MIT | the two postfixes that let LaunchBox read our emulator rows |

The version of Harmony is pinned to 2.4.2 across every plugin that uses it, and to what ExtendDB
ships: `HarmonySharedState` is found by module name across assemblies, so two different versions in
one process would each keep their own state.

## Merged into `Ppsspp.dll`

| Component | Licence | Used for |
|---|---|---|
| [CHDSharp](https://github.com/purelogiccode/CHDSharp) | MIT | reading `.chd` disc images |
| VendoredLZMA (LZMA SDK) | public domain | a CHD codec |
| VendoredZLib | zlib | a CHD codec |
| VendoredZSTD | MIT | a CHD codec |
| **VendoredFlac** | **LGPL-2.1** | a CHD codec — see below |
| Microsoft.Extensions.Logging.Abstractions | MIT | a CHDSharp dependency |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT | idem |
| System.Diagnostics.DiagnosticSource | MIT | idem |
| System.IO.Hashing | MIT | idem |

All four CHD codecs are needed to open a CHD, not just the one a given file uses: `chdman` writes
its whole codec list into the header by default (`lzma, zlib, huff, flac`), and the codec table is
built at open time. Removing the FLAC decoder makes reads throw — measured, not assumed.

## LGPL-2.1 and how it is satisfied

VendoredFlac is LGPL-2.1, and merging it in is static linking. LGPL-2.1 §6 requires that a recipient
be able to relink the work with a modified version of the library. That is satisfied here by
**§6(a)**: this repository is public and holds the complete corresponding source of the plugin,
together with the exact build step that performs the merge, so anyone can substitute their own build
of the FLAC decoder and rebuild.

Concretely, to relink with a different FLAC:

```
dotnet tool restore                      # pins ILRepack, see dotnet-tools.json
dotnet build src\Ppsspp\Ppsspp.csproj -c Release
dotnet build src\Flycast\Flycast.csproj -c Release
```

The merge is the `MergePlugin` target at the end of `src\Ppsspp\Ppsspp.csproj`. Replace
`VendoredFlac.dll` in `src\Ppsspp\bin\Release\` before that target runs — or change the
`CHDSharp` package reference — and the merged output picks up your copy.

## `tools/melonds-nand`, `src/Shared.Dsi`, `src/MelonDs` and `src/NoGba` - GPL-3.0

Four directories in this repository are **not** MIT, and they are the ones that touch melonDS's
NAND code.

`tools/melonds-nand` is compiled together with source files from
[melonDS](https://github.com/melonDS-emu/melonDS) - `DSi_NAND.cpp`, `FATIO.cpp`, FatFs, tiny-AES-c
and SHA-1 - which are GPL-3.0-or-later. Its own sources carry that notice, and
`tools/melonds-nand/LICENSE` holds the licence text. It builds two things: `melonds-nand.dll`, the C
door the plugin calls, and `melonds-nandtool.exe`, the same operations from a command line.

| Component | Licence | Used for |
|---|---|---|
| [melonDS](https://github.com/melonDS-emu/melonDS) | GPL-3.0-or-later | the whole DSi NAND implementation: decryption, FAT access, title import, save export |

**`src/Shared.Dsi` is GPL-3.0-or-later**, because it is the code that calls that library: the DSi
engine - choosing a dump, building a console, installing a title, taking the difference a session
made, packing it as a `.dsisave`, rebuilding a console from a recipe. It lived inside `src/MelonDs`
until no$gba needed the same machinery, and it is compiled into both plugins rather than copied,
because four thousand lines that took three measured defects to get right should not exist twice.

**`src/MelonDs` and `src/NoGba` are GPL-3.0-or-later with it**, because each one compiles that engine
in and therefore loads the library into its own process through P/Invoke - see the LICENSE.md in
each. An earlier version invoked an executable at arm's length and stayed MIT; direct calls were
chosen knowingly, since reading a DSiWare save out of its NAND happens while a page is drawn rather
than once per launch, and the same reasoning now covers both emulators.

This paragraph used to say the other plugins stayed MIT *because* this repository copies its shared
pieces rather than linking them. That is still true of `Log.cs`, `Archives.cs` and the metadata-row
trio, where two copies differ by a namespace and a comment. It stopped being true of the DSi engine
the day a second emulator needed it, and the licence follows the code rather than the other way
round.

**Flycast, Xenia and PPSSPP are unaffected and remain MIT**: none of them touches the NAND library,
the shared DSi folder, or any assembly that does.

Two files in the tool are melonDS's own work rather than ours, and say so at the top:
`aes_key.cpp` carries `DSi_AES::ROL16` and `DSi_AES::DeriveNormalKey` copied verbatim from
`src/DSi_AES.cpp`, because compiling that file would have pulled in most of the emulator for twenty
lines of arithmetic.

## Not merged

`vendor\Unbroken.LaunchBox.Plugins.dll` is Unbroken Software's SDK. It is referenced with
`<Private>false</Private>` and deliberately **not** merged: the host must provide it at runtime so
the plugin's `EmulatorPlugin` and `IEmulator` types are the same types the host uses. It is
committed to this repository only so the projects build without a LaunchBox installation.

## This repository

Everything written here is MIT, `LICENSE`.
