# Save probe (test tool, never shipped)

Two save-management plugins, **Nixx-SaveProbeMain** and **Nixx-SaveProbeSecondary**, each with a fake emulator
(`SaveProbeMain.exe`, `SaveProbeSecondary.exe`) whose buttons write a save or a state. Every call LaunchBox makes on their
save methods is logged, with who asked, to `%LOCALAPPDATA%\lb-integrations-plugins\saveprobe.log`. The Main plugin also
logs - without changing anything - what LaunchBox calls on RetroArch, Dolphin, PCSX2 and on the SDK's default bodies
(RPCS3, DuckStation... and the pack's plugins).

Deploy (LaunchBox closed): `tools\save-probe\deploy-probe.ps1`. At start-up each plugin creates its emulator entry when
missing (a SNES row, not the default). Remove: `deploy-probe.ps1 -Remove`, then delete the two emulator entries.

`Emulators\Nixx-SaveProbe<Role>\probe.ini`, read at every call:

| key | values | for |
|---|---|---|
| `Supports` | true / false | `SupportsSaveManagement()` - is it what decides who is asked? |
| `OwnGamesOnly` | false / true | answer for any game (as RetroArch) or only for its own (as Dolphin) |
| `Container` | false / true | `IsSaveContainer()` - LaunchBox copies, or asks `TryBackupSave` |
| `Signature` | content / fixed / none / base | `TryComputeSaveSignature` - what makes a backup |
| `SaveExt`, `StateExt` | `.mainsave`, `.mainstate`... | set both roles to `.srm` / `.state` to test name collisions |

The questions (07/10): for a game whose emulator is Main, launched with Main and then with Launch With > Secondary -

1. at the end of a game: is the emulator that ran asked (`GetSaves`, `TryComputeSaveSignature`, `TryBackupSave`)?
2. Game Saves: who gets `GetSaves`?
3. Restore of a save Secondary made: does `AddSaveFile` go to Secondary, or to Main?
