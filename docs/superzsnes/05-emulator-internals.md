# 05 · L'intérieur de l'émulateur (build 0.310)

Tout ce qui suit a été lu sur le build 0.310 : types et adresses par Il2CppInspectorRedux,
enchaînements d'appels par `xref.ps1`, textes dans `level0` et `resources.assets`, et confirmé
quand c'était possible par le journal du jeu. Les **noms** sont ce qui compte pour le plugin ; les
adresses ne servent qu'à retrouver un point dans un dump du même build.

## Les singletons et l'API utile

| classe | membres utiles |
|---|---|
| `MainMenuManager` | `static Instance` ; `mainMenuSettings` ; `gameRunning`, `gameToLoad`, `startUpGameLoad`, `gameLoadLastState` ; `IsInMenu()`, `OpenMenu()`, `LoadGame(string fileName, bool loadLastState)`, `OnExit()`, `GetGameSettings(string filename = "")`, `GetGameSpecificSave(filename)`, `SaveMainMenuSave()`, `ShowBasicDialog(titre, texte)`, `ShowBasicDialogYesNo(...)`, `GetVersionNo()` ; `GameObject supportUs`, `newVersion`, `welcomeDialog`, `aboutDialog`, `optionsDialog`… ; `uiInterface`, `zInputSystem`, `gameStateUI` |
| `MasterExecutor` | `static Instance` ; `mainMenuManager`, `uiInterface`, `gameStateUI`, `CoreMemoryMap`, `modLoadData`, `snesRenderer` ; `SaveState(string slotPostfix = "")`, `LoadState(string slotPostfix = "")`, `LoadStateFilename(nom)`, `LoadSaveStateInfo(slot)`, `PauseGame()`, `ResumeGame()`, `EscapeBackToMenu(bool clearUIString = true)`, `LoadRom(fileName, loadLastState)`, `Reset()`, `ReturnToGame()`, `IsExecuting()`, `ApplyCheatCodes()`, `CaptureScreenshot()` |
| `ZInputSystem` | `static Instance` ; `EscapePressed(bool fromGame = false)`, `ExitPressed(bool fromGame = false)`, `ButtonPressed(int controlNo, GameInput)`, `ButtonPressedThisFrame(...)`, `GetRewindValue()`, `GetFFValue()`, `SetRumble(...)` ; enum `GameInput` ci-dessous |
| `GameStateUI` | `SetGameUIString(string)` (ligne de texte en jeu, minuteur d'effacement), `SetGameInfoString(string)`, `SetGameSceneString(string, Color)` |
| `RomLoader` | `static Instance` ; `romInfo { filename, cartridgeTitle, memMapMode, romType, coprocessorType, romSize, ramSize, country, devID, romVer, checksum, checksumComp, IsMSU1 }` ; `IsHiROM()`, `StartLoadRom(...)` |
| `MainMemoryMap` | `SaveSaveData()`, `LoadSaveData()`, `GetSram()`, `GetRam()` |
| `RetroAchievements` | `static Instance` ; `Login(...)`, `Logout()`, `IsLoggedIn`, `CurProfileData`, `LoadRom(...)`, `ComputeMD5(...)` |
| `ZRCheevosIntegration` | `static Instance` ; wrappers rcheevos : `rc_client_begin_login_with_password`, `rc_client_begin_login_with_token`, `rc_client_set_hardcore_enabled`… |
| `StringEnc` | `Init()`, `byte[] GetEPW(string)`, `string GetNPW(byte[])` |
| `ModLoadData` | `List<ModInfo> modInfo`, `audioModInfo` ; `ModInfo { resourceName, uint[] titleCRC }` |
| `VersionNo` (MonoBehaviour) | `string Version`, `string[] SupportedEnhancedGames`, `DSPDetection[] dspDetections { dspRomName, titleChecksums[] }`, `EnhancementCredits[]` |
| `CommonTools` | `ModifyPathWithCustomPath(path, customPath)` : jetons `{persist}` → `persistentDataPath`, `{exec}` → dossier de l'exe |
| `FileHelper` | `GetParsedPath`, `FileExists`, `ReadBytesFromFile`, `PathGetDirectoryName`… (mêmes jetons) |

### `ZInputSystem.GameInput`

```
None=0, Run=1, SaveState=2, LoadState=3, StateSelect=4, Exit=5,
DisableBG0=100 … DisableBG3=103, DisableObj=104, FastForward=200, Rewind=201, Screenshot=300,
U=1000, D, L, R, A, B, X, Y, LB, RB, ST, SL=1011
```

Liaisons : `MainMenuSettings.inputData`,
`Dictionary<ValueTuple<GameInput,int controllerNo>, InputConfig { Input inputA, inputB, inputC }>`,
`Input { controllerNo, ControllerType ctrlType, Key keyCode, ButtonType button, List<Input> multiPress }`.
Défauts constatés : F2 sauvegarde, F4 charge, Échap = `EscapePressed`.

## Les réglages : `MainMenuSettings`

Fichier : `Application.persistentDataPath + "/szsnes_ui.data"`, soit par défaut
`%USERPROFILE%\AppData\LocalLow\ZEMU Software Inc_\SUPERZSNES\szsnes_ui.data` (le `_` remplace le
point final de « ZEMU Software Inc. »), soit `<exe>\portable\szsnes_ui.data` avec P1.

Format **NRBF** (`BinaryFormatter`) : en-tête `00 01 00 00 00 FF FF FF FF 01 00 00 00 00 00 00 00`,
`BinaryLibrary` « Assembly-CSharp », racine `MainMenuManager+MainMenuSettings`. Écrit par
`SaveMainMenuSave` à la fermeture et à chaque retour au menu ; lu par `LoadMainMenuSave`. Jamais
parsé par le pack : les surcharges se font en mémoire (03, P3–P5).

Champs (offsets du build 0.310, indicatifs) : voir 04 pour la liste ; les notables :
`srmPath`, `chtPath`, `bpsPath`, `gameSavePath` (chaînes, `null` par défaut = à côté de la ROM),
`noDirectoryForSaves`, `inputData`, `gameSettings : Dictionary<string nomDeFichier, GameSpecificSettings>`,
`rauserID`, `raencT : byte[]`.

## La ligne de commande de l'émulateur : `MasterExecutor.Awake`

`Environment.GetCommandLineArgs()`, journal `# ARGS: n`, puis pour chaque argument
`ARG<i>: <valeur>` ; comparaison exacte avec ses neuf drapeaux ; tout argument commençant par `-`
autre est ignoré ; le premier argument dont la version minuscule finit par `.smc .sfc .zip .swc .ufo`
est la ROM (`FOUND Filename Arg: <chemin>`), chargée par le jeu lui-même.

## Saves et états

| quoi | où, par défaut | option |
|---|---|---|
| SRAM | `<rom>.srm` à côté de la ROM | `srmPath` |
| états | dossier `<rom>.data.szsnes\` à côté de la ROM : `<rom>.szst0` (slot 0), `<rom>.szst-last` (reprise, écrit à chaque ouverture du menu), `.szhistory`, `.bookmark-szst` | `gameSavePath`, `noDirectoryForSaves` |
| cheats | `<rom>.cht` | `chtPath` |
| patch | `<rom>.bps` | `bpsPath` |
| mods externes | `<rom>.zsmod`, `<rom>.zsaudiomod`, `<rom>.zlua` à côté de la ROM | — |

Écriture SRAM : `MainMemoryMap.SaveSaveData()`, appelée au retour au menu (`EscapePressed` →
`Update`), par `EscapeBackToMenu`, et à chaque `LoadRom`. **Pas** dans la branche `ExitPressed`.

## Les améliorations dédiées

Onze mods embarqués dans `resources.assets` (TextAssets NRBF de `ModData`) : chr, cv4, fze, gng,
gr3, loz, mmx, smk, smt, smw, starfox, certains avec un `.Audio`/`.Sound`. Reconnaissance dans
`LoadRom` : CRC32 sur l'UTF-8 des **21 octets du titre d'en-tête SNES**, espaces compris
(`ZSNESUIInterface.HasEnhancement` : `RomLoader.IsHiROM` → offset d'en-tête, `Char.ToString` +
`Concat`, `Encoding.UTF8`, `Crc32.ComputeChecksum`), comparé aux `titleCRC` de `ModLoadData`,
journal `Title Checksum: <n>`.

| mod | titre d'en-tête | CRC32 |
|---|---|---|
| smw | `SUPER MARIOWORLD     ` | 34BBDFD0 |
| chr | `CHRONO TRIGGER       ` | F10A5C5B |
| mmx | `MEGAMAN X            ` | B216C093 |
| fze | `F-ZERO               ` | CF0FBFE6 |
| smt | `Super Metroid        ` | 18CA5EF6 |
| loz | `THE LEGEND OF ZELDA  ` | 25AC94B5 |
| smk | `SUPER MARIO KART     ` | BBC1C3FD |
| cv4 | `SUPER CASTLEVANIA 4  ` | 4A7B50A2 |
| gng | `SUPER GHOULS'N GHOSTS` | 8F103F67 |
| starfox | `STAR FOX             ` | EAFD11DE |
| gr3 | Gradius III, chaîne non retrouvée | 5C63BE5C |

Les mods Lua (`zluaData`, entrées `OnROMLoaded(crc)`, `OnUpdateEmuFrame()`, `OnGSUStartFrame()`,
API `SZSNES.*`) reçoivent le **CRC32 de la ROM entière** (« Sending ROM CRC of : »). Star Fox exige
`2412046032` = `0x8FC4E6D0` = Star Fox (USA) (Rev 2). `DSPDetection.titleChecksums` associe les
firmwares `dsp1..4.rom`, `st010/011/018.rom` aux titres par le même CRC de titre.
`GameSpecificSettings` est indexé par **nom de fichier**, pas par CRC.

## RetroAchievements

Client rcheevos complet, dialogue Config > Retroachievements (`webAPIKey`). Stockage dans
`MainMenuSettings` : `rauserID` et `raencT` chiffré :

```
clé    = SHA256( UTF8( "VWSZaVVTXSC" + SystemInfo.deviceUniqueIdentifier + "YYZSUJA" ) )
raencT = IV(16) || AES-256-CBC-PKCS7( clé, IV, UTF8(jeton) )
```

`StringEnc.Init` dérive la clé, `GetEPW` chiffre, `GetNPW` déchiffre (`Array.Copy` de l'IV puis
`CryptoStream` + `StreamReader`). `RetroAchievements.UserProfileResult` écrit `raencT` après
connexion ; `Start` déchiffre et appelle `Login`, qui envoie
`dorequest.php?r=login2&u=<user>&t=<jeton>`. **Depuis le plugin**, appeler `StringEnc` ou `Login`
directement rend `deviceUniqueIdentifier` inutile ; hors processus, `tools\il2cpp-xref\radecrypt.ps1`
calibre la recette par force brute. Non implémenté à ce jour.

## Version et site

- Version installée : caption « v0.310b » dans `SUPERZSNES_Data\level0` (champ de script, avec deux
  étiquettes figées `v0.001` et `v0.100a` à ignorer : la plus haute gagne) ; l'exe ne porte que la
  version d'Unity.
- Site : `https://www.zsnes.com/` carte `<h3>Windows</h3>` + `<a href="files/SuperZSNES_v0.310.zip">` ;
  flux `https://zsnes.com/version.txt` = `Windows,0.310` / `Linux,…` / `Mac,…`.
- L'archive : 26 fichiers à plat, ~100 Mo ; contient `SUPERZSNES_Data\smw.srm`, une SRAM de test
  oubliée par les auteurs.

## Popups

`MainMenuManager.supportUs` : « Just this once, we want to let you know that you can check out our
Discord server for up to date news. Plus we've got a Patreon account… ». `newVersion` : « A new
version of SUPER ZSNES is out! You'll need to download it over at www.zsnes.com! ».
