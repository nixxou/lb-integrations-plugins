# melonDS et le DSi — tout ce qui a été mesuré

Ce document s'adresse à quelqu'un qui démarre sur un contexte neuf, pour **forker melonDS** et y
intégrer la gestion des DSi directement, en mémoire, sans NAND temporaire sur disque.

Il rassemble tout ce qu'on a établi en construisant le greffon d'intégration `src/MelonDs`, qui fait
la même chose **de l'extérieur** — donc avec des contraintes que le fork n'aura pas. Chaque fait
ci-dessous a été mesuré sur une machine réelle, pas lu sur un wiki. Quand c'est une déduction et non
une mesure, c'est écrit.

Les références de ligne pointent le tag **1.1** de `melonDS-emu/melonDS`, publié le 2025-11-18.

---

## 1. Pourquoi un fork changerait tout

Le greffon travaille depuis l'extérieur : il ne peut parler à melonDS que par son fichier de
configuration et par une copie de NAND sur disque. Ça impose trois choses dont un fork se passerait.

**Une image de 240 Mo sur disque.** Pour installer un titre DSiWare dans une NAND, il faut une NAND
inscriptible. Le greffon copie donc la NAND de l'utilisateur dans `dsi\work.bin` et installe dedans.
Un fork ouvrirait l'image en lecture seule et appliquerait l'installation dans un tampon mémoire, ou
en copie-sur-écriture — plus de copie, plus de cache, plus de question de place disque.

**Pas d'événement de fin de session.** Le greffon ne sait pas quand melonDS a fini. Il capture donc
l'état **au lancement suivant**, avant de reconstruire. Un fork sait exactement quand la NAND est
démontée.

**Rien ne peut être refusé proprement.** `PrepareEmulatorForLaunch` ne peut pas annuler un lancement ;
au mieux il ouvre une fenêtre et melonDS échoue ensuite avec sa propre erreur. Un fork refuse avant.

Ce qui ne change pas, en revanche : **une NAND ne se fabrique pas**. `NANDImage` s'ouvre sur un
fichier existant et en lit le `ConsoleID` (`DSi_NAND.h:52-64`, `:77`) ; le déchiffrement dépend de
données propres à la console plus la clé ES lue dans `dsi_bios7.bin` à `0x8308`. Un fork aussi devra
partir du dump de quelqu'un.

---

## 2. Carte des sources melonDS

| fichier | ce qu'on y a lu |
|---|---|
| `src/DSi_NAND.h` / `.cpp` | toute la NAND : `NANDImage`, `NANDMount`, `ImportTitle`, `ImportFile`, `ExportFile`, `RemoveFile`, `ListTitles`, `GetTitleDataMask`, `ImportTitleData` / `ExportTitleData`, `ReadUserData` / `ApplyUserData`, `DSiSerialData`, l'énum `ConsoleRegion` |
| `src/NDS_Header.h` | l'en-tête de ROM, l'énum `RegionMask` (`:29-39`), les prédicats DSi (`:206`, `:219`), `static_assert(sizeof(NDSHeader) == 4096)` |
| `src/DSi_TMD.h` | `TitleMetadata`, `static_assert(sizeof(...) == 520)` |
| `src/fatfs/ff.c`, `ffconf.h`, `ffsystem.c` | le système de fichiers, `FF_USE_MKFS 1`, et `get_fattime()` à `ffsystem.c:107` |
| `src/FATStorage.cpp:1115` | le seul `f_mkfs` de l'arbre — pour les images de carte SD |
| `src/frontend/qt_sdl/EmuInstance.cpp` | `verifySetup:633-665`, `loadFirmware:1012-1050`, `getAssetPath:445-484`, `getSavestateName:696-707`, `SetupDirectBoot` côté DSi |
| `src/frontend/qt_sdl/EmuInstanceInput.cpp` | `buttonNames:30`, `hotkeyNames:46`, `inputLoadConfig:114`, l'encodage joystick `joystickButtonDown:359-405`, l'encodage clavier `:302-318` |
| `src/frontend/qt_sdl/Config.cpp` | le TOML, les défauts épars `:49-128`, `FindDefault:663-679`, `Save:809-819` |
| `src/frontend/qt_sdl/Window.cpp` | les raccourcis câblés `:353-401`, les extensions acceptées `:95-135` |
| `src/frontend/qt_sdl/CLI.cpp` | la ligne de commande, `:36-116` |
| `src/frontend/qt_sdl/TitleManagerDialog.cpp` | l'import graphique, `:146-169`, et l'URL NUS `:485` |

