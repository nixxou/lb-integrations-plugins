# RASnes9x 1.2 (x64)

`RASnes9x-x64-1.2.zip` is RetroAchievements' own build of RASnes9x 1.2, byte for byte as
<https://retroachievements.org/bin/RASnes9x-x64.zip> served it on 2026-10-06 (saved there as `RASnes9x-x64.zip`).

- SHA-256 `51d59b891224919bdf2a6275ac38aff2506b92b5279f08ba6af5557e78bdfff1`, 5,235,806 bytes.
- `RASnes9x.exe`: MD5 `e81f98f95eec0fd69166b4ac97a9f2be`, 12,728,832 bytes, built 2024-11-14; its version resource says
  Snes9x's, 1.63. Source: <https://github.com/RetroAchievements/RASnes9x>, tag `RASnes9x.1.2` (2024-12-09: "Upgrade to
  official 1.63 version of Snes9x", SA-1 memory exposed to achievements, achievement state kept in rewind frames).
- RetroAchievements' toolset, `RA_Integration-x64.dll`, is **not** in the zip: the emulator downloads it from
  RetroAchievements itself at its first start.

## Why it is kept here

The release on GitHub carries no file: RetroAchievements publishes the build on its own site only, and that site answers
a program's request on its API with 403 (measured 2026-10-06). The plugin downloads from the site first and falls back
to this copy; either way the SHA-256 above is checked before anything is extracted (`src/RaSnes9x/RaSnes9xBuild.cs`).

## Licence

Snes9x's own licence (the zip's `LICENSE`): free to use and to redistribute unmodified, for no money. The source of this
build is public, at the address above.
