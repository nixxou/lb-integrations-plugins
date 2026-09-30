# SUPER ZSNES × LaunchBox : le dossier de bord

Ce dossier documente **tout** ce que le pack Nixx fait avec l'émulateur SUPER ZSNES : ce qui a été
mesuré sur le binaire, comment, ce qui a été construit dessus, et comment le réparer quand une mise
à jour de l'émulateur casse quelque chose. Il est écrit pour quelqu'un, humain ou agent, qui
reprend le projet sans l'avoir suivi.

Il vit à deux endroits, toujours identiques :

- dans le dépôt, `docs\superzsnes\` ;
- dans chaque dossier d'émulateur où le pack a installé BepInEx, `BepInEx\nixx-docs\`, écrit par
  le greffon LaunchBox à l'installation et remis à jour à chaque lancement. Si vous lisez ceci à
  côté de `SUPERZSNES.exe`, c'est que le pack est passé par là.

## Par où commencer

| vous voulez | lisez |
|---|---|
| comprendre ce qui tourne et où | [01-architecture.md](01-architecture.md) |
| savoir ce qu'est BepInEx ici, quelle version, quels fichiers | [02-bepinex.md](02-bepinex.md) |
| la liste des patchs posés dans l'émulateur et pourquoi chacun | [03-patches.md](03-patches.md) |
| la grammaire de la ligne de commande et le catalogue des 67 options | [04-options.md](04-options.md) |
| ce qu'on sait de l'intérieur de l'émulateur : classes, champs, fichiers, mods, RetroAchievements | [05-emulator-internals.md](05-emulator-internals.md) |
| refaire une mesure sur un nouveau build : les outils et la méthode | [06-reverse-engineering.md](06-reverse-engineering.md) |
| **quelque chose est cassé après une mise à jour** | [07-runbook.md](07-runbook.md) |
| compiler, tester, livrer | [08-build-and-test.md](08-build-and-test.md) |
| la trace datée de chaque mesure, hash et offset | [09-measurements.md](09-measurements.md) |

## Les faits en une page

- **L'émulateur.** SUPER ZSNES, par zsKnight et _Demo_, propriétaire, Unity **6000.3.6f1**, compilé
  **IL2CPP**, métadonnées **v39**, exécutable **x86 32 bits**. Build de référence : **0.310** du
  26 septembre 2026. Rien n'est obfusqué.
- **Le problème.** Pas de source, pas d'IL, pas de fichier de config lisible (NRBF), aucun mode
  portable, des adresses qui bougent à chaque build.
- **La réponse.** Deux greffons : `SuperZsnes.dll` dans LaunchBox (catalogue, téléchargement,
  mise à jour, fenêtre d'options, déploiement) et `SuperZsnes.BepInEx.dll` dans l'émulateur
  (patchs Harmony résolus **par nom**, surcharges de réglages en mémoire, confirmation d'Échap,
  popups, écran principal). Entre les deux, la **ligne de commande** : `--nixx-…`.
- **Le chargeur.** BepInEx 6 IL2CPP, build épinglé **be.788**, téléchargé et vérifié par sha256 à
  l'installation, déployé **silencieux** : ni console, ni journal disque.
- **La règle d'or.** Tout est ancré par nom (classe, méthode, champ), jamais par offset. Une mise
  à jour qui renomme coûte une fonctionnalité et une ligne dans `portable\nixx-errors.log`, pas
  le plugin.

## Vocabulaire

| terme | sens ici |
|---|---|
| greffon LaunchBox | `src\SuperZsnes`, `SuperZsnes.dll`, chargé par LaunchBox ou LiteBox |
| plugin BepInEx, plugin in-process | `tools\superzsnes-bepinex`, `SuperZsnes.BepInEx.dll`, chargé dans `SUPERZSNES.exe` |
| interop | les assemblies `.NET` générées par Il2CppInterop dans `BepInEx\interop\`, image du code du jeu |
| `portable\` | le dossier à côté de l'exe où le plugin redirige les données persistantes de l'émulateur |
| la sonde | `src\Probe`, l'hôte de test du pack, `--superzsnes` pour ce qui nous concerne |
