# Notes de conception

Ce que doit remplir un plugin d'intégration, ce qu'on refuse de faire, et les pièges qui ont coûté
du temps. Le README est en anglais et s'adresse à quelqu'un qui installe le plugin ; ce document
s'adresse à celui qui en écrira le prochain.

## 1. Le contrat `EmulatorPlugin`

Un plugin d'intégration est une classe publique non abstraite dérivant de
`Unbroken.LaunchBox.Plugins.EmulatorPlugin`. Le SDK expose **6 membres abstraits** et une trentaine
de virtuels, tous pourvus d'un défaut inerte.

### Obligatoires

| Membre | Rôle |
|---|---|
| `EmulatorName` | nom d'affichage ; doit correspondre à celui de la base de métadonnées LaunchBox |
| `GetApplicableEmulators(IEnumerable<IEmulator>)` | **quels émulateurs je revendique** |
| `GetCurrentVersion(applicationPath)` | version installée, ou `null` |
| `GetInstallableVersions()` | versions téléchargeables |
| `InstallEmulator(InstallEmulatorArgs)` | télécharge et installe |
| `IsPlatformSupported(platform)` | `Supported` + `Recommended` |

`GetApplicableEmulators` est le point dur. Les plugins d'Unbroken comparent le nom de fichier de
l'exécutable — `Path.GetFileName(ApplicationPath) == "xemu.exe"`, ou un `StartsWith("pcsx2")` — et
rien d'autre : un exécutable renommé leur devient invisible. Deux styles coexistent chez eux :
RetroArch prend **tous** les jeux qu'on lui passe (d'où des sauvegardes RetroArch remontées sur un
jeu assigné à un autre émulateur), tandis que Dolphin et PCSX2 filtrent en plus sur l'émulateur
assigné. Le second est le bon modèle.

Le résultat est **mis en cache par identifiant d'émulateur** côté hôte, y compris les refus. Un
plugin ne peut donc pas changer d'avis en cours de session.

### Optionnels, par blocs

- **Lancement** — `PrepareEmulatorForLaunch` (peut réécrire la ligne de commande via
  `NewCommandLine` ; reçoit les identifiants RetroAchievements quand l'émulateur a coché
  `LoginToCheevoOnGameLaunch`), `NormalizeCommandLineForExecutable`.
- **BIOS** — `GetBiosFilesForPlatform(appPath, platform, commandLine)`. L'hôte passe la ligne de
  commande **de la plateforme**, pas celle de l'émulateur : c'est elle qui porte le `-L <cœur>` dont
  RetroArch déduit quel jeu de BIOS s'applique.
- **Mise à jour** — `IsUpdateAvailable` a un défaut fonctionnel, mais mauvais : il compare deux
  chaînes et déclare une mise à jour dès que `GetCurrentVersion` rend `null`. À surcharger.
- **Cœurs** — interface séparée `IEmulatorWithCores`. Ne concerne qu'un multi-cœurs.
- **RetroAchievements** — `SupportsRetroAchievements`, `InjectRetroAchievementsCredentials`.
- **Import** — `GetImportActions`, `FireEmulatorImportAction`. **LiteBox ne les appelle pas.**
- **Sauvegardes** — 13 membres derrière le portillon `SupportsSaveManagement()`. Voir §6.

## 2. Ce qu'on ne fait pas comme Unbroken

Leurs plugins **dépendent du cœur obfusqué** de LaunchBox : `new Emulator{…}`,
`Root.DataManager.Platforms/Games`, `LocalDbEmulator.Get*AutoHotkeyScript`, `GamesDb`,
`NamingHelper.RootFolder`, `SevenZip.Extract`, `RushDownloader`, `HttpClientPool`. C'est pour ça
qu'un hôte comme LiteBox doit leur fabriquer un miroir de bibliothèque pour faire tourner leur
`InstallEmulator`.

