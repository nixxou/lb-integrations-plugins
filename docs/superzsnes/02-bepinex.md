# 02 · BepInEx

## Ce que c'est, et pourquoi lui

BepInEx est un chargeur de mods pour Unity. Sa variante **IL2CPP** met un runtime .NET complet dans
le processus du jeu et rend le code compilé adressable **par nom** depuis du C# ordinaire. C'est la
seule des cinq pistes essayées (voir 09-measurements, §4 du doc historique) qui donne du contrôle
sur l'émulateur et survit aux mises à jour sans table d'offsets.

| pièce | rôle | version dans be.788 |
|---|---|---|
| Doorstop | `winhttp.dll` relais posé à côté de l'exe ; `UnityPlayer.dll` l'importe statiquement, Windows la prend dans le dossier de l'exe avant System32 (mesuré au ProcMon : première DLL cherchée là) ; elle démarre CoreCLR et appelle BepInEx | 4.5.0 |
| `dotnet\` | runtime .NET embarqué, même architecture que le jeu | 6.0.7 x86 |
| Preloader / Chainloader | découvre `BepInEx\plugins\*.dll`, les charge, appelle `Load()` | be.788 |
| Cpp2IL | lit `global-metadata.dat` + `GameAssembly.dll`, produit les types et signatures | métadonnées 23 → 106 |
| Il2CppInterop | génère `BepInEx\interop\*.dll`, une classe .NET par type IL2CPP, chaque champ exposé comme propriété (`get_srmPath` / `set_srmPath`), chaque méthode comme appel natif | 1.5.3 |
| HarmonyX + Dobby | `[HarmonyPatch]` sur une méthode interop = détour natif sur l'adresse résolue par nom à l'exécution | 2.10.2 |

## Le build épinglé

**`6.0.0-be.788+5b766a3`**, 1er septembre 2026, depuis https://builds.bepinex.dev/projects/bepinex_be.

| paquet | URL | sha256 |
|---|---|---|
| win-x86 | `https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.788%2B5b766a3.zip` | `D5954A5993EC39CD1133603D85BFF93875D30B6411B712CC13DCF03C8E08A4D3` |
| win-x64 | `https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip` | `F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A` |

Épinglé et non « latest » : un chargeur n'est pas quelque chose à laisser dériver sous un plugin
compilé contre lui. Téléchargé et non embarqué : LGPL 2.1, 30 Mo, et le pack ne redistribue rien
de lui. Les deux constantes sont dans `src\SuperZsnes\SuperZsnesBepInEx.cs` (`Build`, `Packages`).
L'architecture est lue dans l'en-tête PE de `SUPERZSNES.exe` (`0x14C` x86, `0x8664` x64).

### Ce qui compte dans l'historique de BepInEx

- commit `3fab71a` « Improved support for IL2CPP metadata v23-106 (Unity 6+) » : c'est ce qui rend
  la v39 de SUPER ZSNES lisible ; les builds antérieurs à 785 refusent avec
  « Unsupported metadata version found! We support 23-31, got 39 ».
- build 785 : « Revert Cpp2IL back to development.1452 because of regressions ».
- issue #1395 (ouverte, août 2026) : la v107 d'Unity 6000.5 n'est pas encore supportée. Si SUPER
  ZSNES passe en 6000.5, il faudra un build BepInEx plus récent : voir 07-runbook.

## L'installation dans un dossier d'émulateur

L'archive est **extraite par-dessus** le dossier de l'exe (`Archives.ExtractOver`) : `winhttp.dll`,
`doorstop_config.ini`, `.doorstop_version`, `dotnet\`, `BepInEx\core\`, `changelog.txt`. Rien de
l'émulateur n'est touché. Puis le déployeur écrit :

- `BepInEx\config\BepInEx.cfg` **si absent** : la config silencieuse (ci-dessous) ;
- `BepInEx\plugins\SuperZsnes.BepInEx.dll` si absent ou différent (octet à octet) ;
- `BepInEx\nixx-docs\*.md`, pareil.

`doorstop_config.ini` n'est pas modifié : sa cible par défaut est
`BepInEx\core\BepInEx.Unity.IL2CPP.dll`, CoreCLR dans `dotnet\`.

### La config silencieuse

```ini
[Logging.Console]

Enabled = false

[Logging.Disk]

Enabled = false

WriteUnityLog = false
```

BepInEx complète toutes les autres clés à son premier passage. Ce fichier n'est **jamais réécrit**
par le pack : un utilisateur qui a remis `Enabled = true` pour diagnostiquer garde son réglage.

## Le premier lancement

Dans l'ordre, une bonne minute au total, et **rien à l'écran** puisque la console est coupée :

1. Doorstop → CoreCLR → BepInEx.
2. `InteropManager` télécharge `https://unity.bepinex.dev/libraries/6000.3.6.zip` (les assemblies
   Unity gérées de la version exacte) dans `BepInEx\unity-libs\`. **Réseau nécessaire** ; le zip
   est réutilisé ensuite.
3. Cpp2IL lit les métadonnées : « Using actual IL2CPP Metadata version 39 », 91 572 méthodes
   cartographiées, ~10 s.
4. Il2CppInterop génère 112 assemblies dans `BepInEx\interop\`, ~20 s. `Assembly-CSharp.dll`
   (1,4 Mo) est le jeu.
5. Le chainloader charge notre plugin, le jeu démarre.

Les lancements suivants sautent 2 à 4 tant que ni le jeu ni BepInEx ne changent.
`UpdateInteropAssemblies = true` (défaut) refait la génération quand `GameAssembly.dll` ou
`global-metadata.dat` changent : **une mise à jour de l'émulateur se régénère seule** au premier
lancement suivant.

## Ce qui est élagué, et les contournements

IL2CPP retire du build ce que le jeu n'utilise pas. Deux victimes mesurées, signature
« Method unstripping failed » dans le journal :

| élagué | contournement |
|---|---|
| `UnityEngine.Object.FindObjectOfType<T>()` | les singletons du jeu : `MasterExecutor.Instance`, `MainMenuManager.Instance`, `ZInputSystem.Instance`, `RomLoader.Instance`, `RetroAchievements.Instance` |
| `UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame` (nouveau système d'entrée) | `BepInEx.UnityInput.Current.GetKeyDown(KeyCode)`, qui passe par l'ancien `Input`, que `MasterExecutor.Update` appelle lui-même |

## Ce que le pack ne fait pas avec BepInEx

- pas de mise à jour automatique de BepInEx : le build est épinglé, le changer est une décision
  (07-runbook, cas 4) ;
- pas de plugin autre que le nôtre ; `BepInEx\plugins\` est à l'utilisateur ;
- pas de journal : ni console, ni `LogOutput.log`, sauf réactivation manuelle ;
- pas de retrait automatique : supprimer `winhttp.dll` suffit à désactiver tout BepInEx, l'émulateur
  redevient exactement ce qu'il était.
