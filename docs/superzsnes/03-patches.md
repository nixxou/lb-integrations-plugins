# 03 · Les patchs posés dans l'émulateur

Tous dans `tools\superzsnes-bepinex`, tous des `[HarmonyPatch]` résolus par nom sur les types de
`BepInEx\interop\Assembly-CSharp.dll` (ou `UnityEngine.CoreModule.dll`). Chacun est posé
**séparément** dans `Plugin.Load()` par `Patch(harmony, type, "nom de la fonctionnalité")` : si la
cible n'existe plus, cette fonctionnalité tombe et une ligne
`NOT patched - <fonctionnalité> is off for this build: <message>` part dans le journal BepInEx et
dans `portable\nixx-errors.log`. Le message contient le nom introuvable.

Pour chaque patch : la cible, ce qu'il fait, **pourquoi cette cible et pas une autre**, comment
c'est mesuré, et comment le retrouver si le nom change (voir 06 pour la méthode).

---

## P1 · `UnityEngine.Application.persistentDataPath` (getter) — préfixe

**Classe** : `PersistentDataPathPatch`. **Option** : `--nixx-portable=on|off` (défaut on).

**Fait** : répond `<exe>\portable` et saute l'original.

**Pourquoi là.** L'émulateur construit le chemin de ses réglages en **trois** endroits :
`MainMenuManager.GetConfigFilePath` (128 octets), `LoadMainMenuSave` et `SaveMainMenuSave`, chacun
appelant le getter et concaténant le littéral `/szsnes_ui.data`. Patcher `GetConfigFilePath` en
raterait deux sur trois. Le getter est ce que les trois partagent, ainsi que le jeton `{persist}`
de `CommonTools.ModifyPathWithCustomPath` et `FileHelper.GetParsedPath`. C'est une API Unity : la
cible la plus stable du lot.

**Mesuré** : journal du jeu `Save: …\x\portable/szsnes_ui.data`, LocalLow non modifié ensuite.

**Ne bouge pas** : `Player.log`, rapporteur de plantage (natifs).

**Si ça casse** : Unity ne renommera pas ce getter. Si le journal montre encore LocalLow, c'est que
le patch n'a pas été posé (voir erreurs) ou que le jeu a cessé de passer par le getter géré.

---

## P2 · `ZInputSystem.EscapePressed(bool fromGame)` — postfix

**Classe** : `EscapePatch`. **Options** : `--nixx-quit-confirm=on|off` (défaut on),
`--nixx-menu-key=<KeyCode>` (défaut F1).

**Fait**, à chaque appel, seulement si `MainMenuManager.gameRunning && !IsInMenu()` :
- si la touche de menu vient d'être pressée (`UnityInput.Current.GetKeyDown`) : réponse **vraie**
  (le menu s'ouvre exactement comme avec l'ancien Échap : état `-last`, pause, SRAM), une fois par
  `Time.frameCount` ;
- sinon si la réponse originale est vraie (Échap réel) : réponse **fausse** ;
  - pas armé → affiche « Press ESC again to quit (F1: menu) » via `GameStateUI.SetGameUIString`,
    arme une fenêtre de 2,5 s ;
  - armé et autre frame → `MasterExecutor.EscapeBackToMenu(true)` (la sauvegarde SRAM de
    l'émulateur, sa mise en pause) puis `MainMenuManager.OnExit()` (arrêt du serveur web,
    `Application.Quit`).
- la fenêtre expirée efface la ligne.

**Pourquoi là.** Lu dans `MasterExecutor.Update` : il interroge `EscapePressed()` chaque frame et,
si vrai, sauvegarde `-last`, ouvre le menu, met en pause, écrit la SRAM. Il interroge aussi
`ExitPressed()` (le bouton `Exit` du pad) et, si vrai, quitte **directement sans écrire la SRAM**.
`EscapeBackToMenu`, la cible évidente, n'est atteinte que par une autre branche : un préfixe dessus
est resté muet sur cinq appuis (mesuré). `EscapePressed` est le point où la réponse est encore un
booléen que personne n'a consommé.

**Pièges** : `Update` appelle `EscapePressed` **deux fois par frame** (offsets +031C et +049A), d'où
la garde par numéro d'image. Le nouveau système d'entrée est élagué, d'où `UnityInput`.

**Mesuré** : « Escape confirmed - saving through the emulator, then quitting », puis
`Save State: -last`, `Save: …szsnes_ui.data`, fermeture.

**Si ça casse** : `xref.ps1 -Targets MasterExecutor.Update` et chercher les appels à
`ZInputSystem.*Pressed` ; le nom de la méthode qui précède `SaveState`/`OpenMenu` est la nouvelle cible.

