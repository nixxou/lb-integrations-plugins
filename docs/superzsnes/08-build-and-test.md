# 08 · Compiler, tester, livrer

## Prérequis

- .NET SDK 9 ou 10 (le pack cible net9.0-windows ; le plugin BepInEx cible net6.0 pour le runtime
  6.0.7 embarqué par BepInEx ; le SDK 10 le compile avec un avertissement NETSDK1138 sans
  conséquence).
- `dotnet tool restore` à la racine (ILRepack).
- **Pour le plugin BepInEx** : un dossier SUPER ZSNES **lancé une fois avec BepInEx dedans**, donc
  possédant `BepInEx\interop\Assembly-CSharp.dll`. Le projet référence ces assemblies générées ; il
  ne peut pas se compiler sans. Passer le dossier en `-p:SuperZsnesDir=…` ou via la variable
  d'environnement `SUPERZSNES_DIR`.

## Le plugin BepInEx

```powershell
dotnet build tools\superzsnes-bepinex\SuperZsnes.BepInEx.csproj -c Release -p:SuperZsnesDir=D:\Emulators\Nixx-SuperZSNES
```

Sortie par défaut : `<SuperZsnesDir>\BepInEx\plugins\SuperZsnes.BepInEx.dll` (compiler = déployer
dans ce dossier de test). Pour que le greffon LaunchBox l'**embarque**, il doit être dans
`build\bepinex\SuperZsnes.BepInEx.dll` : `-p:OutDir=<dépôt>\build\bepinex\`. `build-release.ps1` et
`deploy-dev.ps1 -Plugin SuperZsnes` le font.

Références : `BepInEx\core\{BepInEx.Core, BepInEx.Unity.IL2CPP, BepInEx.Unity.Common, 0Harmony,
Il2CppInterop.Runtime, Il2CppInterop.Common}.dll` et `BepInEx\interop\{Assembly-CSharp,
Il2Cppmscorlib, UnityEngine, UnityEngine.CoreModule, UnityEngine.InputLegacyModule}.dll`,
toutes `Private=false` : rien n'est copié à côté du plugin, l'émulateur les fournit.

## Le greffon LaunchBox

```powershell
dotnet build src\SuperZsnes\SuperZsnes.csproj -c Release
```

Produit `bin\Release\merged\SuperZsnes.dll` (ILRepack, dépendances repliées). Embarque
`docs\superzsnes\*.md` (toujours) et `build\bepinex\SuperZsnes.BepInEx.dll` (si présent) comme
ressources `docs/…` et `bepinex/…`. Vérifier :

```powershell
[Reflection.Assembly]::LoadFile((Resolve-Path src\SuperZsnes\bin\Release\merged\SuperZsnes.dll)).GetManifestResourceNames()
```

## La sonde

```powershell
dotnet build src\Probe\Probe.csproj -c Release
dotnet .\src\Probe\bin\Release\Probe.dll src\SuperZsnes\bin\Release\SuperZsnes.dll --platform "Super Nintendo Entertainment System" --superzsnes
```

(`dotnet Probe.dll` plutôt que `Probe.exe` : l'apphost fraîchement compilé est parfois bloqué par
l'antivirus quelques secondes.) `--superzsnes` couvre, hors ligne et sur des fixtures : la page et le
flux du site, le calcul de version, le contrat du greffon, la ligne de catalogue, les scripts, le
rendu de la ligne de commande, l'onglet WinForms (construit sur un thread STA, relu, sauvegardé), et
le déploiement de BepInEx depuis une archive forgée. Les sections réseau ordinaires interrogent
zsnes.com. Tout doit finir par « OK - SUPER ZSNES agrees with what was measured ».

Ce que la sonde **ne** couvre pas : le comportement dans l'émulateur. Pour ça, un lancement réel :

```
SUPERZSNES.exe --nixx-log --nixx-display=primary --nixx-set:gfxMode=Scanlines --nixx-game:overclock=150 --nixx-dump-options "<rom>"
```

et lire `portable\nixx.log`, `portable\options.json`, puis vérifier que `portable\szsnes_ui.data`
ne contient pas « Scanlines » après fermeture.

## Déployer en développement

```powershell
.\deploy-dev.ps1 -Plugin SuperZsnes -LbRoot 'G:\LB1326' -SuperZsnesDir 'D:\Emulators\Nixx-SuperZSNES'
```

Compile le plugin BepInEx (si `-SuperZsnesDir` est utilisable, sinon avertissement), puis le
greffon, et copie `SuperZsnes.dll` + `manifest.json` + `LbIntegrations.Catalog.dll` dans
`Local\Plugins\Nixx-SuperZSNES\`, vérifié par hash. Redémarrer LaunchBox.

## Livrer

```powershell
.\build-release.ps1 -SuperZsnesDir 'D:\Emulators\Nixx-SuperZSNES'
```

Étape 0 : compile le plugin BepInEx dans `build\bepinex\` (**obligatoire** : sans dossier de jeu,
le script s'arrête, parce qu'un pack qui installe BepInEx sans rien mettre dedans est pire que pas
de BepInEx). Puis les sept greffons, le relais de menus, les natifs, et `release\NixxIntegrations.exe`.

## Liste de contrôle avant release

- [ ] `--superzsnes` de la sonde vert
- [ ] un lancement réel : P1 (Save: …portable), P2 (Échap ×2), P5 (Scanlines absent du fichier), P6
      (popup absente), P7 (écran principal) lus dans `nixx.log`
- [ ] `build\bepinex\SuperZsnes.BepInEx.dll` embarqué (ressource `bepinex/…`)
- [ ] `docs\superzsnes\*.md` embarqués (ressources `docs/…`), et 09 daté
- [ ] `Build` / `Packages` de `SuperZsnesBepInEx.cs` cohérents avec 02
- [ ] THIRD-PARTY.md à jour

## Convention de version du plugin BepInEx

`[BepInPlugin(..., "0.5.0")]` et `Plugin.Version` dans `Plugin.cs`. Incrémenter à chaque changement
de comportement ; la première ligne de `nixx.log` la répète.