**Rien de tout ça n'est nécessaire.** `IDataManager.AddNewEmulator()` et
`IEmulator.AddNewEmulatorPlatform()` sont dans le SDK public, et font le même travail. La règle du
dépôt est donc : **aucune référence à un assembly du cœur**, jamais. Le plugin tourne à l'identique
sous LaunchBox et sous LiteBox.

Équivalents utilisés :

| Chez Unbroken | Ici |
|---|---|
| `RushDownloader` / `HttpClientPool` | un `static readonly HttpClient` + boucle sur `ContentLength` |
| `SevenZip.Extract` | `System.IO.Compression.ZipFile` |
| `Paths.GetTempFilePath()` | `Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))` |
| `NamingHelper.RootFolder` | remontée depuis l'emplacement de l'assembly jusqu'au dossier ayant `Core\` et `Data\` |
| `Root.Logging.*` | `Console.WriteLine` préfixé `[<emu>]` |

### Défauts relevés chez eux, à ne pas reproduire

- `Pcsx2.InstallEmulator` : les branches de résolution de l'exécutable sont **inversées** et le
  chemin d'échec appelle `Path.GetFileName(null)`.
- `InjectRetroAchievementsCredentials` : `File.Delete` **avant** de réécrire la configuration. Une
  exception entre les deux et l'utilisateur n'a plus de configuration. → Ici, toute écriture passe
  par un fichier temporaire suivi d'un `File.Replace`.
- `GetCurrentVersion` : lance l'émulateur et fait `WaitForExit()` **sans délai maximal**.
- `FindCardByName` : retombe silencieusement sur la première carte mémoire trouvée quand la bonne
  est introuvable — une restauration peut atterrir sur la mauvaise.
- Limite de débit GitHub avalée : PCSX2 attrape l'exception et rend `null`, ce qui s'affiche
  « aucune version disponible », indiscernable d'un projet mort.

## 3. Ce que l'hôte garantit (et ne garantit pas)

- **Tout appel dans un plugin est enveloppé d'un `catch` nu.** Une exception ne casse rien — elle
  éteint la fonction en silence, ce qui est bien plus dur à diagnostiquer qu'un échec journalisé.
  D'où : on rattrape, on journalise, on dégrade.
- **`PrepareEmulatorForLaunch` ne doit jamais faire échouer un lancement.** Quelqu'un qui n'arrive
  pas à se connecter à RetroAchievements veut quand même jouer.
- LiteBox sérialise tout appel derrière un verrou global au processus (`EmuPlugins.CallGate`), parce
  que les plugins d'Unbroken appellent le cœur obfusqué, dont le déchiffreur de corps de méthode est
  un hook JIT qui n'est pas sûr en compilation concurrente. Un plugin qui ne touche que le SDK
  échappe à ce risque — argument de plus pour la règle du §2.
- `ApplicationPath` est stocké **relatif à la racine LaunchBox** quand le fichier est sous elle.

## 4. Où le plugin doit être posé

`<LB>\Plugins\<Nom>\<assembly>.dll`.