---

## 3. Le DSiWare : ce qui marche et ce qui ne marche pas

### 3.1 Le levier est `Emu.DirectBoot`, pas `DSi.FullBIOSBoot`

`DirectBoot` à vrai — le défaut de melonDS — fait appeler `SetupDirectBoot` et la ROM démarre
immédiatement. À faux, la console démarre par son firmware, qui en mode DSi est **le menu DSi tenu
dans la NAND** (`EmuInstance.cpp:1469-1473`, `:1951-1954`).

`FullBIOSBoot` ne fait qu'éviter d'écraser le vecteur de reset du BIOS par `0xEAFFFFFE`. Ce n'est pas
le bon levier, et on a perdu du temps dessus.

### 3.2 Un DSiWare démarré directement perd sa sauvegarde — MESURÉ DEUX FOIS

Passé en argument, melonDS ne refuse pas un `.nds` DSiWare : `UnitCode & 0x02` l'envoie dans la
branche DSi de `SetupDirectBoot`, la NAND est montée, le jeu s'ouvre sans menu. **Et sa sauvegarde
n'arrive jamais dans le dossier de données du titre.**

Première mesure, sur un titre installé avec un tmd **fabriqué** : `public.sav` inchangé. Résultat
laissé en suspens parce que ce tmd s'est révélé mauvais par ailleurs.

Deuxième mesure, sur un titre installé depuis de vraies métadonnées signées :

```
nand.bin     a changé          melonDS a bien écrit dans l'image
public.sav   identique         rien du jeu n'est arrivé dans sa sauvegarde
```

Et le jeu l'a dit à l'écran : *« 99Bullets data was corrupted and has been deleted »*. Même cet
effacement n'a pas atterri. L'extraction a tourné **après** que la NAND a changé et a ressorti les
mêmes octets : l'échec est en amont d'elle.

**Deux défauts sans rapport produisaient la même phrase à l'écran** — un tmd non signé refusé par le
menu DSi, et ceci. C'est ce qui a fait prendre deux passes.

Conclusion : un DSiWare se démarre **par le menu DSi**, ce qui coûte un clic. Pour un fork, c'est le
premier endroit à creuser : pourquoi la branche direct-boot n'attache pas la sauvegarde du titre.

### 3.3 Le menu DSi vérifie la signature

Un tmd fabriqué à partir de l'en-tête de la ROM porte tout ce que melonDS **lit**, mais n'est pas
signé. Mesuré : un titre importé avec un tmd fabriqué démarrait en cartouche et échouait depuis le
menu. Le même titre avec le vrai tmd démarre.

C'est une déduction forte, pas une lecture de code : on n'a pas trouvé où le menu vérifie.

---

## 4. Les métadonnées de titre (TMD)

### 4.1 Structure, mesurée contre un vrai fichier

```
taille                520 octets   (static_assert dans DSi_TMD.h)
title id              0x18C, GROS-boutiste, catégorie puis id
version du titre      0x1DC, gros-boutiste, 2 octets
SHA-1 du contenu      0x1F4, 20 octets
PublicSaveSize        PETIT-boutiste dans les vrais tmd
```

**Le piège :** le getter de melonDS lit `PublicSaveSize` en gros-boutiste. C'est sans conséquence
pour lui parce que les tailles viennent de l'en-tête de la ROM, pas du tmd — mais un fork qui
fabrique un tmd doit l'écrire en petit-boutiste, sinon il produit un fichier qui ne ressemble pas à
un vrai. On s'est trompé une fois dans ce sens, plus `SrlFlag` mis à 1 alors qu'il vaut 0.

