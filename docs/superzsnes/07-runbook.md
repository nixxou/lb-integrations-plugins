# 07 · Runbook : quelque chose est cassé après une mise à jour

Une procédure par symptôme. Commencer toujours par les trois premiers points.

## Les trois gestes de base

1. **Où est le journal ?** Le pack déploie BepInEx silencieux. Ouvrir `portable\nixx-errors.log`
   dans le dossier de l'émulateur : c'est là que va tout patch non posé. Pour tout le reste, cocher
   « Write the plugin's diagnostic log » dans la fenêtre Nixx (ou lancer avec `--nixx-log`) et lire
   `portable\nixx.log`. Pour le journal complet de BepInEx et les lignes du jeu, mettre
   `Enabled = true` sous `[Logging.Disk]` dans `BepInEx\config\BepInEx.cfg` et lire
   `BepInEx\LogOutput.log`.
2. **Quelle version de quoi ?** `lbip-superzsnes-build.txt` (version installée par le greffon),
   `.doorstop_version` et `BepInEx\core\BepInEx.Core.dll` (BepInEx), la première ligne de
   `nixx.log` (version du plugin), `BepInEx\LogOutput.log` ligne « Running under Unity … ».
3. **Reproduire à la main** : lancer `SUPERZSNES.exe --nixx-log "<rom>"` depuis le dossier, sans
   LaunchBox. Si ça marche à la main et pas depuis LaunchBox, le problème est la ligne de commande
   (vérifier `superzsnes.log` côté LaunchBox : `launch: +N option(s): …`).

---

## Cas 1 · L'émulateur démarre, aucune fonctionnalité Nixx n'agit, aucun journal

BepInEx ne s'est pas chargé.

- `winhttp.dll` est-il à côté de `SUPERZSNES.exe` ? Sinon : fenêtre Nixx → bouton **Install now**,
  ou réinstaller l'émulateur depuis LaunchBox.
- Un antivirus l'a-t-il retiré ? Regarder la quarantaine (les relais `winhttp.dll` sont une technique
  connue et parfois signalée). Exclure le dossier, remettre.
