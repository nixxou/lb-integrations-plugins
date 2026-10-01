# 01 · Architecture

## Deux processus, un contrat

```
LaunchBox / LiteBox                                  SUPERZSNES.exe
┌──────────────────────────────┐                     ┌──────────────────────────────────────┐
│ SuperZsnes.dll (greffon)     │                     │ UnityPlayer.dll                       │
│  · catalogue, téléchargement │   ligne de          │  └ charge winhttp.dll (Doorstop)      │
│  · version, mise à jour      │   commande          │      └ CoreCLR .NET 6 (dotnet\)       │
│  · onglet fenêtre Nixx       │ ───────────────►    │          └ BepInEx chainloader        │
│  · PrepareEmulatorForLaunch  │  --nixx-… + ROM     │              └ SuperZsnes.BepInEx.dll │
│  · déploie BepInEx + plugin  │                     │                  · patchs Harmony      │
│    + cette doc               │                     │                  · surcharges          │
└──────────────────────────────┘                     │ GameAssembly.dll (le jeu, IL2CPP)     │
                                                     └──────────────────────────────────────┘
```

Le seul canal entre les deux est la **ligne de commande** que LaunchBox construit : la ligne de
l'émulateur (celle que l'utilisateur a saisie), ce que le greffon y ajoute dans
`PrepareEmulatorForLaunch`, puis le chemin de la ROM que l'hôte colle à la fin. Rien d'autre :
pas de fichier d'échange, pas de tube, pas de registre. C'est volontaire : la ligne est visible
dans LaunchBox, reproductible à la main, et l'émulateur ignore tout argument qu'il ne connaît pas.

## Ce que fait chaque côté

### Côté LaunchBox (`src\SuperZsnes`)

