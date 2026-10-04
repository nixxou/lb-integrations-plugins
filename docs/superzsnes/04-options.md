# 04 · Les options et la ligne de commande

## Où chaque option se règle (01/10)

Choix de Mehdi : le catalogue garde les 67 options, mesurées et rendues, mais chacune a une **portée**
(`SuperZsnesOptions.ScopeOf`) :

| portée | où | options |
|---|---|---|
| Global | onglet SUPER ZSNES de la fenêtre Nixx, `settings.ini` | `plugin.bepinex`, `quit-confirm`, `menu-key`, `portable`, `support-popup`, `version-popup`, `persist` |
| Game | clic droit sur un jeu, « Nixx-SuperZSNES : Options... », `games\<id LaunchBox>.ini` à côté de `settings.ini` | fenêtre (`unity.screen-*`, `popupwindow`, `monitor`), `native.loadstate`, affichage (`gfxMode`, `scanlineStrength`, `interpolationMode`, `noBilinearFiltering`, `maxBrightness`, `use87aspect`), `rewindDisabled`, `snesRumble`, `rightStickGameSpeed`, `swapAcceptCancel`, les trois volumes |
| Hidden | nulle part, jamais envoyée | tout le reste (dossiers, `game.*`, le reste du gameplay et de l'affichage, `plugin.log`) |

Au lancement, `SuperZsnesSettings.ForLaunch` envoie les options Global de `settings.ini` et les
options Game du fichier du jeu ; une clé d'une autre portée (écrite par une version précédente ou à la
main) est ignorée. Celles du plugin passent en mémoire : le fichier de l'émulateur n'en reçoit aucune,
sauf `persist`.

**Les options Window sont à Unity, pas au plugin** : `-screen-*`, `-popupwindow`, `-monitor` sont
appliqués par le lecteur avant tout plugin, et Unity écrit l'écran utilisé dans
`HKCU\Software\ZEMU Software Inc.\SUPERZSNES` en quittant - clé commune à toutes les installations et à
tous les jeux. `SuperZsnesScreenSession` les rend à la session : un lancement qui en **ajoute** une (ou
`--nixx-display=primary`) note d'abord les valeurs d'écran de la clé (`<données du greffon>\screen-session.txt`),
et sa surveillance les remet une fois SUPER ZSNES fermé (+1 s), en effaçant celles que la session a
ajoutées ; une note restée (plantage) est remise au lancement suivant, au démarrage de l'hôte et à
l'ouverture de l'émulateur seul (« Open emulator »). Les compteurs `unity.*` voisins ne sont pas touchés.

## La grammaire

Tout ce qui est à nous commence par `--nixx-`. L'émulateur (`MasterExecutor.Awake`) ignore tout
argument qui commence par `-` sauf ses neuf drapeaux (`--trace --fulldebuglogs --tracecpu
--traceinst --spctrace --pputrace --gsutraceop --gsutracemisc --loadstate`), et prend pour ROM le
premier argument dont la fin, en minuscules, est `.smc .sfc .zip .swc .ufo`. Nos drapeaux peuvent
donc précéder la ROM que LaunchBox colle en fin de ligne.

| syntaxe | famille | traité par | effet |
|---|---|---|---|
| `--nixx-set:<champ>=<valeur>` | Setting | plugin BepInEx | champ de `MainMenuSettings`, en mémoire |
| `--nixx-game:<champ>=<valeur>` | Game | plugin BepInEx | champ de `GameSpecificSettings` du jeu lancé |
| `--nixx-<nom>=<valeur>` | Plugin | plugin BepInEx | comportement du plugin |
| `--loadstate` | Native | émulateur | ses propres drapeaux |
| `-screen-fullscreen 1`, `-screen-width N`… | Unity | UnityPlayer | commutateurs standard d'un lecteur Unity, **espace** et non `=` |

Valeurs : booléens `true/false/on/off/1/0/yes/no` ; nombres en culture invariante (le point) ;
énumérations par nom de membre ou entier ; chaînes telles quelles. Une valeur avec espace est **un
seul argument entre guillemets**, Windows le déquote avant que le processus le voie :
`"--nixx-set:srmPath=D:\mes saves"`.

Côté LaunchBox, `SuperZsnesSettings.Render` produit ces formes et `Append` n'ajoute pas un
drapeau dont le **nom** (`--nixx-set:srmPath=`, `-screen-fullscreen`, `--loadstate`) est déjà sur
la ligne saisie par l'utilisateur.

## Le catalogue

`src\SuperZsnes\SuperZsnesOptions.cs`, 67 entrées. Clé ini = `<famille>.<clé>` dans
`Local\Plugins\.data\3f6c1a2e-9b7d-4e58-a1c4-7d2f0b9e6a51\settings.ini` ; une clé présente = une
surcharge envoyée.

### Intégration (famille Plugin)

