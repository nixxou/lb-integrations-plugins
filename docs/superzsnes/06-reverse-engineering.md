# 06 · Refaire une mesure : les outils et la méthode

Quand un nouveau build casse quelque chose, il faut retrouver **un nom** : la méthode qui a remplacé
celle qu'on patchait, le champ qui a changé. Voici la boîte à outils, dans l'ordre du moins cher au
plus cher.

## 0. Le journal, d'abord

Avec `--nixx-log` (ou la case « Write the plugin's diagnostic log » dans la fenêtre Nixx) le plugin
écrit `portable\nixx.log`. Sans rien, il écrit quand même `portable\nixx-errors.log` pour les
patchs non posés. Le journal BepInEx complet (`BepInEx\LogOutput.log`, y compris les lignes Unity du
jeu) se réactive dans `BepInEx\config\BepInEx.cfg` : `[Logging.Disk] Enabled = true`, et
`[Logging.Console] Enabled = true` pour la fenêtre.

Lignes du **jeu** qui servent d'oracle : `# ARGS: n`, `ARG1: …`, `FOUND Filename Arg:`,
`LOAD SETTINGS`, `LoadMainMenuSave`, `Save: <chemin>`, `TITLE: <titre>`, `Title Checksum: n`,
`Sending ROM CRC of : n`, `Save State: -last`, `CONTINUE`.

## 1. Les chaînes de `global-metadata.dat`

Sans outil. Le fichier `SUPERZSNES_Data\il2cpp_data\Metadata\global-metadata.dat` contient les
identifiants (terminés par zéro) et les littéraux (concaténés, préfixés par longueur dans une table
séparée : **ne pas** les découper en lignes, chercher un motif avec contexte).

```powershell
$b = [IO.File]::ReadAllBytes('...\global-metadata.dat')
$t = [Text.Encoding]::GetEncoding(28591).GetString($b)        # PS 5.1 : pas de Latin1, GetEncoding(28591)
$i = $t.IndexOf('srmPath', [StringComparison]::Ordinal)
$t.Substring($i-200, 500) -replace '[^\x20-\x7E]', '·'
```

Ce qu'on y trouve directement : noms de classes/champs/méthodes, URLs, extensions, textes
d'erreur. Ce qu'on n'y trouve pas : quel code appelle quoi.

## 2. Les fichiers de scène et d'assets

`SUPERZSNES_Data\level0` : textes d'interface, valeurs sérialisées de scripts (dont la version).
`resources.assets` : mods, Lua, HTML du serveur web. Même technique de recherche de motif ; les
chaînes Unity sont préfixées par leur longueur et encadrées d'octets de contrôle (utile pour
distinguer un littéral entier d'un fragment : voir la regex de version dans `SuperZsnesPaths.cs`).

## 3. Il2CppInspectorRedux : le dump complet

Il2CppDumper refuse les métadonnées v39 ; **Il2CppInspectorRedux** (LukeFZ) les lit, version 2026.2.

```powershell
git clone --depth 1 --recurse-submodules https://github.com/LukeFZ/Il2CppInspectorRedux.git
dotnet build .\Il2CppInspector.CLI\Il2CppInspector.CLI.csproj -c Release
$exe = '.\Il2CppInspector.CLI\bin\Release\net10.0\win-x64\Il2CppInspector.exe'
& $exe -i <jeu>\GameAssembly.dll -m <jeu>\SUPERZSNES_Data\il2cpp_data\Metadata\global-metadata.dat `
       --select-outputs -c types.cs -o metadata.json -p il2cpp.py            # jeu seul
& $exe -i ... -m ... --select-outputs -c types-all.cs -e none                # + Unity, System : indispensable pour nommer les appels moteur
```

Attention : il écrit aussi un dossier `cpp\` d'échafaudage C++ (90 Mo) dans le répertoire courant ;
lancer depuis un dossier de travail, pas depuis le dépôt.

- `types.cs` : classes, champs avec offsets, méthodes avec **adresses début-fin**
  (`// 0x104EC5D0-0x104EC740`).
- `metadata.json` : dont `stringLiterals[] { virtualAddress, name, string }` : l'adresse de la
  variable globale qui pointe chaque littéral.

## 4. `xref.ps1` et `refs.ps1` : lire une méthode sans désassembleur

`tools\il2cpp-xref\`. Ils supposent un dossier avec `x\GameAssembly.dll` et
`inspector\types-all.cs`, `inspector\metadata.json` (chemins en tête des scripts, à adapter).

**`xref.ps1 -Targets 'Classe.Methode'[,...] [-Hex] [-Rebuild]`** : pour chaque méthode, dans l'ordre
des octets, les `call` (`E8 rel32` vers le début d'une méthode connue) et les littéraux (mot de 32
bits égal à l'adresse d'un littéral). Premier lancement avec `-Rebuild` : construit
`inspector\symbols2.clixml` depuis `types-all.cs` (suivi de la profondeur d'accolades pour les
classes imbriquées) et `metadata.json`.

**`refs.ps1 -Methods ... -Literals ... [-From 0x… -To 0x…]`** : l'inverse, qui appelle une méthode,
qui utilise un littéral, sur une plage de code. La plage par défaut s'arrête avant `MasterExecutor`
(0x104C0000) : passer `-To 0x10510000` pour tout Assembly-CSharp.

Exemples qui ont servi :

```powershell
.\xref.ps1 -Targets MasterExecutor.Update            # qui est interrogé chaque frame → EscapePressed, ExitPressed
.\xref.ps1 -Targets StringEnc.Init -Hex              # l'ordre des push → l'ordre des arguments de Concat
.\refs.ps1 -Methods StringEnc.GetEPW                 # qui chiffre → RetroAchievements.UserProfileResult
.\refs.ps1 -Literals '.portable','{exec}' -To 0x10510000
```

Lecture x86 32 bits : les arguments sont **poussés de droite à gauche**, donc le dernier `push`
avant `call` est le premier argument ; `6A 00` pousse le `MethodInfo*` final ; `FF 35 imm32` pousse
un littéral ; `A1 imm32` charge un global. Un `E8` dont la cible n'est pas un début de méthode est
un saut dans un stub.

## 5. Process Monitor

Filtre `Process Name is SUPERZSNES.exe`. Utile pour : ordre de chargement des DLL (quelle DLL
relais serait chargée en premier), fichiers cherchés (marqueurs), registre (`Screenmanager…`,
`UnitySelectMonitor`). Pas utile pour WMI ni pour les appels internes.

## 6. Depuis l'intérieur : le plugin comme sonde

Le plus fiable pour un nouveau build : un plugin BepInEx qui **liste** par réflexion ce qu'il
cherche. `--nixx-dump-options` fait déjà ça pour les champs de réglages. Pour vérifier qu'une
méthode existe encore : `typeof(MasterExecutor).GetMethod("EscapeBackToMenu")` dans `Load()`, et
une ligne de journal. Les exceptions de Harmony (`HarmonyLib.HarmonyException`, message « … could not
be found ») nomment déjà la méthode manquante.

## Pièges rencontrés

- Le dump sans `-e none` laisse les méthodes Unity anonymes (`Application.FreeFunction` au lieu de
  `get_persistentDataPath`).
- Plusieurs méthodes vides partagent une adresse (stubs) : un `call` peut se résoudre en une liste
  de noms séparés par `|`.
- Une propriété apparaît deux fois dans `types-all.cs` (getter, setter) : `xref.ps1` suffixe `.get` /
  `.set`.
- PowerShell 5.1 : `0xFFFFFFFF` est un `Int32` négatif ; écrire `0xFFFFFFFFL` pour les CRC.
- Les littéraux de `metadata.json` peuvent se chevaucher dans une extraction naïve ; prendre les
  adresses de la table, pas des découpes de texte.
