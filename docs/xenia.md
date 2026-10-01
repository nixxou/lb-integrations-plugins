# Le greffon Xenia

Ce que fait le greffon Xenia (Canary) au-delà de l'installation et des sauvegardes, comment, et ce qui
a été mesuré. Les sources sont dans `src\Xenia` ; chaque fichier porte en tête le détail de ce qu'il
fait. Les sondes sont dans `src\Probe` (`XeniaSetupCheck.cs`, `XeniaScanCheck.cs`, `XeniaCompatCheck.cs`).

## 1. Profil et console, à la première installation

Sans profil, Xenia s'arrête à chaque démarrage sur « No Profiles Found », et un jeu ne peut pas
sauvegarder. Une **première** installation (pas une mise à jour) règle les deux, puis une notification
LaunchBox dit ce qui a été fait, avec **Yes** / **Change...** (`XeniaSetup`).

- **Le profil** (`XeniaProfile`) : un seul fichier,
  `content\<XUID>\FFFE07D1\00010000\<XUID>\Account`, 0x194 octets, chiffré HMAC-SHA1 + RC4 avec la
  clé 0x19 de `crypto_utils.cc`. Le bouton « Create Profile » de Xenia écrit exactement ce fichier :
  mesuré octet pour octet. Le nom vient du compte Windows, nettoyé selon `IsGamertagValid`.
  Xenia s'y connecte au démarrage par `logged_profile_slot_0_xuid` (section `[Profiles]` du TOML).
- **La console** (`XeniaConsole`) : `xconfig.settings`, la structure `XConfigData` brute de
  `xconfig.h`, 6680 octets, gros-boutiste. Ce n'est pas un cvar. Nos valeurs par défaut sont
  identiques octet pour octet au fichier que Xenia écrit. La langue, le pays, le fuseau et l'heure
  suivent Windows ; la langue est celle que les jeux affichent.
- Les deux s'éditent ensuite dans la page Xenia du NixxMenu (`XeniaConsolePanel`), jamais pendant que
  Xenia tourne : il réécrit son TOML en quittant.

## 2. Les options

Toutes passent en ligne de commande, `--cvar=valeur`, jamais dans le TOML : seule la valeur de config
est réécrite par Xenia, la valeur de la ligne de commande ne l'est pas (`config.cc`). Réglages communs
dans le NixxMenu, réglages par jeu dans son clic droit « Nixx-Xenia : Options... », le jeu l'emportant
(`XeniaOptions`, `XeniaOptionRows`). `--license_mask=1` est toujours passé : la version complète des
jeux XBLA.

## 3. Compatibilité

La liste vient de `compatibility_data.json`, publié toutes les 8 h par le dépôt
`xenia-canary/game-compatibility` en release : hors du quota de l'API GitHub. Téléchargée à
l'installation et à la mise à jour, redemandée en arrière-plan après une partie et à l'ouverture de
l'onglet Compatibility si la copie a plus de 8 h, en requête conditionnelle. Un fichier reçu n'est gardé
que s'il passe les contrôles de `XeniaCompat.Problem`.

## 4. Le tri des fichiers Xbox 360 (`XeniaScan`)

