# Saves, lot 5: the desk on LaunchBox's integrations without save support

Plan: `docs\saves-lb14-routing.md` §4b, row 5 and decision D6. Lot 4 (`docs\saves-lot4.md`) put a desk on LaunchBox's
RetroArch, Dolphin and PCSX2; this lot does the same for the seven of its integrations that have no save support at all,
so that a PS1 game set to DuckStation lists, backs up and restores the saves another emulator (RetroArch, ares) keeps
for it.

## 1. Lot 5.0: what they are (measured 12/10, G:\LB1326, LaunchBox 14.0.1)

- **The seven**: `BigPEmuPlugin`, `CemuPlugin`, `DuckStationPlugin`, `MamePlugin`, `Rpcs3Plugin`, `ScummVmPlugin`,
  `XemuPlugin`, in `System\Plugins\<X> LaunchBox Integration\Unbroken.LaunchBox.Windows.<X>.dll`; emus by the router's
  rule: `lb.bigpemu`, `lb.cemu`, `lb.duckstation`, `lb.mame`, `lb.rpcs3`, `lb.scummvm`, `lb.xemu`.
- **None overrides any of the 13 save methods** (read in each dll's metadata, then checked on the loaded types by the
  probe): they run the SDK's bodies - already hooked once by lot 4 (StockDesk), filtered on the instance's type.
- **The SDK's bodies** (decompiled, `Core\Unbroken.LaunchBox.Plugins.dll`), checked on each instance by the probe:

  | Method | Answer |
  |---|---|
  | `SupportsSaveManagement` | false |
  | `GetSaves` | a failure: "This feature is not supported by this emulator plugin" |
  | `AddSaveFile` | a failure: "This feature is currently not supported by this emulator plugin!" |
  | `RemoveSave` | **deletes the row's file** (`File.Delete`), success |
  | `IsSaveActive` | true |
  | `TryBackupSave`, `TryComputeSaveSignature`, `TryGetSaveGroupInfo` | false |
  | `IsSaveContainer`, `UseSaveGroupIdForPersistedMatch`, `IsSecondarySaveFile` | false |
  | `GetCompanionSaveFiles`, `GetPotentialSaveSlots` | empty |

- `SupportsSaveManagement()` takes no platform: D6 ("supports saves as soon as a save-capable plugin covers the platform")
  is decided per integration, over the platforms of all the emulators it drives.
- Sandbox: no DuckStation emulator entry; one PS1 game (Final Fantasy VII, on RetroArch); PS1 covered by RetroArch and
  Nixx-ares; MAME 0.288 for Arcade, which no save-capable plugin covers.

Probe: `--stock-saves <root> --scratch <dir>`, section 9.
