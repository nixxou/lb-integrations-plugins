# LaunchBox 14.1 SDK - for reference only

`Unbroken.LaunchBox.Plugins.dll` from LaunchBox **14.1 beta 3** (assembly version **14.1.0.3**), 367 928 bytes,
MD5 `4e75793012549cd2c4431374a90222c6`. Copied on 16/10 from `G:\LB14B3\Core\`.

**No project references it.** The pack is built against `..\Unbroken.LaunchBox.Plugins.dll` (14.0.1.2) so one build
runs under LaunchBox 14.0 and 14.1: compiled against 14.1, a plugin naming a v2 type would fail to load under 14.0.
It is here to read the v2 plugin model (decompile it, diff it against 14.0.1.2) and to build a probe against it.

What changed, read 16/10 (`docs\lb141-plan.md`): no type or member of 14.0.1.2 was removed or changed its signature -
only nullable annotations and `[Obsolete]` (`EmulatorPlugin` is now the "schema version 1" base, `IPluginPaths` and
`IEmulatorWithCores` are deprecated). Added: the "schema version 2" model (`Components.*`: `IEmulatorIntegration` and its
capability interfaces, achievements, game data, discovery, storefronts) and injected services (`Services.*`).