Sous LiteBox, un plugin est un **fichier à n'importe quelle profondeur** sous `Plugins\`, à
condition que ses métadonnées PE référencent `Unbroken.LaunchBox.Plugins` (rien n'est chargé pour en
décider). Sous LaunchBox 14, `System\Plugins\` appartient au gestionnaire officiel.

**Piège de nommage** : `HostBoot.IsLaunchBoxOwned` traite comme appartenant à LaunchBox — donc
active par défaut même absente de `EnabledPlugins=` — toute plugin dont le chemin contient
`LaunchBox Integration`, ou dont le DLL commence par `Unbroken.LaunchBox.`. On évite ces deux
formes : usurper le nommage d'Unbroken à côté de ses vrais plugins rendrait un diagnostic
impossible. Le prix est une case à cocher, une fois.

Le SDK est référencé avec **`<Private>false</Private>`**. Une copie du SDK à côté du plugin lui
donnerait des types `EmulatorPlugin` / `IEmulator` **différents** de ceux de l'hôte, et le test
`obj is EmulatorPlugin` de l'hôte échouerait sans un mot.

## 5. Mesurer plutôt que déduire

`src\Probe` charge un plugin hors de tout frontend et appelle la moitié en lecture du contrat. Il a
trouvé un vrai défaut à sa première exécution : l'analyse des assets GitHub par expression
régulière rendait **zéro** asset, parce que chaque asset porte un objet `"uploader":{…}` imbriqué
entre `"name"` et `"browser_download_url"`. Le motif compilait, tournait, ne levait rien, et
n'aurait produit qu'un « aucune version disponible » à l'écran. D'où l'analyseur JSON.

Deuxième leçon, sur le harnais lui-même : après une écriture **refusée**, il relisait les valeurs
laissées par un passage précédent et annonçait « OK ». Un harnais qui ment est pire que pas de
harnais. Il ne juge désormais l'aller-retour que si l'écriture s'est déclarée réussie.

## 6. Les sauvegardes PSP : la forme et pourquoi

Une save PSP est un **conteneur**, au sens de PCSX2 : `FileLocation` désigne quelque chose qui
héberge plusieurs saves (`PSP\SAVEDATA`, partagé par tous les jeux) et la save doit en être
extraite. `IsSaveContainer` rend vrai, `TryBackupSave` fait le travail.

**`FileLocation` vise le dossier primaire de l'unité, pas `SAVEDATA`.** Mesuré dans
`Host/Saves/SaveManager.cs` : `ActiveIsDirectory` se déduit de `Directory.Exists` (:1410), et
`NeedsBackup` (:492) n'a aucun bras conteneur — pointé sur `SAVEDATA`, il hacherait les saves de
**tous** les jeux du memstick à chaque rendu de page, ce qui est exactement le défaut d'Argosy dans
`SyncCoordinator.buildInventory`. Contrepartie assumée : une modification confinée à un dossier
frère n'allume pas la pastille de fraîcheur. C'est cosmétique — `NeedsBackup` n'a qu'un appelant, et
la vraie décision de copier passe par `Backup(force:false)`, qui compare l'unité **entière**
extraite.

**Ce n'est pas un « set » multi-fichiers.** La machinerie de LiteBox (`TailsOf`, `UniqueStem`,
`Renamed`) suppose des membres partageant un tronc et ne différant que par une queue ; `PARAM.SFO`,
`ICON0.PNG` et `DATA.BIN` n'ont aucun tronc commun. D'où `GetCompanionSaveFiles` qui rend vide.

### Le contrat de disposition, et pourquoi il ne se négocie pas

`TryBackupSave` écrit chaque dossier de l'unité **directement** dans `destinationFolder`, sous son
propre nom, sans niveau intermédiaire. LiteBox recopie ensuite ce dossier tel quel dans le coffre,
puis le hache avec `SaveHash.OfDirectory` et le sert avec `WriteFolderZip` — deux fonctions qui
nomment leurs entrées relativement à la racine du dossier. Écrit ainsi, on obtient
`ULUS10064DATA00/PARAM.SFO`, c'est-à-dire exactement ce qu'Argosy zippe et ce sur quoi RomM calcule
son empreinte. **Un niveau de trop et tout diverge.**

Mesuré le 2026-09-21 : `OfDirectory` appliqué au dossier de la save rend `4974D54B…`, appliqué au
parent il rend `40382D4F86536C9A4FBDBC2D9EB99E91` — le vecteur d'or publié de sigil, au bit près.
La formule est la même des deux côtés ; seule la racine décide.

`src\Probe --save-unit` fige cette décision : il extrait, vérifie qu'aucune entrée n'est à la racine,
calcule l'empreinte de RomM avec une implémentation **indépendante** de celle du plugin, et la
compare à un attendu. Le contrôle négatif compte autant que le test : avec un `--expect-hash` faux,
la sonde sort en 1.

### L'unité, et le danger de la tronquer

L'unité est **tous** les dossiers frères dont le nom commence par le DISC_ID de 9 caractères.
`PrefixBundleFolderHandler.extractDownload` d'Argosy **supprime tous les appariés** avant de
décompresser une restauration : livrer une unité incomplète ne synchronise pas mal, ça **détruit**
les dossiers omis sur l'autre appareil. Notre `AddSaveFile` fait la même chose, pour la même raison,
et la sonde vérifie qu'un autre jeu et le dossier de données installées survivent à l'opération.

## 7. La fusion des dépendances, et pourquoi elle est non négociable

Le dossier d'un plugin est un **espace de noms partagé** : l'hôte résout une dépendance par nom
simple à travers le dossier de *chaque* plugin, premier trouvé gagnant. Deux plugins embarquant deux
versions de la même bibliothèque est donc un vrai danger, pas une hypothèse d'école.

D'où l'étape `MergePlugin` à la fin de `src\Ppsspp\Ppsspp.csproj` : ILRepack `/internalize` replie
toutes les dépendances dans l'assembly et rend leurs types `internal`. Mesuré sur l'artefact réel,
la table de références du résultat ne contient **qu'une entrée non-framework** :

```
Unbroken.LaunchBox.Plugins 13.27.0.0
```

Il ne reste donc rien à résoudre, pas même un nom. `/internalize` vaut mieux qu'un renommage
d'espace de noms : les types ne sont plus visibles hors de l'assembly, et surtout **il ne subsiste
aucune identité d'assembly** capable d'entrer en conflit.

Le SDK, lui, n'est **jamais** fusionné : `Private=false` le garde hors de `bin\`, et l'hôte doit le
fournir pour que les types d'interface s'unifient (§4).

Ce que la fusion ne règle pas, et qu'il faut garder en tête : deux plugins qui revendiquent le même
émulateur (le premier qui répond gagne, et le résultat est mis en cache), et le verrou global de
LiteBox, qui fait qu'un `GetSaves` lent bloque les appels de tous les autres plugins.

## 8. Ce qui reste à faire

- Le bloc sauvegardes pour PPSSPP (v2) : dossiers `PSP\SAVEDATA\<DISC_ID><suffixe>\` énumérés par
  préfixe, titre et vignette lus dans `PARAM.SFO` et `ICON0.PNG` ; savestates
  `PSP\PPSSPP_STATE\<DISC_ID>_<DISC_VERSION>_<slot>.ppst` avec leur `.jpg` voisin, slots 0-basés sur
  disque mais 1-basés à l'écran, et des `.undo.ppst` qui ne sont pas des slots.
- Les émulateurs suivants. `Freegosy` (MIT) porte deux jeux de données qui font gagner du temps :
  un registre BIOS d'environ 37 émulateurs avec les MD5, et une table dépôt/filtres d'assets/nom
  d'exécutable pour 18 émulateurs. Le greffon melonDS en a tiré son dépôt, ses filtres d'assets et
  les MD5 des BIOS DS ; crédité dans `THIRD-PARTY.md`.
- Éprouver le dispositif DSiWare sur du vrai : il demande un dump de NAND, un BIOS DSi et un
  DSiWare avec son `.tmd`, dont aucun n'existe sur cette machine. La mécanique est couverte par la
  sonde, le jugement non.
- La mesure qui valide tout le dispositif, et qui demande un dump de NAND et un DSiWare qu'on n'a
  pas : titre importé à la main, melonDS lancé avec le `.nds` en argument, et voir s'il démarre et
  sauvegarde. Le raisonnement tient (`SetupDirectBoot` interroge `SDMMC.GetNAND()`, donc la NAND est
  montée pendant un démarrage direct), mais l'accès au save se fait à l'exécution.