### 4.2 Un fichier téléchargé fait 2312 octets

520 de métadonnées puis 1792 de chaîne de certificats. Mesuré sur 1889 fichiers de préservation :
**il n'existe que deux queues distinctes**, celle-là et vide. melonDS lit `sizeof(TitleMetadata)` et
ne regarde jamais plus loin — la queue est donc 3,1 Mo d'une même valeur répétée.

### 4.3 Le serveur de Nintendo répond encore

```
http://nus.cdn.t.shop.nintendowifi.net/ccs/download/<catégorie><id>/tmd
```

C'est l'adresse que le dialogue de melonDS utilise lui-même (`TitleManagerDialog.cpp:485`). Mesuré :
200 avec 2312 octets, dont les 520 premiers sont octet pour octet le tmd que melonDS avait écrit
quand on faisait l'opération à la main. La boutique DSi a fermé ; le serveur de distribution, non.

### 4.4 La révision se choisit par empreinte, pas par date

Une centaine de titres ont plusieurs révisions, et un tmd porte le SHA-1 du **contenu** qu'il décrit.
Le bon tmd pour un dump donné est donc celui dont l'empreinte **est** celle du dump — une question à
réponse exacte, pas un pari sur « probablement la plus récente ».

---

## 5. La NAND

### 5.1 Ce qu'il y a dedans

Une NAND vierge (dump 1.4.5) : **53 fichiers, 55 répertoires, 26,6 Mo** dans une image de 240 Mo.
Compressés, 21 Mo. Les gros morceaux sont les applications système du DSi.

```
0:/sys/HWINFO_S.dat                        identité console
0:/shared1/TWLCFG0.dat, TWLCFG1.dat        réglages utilisateur (deux copies)
0:/shared2/0000, 0:/shared2/launcher/...   données du menu
0:/ticket/00030004/<id>.tik                le ticket d'un titre
0:/title/<cat>/<id>/content/00000000.app   le titre lui-même
0:/title/<cat>/<id>/content/title.tmd
0:/title/<cat>/<id>/data/public.sav        sa sauvegarde
```

Catégories (dsibrew) : `00030004` DSiWare, `00030005` applications intégrées, `00030015` applications
système, `00030017` menu système.

### 5.2 Le système de fichiers commence à `0x10EE00`

En dessous : MBR, chargeur *stage2*, et le pied de page `"DSi eMMC CID/CPU"` qui porte l'**eMMC CID**
et le **ConsoleID** — ce dont dérive la clé de déchiffrement.

**Mesuré : rien n'y bouge pendant une session.** Comparaison octet à octet entre `base.bin` et une
NAND réellement jouée : 1529 blocs de 4 Ko différents, **zéro sous `0x10EE00`**, pied de page
identique. Tout ce qui change pendant une partie est un fichier.

### 5.3 Ce qui change après une session

Reconstruire une image (base + install du titre) et la comparer à celle qui a tourné donne le delta.
Deux mesures, sur deux sessions différentes :

```
session 1     4 fichiers, 65 Ko + la sauvegarde du jeu
session 2    11 fichiers, 4,2 Mo
```

Toujours les mêmes catégories : `shared1/TWLCFG0.dat` et `TWLCFG1.dat`, `shared2/launcher/wrap.bin`,
`shared2/0000`, `sys/log/sysmenu.log`, la sauvegarde du jeu, et les sauvegardes privées des
applications intégrées et du menu.

**La leçon pour un fork :** la sauvegarde d'un DSiWare n'est PAS son `public.sav`. Mesuré, c'était
16 Ko sur 4,2 Mo. Le reste, c'est la console qui a bougé autour.

### 5.4 Comparer deux NAND se fait par FICHIER, jamais par octet

`get_fattime()` (`ffsystem.c:107`) renvoie l'heure système, et chaque entrée de répertoire FAT la
porte. Mesuré : **deux installs identiques à une seconde d'intervalle donnent des octets différents
et des manifestes identiques.**

