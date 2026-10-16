# Nixx-LaunchWithLabels — PoC

> Preuve de concept, faite le 16/10/2026 sur LaunchBox / Big Box **14.0.1.2** (`G:\LB1326`).
> But : servir de référence à l'intégration dans le projet principal. Tout ce qui est marqué
> **mesuré** a été observé dans LaunchBox ou Big Box réels. Le reste est déduit et dit comme tel.

## 1. Ce que fait le PoC

Dans le menu **« Launch With »**, chaque émulateur affiche son niveau de compatibilité avec le jeu
concerné, lu dans une table JSON :

| Hôte | Où | Rendu |
|---|---|---|
| **LaunchBox** (bureau) | clic droit sur un jeu → *Launch With...* (sous-menu WPF) | une **pastille colorée** après le nom : `Nixx-bsnes [Parfait]...` |
| **Big Box** | menu du jeu → *Launch With* (popup) | le libellé en texte : `Nixx-bsnes  [Parfait]` |

Trois propriétés à préserver dans l'intégration :

- **Affichage seulement.** Aucun émulateur, aucun jeu, aucun fichier n'est modifié. Le PoC ne
  change que des objets d'interface (un `MenuItem.Header`, un `PopupOption.DisplayString`).
  `Emulators.xml` est resté intact après les essais (mesuré).
- **Aucun patch Harmony.** Uniquement des gestionnaires de classe WPF (`EventManager.RegisterClassHandler`)
  et de la réflexion. Les raisons sont au §3.
- **Indépendant de la langue et du thème.** LaunchBox : le sous-menu est reconnu par son libellé
  localisé, lu dans le resx de LaunchBox. Big Box : la popup est reconnue par le type de son ViewModel,
  pas par la vue du thème.

## 2. Le contexte technique : LaunchBox est protégé par .NET Reactor (NecroBit)

C'est le point de départ de toute la conception.

- **Les corps de méthodes sont chiffrés.** Sur disque, une méthode comme
  `DataManager.GetAllEmulatorsForPlatform` ne contient que `nop; nop; ldnull; ret` (vu avec
  `ilspycmd -il`). Le vrai IL est fourni au JIT à l'exécution. Aucun outil statique (ILSpy, rea,
  Ghidra) ne peut donc lire la logique de LaunchBox.
- **Les métadonnées sont volontairement abîmées** : 2 lignes `Module`, 2 lignes `Assembly`, index
  GUID hors limites (vu avec rea, `inspect_managed_artifact`). L'image est marquée mixte C++/CLI, et
  il n'y a que 121 chaînes en clair pour 14 353 méthodes : les chaînes sont chiffrées aussi.
- **Ce qui reste lisible** : les noms des types et des membres publics (`Emulator.Title`,
  `SelectEmulatorMenuAction`, `PopupOption.DisplayString`…). Les champs privés et les méthodes
  internes sont renommés (`m_ProcessorEngineWriterContent`, `GenerateRandomFinalizerExplorer`…), et
  **ces noms changent d'un build à l'autre**.
- **Conséquences pratiques :**
  - on peut **appeler** une méthode protégée, y compris un accesseur : le JIT la déchiffre
    normalement. `nixx-retroarch` appelle ~64 membres protégés sans problème (son A/B probe le prouve) ;
  - on **ne peut pas patcher** une méthode protégée avec Harmony, car Harmony recopie l'IL leurre
    (testé par Mehdi, dans tous les sens) ;
  - on peut patcher le code **non protégé** : .NET, WPF, `System.Data`, `System.Xml`, Caliburn.Micro,
    Newtonsoft… et `Unbroken.LaunchBox.LocalDb.dll`, qui n'est pas chiffré (c'est sans doute pour ça que
    le hook Harmony de NixxMenus sur la lecture SQLite fonctionne).

Outils utilisés : `ilspycmd` (liste des types, IL brut, décompilation des signatures) et rea.
Le dump complet de rea dépasse son budget de 64 Mo sur `Unbroken.LaunchBox.Windows.dll`,
`LaunchBox.dll` et `BigBox.dll` ; il faut filtrer par type (`--type`).

## 3. Les approches essayées, et pourquoi elles ont été écartées

### 3a. Patcher la construction des menus avec Harmony — impossible
Les constructeurs de menus (`LaunchWithMenuAction`, `SelectEmulatorMenuAction`,
`DataManager.GetAllEmulatorsForPlatform`…) sont tous protégés.

