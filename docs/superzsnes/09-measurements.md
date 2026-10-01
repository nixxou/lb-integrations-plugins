# 09 · Journal des mesures

Chaque ligne : ce qui a été mesuré, comment, le résultat. Dates 2026. Tout sur le build 0.310 sauf
mention. Quand un nouveau build change une de ces lignes, ajouter la nouvelle en dessous avec sa
date plutôt que d'écraser.

## Identité du build

| quoi | valeur | comment |
|---|---|---|
| archive | `SuperZSNES_v0.310.zip`, 99,7 Mo (104 493 660 octets), Last-Modified 26/09 15:57:58 GMT | `curl -I` |
| sha256 archive | `747E05A81DF81925387801B00EDB6C5DAC5996E1D81BB42E5A1C20B46D14A906` | Get-FileHash |
| sha256 GameAssembly.dll | `6B7AFC4A83571646EAB82F6D2323140778D3D7A7785A97C0C0CBD995549A88E2` | idem |
| Unity | 6000.3.6f1 (bbb010bdb8a3) | version resource de l'exe, Player.log |
| IL2CPP metadata | version 39 | en-tête de global-metadata.dat, Cpp2IL |
| architecture | x86 (PE machine 0x14C) | en-tête PE ; `Plugins\x86`, `UnityCrashHandler32.exe` ; BepInEx « Process bitness: 32-bit » |
| company / product | « ZEMU Software Inc. » / « SUPERZSNES » | `SUPERZSNES_Data\app.info` |
| version affichée | « v0.310b » | `level0` @ 0x249445 (champ de script) ; leurres « v0.001 » @ 0xF55BC, « v0.100a » @ 0xF6B0C (étiquettes) |
| site | carte Windows `files/SuperZSNES_v0.310.zip` ; `version.txt` = `Windows,0.310` / `Linux,0.310` / `Mac,0.310` | curl 30/09 |

## Fichiers et chemins