Un delta d'octets serait donc du bruit, et surtout il ne s'appliquerait pas à une référence
régénérée plus tard. Un fork qui veut une comparaison stable doit soit comparer par fichier, soit
épingler `get_fattime` — ce qui est possible sans toucher l'arbre melonDS, par un
`COMPILE_DEFINITIONS` sur la seule unité `ffsystem.c`.

`CreateTicket` (`DSi_NAND.cpp:917`) est en revanche entièrement déterministe : pas de hasard, pas
d'horodatage.

### 5.5 La région se lit dans la NAND

`0:/sys/HWINFO_S.dat`, octet **`0x90`**, valeurs de `ConsoleRegion` (`DSi_NAND.h:210-218`) :
0 Japon, 1 USA, 2 Europe, 3 Australie, 4 Chine, 5 Corée.

**L'offset est mesuré, pas déduit** : le fichier annonce `EntrySize = 0x1C`, et
128 (le HMAC RSA-SHA1) + 4 + 4 + 28 = 164, exactement le `static_assert` sur `DSiSerialData`.
Vérifié sur six dumps — AUS, CHN, EUR, JPN, KOR, USA — la région lue correspond au nom dans les six
cas, et le masque de langues à `0x88` correspond à `AmericaLanguages`, `EuropeLanguages` etc.

Le fichier fait 16 Ko dont 164 utiles, le reste à `0xFF`.

### 5.6 Reconstruire une NAND depuis ses fichiers : faisable, non fait

Les pièces existent : `f_mkfs` est compilé (`ffconf.h: FF_USE_MKFS 1`) et melonDS s'en sert déjà pour
ses images de carte SD (`FATStorage.cpp:1115`) ; `ImportFile` remet un fichier ; il faut garder
verbatim le squelette sous `0x10EE00` (1,11 Mo, 0,32 Mo compressé) et le pied de page.

Total : **21,3 Mo au lieu de 252 Mo, soit 12×**. L'image, elle, ne se comprime qu'à 75,8 % — elle est
chiffrée.

Ce qui reste inconnu : que `f_mkfs` reproduise la géométrie exacte que le DSi attend (type de FAT,
taille de cluster, secteurs réservés). C'est le seul chantier du lot qui dégraderait mal — une NAND
qui ne démarre pas ne se répare pas. Il se teste sans risque en reconstruisant dans un fichier neuf
et en comparant par fichier avec l'original.

---

## 6. La région d'un jeu

Trois sources dans l'en-tête, concordantes, par ordre d'autorité :

| source | offset | exemple mesuré |
|---|---|---|
| `DSiRegionMask` | `0x1B0`, u32 petit-boutiste | `0x02` = USA |
| `GameCode` | `0x0C`, 4 ASCII, la 4ᵉ est la région | `K99E` |
| title id bas | `0x230` — **c'est le même game code** | `4b393945` |

Bits du masque (`NDS_Header.h:29-39`) : Japon 1, USA 2, Europe 4, Australie 8, Chine 16, Corée 32 ;
`0xFFFFFFFF` = sans région.

**Le title id bas EST le game code**, lu en u32 petit-boutiste — donc la lettre de région est son
**octet de poids faible** : `0x4b393945` → `0x45` = `'E'`. Vérifié aussi sur le menu système d'une
NAND USA, `484e4145` → `E`.

Table des lettres (dsibrew) : `J` Japon, `E` USA, `P` Europe, `U` Australie, `C` Chine, `K` Corée,
`O` USA+Europe+Australie, `T` USA+Australie, `V` Europe+Australie, `A` sans région,
`L`/`B` → USA, et `D F H I M N Q R S W X Y Z` → Europe.

Le masque est préféré : il sait dire « plusieurs régions » et « sans région » sans table de cas
particuliers. Un masque à 0 est un champ jamais rempli, **pas** un titre sans région.

---

## 7. Les pré-requis, et un piège qui se paie

