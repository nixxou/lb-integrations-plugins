# Saves, lot 4: the front desk and our ids on LaunchBox's RetroArch, Dolphin and PCSX2

State on 10/10/2026, branch `savegroupid` (nothing pushed). Plan: `docs\saves-lb14-routing.md` §4b, lot 4. Decisions
(Mehdi, 10/10): Harmony on LaunchBox's own integrations (not the `nixx-retroarch` clone), all three in this lot, and
everything at once - the desk AND their native ids translated into our JSON (`native` keeps the original), the cards
already persisted migrated. The way back at uninstall is lot 7.

## 1. Lot 4.0: measured outside LaunchBox (probe `--stock-saves`)

`Probe --stock-saves <LaunchBox root> --scratch <empty folder>`: the real dlls of `<root>\System\Plugins` loaded,
LaunchBox's `Core` resolved for them, RetroArch called on a copy of its folder (settings, saves, states; no cores).

### 1.1 Who overrides what (G:\LB1326, LaunchBox 14.0.1)

Types: `Unbroken.LaunchBox.Windows.RetroArch.RetroArchPlugin`, `…Dolphin.DolphinPlugin`, `…Pcsx2.Pcsx2Plugin`, in
`System\Plugins\<RetroArch|Dolphin|PCSX2> LaunchBox Integration\`. `EmulatorName` "RetroArch", "Dolphin", "PCSX2": the
router's emus are `lb.retroarch`, `lb.dolphin`, `lb.pcsx2`.

| Method | RetroArch | Dolphin | PCSX2 |
|---|---|---|---|
| SupportsSaveManagement, GetSaves, AddSaveFile, UseSaveGroupIdForPersistedMatch, GetPotentialSaveSlots | own | own | own |
| RemoveSave | SDK | own | own |
| IsSaveActive, IsSaveContainer, TryBackupSave | SDK | SDK | own |
| TryComputeSaveSignature | own | SDK | own |
| TryGetSaveGroupInfo | own | SDK | SDK |
| IsSecondarySaveFile, GetCompanionSaveFiles | own | SDK | SDK |

"SDK" = `EmulatorPlugin`'s own body: a patch there runs for every plugin and must filter on the instance's type.

### 1.2 How they answer, row by row (rows made as LaunchBox persists them)

- **RetroArch `UseSaveGroupIdForPersistedMatch` is true ONLY for a `saturn-…` id** - false for an empty id, a GUID, our
  JSON. RetroArch matches cards by id for its Saturn sets only.
- RetroArch `TryGetSaveGroupInfo` and `TryComputeSaveSignature` work from the FILE, whatever id comes in: Atlantis's
  `.bcr` gives `saturn-atlantis - the lost tales (fr) (disc 1)` and a set signature; Zelda's `.srm` and state: false.
- **PCSX2 decides by the id**: `UseSaveGroupIdForPersistedMatch` and `IsSaveContainer` true only for `pcsx2:…`; its
  `IsSaveActive` and `TryComputeSaveSignature` need an install (none in the sandbox: they throw on our stub).
- The SDK's `IsSaveActive` answers true.
- So a JSON id reaching them unchanged would turn Saturn and PCSX2 matching off: every inbound row must be decoded to
  its native id before the original runs.

### 1.3 Harmony

A prefix that replaces the row argument (`ref GameSaveBase __0`, a `MemberwiseClone` with another `SaveGroupId`) reaches
the original: a later prefix and the original see the copy, the caller's row is untouched.

### 1.4 Not measurable outside LaunchBox

Their `GetSaves` reach LaunchBox's core (RetroArch: `Unbroken.LaunchBox.Tokens.TokenLocator`; Dolphin: an obfuscated
resolver), whose static set-up only LaunchBox does - both throw in the probe. The listed rows, Dolphin's native ids,
which row LaunchBox hands the per-row methods when listed and persisted ids differ, and the scan's cost are measured in
LaunchBox with the logging-only build (step 5 of the plan).

Harness notes: `IGame.GetEffectiveCommandLine()` is a method, the probe's `StubGame` now answers a method given as
`"Name()"`; RetroArch reads its `-L "cores\<core>.dll"` from it.

### 1.5 The hooks in the probe (StockDesk, watch only)

- Typed hooks, one pair per method: a hook taking `object[] __args` LOSES the out values (measured: RetroArch's
  `TryGetSaveGroupInfo` then gave back an empty id). With typed hooks every answer is the original's (RetroArch, PCSX2).
- An SDK body is patched where it is declared (`EmulatorPlugin`), once for the three; Harmony refuses it seen from the
  derived type. Each integration is marked hooked BEFORE its methods are patched: a failure never leads to a second
  prefix (measured once: two prefixes on `GetSaves` after a failure). 21 methods, 1 prefix each, `Install` twice.
- In the probe, Harmony cannot hook Dolphin's GetSaves/AddSaveFile/RemoveSave and PCSX2's AddSaveFile/RemoveSave/
  TryComputeSaveSignature: `The type initializer for '<Module>' threw` - their module initializer needs LaunchBox, as
  their GetSaves do. To be seen in LaunchBox.