- `BepInEx\plugins\SuperZsnes.BepInEx.dll` présent ? Sinon le greffon LaunchBox le remet au prochain
  lancement **s'il l'embarque** : un pack compilé sans `build\bepinex\` ne le porte pas (08).

## Cas 2 · Journal BepInEx : « Unsupported metadata version found! We support 23-106, got N »

L'émulateur est passé à une version d'Unity dont les métadonnées dépassent ce que le build BepInEx
épinglé connaît (ex. 6000.5 → v107, issue BepInEx #1395).

1. Chercher sur https://builds.bepinex.dev/projects/bepinex_be un build dont le changelog mentionne
   la version N, ou tester le dernier.
2. Télécharger les zips x86 et x64, calculer leurs sha256.
3. Mettre à jour `Build` et `Packages` dans `src\SuperZsnes\SuperZsnesBepInEx.cs`, et 02-bepinex.md.
4. Dans un dossier de test : supprimer `BepInEx\core`, `dotnet`, `winhttp.dll`, extraire le nouveau
   build, lancer une fois (régénération de l'interop), recompiler le plugin (08), vérifier.
5. Si aucun build ne supporte N : BepInEx est bloqué, l'émulateur tourne sans lui. Rien à faire côté
   pack sauf attendre ; le documenter dans 09.

## Cas 3 · `nixx-errors.log` : « NOT patched - <fonctionnalité> is off for this build: … »

Une méthode ou une classe a été renommée. Le message contient le nom introuvable.

1. Régénérer le dump du nouveau build (06 §3) et chercher le nom disparu et ses voisins dans
   `types.cs` : souvent un simple renommage.
2. Pour une méthode dont on ne connaît que le rôle, repartir de l'appelant stable : 03 dit pour
   chaque patch d'où on l'a déduite (ex. P2 : `xref.ps1 -Targets MasterExecutor.Update`, chercher
   l'appel qui précède `SaveState`/`OpenMenu`).
3. Corriger le nom dans `tools\superzsnes-bepinex\*.cs` (les `[HarmonyPatch(typeof(X), "nom")]` et
   les accès directs `MasterExecutor.Instance.gameStateUI` etc.), recompiler contre la nouvelle
   interop (08), redéployer.
4. Mettre à jour 03 et 09.

## Cas 4 · « Method unstripping failed » dans le journal

Le plugin appelle une méthode Unity que ce build a élaguée (voir 02, tableau).

- Identifier l'appel : `--nixx-log` puis la pile dans la première exception journalisée
  (`EscapePressed postfix - …`, `popup patch - …`, `primary display: …`).
- Remplacer par une voie que le jeu utilise lui-même (singletons plutôt que `FindObjectOfType`,
  `UnityInput` plutôt que le nouveau système d'entrée, `Screen.SetResolution` plutôt que
  `Screen.MoveMainWindowTo`…). Vérifier dans `types-all.cs` que la méthode de remplacement a bien
  une adresse dans ce build.

## Cas 5 · Les surcharges `--nixx-set` ne font rien, avertissement « no scalar field named … »

Le champ a changé de nom ou de type dans `MainMenuSettings`.

1. Lancer une fois avec `--nixx-dump-options`, lire `portable\options.json` : la liste réelle.
2. Corriger `SuperZsnesOptions.cs` (côté LaunchBox) : renommer, retirer, ajouter. Le plugin n'a rien
   à changer, il travaille par réflexion.
3. Sonde : `dotnet .\src\Probe\bin\Release\Probe.dll src\SuperZsnes\bin\Release\SuperZsnes.dll --superzsnes`.

## Cas 6 · Les réglages se sont perdus / le fichier est revenu sous LocalLow

Soit P1 n'est pas posé (cas 3), soit BepInEx ne se charge plus (cas 1). Les réglages sont alors
relus dans `%USERPROFILE%\AppData\LocalLow\ZEMU Software Inc_\SUPERZSNES\szsnes_ui.data`. Rien n'est
perdu : `portable\szsnes_ui.data` est toujours là ; le recopier vers LocalLow ou réparer P1.

## Cas 7 · Échap ouvre le menu au lieu de demander confirmation

- Vérifier `--nixx-quit-confirm` n'est pas `off` (fenêtre Nixx).
- `nixx-errors.log` : P2 non posé → cas 3 ; `EscapePressed` a peut-être changé de nom ou de
  signature (le postfix attend `ref bool __result`).
- Si le patch est posé mais muet : `xref.ps1 -Targets MasterExecutor.Update` ; si `Update` n'appelle
  plus `EscapePressed`, la nouvelle méthode interrogée est la cible.

## Cas 8 · La popup Patreon revient

`MainMenuManager.supportUs` renommé ou déplacé. `types.cs`, classe `MainMenuManager`, chercher un
`GameObject` au nom proche ; sinon `refs.ps1 -Literals 'Just this once'` pour trouver la classe qui
porte le texte.

## Cas 9 · Le premier lancement après mise à jour dure une minute et « ne fait rien »

Normal : Cpp2IL et Il2CppInterop régénèrent `BepInEx\interop\` en silence. Attendre. Si ça ne
revient pas : cas 2. Pour voir le déroulé : `[Logging.Console] Enabled = true`.

## Cas 10 · La mise à jour de l'émulateur n'est pas détectée dans LaunchBox

Côté greffon : `superzsnes.log`. « the home page no longer carries a Windows download the way this
plugin reads it » → la page a changé de forme : ajuster `SuperZsnesSite.ParsePage` (regex
`<h3>Windows</h3>` + `href`), la sonde a les fixtures. « version.txt has no Windows line » → le flux
a changé. « the site's version … is not a number » → le nom de fichier a perdu son numéro : le flux
prend le relais, sinon corriger l'extraction `_v(\d+(?:\.\d+)+)`. Version installée non lue :
`SuperZsnesPaths.InstalledVersion` cherche `v<n>.<nnn>[lettre]` encadré d'octets de contrôle dans
`SUPERZSNES_Data\level*` ; vérifier que la caption « v0.xxx » est toujours là (06 §2).

## Cas 11 · Le déploiement échoue : « does not match its pinned sha256 »

Le zip téléchargé n'est pas celui attendu : builds.bepinex.dev a republié le build (rare) ou le
téléchargement est corrompu/intercepté. Ne jamais désactiver la vérification ; re-hasher un
téléchargement manuel, comparer, et si le site a vraiment changé le fichier, réépingler (cas 2,
étapes 2-3).

## Après toute réparation

- `dotnet .\src\Probe\bin\Release\Probe.dll src\SuperZsnes\bin\Release\SuperZsnes.dll --platform "Super Nintendo Entertainment System" --superzsnes`
- un lancement réel avec `--nixx-log --nixx-set:gfxMode=Scanlines`, puis vérifier que
  `portable\szsnes_ui.data` ne contient pas « Scanlines » (P5), qu'Échap demande deux fois (P2), que
  la popup ne revient pas (P6).
- mettre à jour 03, 05 et 09 avec ce qui a changé et la date.