Un dossier et ses sous-dossiers. Fichiers ouverts : `.iso`, `.xex`, `.zar`, `.zip`, `.7z`, une
« extension » contenant un chiffre (les TU de XboxUnity), et un fichier sans extension **nommé comme un
paquet** (40 à 42 hex, `TU_...`, `tu<8>_<8>`) ou **rangé comme un paquet** (`<title id>\<type>\`). Le nom
ne sert qu'à décider quoi ouvrir : **le classement vient toujours du contenu**.

- Paquet STFS (`CON `/`LIVE`/`PIRS`) : son en-tête donne le type, le title id, le nom, le content id.
- XEX, image XDVDFS, dossier avec `default.xex` (un seul jeu), `.zar` (jeu sans id).
- Dans une archive, chaque entrée est lue **sans extraction** : on décompresse juste assez loin.
  Un ISO dans une archive est noté, pas lu.
- Un fichier retenu mais illisible est **invalide, avec sa raison**.
- Un jeu Indie (XBLIG) est rangé comme un DLC : contenu `00000002` du title id commun `584E07D2`.
  C'est un programme XNA (.NET), que Xenia ne sait pas lancer (aucun support dans son code ; mesuré sur
  Real Evil : « File not found: GAME:\default.xex ») : invalide, retiré à l'import, refusé au lancement.
- Cache `xenia-scan.tsv` par chemin, taille et date, format versionné : un second passage ne relit que
  ce qui a changé.

## 5. Mises à jour et DLC

**Où Xenia les lit** : chaque fichier de `content\0000000000000000\<title id>\000B0000` (TU : il applique
**la première**, seulement si son patch nomme l'exécutable du jeu) et `\00000002` (DLC : le jeu les voit
toutes). Un paquet LIVE/PIRS y est lu entier, son en-tête dedans : pas besoin de `.header`.

**Le rattachement, par le contenu seul** (`StfsFiles`, `XeniaPackageRead`). Les fichiers *dans* un
paquet STFS se lisent avec l'arithmétique de `stfs_container_device.cc`, portée en C#. Une TU se lit par
son `default.xexp` : son descripteur de patch donne la version source, la version cible et le
`digest_source`. Un jeu se lit par son `default.xex` : SHA-1 de sa signature RSA. **Les deux égaux,
c'est le test de Xenia** (`IsPatchSignatureProper`). Mesuré : Real Steel et sa TU v3 ont la même
empreinte ; les TU Europe et USA de Dead or Alive Xtreme 2 en ont deux différentes. Le dossier
`55E5891E/` des zip No-Intro de TU de disque, et le « (v3) » des noms, ne servent à rien.

**Le choix** : onglet « Updates & DLC » de la fenêtre d'options du jeu (`XeniaExtrasTab`). Une TU au plus
(elles sont cumulatives), celles d'une autre version du jeu grisées ; des cases pour les DLC, un même
paquet dans deux fichiers une seule fois. Par défaut, la TU qui va le plus loin et tous les DLC. Les
candidats viennent de **tout le cache** du scan, pas seulement du dossier du jeu : les sets No-Intro
rangent les TU à côté des jeux, pas dessous.

**La mise en place, à chaque lancement** (`XeniaExtras.Prepare`) :

1. Un jeu zippé est **décompressé par le greffon**, jamais par LaunchBox (`AutoExtract` forcé à non).
   Xenia le reçoit par `--target=<paquet>` : LaunchBox ajoute quand même le zip derrière la ligne, et
   Xenia le laisse de côté (mesuré).
2. **Où** : si quelque chose de ce jeu est déjà sur le disque, le disque ; sinon, si le tout passe sous
   le seuil (2 Go par défaut), un **ramdisk** pour la session (`XeniaRamSession`) ; sinon le disque.
   Chaque fichier est décompressé directement à sa place, par un `.tmp` renommé.
3. Sur le disque : `<dossier de contenu>\<jeu>\store` (une fois), `\game` pour le jeu décompressé. Un
   paquet en vrac sur le même disque est lié, pas copié.
4. `<jeu>\000B0000` et `\00000002` contiennent des liens physiques vers `store`, pour ce qui est
   choisi : changer le choix ne réextrait rien.
5. Les dossiers de Xenia pour ce title id deviennent des **jonctions** vers ces deux dossiers
   (`mklink /J`, sans administrateur). Un vrai dossier de Xenia qui a du contenu n'est jamais touché.
6. **Limite de taille** (0 = aucune) : les jeux lancés il y a le plus longtemps perdent leur dossier
   entier, jonctions comprises ; jamais celui qu'on lance, ni un jeu marqué « garder » (exclu aussi du compte).
7. **Onglet Session** (XeniaSessionTab, XeniaExtras.Plan) : tailles, destination et raison ; par jeu,
   placement automatique / toujours le ramdisk / toujours le disque, la case « garder », et la libération de ce
   qui a été extrait puis décoché (il reste sinon dans store, non lié).

Le ramdisk est celui de LiteBox, melonDS et Vita3K. Marqueur `ramdisk.where` ; libéré quand Xenia se
ferme, au lancement suivant et au démarrage de l'hôte, ses jonctions retirées avant.

Mesures sur Real Steel (01/10) : sur le disque, la TU appliquée (`0.0.0.5 -> 0.0.3.5`) et les trois
add-ons vus ; le choix changé, rien de réextrait, plus de patch et deux add-ons. Sur le ramdisk :
207 Mo sur Z: en 1,8 s, lancé en 0.0.3.5 avec ses add-ons, libéré en 20 s.

## 6. L'assistant d'import

Quand la plateforme est « Microsoft Xbox 360 », la liste de l'assistant est corrigée une fois remplie
(`XeniaLbImport`) : ses dossiers sont scannés, puis les TU, les DLC, les autres contenus et ce qui n'est
pas du Xbox 360 en sont retirés, avec la raison dans le journal. Rien n'est à enregistrer : le cache sert
au lancement. Désactivable dans la page Xenia. Sonde `--xenia-import-clean` : sur un vrai dossier,
7 TU et DLC retirés, 2 jeux gardés.

## 7. Sondes

| Sonde | Ce qu'elle vérifie |
|---|---|
| `--xenia-setup [--xconfig f] [--account f] [--toml f]` | profil, console, TOML, options : contre des fichiers écrits par Xenia |
| `--xenia-firstrun <exe>` | la configuration d'une première installation, pour de vrai |
| `--xenia-compat [--online]` | la liste de compatibilité et ses contrôles |
| `--xenia-scan [--iso f]` | le tri sur un dossier fabriqué, et le cache |
| `--xenia-scan-dir <dossier>` | le tri d'un vrai dossier, tous les champs |
| `--xenia-stfs <paquet ou archive>` | les fichiers dans un paquet, le patch d'une TU, l'empreinte d'un jeu |
| `--xenia-prepare --emu <exe> --rom <jeu> [--settings-dir d]` | la mise en place d'un lancement, pour de vrai |
| `--xenia-ram-release [--settings-dir d]` | la libération du ramdisk |
| `--xenia-import-clean <dossier>` | le filtre de l'assistant sur une fausse liste |
| `--xenia-shot <png> --emu <exe> [--rom f]` | la page Nixx et la fenêtre du jeu, dessinées |

## 8. Ce qui n'est pas mesuré

- Un lancement réel par LaunchBox d'un jeu zippé, du clic jusqu'à `--target` (mesuré sans LaunchBox,
  avec la même ligne).
- Le filtre sur le vrai assistant de LaunchBox (mesuré sur une liste qui l'imite).
- Les Games on Demand : leur empreinte n'est pas lue (SVOD), donc toutes les TU de leur title id leur
  sont proposées.
