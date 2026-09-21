# Third-party components

`Ppsspp.dll` is built by merging its dependencies into a single assembly with ILRepack
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

Dreamcast discs are distributed as `.chd` as often as `.gdi`, and the disc id lives inside the
image, so reading CHD is not optional here. Flycast's release archive is a plain `.zip`, so unlike
the Xenia plugin this one needs no SharpCompress - `System.IO.Compression` reads it.

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

## Not merged

`vendor\Unbroken.LaunchBox.Plugins.dll` is Unbroken Software's SDK. It is referenced with
`<Private>false</Private>` and deliberately **not** merged: the host must provide it at runtime so
the plugin's `EmulatorPlugin` and `IEmulator` types are the same types the host uses. It is
committed to this repository only so the projects build without a LaunchBox installation.

## This repository

Everything written here is MIT, `LICENSE`.