Lu dans `verifySetup` (`:633-665`) et `loadFirmware` (`:1012-1050`) :

| on lance | il faut |
|---|---|
| jeu DS, `Emu.ExternalBIOSEnable` faux | **rien** — FreeBIOS + firmware généré |
| jeu DS, `ExternalBIOSEnable` vrai | `bios7.bin`, `bios9.bin`, `firmware.bin` |
| **DSiWare** | les deux BIOS DSi, le firmware DSi, **et une NAND de la bonne région** |

**LE PIÈGE :** `verifySetup` ne vérifie le firmware DSi que si `ExternalBIOSEnable` est vrai, ce qui
le fait passer pour optionnel. Il ne l'est pas : la branche « BIOS intégré » de `loadFirmware` pour
le mode DSi est un `// TODO` **vide** (`:1016-1019`) qui retombe sur l'ouverture de
`DSi.FirmwarePath`. Les deux étapes se contredisent.

Autre conséquence : `verifySetup` exige le BIOS **DS** dès que `ExternalBIOSEnable` est vrai, **quel
que soit le type de console**. Un lancement DSi peut donc être refusé pour une raison qui n'a rien
de DSi.

Tailles exigées (`:487-607`) :

```
BIOS ARM9 DS     0x1000       BIOS ARM9 DSi    0x10000
BIOS ARM7 DS     0x4000       BIOS ARM7 DSi    0x10000
firmware DS      0x20000 (accepté, « non amorçable »), 0x40000 ou 0x80000
firmware DSi     0x20000 exactement
```

Les deux BIOS DSi ont la même taille. Pour les distinguer par contenu : l'**ARM7 porte la clé ES à
`0x8308`** et l'ARM9 a des zéros là. Mesuré. C'est aussi le discriminant fonctionnel : la clé ES est
ce qui déchiffre une NAND.

Un firmware DSi est à **97,2 % de `0xFF`** contre 1,9 % pour un firmware DS — de quoi les séparer
quand tous deux font 128 Ko.

---

## 8. Entrées et raccourcis

### 8.1 melonDS ne livre AUCUN mapping

`Instance*.Keyboard` et `Instance*.Joystick` valent `-1` dans la table des défauts
(`Config.cpp:51-52`) et il n'existe aucune autre table de défauts, aucun bouton « restaurer » dans le
dialogue d'entrée. **Une installation neuve ne répond à rien.**

### 8.2 Clavier : des codes de touche Qt

Table `[Instance0.Keyboard]`, clés `A B Select Start Right Left Up Down R L X Y` plus les `HK_*`
(`EmuInstanceInput.cpp:30`, `:46`). Valeurs vérifiées contre une vraie configuration :

```
16777234 = Qt::Key_Left     16777220 = Qt::Key_Return
16777235 = Qt::Key_Up       16777219 = Qt::Key_Backspace
16777236 = Qt::Key_Right    16777217 = Qt::Key_Tab
16777237 = Qt::Key_Down     32 = espace, une lettre = son ASCII majuscule
```

Qt nomme une touche par le caractère qu'elle **produit**, donc un mapping écrit en lettres suit la
disposition de son propriétaire.

### 8.3 Manette : du joystick BRUT, pas GameController

`SDL_GameControllerOpen` n'est ouvert que pour la vibration et les capteurs
(`EmuInstanceInput.cpp:244-261`). Toutes les entrées passent par `SDL_JoystickGetButton`, `GetHat`,
`GetAxis` — **des index bruts, spécifiques au périphérique**.

Encodage d'une liaison, un seul entier (`joystickButtonDown:359-405`) :

```
-1                        rien
16 bits bas != 0xFFFF     un BOUTON, son numéro
  ... sauf si bit 0x100   un CHAPEAU : numéro (v>>4)&0xF, direction v&0xF
                          1 haut, 4 bas, 2 droite, 8 gauche
bit 0x10000               un AXE : numéro (v>>24)&0xF, direction (v>>20)&0xF (0 positif, 1 négatif)
```