---

## P3 · `MainMenuManager.LoadMainMenuSave()` — postfix

**Classe** : `Overrides.AfterLoad`. **Options** : `--nixx-set:<champ>=<valeur>`, `--nixx-dump-options`.

**Fait** : sur `__instance.mainMenuSettings`, applique chaque `--nixx-set` par réflexion (propriété
du même nom, insensible à la casse, type scalaire : bool/int/float/double/long/string/enum), en
mémorisant la valeur d'origine la première fois. Écrit `portable\options.json` si demandé.

**Pourquoi là.** C'est le moment où l'objet `MainMenuSettings` existe, désérialisé du fichier, et
avant que le jeu ne s'en serve.

---

## P4 · `MainMenuManager.GetGameSettings(string filename)` — postfix

**Classe** : `Overrides.AfterGameSettings`. **Option** : `--nixx-game:<champ>=<valeur>`.

**Fait** : applique les `--nixx-game` sur l'objet `GameSpecificSettings` retourné, à chaque appel
(idempotent). Cette méthode crée l'entrée du dictionnaire `gameSettings` quand elle manque, donc
c'est aussi le seul moyen d'atteindre un jeu jamais configuré.

---

## P5 · `MainMenuManager.SaveMainMenuSave()` — préfixe + postfix

**Classe** : `Overrides.AroundSave`. **Option** : `--nixx-persist` (défaut : off, donc actif).

**Fait** : préfixe = remettre chaque valeur d'origine mémorisée par P3/P4 ; postfix = remettre les
surcharges. Le fichier écrit ne porte donc jamais nos valeurs. Avec `--nixx-persist`, ne fait rien.

**Pourquoi.** L'émulateur sérialise l'objet entier à chaque retour au menu et à la fermeture ; sans
ce sandwich, une surcharge passée une fois sur la ligne resterait dans le fichier de l'utilisateur.

**Mesuré** : à vérifier sur chaque nouveau build : lancer avec `--nixx-set:gfxMode=Scanlines`,
quitter, puis chercher `Scanlines` dans `portable\szsnes_ui.data` : il ne doit pas y être.

---

## P6 · `MainMenuManager.Update()` — postfix

**Classe** : `PopupPatch`. **Options** : `--nixx-support-popup=on|off` (défaut off = cachée),
`--nixx-version-popup=on|off` (défaut off depuis le 01/10), `--nixx-welcome-popup=on|off` (défaut
off depuis le 13/10), `--nixx-display=primary`.

**Fait**, chaque frame :
- si `supportUs.activeSelf` et popup non voulue → `SetActive(false)` (la boîte « Just this once, we
  want to let you know that you can check out our Discord server… Patreon… ») ;
- idem `newVersion` ;
- `welcomeDialog` (« Welcome to SUPER ZSNES! This build is early in development… ») fermé par son
  propre `BasicOverlay.OnClose()` (ce que fait son bouton Okay), désactivé à défaut ; et, **seulement
  dans une session où il est apparu**, une fois, `enhancementDialog` (`EnhancementOverlay`, « How to
  play: Select Game->Load… » + la liste des jeux enrichis), levé en dessous au même moment. Les deux
  n'apparaissent qu'au **premier démarrage**, quand `szsnes_ui.data` n'existe pas encore : ce n'est
  pas `welcomeDialogShown` qui les déclenche (mesuré le 13/10 : `--nixx-set:welcomeDialogShown=true`
  ne change rien). Le même dialogue d'enrichissements ouvert plus tard depuis le menu est laissé ;
- `PrimaryDisplay.Tick()` si `--nixx-display=primary` (voir P7).

**Pourquoi là.** `supportUs` et `newVersion` sont des `GameObject` publics de `MainMenuManager` ;
qui les active est indifférent, les endormir la frame où ils s'allument suffit et ne dépend d'aucune
logique interne.

---

## P7 · l'écran principal — dans le postfix P6, code `Display.cs`

**Option** : `--nixx-display=primary`.

**Fait**, machine à états sur les premières frames (abandon après 300) :
1. trouve la fenêtre Unity du processus (`EnumWindows`, classe `UnityWndClass`, PID courant) ;
2. rectangle de l'écran principal : `MonitorFromPoint((0,0), MONITOR_DEFAULTTOPRIMARY)` +
   `GetMonitorInfoW` → `rcMonitor` ;
3. si la fenêtre est déjà dessus : `Screen.SetResolution(w, h, FullScreenWindow)` si besoin ;
   sinon `Screen.SetResolution(640, 480, Windowed)`, frame suivante `SetWindowPos` sur l'écran
   principal, frame suivante `Screen.SetResolution(w, h, FullScreenWindow)`.