| fichier | rôle |
|---|---|
| `SuperZsnesPlugin.cs` | le contrat `EmulatorPlugin` : revendication de l'exe, versions, installation, ligne de catalogue, `PrepareEmulatorForLaunch` |
| `SuperZsnesSite.cs` | zsnes.com : la carte Windows de la page, `version.txt`, le téléchargement, l'estampille d'installation |
| `SuperZsnesPaths.cs` | l'exe, la version lue dans `level0`, le fichier de réglages (LocalLow ou `portable\`) |
| `SuperZsnesOptions.cs` | le **catalogue** des options, cinq familles |
| `SuperZsnesSettings.cs` | `settings.ini` de l'utilisateur, rendu de la ligne de commande, `Append` sans doublon |
| `SuperZsnesSettingsPage.cs` | l'onglet SUPER ZSNES de la fenêtre Nixx, plus le bouton d'installation de BepInEx |
| `SuperZsnesBepInEx.cs` | le **déployeur** : archive épinglée, hash, extraction, config silencieuse, plugin, docs |
| `SuperZsnesAhk.cs` | les scripts AutoHotkey (aujourd'hui : sortie Alt+F4, le reste en commentaires) |
| `Archives.cs`, `Log.cs` | extraction par-dessus, journal `%LOCALAPPDATA%\lb-integrations-plugins\superzsnes.log` |

Le greffon **embarque** `SuperZsnes.BepInEx.dll` (quand il a été compilé, voir 08) et ce dossier de
doc comme ressources, et les écrit dans le dossier de l'émulateur. Deux moments : à
l'installation ou mise à jour de l'émulateur (avec téléchargement de BepInEx), et à chaque
lancement pour remettre plugin et doc s'ils manquent ou diffèrent (sans réseau). Le bouton de
l'onglet fait la même chose que l'installation pour un émulateur déjà présent.

### Côté émulateur (`tools\superzsnes-bepinex`)

| fichier | rôle |
|---|---|
| `Plugin.cs` | point d'entrée BepInEx, lecture des options, pose des patchs **un par un**, journal |
| `Options.cs` | la grammaire `--nixx-…`, conversion des valeurs |
| `Overrides.cs` | surcharges de `MainMenuSettings` et `GameSpecificSettings` par réflexion, restauration autour des sauvegardes, export du schéma |
| `Display.cs` | plein écran fenêtré sur l'écran principal, par Win32 |
| `NixxLog.cs` | journal du plugin : `portable\nixx.log` sur demande, `portable\nixx-errors.log` toujours |

## Les dossiers

### Dans le dossier de l'émulateur, après déploiement

```
SUPERZSNES.exe, UnityPlayer.dll, GameAssembly.dll, SUPERZSNES_Data\   l'émulateur, intacts
winhttp.dll, doorstop_config.ini, .doorstop_version                   Doorstop (BepInEx)
dotnet\                                                               runtime .NET 6 x86
BepInEx\core\                                                         BepInEx, Cpp2IL, Il2CppInterop, HarmonyX
BepInEx\config\BepInEx.cfg                                            écrit silencieux par le pack, jamais réécrit
BepInEx\interop\                                                      généré au premier lancement, 112 assemblies
BepInEx\unity-libs\                                                   téléchargé au premier lancement
BepInEx\cache\                                                        cache de BepInEx
BepInEx\plugins\SuperZsnes.BepInEx.dll                                notre plugin
BepInEx\nixx-docs\*.md                                                cette documentation
BepInEx\LogOutput.log                                                 SEULEMENT si le journal BepInEx est réactivé
portable\szsnes_ui.data                                               les réglages de l'émulateur, déplacés ici
saves\<rom>.srm, states\<rom>.data.szsnes\, cheats\<rom>.cht         ce que l'émulateur écrit pour un jeu (DataFolders.cs, --nixx-data-folders)
portable\options.json                                                 le schéma des options, sur --nixx-dump-options
portable\nixx.log                                                     notre journal, sur --nixx-log
portable\nixx-errors.log                                              nos erreurs, toujours (patch non posé…)
lbip-superzsnes-build.txt                                             version installée, écrite par le greffon
```

Ce qui reste hors du dossier : `Player.log` d'Unity et le rapporteur de plantage, sous
`%USERPROFILE%\AppData\LocalLow\ZEMU Software Inc_\SUPERZSNES\` (le côté natif d'Unity ne passe pas
par le getter patché), et les clés `Screenmanager…` d'Unity dans
`HKCU\Software\ZEMU Software Inc.\SUPERZSNES`.

### Dans LaunchBox

```
Local\Plugins\Nixx-SuperZSNES\SuperZsnes.dll, manifest.json, LbIntegrations.Catalog.dll
Local\Plugins\.data\3f6c1a2e-9b7d-4e58-a1c4-7d2f0b9e6a51\settings.ini      les options cochées
Emulators\Nixx-SuperZSNES\                                                 l'émulateur installé par le greffon
%LOCALAPPDATA%\lb-integrations-plugins\superzsnes.log                      le journal du greffon
```

## Le flux d'un lancement

1. LaunchBox appelle `PrepareEmulatorForLaunch`. Un lancement à la fois, comme pour tout le pack
   (`Shared.Lbip\LbipLaunchGate.cs`) : refusé tant que le précédent n'est pas fini (+1 s), en silence
   dans ses 5 premières secondes (un double clic), et refusé aussi si `SUPERZSNES.exe` est déjà ouvert.
   Sinon, le greffon lit `settings.ini`, remet plugin et
   doc dans le dossier de l'émulateur si BepInEx y est et qu'ils manquent, puis ajoute les
   drapeaux à la ligne : `--nixx-…` et `-screen-…`, sans répéter un drapeau déjà présent.
2. LaunchBox lance `SUPERZSNES.exe <ligne> "<rom>"`.
3. `UnityPlayer.dll` charge `winhttp.dll` depuis le dossier de l'exe (ordre de recherche Windows) ;
   Doorstop démarre CoreCLR, BepInEx charge nos assemblies d'interop puis notre plugin.
4. `Plugin.Load()` : lecture des `--nixx-…`, pose des patchs un par un.
5. Le jeu démarre : `MasterExecutor.Awake` lit ses propres arguments, trouve la ROM (premier
   argument en `.smc/.sfc/.zip/.swc/.ufo`), ignore les nôtres. `LoadMainMenuSave` charge les réglages
   depuis `portable\` (getter patché), notre postfix applique les surcharges.
6. En jeu : Échap → confirmation ; F1 → menu ; popup Patreon → endormie ; écran principal → forcé.
7. À la fermeture : `SaveMainMenuSave` écrit `portable\szsnes_ui.data` **sans** nos surcharges
   (préfixe qui restaure, postfix qui réapplique).