Décodé sur une vraie table de manette Xbox : `257` = chapeau 0 haut, `4` et `5` = boutons LB et RB.

### 8.4 Savestates et sortie : câblés, non configurables

L'énumération `HK_*` (`EmuInstance.h:36-59`) ne contient **ni sauvegarde d'état, ni chargement, ni
quitter**. Ce sont des raccourcis de menu Qt figés :

```
Window.cpp:359   Shift+F1..F8   sauver l'emplacement 1 à 8
Window.cpp:375   F1..F8         charger
Window.cpp:387   F12            annuler un chargement
Window.cpp:401   Ctrl+Q         quitter (QKeySequence::Quit)
```

Emplacements 1 à 8, numérotés pareil sur le disque et à l'écran. **`.mln` est un leurre** : il
n'apparaît que comme filtre de boîte de dialogue (`Window.cpp:1583`). Les fichiers sont `.ml1`
à `.ml8` (`getSavestateName:696-707`).

---

## 9. Fichiers, chemins, formats

**La configuration est `melonDS.toml`** à côté de l'exécutable. `pathInit` (`main.cpp:180-214`) :
un **dossier** `portable\` à côté de l'exe gagne, sinon `WIN32_PORTABLE` pointe sur le dossier de
l'exe ; la branche `%APPDATA%` n'est pas compilée. `PORTABLE` est `ON` par défaut
(`CMakeLists.txt:196-201`).

**Les défauts sont épars.** `FindDefault` (`Config.cpp:663-679`) remonte de segment en segment, donc
une clé absente **prend sa valeur par défaut** au lieu d'être déliée. On peut écrire un TOML minimal.
Contraste avec `controls.ini` de PPSSPP, dont `LoadFromIni` efface le mapping par défaut de chaque
action nommée.

**melonDS réécrit le fichier en quittant** (`Config::Save`, `:809-819`) : il tronque et resérialise
`RootTable`. Les clés inconnues survivent, les commentaires non. **Corollaire : n'écrire que melonDS
fermé.**

**Une sauvegarde est `<nom de ROM sans la dernière extension>.sav`** (`getAssetPath:445-484`).
Depuis une archive, le nom vient de l'**entrée intérieure**, pas de l'archive.

**Extensions acceptées** (`Window.cpp:95-135`) : ROM `.nds .srl .dsi .ids` ; archives `.zip .7z .rar
.tar` et toute la famille `tar.*` de libarchive, `ARCHIVE_SUPPORT_ENABLED` étant posé sans condition.

**La ligne de commande** (`CLI.cpp:36-116`) : une ROM en positionnel, `-f/--fullscreen`,
`-b/--boot auto|always|never`, `-a/--archive-file`. **Rien** sur le type de console, la NAND, les
DSiWare, l'import de titre, ni le choix du fichier de configuration. Identique sur `master`.

C'est pour ça que le greffon passe par le TOML, et c'est la première chose qu'un fork peut corriger.

---

## 10. L'API NAND, et ce qu'on a dû y ajouter

`DSi_NAND.h:84-128` offre déjà : `ImportTitle`, `DeleteTitle`, `TitleExists`, `ListTitles`,
`GetTitleInfo`, `GetTitleDataMask`, `ImportTitleData` / `ExportTitleData`, `ImportFile`,
`ExportFile`, `RemoveFile`, `RemoveDir`, `ReadUserData` / `ApplyUserData`, `ReadHardwareInfo`.

**Ce qui manque en amont : l'énumération de répertoire.** `f_opendir` / `f_readdir` sont utilisés à
trois endroits (`DSi_NAND.cpp:599`, `:760`, `:830`) mais jamais exposés. On l'a ajouté côté C
(`tools/melonds-nand`) et c'est ce qui a permis toutes les mesures de la section 5.

**Contrainte à connaître :** le constructeur par déplacement de `NANDMount` est supprimé parce que
fatfs garde un pointeur **global** vers le système monté. Une seule NAND ouverte à la fois.

**`NANDMount::ImportTitle` n'a aucun point d'entrée hors du QDialog.** C'est pour ça qu'on a construit
une cible CMake à part, liée à `core` seul — `DSi_NAND.cpp` est dans la bibliothèque `core`
(`src/CMakeLists.txt:5`, `:26`), pas dans `src/frontend/qt_sdl/`. Un fork n'a pas ce problème.

Le code de `tools/melonds-nand` reste un bon point de départ : il isole exactement les appels utiles,
et son `main.cpp` en fait un outil en ligne de commande (`list`, `exists`, `import`, `delete`,
`export-save`, `import-save`, `export-file`, `import-file`, `remove-file`, `walk`).

---

## 11. Les pièges qui ont coûté du temps

**Les offsets de l'en-tête NDS ne se calculent pas depuis la structure.** Le `static_assert` ne
vérifie que la taille totale ; le padding ment. Mesurer.

**`DSiSerialData`, en revanche, est compacte**, et son `EntrySize` le prouve à la lecture.

**Un `.rar` qui « ne se lit pas » peut être un `.rar` qui s'ouvre très bien et ne contient pas ce
qu'on cherche.** Séparer « est-ce une archive » de « a-t-elle pu être ouverte ».

**Deux défauts sans rapport peuvent produire la même phrase à l'écran.** Cf. § 3.2.

**Un assembly se résout à l'entrée d'une méthode**, pas à l'exécution de la ligne qui l'utilise — un
`try/catch` à l'intérieur arrive trop tard. Vrai côté .NET ; l'équivalent côté C++ est le chargement
différé d'une DLL.

**`Process.GetProcesses()` ne voit pas un émulateur qui n'a pas encore démarré.** Entre écrire un
fichier et lancer le processus, il y a une fenêtre où « l'émulateur ne tourne pas » est vrai et
trompeur.

---

## 12. Ce que le greffon fait aujourd'hui, en résumé

Pour comparaison, et pour savoir ce qu'un fork rend inutile.

```
<install>\bios\                      BIOS, firmware, NAND par région (fournis par l'utilisateur)
<install>\dsi\work.bin               l'image de travail, reconstruite à chaque lancement
<install>\dsi\work.title             quel titre elle porte, + empreinte de la ROM + NAND source
<install>\dsi\nands.txt              région de chaque dump, en cache
<install>\dsi\<titleid>\title.tmd    les métadonnées retenues
<install>\dsi\<titleid>\reference.txt  le parcours d'une install fraîche
<install>\dsi\<titleid>\state\       les fichiers qui en diffèrent — LA sauvegarde
```

Au lancement d'un DSiWare : résoudre la région → choisir la NAND → capturer la session précédente →
reconstruire `work.bin` → installer le titre → parcourir (c'est la référence) → réappliquer l'état →
pointer `DSi.NANDPath` et `Emu.ConsoleType = 1`, `DirectBoot = false`.

Relancer le même jeu depuis la même ROM sur la même NAND **réutilise l'image telle quelle** — cache
de un, l'éviction étant la reconstruction suivante.

Les quatre sources de tmd, dans l'ordre : `<rom>.nds.tmd` à côté de la ROM, l'index embarqué
(1889 entrées, 0,6 Mo, blocs deflate de 16 entrées, index en clair pour la dichotomie), le serveur de
Nintendo, puis un tmd construit — non signé, et le journal le dit.

---

## 13. Ce qui reste ouvert

- **Pourquoi le démarrage direct n'attache pas la sauvegarde du titre.** C'est la question qui vaut
  le fork : la NAND est montée, le dossier de données existe, et rien n'y arrive.
- **Où le menu DSi vérifie la signature d'un tmd.** Déduit, jamais lu.
- **La reconstruction d'une NAND depuis ses fichiers** (§ 5.6) — `f_mkfs` et la géométrie.
- **Un firmware DSi généré.** Le `// TODO` de `loadFirmware:1016-1019`. S'il existait, le DSi
  n'aurait plus besoin d'un firmware fourni.