**Pourquoi.** `-monitor N` d'Unity est un index dans son ordre d'énumération, pas « le principal ».
Unity passe en plein écran fenêtré sur l'écran où se trouve la fenêtre. `FullScreenWindow` plutôt
qu'exclusif : le journal de l'émulateur montre « Failed to change display to
ExclusiveFullscreen...reverting to FullscreenWindow ».

**État** : écrit, compilé, **non encore observé en conditions réelles** au moment de cette doc ;
vérifier les lignes `primary display: …` du journal. (Observé depuis : 01/10 en 0.310, 13/10 en
0.320 x64, `primary display: 0,0 1920x1080 … full screen window`.)

---

## P8 · le compte RetroAchievements — `MainMenuManager.LoadMainMenuSave()` postfix, code `Cheevos.cs`

**Classes** : `Cheevos.AfterLoad` (posée seulement quand un compte attend), `Cheevos.LoginResult`
(préfixe de `ZRetroAchievements.LoginCallback`, journal seul). **Pas d'option** : c'est LaunchBox
qui décide, en passant ou non un compte au lancement (`RetroAchievementCredentials`).

**Fait** : juste avant le lancement, le greffon LaunchBox (`SuperZsnesCheevos.Write`) écrit
`portable\nixx-ra-login.bin` = `"v1\n<utilisateur>\n<jeton>"` protégé **DPAPI** pour l'utilisateur
Windows (entropie `nixx-superzsnes-ra-v1`) : le jeton n'est jamais en clair sur le disque. Le plugin
le lit et **le supprime** dans `Load()`, puis à la fin de `LoadMainMenuSave` écrit
`settings.rauserID = utilisateur` et `settings.raencT = new StringEnc().Init().GetEPW(jeton)` si le
compte n'est pas déjà celui-là. L'émulateur fait le reste lui-même, comme si l'utilisateur avait
tapé ses identifiants dans Config > Retroachievements, et garde le compte dans ses réglages, chiffré
à sa façon.

**Pourquoi là.** `raencT` est chiffré avec une clé dérivée de `SystemInfo.deviceUniqueIdentifier`
(05) : seul le processus de l'émulateur la calcule sans deviner. `LoadMainMenuSave` finit avant
`ZRetroAchievements.Start`, qui lit les réglages et se connecte (mesuré : 32,155 s puis 32,770 s).
Pas de mot de passe : LaunchBox donne le **jeton de connexion**, celui que rcheevos prend
(`rc_client_begin_login_with_token`).

**Hardcore** : jamais. La 0.320 n'en a pas (notes de version) et laisse états, rembobinage et
triches ; un client rcheevos en hardcore à côté, c'est ce qui fait signaler un compte. Le greffon
annonce `isHardcore: false` et une demande de hardcore de LaunchBox devient une ligne de journal.

**Mesuré le 13/10** (compte factice) : `RetroAchievements: a login from LaunchBox for nixxtest`,
`login set for nixxtest`, puis l'émulateur seul : `StringEnc.GetNPW -> <19 chars>`,
`rcheevos: Attempting to log in nixxtest (with token)`, `Login failed: HTTP/1.1 401 Unauthorized` ;
fichier supprimé ; au lancement suivant sans fichier, même tentative : le compte est resté dans ses
réglages. **Reste à observer** : une connexion réussie avec un vrai compte, depuis LaunchBox.

**Avant la 0.320** (pas de `ZRetroAchievements` dans l'interop) : rien n'est posé, une ligne
d'information dans `nixx.log`, **pas** d'erreur dans `nixx-errors.log` (mesuré le 13/10 sur la 0.310
avec le plugin 0.6.0 compilé contre la 0.320).

**Si ça casse** : `rauserID`/`raencT` renommés → `--nixx-dump-options` ; `StringEnc` renommé →
`--nixx-ra-probe` (qui journalise `GetEPW`/`GetNPW`) et 06 ; la connexion ne part plus de
`ZRetroAchievements.Start/Initialize` → `--nixx-ra-probe` dit qui se connecte et quand.

---

## Ce qui n'est PAS patché, volontairement

- `ZInputSystem.ExitPressed` : le bouton `Exit` du pad garde son comportement d'origine
  (`Application.Quit` direct).
- `MasterExecutor.Awake` (analyse des arguments) : il ignore déjà nos `--nixx-…` et trouve la ROM
  quelle que soit sa position.
- Le client RetroAchievements lui-même (`ZRetroAchievements`, `ZRCheevosIntegration`) : seul son
  compte est fourni (P8) ; `--nixx-ra-probe` l'observe sans le modifier.
- Les entrées manette (`inputData`) : hors périmètre pour l'instant.