| clé ini | ligne | défaut | effet |
|---|---|---|---|
| plugin.bepinex | *(lue par LaunchBox, jamais envoyée)* | on | installer/entretenir BepInEx et le plugin |
| plugin.log | `--nixx-log` | off | journal du plugin dans `portable\nixx.log` |
| plugin.quit-confirm | `--nixx-quit-confirm=on\|off` | on | Échap demande deux fois |
| plugin.menu-key | `--nixx-menu-key=<KeyCode>` | F1 | touche du menu |
| plugin.portable | `--nixx-portable=on\|off` | on | P1 |
| plugin.support-popup | `--nixx-support-popup=on\|off` | off | popup Patreon |
| plugin.version-popup | `--nixx-version-popup=on\|off` | off (01/10) | popup nouvelle version |
| plugin.display | `--nixx-display=primary` | on (04/10) | écran principal, P7 ; réglage pour tous les jeux, pas passé pour un jeu qui règle lui-même `screen-fullscreen`, `screen-width`, `screen-height`, `popupwindow` ou `monitor` |
| plugin.persist | `--nixx-persist=on\|off` | off | écrire les surcharges dans le fichier |

Non exposé dans la fenêtre, mais accepté : `--nixx-dump-options`.

### Démarrage (Native) et Fenêtre (Unity)

| clé ini | ligne | plage |
|---|---|---|
| native.loadstate | `--loadstate` | — |
| unity.screen-fullscreen | `-screen-fullscreen 1` / `0` | fullscreen / windowed |
| unity.screen-width | `-screen-width N` | 320–7680 |
| unity.screen-height | `-screen-height N` | 240–4320 |
| unity.popupwindow | `-popupwindow` | — |
| unity.monitor | `-monitor N` | 1–8 |

### `MainMenuSettings` (famille Setting)

| champ | type | groupe |
|---|---|---|
| gfxMode | enum None/Scanlines/Gimmick3D | Display |
| scanlineStrength | float 0–1 | Display |
| interpolationMode | int 0–8 (index interne) | Display |
| noBilinearFiltering, maxBrightness, use87aspect, disableUIInput | bool | Display |
| fontType | enum Roboto/ZSNESOrigin/Upheaval/Karmatic | Display |
| guiEffect, cursorSize, cursorType | int 0–8 (index interne) | Display |
| rewindDisabled, rewindWidgetDisabled, historyDisabled, historyWidgetDisabled, enhanceWidgetDisabled | bool | Gameplay |
| rewindMode | int 0–8 | Gameplay |
| numRewindFrames | int | Gameplay |
| rewindFPS | int 1–60 | Gameplay |
| rewindSpeed | float 0–10 | Gameplay |
| autoLoadLastSaveState, lastSaveStateDisabled, startAtQuick, allowLoadFromTap, snesRumble, rightStickGameSpeed, swapAcceptCancel | bool | Gameplay |
| uiVolumeInv, gameVolumeInv, msu1VolumeInv | float 0–1 (échelle « Inv » de l'émulateur) | Audio |
| srmPath, gameSavePath, chtPath, bpsPath, loadPath | string, jetons `{exec}` `{persist}` | Files |
| noDirectoryForSaves, disableFetchDrives, welcomeDialogShown | bool | Files |

Champs de `MainMenuSettings` **non catalogués** (atteignables quand même par `--nixx-set`) :
`appletvDialogShown`, `mobileResX`, `mobileResY`, `mobileDisableCtrlMove`, `numLaunches`,
`mobileHideHUD`, `androidVibration`, `dpadTouchMove`, `dpadTouchDistance`, `mobileOrientation`,
`lastVersionChecked`, `rauserID` ; et les non-scalaires : `lastLoadedFiles`, `lastLoadedFile`,
`loadedFilesLocked`, `saveData`, `inputData`, `gameSettings`, `themeStorage`, `uiColors`,
`lastVersionCheck`, `raencT`.

### `GameSpecificSettings` (famille Game)

| champ | type |
|---|---|
| disEnhanceHires, disEnhanceTextures, disEnhance3D, disEnhanceAudio, disEnhanceOC, disEnhanceWide, disEnhanceBorder, disEnhanceGSU | bool |
| inaccurateEmuMode | bool |
| overclock | int 0–400 (%) |
| widescreenOverride | bool |
| wideScreenBG, widescreenM7, widescreenOBJ, widescreenCOL | int 0–7 (tuiles) |
| aspectOverride | int 0–8 |

Non catalogué : `gameSpecificInput` (enum `GameSpecificInput` : None, MousePort0, MousePort1,
Superscope…).

## Le schéma vivant : `--nixx-dump-options`

Le plugin écrit `portable\options.json` : pour `MainMenuSettings` (avec valeurs courantes) et
`GameSpecificSettings`, chaque propriété scalaire avec son type et, pour les enums, ses membres.
C'est la source de vérité pour vérifier le catalogue après une mise à jour : un champ du catalogue
absent du schéma est à retirer ou renommer, un champ du schéma absent du catalogue est à ajouter
si utile.

## Ajouter une option

1. **Côté émulateur** : si c'est un champ de `MainMenuSettings` ou `GameSpecificSettings`, rien à
   coder ; le moteur de réflexion le prend. Sinon, `Options.cs` (parse) et le code qui l'applique.
2. **Côté LaunchBox** : une ligne dans `SuperZsnesOptions.All` (famille, clé, type, groupe,
   libellé, aide, plage). La fenêtre et le rendu suivent. Un cas de rendu particulier va dans
   `SuperZsnesSettings.Render`.
3. **La sonde** : `src\Probe\SuperZsnesCheck.cs`, section « the command line », un `Check` sur le
   rendu.
4. Cette page.