| quoi | résultat | comment |
|---|---|---|
| réglages | `%USERPROFILE%\AppData\LocalLow\ZEMU Software Inc_\SUPERZSNES\szsnes_ui.data`, 13–17 Ko, NRBF | lancement 30/09, Player.log « Save: » |
| chemin construit | `Application.persistentDataPath + "/szsnes_ui.data"` en 3 endroits | xref GetConfigFilePath (0x1040EF50, 128 o), LoadMainMenuSave (0x10410870), SaveMainMenuSave (0x104113E0) |
| mode portable natif | aucun ; `.portable` = MoonSharp `PlatformAccessorBase.GetPlatformName` | refs.ps1 sur le littéral ; ProcMon : aucune sonde de marqueur |
| jetons de chemin | `{persist}`, `{exec}` | xref CommonTools.ModifyPathWithCustomPath (0x104924A0), FileHelper.GetParsedPath |
| SRAM | `<rom>.srm` à côté de la ROM (défaut) | texte du dialogue Files + MainMemoryMap.SaveSaveData |
| états | `<rom>.data.szsnes\<rom>.szst0`, `.szst-last` | lancement 30/09 avec Star Fox, fichiers observés |
| `{exec}` dans les chemins | `srmPath={exec}/saves` → `<exe>\saves\<rom>.srm` ; `gameSavePath={exec}/states` → `<exe>\states\<rom>.data.szsnes\<rom>.szst0` (F2) ; les trois dossiers créés par l'émulateur | lancements 01/10 09:57 et 09:59, `--nixx-set:` |
| SRAM lue où elle est écrite | avec `srmPath` posé, la SRAM est LUE dans `saves\` : une `.srm` laissée à côté de la ROM n'est plus vue | idem |
| dossiers du pack (`DataFolders.cs`) | posés dans le postfix de `LoadMainMenuSave`, déplacement **avant** « SRAM Data Loaded: 8192 » : la SRAM déplacée (même hash) est celle chargée ; `som.data.szsnes\` passé dans `states\` ; relancé avec `--nixx-data-folders=off`, les trois chemins sont toujours dans `szsnes_ui.data` | LogOutput 01/10 10:00, `options.json` 10:01 |
| registre | `HKCU\Software\ZEMU Software Inc.\SUPERZSNES` : seulement Screenmanager*, unity.* | ProcMon 14:52 |

## Ligne de commande

| quoi | résultat | comment |
|---|---|---|
| drapeaux propres | `--trace --fulldebuglogs --tracecpu --traceinst --spctrace --pputrace --gsutraceop --gsutracemisc --loadstate` | xref MasterExecutor.Awake (0x104EB5D0) |
| arguments inconnus en `-` | ignorés | idem (`String.StartsWith("-")`) |
| ROM | premier argument finissant par `.smc .sfc .zip .swc .ufo` | idem ; journal `FOUND Filename Arg:` 30/09 |
| ROM derrière `--nixx-*` | chargée | lancement 30/09 |
| ROM en chemin RELATIF | chargée, mais la SRAM est écrite vers `C:\<rom>.srm` (« Access to the path "C:\som.srm" is denied ») : un chemin relatif n'est pas résolu contre le dossier de travail. LaunchBox passe un chemin complet | LogOutput 01/10 01:03 |

## Entrées

| quoi | résultat | comment |
|---|---|---|
| Échap | `ZInputSystem.EscapePressed` (0x104AD760) interrogé 2× par frame dans `MasterExecutor.Update` (0x104F2E70) ; → SaveState "-last", OpenMenu, pause, SaveSaveData | xref Update ; préfixe sur EscapeBackToMenu muet sur 5 appuis |
| Exit pad | `ZInputSystem.ExitPressed` (0x104AD810) → ZSWebServer.Stop, Application.Quit, sans SaveSaveData | xref Update |
| touches par défaut | F2 save, F4 load | utilisateur, 30/09 |
| `Input.GetKeyDown` legacy | appelé par Update (0x119DAFC0) → UnityInput fonctionne | xref ; log « input through LegacyInputSystem » |
| nouveau Input System | `ButtonControl.wasPressedThisFrame` élagué | « Method unstripping failed », 30/09 16:45 |
| `Object.FindObjectOfType` | élagué | idem 16:50 |

## Améliorations

| quoi | résultat | comment |
|---|---|---|
| détection | CRC32(UTF8(titre d'en-tête 21 octets)) vs `ModLoadData.titleCRC` | xref HasEnhancement (0x10467040) ; recalcul : 10/11 titres retrouvés |
| mods embarqués | chr F10A5C5B, cv4 4A7B50A2, fze CF0FBFE6, gng 8F103F67, gr3 5C63BE5C, loz 25AC94B5, mmx B216C093, smk BBC1C3FD, smt 18CA5EF6, smw 34BBDFD0, starfox EAFD11DE | resources.assets, premiers UInt32[] après chaque `.zsmod` |
| Lua | `OnROMLoaded(crc)` reçoit le CRC32 ROM entière ; Star Fox exige 0x8FC4E6D0 = (USA) (Rev 2) | script dans resources.assets @ 0x37EDF87 ; lancement 30/09 « Sending ROM CRC of : 2412046032 », mod chargé |
| mods externes | `<rom>.zsmod`, `.zsaudiomod`, `.zlua` prioritaires | xref LoadRom (0x104ED700) |

## RetroAchievements

| quoi | résultat | comment |
|---|---|---|
| clé | SHA256("VWSZaVVTXSC" + deviceUniqueIdentifier + "YYZSUJA") | xref -Hex StringEnc.Init (0x10499EE0) : ordre des push |
| chiffré | IV(16) ‖ AES-256-CBC-PKCS7 | xref GetEPW (0x10499790), GetNPW (0x10499B20) |
| qui écrit | `RetroAchievements.UserProfileResult` (0x10400580) | refs.ps1 -Methods StringEnc.GetEPW |
| login | `dorequest.php?r=login2&u=&t=` | xref RetroAchievements.Login (0x103FFB30) |
| deux clients dans le build | `RetroAchievements` (C# : login, MD5 de la ROM, session, jeux de succès, `RCAchievementProcessor` évalué à chaque image, `GrantAchievement`, widgets) et `ZRCheevosIntegration` (rcheevos natif : lecture mémoire, appels serveur, login, chargement de jeu) ; écran `RetroAchievementsOverlay` (identifiant, clé Web API) | interop `Assembly-CSharp.dll`, lecture par MetadataLoadContext, 01/10 |
| au démarrage | `RetroAchievements.Awake` appelé, `Instance` présente, non connectée ; `ZRCheevosIntegration.Start` **jamais** appelé | `--nixx-ra-probe`, `nixx.log` 01/10 09:30 |
| le verrou | `OptionsOverlay.retroachievements` est le panneau RetroAchievements lui-même, posé sur les options, **masqué** ; `EnableRetroachievements()` l'affiche ; il s'ouvre sur le message de l'émulateur « Work In Progress! … Achievements aren't sent or saved yet and many achievements aren't supported yet » | `--nixx-ra-drive`, capture d'écran 01/10 09:30 |
| hardcore | aucun réglage, aucun appel ; seule la réponse de session sépare `HardcoreUnlocks` et `Unlocks` | interop 01/10 |
| patcher les rappels natifs | `LoginCallback`, `load_game_callback`, `LogMessage` patchés : appelés aussitôt avec des valeurs aberrantes (rcheevos tient des pointeurs vers eux) - **ne pas les patcher** | `nixx.log` 01/10 09:29 |
| deviceUniqueIdentifier Windows | WMI Win32_BaseBoard/BIOS/OperatingSystem SerialNumber (+Manufacturer, Model, DeviceId dans UnityPlayer) ; recette exacte non calibrée | chaînes UTF-16 UnityPlayer.dll ; doc Unity |

## BepInEx

| quoi | résultat | comment |
|---|---|---|
| build | be.788+5b766a3, 01/09/2026 | builds.bepinex.dev |
| sha256 x86 / x64 | D5954A59…08A4D3 / F4CC496B…4817A | Get-FileHash 30/09 |
| v39 lue | « Using actual IL2CPP Metadata version 39 », 91 572 méthodes | LogOutput 30/09 23:58 (horloge du log) |
| interop | 112 assemblies, Assembly-CSharp.dll 1 483 264 o | dossier |
| premier chargement | Doorstop 4.5.0, runtime 6.0.7 | LogOutput |
| P1 vérifié | « Save: …\x\portable/szsnes_ui.data » | LogOutput 16:45 |
| P2 vérifié | « Escape confirmed - saving through the emulator, then quitting » | LogOutput 16:53 |
| P3–P7 | compilés, **non encore observés** en lancement réel au moment de cette doc (30/09) | — |
| P3 vérifié | `set: gfxMode None -> Scanlines` | `nixx.log` 01/10 01:03, plugin 0.5.0 recompilé, Secret of Mana (USA) |
| P4 vérifié | `game: overclock 0 -> 150` ; au lancement suivant, `overclock 0 -> 120` : la surcharge de jeu n'a pas été enregistrée | `nixx.log` 01/10 01:03 puis 01:06 |
| P5 vérifié | au lancement suivant sans surcharge, `options.json` dit `gfxMode` = `None` et `numLaunches` 13 → 14 : la sauvegarde a eu lieu, sans la surcharge | fermeture par WM_CLOSE (comme Alt+F4), `options.json` 01/10 01:05 |
| P6 | le patch tourne à chaque image sans erreur (aucun « popup patch - ») ; aucune des deux popups ne s'est levée en quatre lancements : **non observé** | `nixx.log` et LogOutput 01/10 |
| P7 vérifié | `primary display: 0,0 2560x1440; window at 0,0 2560x1440 (on it); mode ExclusiveFullScreen` puis `full screen window, 2560x1440` | `nixx.log` 01/10 01:03 |
| `--nixx-dump-options` | `portable\options.json` écrit (4,6 Ko), valeurs lues **avant** les surcharges | 01/10 01:03 |
| déploiement réel | sur un 0.310 fraîchement extrait de l'archive officielle : be.788 x86 téléchargé et vérifié, 228 fichiers, `BepInEx.cfg` silencieux, plugin 27 136 o, 10 fichiers de doc | sonde `--superzsnes-deploy-real`, 01/10 01:08 |
| premier lancement après déploiement | interop générée (112 assemblies), plugin chargé, `nixx.log` 20 s après le départ, **aucun** `LogOutput.log` (silencieux), `portable\szsnes_ui.data` créé | 01/10 01:09 |

## Popups

| quoi | résultat |
|---|---|
| Patreon | `MainMenuManager.supportUs`, texte « Just this once, we want to let you know… » (level0 @ 0x1176684 zone Options) |
| version | `MainMenuManager.newVersion`, « A new version of SUPER ZSNES is out! » (level0 @ 0xF5849) |

## Chronologie

- 30/09 matin : téléchargement 0.310, chaînes, level0, premier lancement (portable absent), ProcMon
  refusé côté agent, Il2CppDumper refuse v39.
- 30/09 midi : Il2CppInspectorRedux, xref/refs, StringEnc, GetConfigFilePath, `.portable` = MoonSharp,
  ProcMon utilisateur, patch d'octets et relais version.dll évalués.
- 30/09 après-midi : BepInEx be.788 installé, interop OK, plugin 0.1 (portable) → 0.3 (Échap/F1,
  singletons) → 0.4 (options, surcharges, popups, écran principal) → 0.5 (journal, déploiement).
- 30/09 soir : déployeur, config silencieuse, doc embarquée.
- 01/10 : intégré au dépôt principal au-dessus de `03a406f` (le diff du transfert, appliqué en trois
  voies ; un seul conflit, `Probe\Program.cs`) et aligné sur ce que le pack a gagné depuis `a14f1bd` :
  un lancement à la fois (`LbipLaunchGate`), fin d'un import vers SUPER ZSNES écoutée
  (`LbImportFinished`), WPF dans le projet. `SuperZsnes.BepInEx.dll` recompilé depuis les sources contre
  l'interop du dossier transféré (27 136 o ; celui du transfert, 26 624 o, datait d'avant les
  dernières sources). Sonde `--superzsnes` verte. P3–P7 toujours à observer en lancement réel.