### 3b. Renommer l'émulateur en mémoire, avec un garde-fou d'écriture — fonctionne, mais écarté
C'est le projet `src/EmuLabels` (+ son banc `src/EmuLabelsBench`), **gardé pour référence**.

- Principe : à chaque `SelectionChanged`, écrire `Titre + étiquette` dans le champ derrière
  `Emulator.Title` (trouvé à l'exécution : `m_ProcessorEngineWriterContent`), entre deux caractères
  invisibles U+2063 / U+2064. Des préfixes Harmony sur `System.Data` (`DataRow`…), sur tous les
  `XmlWriter` concrets et sur Newtonsoft / System.Text.Json retirent l'étiquette avant toute écriture.
- Ce qui marchait (mesuré) : le banc, exécuté sur la vraie classe `Emulator`, montre que l'étiquette
  atteint `Emulators.xml` sans garde-fou et ne l'atteint plus avec. LaunchBox l'affichait bien
  dans ses menus.
- Pourquoi c'est écarté :
  1. **Décalage d'une sélection dans LaunchBox** (mesuré) : le clic droit construit le menu *avant*
     que `SelectionChanged` arrive, donc on voyait l'étiquette du jeu précédent.
  2. **Le titre d'émulateur sert de clé ailleurs.** Une étiquette dans `Title` casse des correspondances
     exactes :
     - ExtendDB : `ArchiveMgsConfig.Resolve(plateforme, titre)` (`GameLaunchHook.cs:293`,
       `ProcessStartLogPatch.cs:453`) choisit le mauvais profil d'archive ; `ArchiveCacheIndex.Record`
       écrit le titre sur disque ; `ArchiveSelectGameMenuItem.cs:113` peut masquer une entrée ;
     - LiteBox : même logique via `RomConfig.Resolve` ; le slot de sauvegarde RomM vaut `"lb-" + titre`
       (`RommAssetsApi.cs:653`), donc les sauvegardes partiraient dans un autre slot ; les scripts AHK
       reçoivent `EmulatorTitle` ;
     - nixx-retroarch : `Installer.cs:304`, `GetDefaultEmulatorExistsForPlatform(emulator.Title, …)`
       pendant l'assistant d'import ;
     - MelonDs / Vita3k : retrouvent leur exécutable par `Title == PackName` (seulement en repli).
  3. **Big Box lance le jeu depuis la popup encore ouverte** (mesuré, §5) : même en limitant le
     renommage à la durée de la popup, `OnBeforeGameLaunching` et tous les plugins auraient vu le titre
     étiqueté.

### 3c. Harmony sur `ObservableCollection<T>.InsertItem` — plantage
Essayé dans la sonde Big Box pour voir les listes se remplir. Ce code générique est **partagé entre
tous les types référence**, et Harmony le réécrit mal : Big Box a planté au démarrage
(`EntryPointNotFoundException` dans `IList<T>.Insert`, pendant le chargement des ressources XAML).
**Règle : ne jamais patcher du code générique partagé.** La sonde a ensuite été refaite sans ce patch.

## 4. LaunchBox (bureau) : le sous-menu « Launch With »

Fichiers : `LaunchBoxMenus.cs`, `LaunchWithLabel.cs`, `Reach.cs`.

### Le principe (repris de `litebox-parental-managed/AdminGuard.cs`)
Un gestionnaire de classe WPF sur `MenuItem.SubmenuOpenedEvent` se déclenche pour **chaque** sous-menu
que LaunchBox ouvre, quel que soit le code qui l'a construit. Il n'y a donc pas de constructeur
protégé à trouver, ni rien à patcher. L'événement remonte (bubbling) : on ne traite que
`sender == e.OriginalSource`.

### Comment une ligne est reliée à son émulateur (mesuré)
LaunchBox **n'utilise pas** d'objet « action » pour ce sous-menu. Chaque ligne est un `MenuItem` dont
le gestionnaire `Click` est une **closure** du ViewModel, et cette closure tient l'émulateur dans un
champ :

```
MenuItem "Nixx-bsnes..."            (Parent : MenuItem "Launch _With...")
  Click -> ContentViewModelBase+<>c__DisplayClass96_3.<méthode>
           champ calculatorProcessor : Unbroken.LaunchBox.Windows.Data.Emulator
           (le jeu est aussi atteignable depuis cette closure)

MenuItem "RetroArch..."             (émulateur à plusieurs lignes de commande / cœurs)
  pas de Click ; ses enfants :
  Click -> ContentViewModelBase+<>c__DisplayClass96_4
           ._FunctionFactory -> <>c__DisplayClass96_3 -> calculatorProcessor : Emulator
```

Les noms des closures et des champs sont **propres au build**, donc on ne s'y fie pas.
`Reach` parcourt le graphe d'objets en largeur, sur 4 niveaux, à partir de `Click`, `Command`,
`CommandParameter`, `Tag` et `DataContext` (s'il est propre à l'élément). Il ne passe que par les
champs, entre dans les cibles des délégués, et n'entre jamais dans les éléments WPF ni dans
l'émulateur ou le jeu eux-mêmes. Les gestionnaires `Click` se lisent via la propriété interne
`UIElement.EventHandlersStore`, dont la méthode `GetRoutedEventHandlers(MenuItem.ClickEvent)` est
publique. Le chemin trouvé est écrit dans le journal : c'est la preuve du lien.

### Ce qui fait qu'une ligne nous concerne
**Un émulateur doit être atteignable ET la ligne doit être dans le sous-menu « Launch With ».** Ce
sous-menu est reconnu par son libellé **localisé**, lu dans le resx de LaunchBox
(`Unbroken.LaunchBox.Properties.Strings`, clés `LabelLaunchWithMenu` = « Launch _With... » et
`LabelLaunchWith`), puis normalisé : minuscules, sans `_`, sans points de suspension. Le principe
est celui d'AdminGuard. Ainsi « Open Nixx-SuperZSNES... », qui tient aussi un émulateur mais se
trouve dans le menu principal, n'est jamais touché.

### Le jeu
Pris dans la closure quand `Reach` le croise (mesuré : c'est le cas ici), sinon dans la sélection
(`StateManager.GetAllSelectedGames()`). Lire le jeu dans la closure évite le décalage du §3b.

### L'affichage
- LaunchBox **reconstruit le sous-menu à chaque ouverture** (mesuré : de nouveaux `MenuItem` à chaque
  fois). Il n'y a donc rien à restaurer. Le PoC garde quand même le texte d'origine par `MenuItem`
  (`ConditionalWeakTable`).
- Avec une couleur : `Header` devient un `StackPanel` composé d'un `AccessText` (le nom, qui garde la
  touche d'accès), d'une `Border` arrondie dans la couleur du niveau contenant le libellé, puis des
  `...` d'origine. Sans couleur : `Header` reste un texte, `"Nom" + format + "..."`.

### Couleurs des pastilles (mesurées et vérifiées)
Mesuré sur une capture du menu : fond **#3F404E**, texte **#F0F0F0**, ligne survolée **#5B5E73**. Le
texte du menu ne pouvant pas être lisible sur toutes les teintes, les pastilles sont **claires** et
leur texte **foncé (#1A1A1A)**. Contrastes WCAG :

| Niveau | Pastille | texte / pastille | pastille / menu | pastille / survol |
|---|---|---|---|---|
| Parfait | #66BB6A | 7,4:1 | 4,3:1 | 2,7:1 |
| Recommandé | #4DD0E1 | 9,5:1 | 5,6:1 | 3,5:1 |
| Jouable | #FFD54F | 12,3:1 | 7,2:1 | 4,5:1 |
| Bugs graphiques | #FFA726 | 9,0:1 | 5,3:1 | 3,3:1 |
| Ne démarre pas | #FF7961 | 6,8:1 | 4,0:1 | 2,5:1 |
| Non testé | #BDBDBD | 9,3:1 | 5,4:1 | 3,4:1 |

La première palette (vert et rouge foncés avec texte clair) était juste sous 4,5:1 pour « Parfait »
et presque invisible sur la ligne survolée (1,03:1 pour le rouge). Elle a été rejetée pour ça.

## 5. Big Box : la popup « Launch With »

Fichier : `BigBoxPopup.cs`. Tout a été mesuré avec la sonde `src/BbProbe` (thème Default).

### La chaîne
```
Entrée -> MainViewModel.OnEnter -> MethodMenuAction.OnEnter
  -> SelectEmulatorMenuAction.OnEnter
     -> new SelectOptionPopupViewModel<Emulator>("Launch With", onSelect, options, ...)
        Items : BindableCollection<PopupOption>
        PopupOption { T Value ; string DisplayString (lecture seule) ; Action<T> callback }
```

### Le lancement part de la popup encore ouverte (mesuré)
```
SelectOptionPopupViewModel.OnEnter -> PopupOption.OnEnter
  -> SelectEmulatorMenuAction.RunVirtualHandler -> MainViewModel.PlayGame
     -> PluginEventDispatcher.RaiseBeforeGameLaunching     <- popup toujours chargée
```
La popup ne s'est déchargée qu'**après** la partie. C'est ce qui interdit de toucher à l'émulateur
lui-même (§3b) : le PoC ne modifie que `DisplayString`, que seul le `TextBlock` lit. Le lancement voit
donc l'émulateur intact (journal : `emulator="Nixx-RASnes9x"`).

### Côté thèmes (fichiers XAML de `G:\LB1326\Themes`)
- `SelectOptionPopupView.xaml` (Default, Old Default) : `ListBox Name="Items"` (convention Caliburn)
  et, dans le template de ligne, `TextBlock Text="{Binding DisplayString}"`. CriticalZoneV2 n'a pas
  cette vue et retombe sur celle de base.
- Aucun thème ne mentionne « emulator ». Dans la doc des thèmes, `DefaultEmulator` et
  `DefaultEmulatorName` ne servent qu'à **afficher** l'émulateur par défaut de la plateforme : ce ne
  sont pas des choix.
- La même popup sert à d'autres listes : *Additional Versions* (`T = AdditionalApplication`), les
  vues, les thèmes… **On filtre donc sur `T : IEmulator`.**
- La vue est un simple `UserControl` dessiné par le thème. On la reconnaît par son **DataContext**
  (le ViewModel de Big Box), ce qui la rend indépendante du thème.

### Écrire `DisplayString`
Propriété en lecture seule, adossée à un champ `readonly` obfusqué (`m_ScheduledDriverID` dans ce
build). Le champ est **trouvé par sentinelle** : on donne une valeur unique à chaque champ `string`
tour à tour, celui que le getter renvoie est le bon, et on remet chaque champ immédiatement. On
l'écrit ensuite par réflexion, ce qui fonctionne sur un `readonly` d'instance. `PopupOption` ne
notifiant pas ses changements, on appelle `ListBox.Items.Refresh()`. Le point d'accroche est un
gestionnaire de classe sur `ItemsControl.Loaded`.

Le jeu est atteint via le callback de l'option : sa cible est `SelectEmulatorMenuAction`, dont la
propriété `Game` est publique. À défaut, on prend la sélection.

Les couleurs n'ont **pas** été faites côté Big Box, à la demande de Mehdi. Pistes si besoin : le
`ListBoxItem` du thème Default lie `Background` par `TemplateBinding`, mais un autre thème peut
l'ignorer, et le texte est forcé en `Foreground="White"`.

### Plantage à la fermeture de Big Box (à éclaircir)
Pendant les sessions avec la **sonde** (qui utilisait Harmony), Big Box a planté à chaque fermeture,
après le « Final Shutdown » : `NullReferenceException` dans
`SharpDX.XAudio2.VoiceShadow…OnVoiceProcessingPassStartImpl`. C'est un rappel audio appelé après
l'arrêt du moteur. Aucun plantage de ce type n'apparaît dans les journaux Windows avant le
16/10. Aucune conséquence sur les données (la sauvegarde était déjà faite). Le PoC n'utilisant pas
Harmony, **il faudrait vérifier une fermeture de Big Box avec le PoC seul**. Ça n'a pas été fait.

## 6. La table : `%LOCALAPPDATA%\lb-integrations-plugins\emu-labels.json`

Hors du dossier du plugin, parce que LaunchBox 14 vérifie le contenu d'un dossier de plugin.
Commentaires et virgules finales acceptés. Relue dès que sa date change, donc modifiable pendant que
LaunchBox tourne.

```jsonc
{
  "format": "  [{label}]",                  // forme du libellé quand il n'y a pas de pastille
  "games":     { "<id de jeu LaunchBox>":    { "<id ou titre d'émulateur>": "Parfait" } },
  "titles":    { "<titre du jeu>|<plateforme>": { "<id ou titre>": "Bugs graphiques" } },
  "platforms": { "<plateforme>":              { "<id ou titre>": "Non testé" } },
  "colors":    { "Parfait": "#66BB6A" },     // pastille (LaunchBox) ; sans couleur = texte simple
  "badgeText": "#1A1A1A"                     // couleur du texte des pastilles
}
```
Pour un émulateur donné, c'est le premier niveau qui le nomme qui l'emporte : `games`, puis
`titles`, puis `platforms`. L'émulateur est désigné par son Id ou par son titre (casse ignorée).
L'exemple en place couvre *Secret of Mana* (par id), *Yoshi's Island* (par titre) et la SNES
(par défaut).

## 7. Construire, installer, tester

```
dotnet build src\LaunchWithLabels -c Release
```
- Sortie : `bin\Release\LaunchWithLabels.dll`. Aucune dépendance à fusionner, puisqu'il n'y a pas
  de Harmony.
- Installation : `G:\LB1326\Plugins\Nixx-LaunchWithLabels\LaunchWithLabels.dll`, dans le dossier
  **classique** `Plugins\`. Dans LaunchBox 14, le dossier géré `Local\Plugins\` (avec `manifest.json`)
  **ne charge que les plugins d'émulateur** (`EmulatorPlugin`). Un plugin d'événements pur y est
  ignoré en silence (mesuré). C'est pour la même raison que `deploy-dev.ps1` place le relais NixxMenus
  dans `Plugins\`. Retiré de `G:\LB1326` après les essais.
- La DLL est verrouillée tant que LaunchBox ou Big Box tourne : il faut fermer l'hôte avant de la
  remplacer.
- Journal : `%LOCALAPPDATA%\lb-integrations-plugins\launch-with-labels.log`. On y trouve, pour chaque
  ligne, le chemin `Reach` vers l'émulateur, ainsi que la clé resx et le champ `DisplayString`
  trouvés.
- Test LaunchBox : clic droit sur *Secret of Mana* puis *Launch With* (7 lignes étiquetées) ; un
  autre jeu SNES n'a que les valeurs par défaut (3 lignes).
- Test Big Box : menu du jeu puis *Launch With*.

## 8. Les autres dossiers laissés dans le dépôt

| Dossier | Statut | Intérêt |
|---|---|---|
| `src/LaunchWithLabels` | **le PoC retenu** | — |
| `src/EmuLabels` | approche abandonnée (§3b) | `TitleField` (trouver un champ par sentinelle), `SaveGuard` (patcher toutes les écritures System.Data / Xml / JSON) |
| `src/EmuLabelsBench` | banc de l'approche abandonnée | montre comment exécuter la **vraie** classe `Emulator` protégée hors de LaunchBox (résolution des assemblies depuis `G:\LB1326\Core`) |
| `src/BbProbe` | sonde Big Box | hooks `Loaded`/`Unloaded` par ViewModel, patch du constructeur non générique `Caliburn.Micro.PropertyChangedBase` avec la pile d'appels, journalisation des lancements. **Utilise Harmony.** |

Rien n'est commité : ces 4 dossiers ne sont pas suivis, sur la branche `zinc-ex`.

## 9. Points ouverts pour l'intégration

- **Big Box, autres thèmes** : testé avec Default seulement. La détection passe par le ViewModel, donc
  elle devrait tenir, mais un thème qui dessinerait la popup sans `ItemsControl` échapperait au hook
  `ItemsControl.Loaded`.
- **Big Box, autres entrées** : `LaunchWithMenuAction` (une entrée par émulateur) existe dans Big Box,
  mais n'a jamais été vu dans le menu du jeu pendant les essais. C'est probablement ce qu'exécute le
  callback de la popup. `OpenEmulatorMenuAction` (« Open <émulateur> ») n'est pas un choix et n'est
  pas étiqueté.
- **LaunchBox, conteneurs** : une ligne type RetroArch prend l'émulateur de ses enfants s'ils en
  partagent tous un seul. Les lignes de cœurs elles-mêmes ne sont pas étiquetées.
- **Robustesse aux mises à jour** : rien ne dépend d'un nom obfusqué. On s'appuie sur des types
  publics (`IEmulator`, `IGame`, `MenuItem`, `SelectOptionPopupViewModel\`1`, `PopupOption.Value` /
  `DisplayString`), sur une clé resx (`LabelLaunchWithMenu`, avec repli en anglais et en français) et
  sur des parcours par réflexion. Un changement de structure (plus de closure, profondeur > 4) se
  verrait dans le journal : la ligne serait alors « left alone ».
- **Source des niveaux** : la table JSON est un exemple. Le format réel des données de compatibilité
  reste à définir.
