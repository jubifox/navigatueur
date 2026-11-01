# Refonte de Navigatueur — audit et plan technique

Ce document couvre les étapes 1 (audit) et 2 (plan) de la refonte. Le détail de
ce qui a été vérifié, et comment, est dans [TESTS.md](TESTS.md).

---

## 1. Audit

### 1.1 Technologies

Le dépôt contient trois applications distinctes :

| Dossier | Plateforme | Technologie | Statut |
|---|---|---|---|
| `src/Navigatueur.App` + `src/Navigatueur.Core` | Windows | **WPF (.NET 8) + Microsoft Edge WebView2** (Chromium) | Application principale, v0.17.2 |
| `linux/` | Linux | Tauri 2 + WebKitGTK | Portage partiel (v0.15), non lancé sur un vrai bureau |
| `android/` | Android | Kotlin + Jetpack Compose + WebView système | Port mobile |

Ce n'est **ni Electron, ni CEF, ni Qt** : la version Windows est une application
WPF native qui embarque Chromium via le contrôle WebView2. La refonte porte sur
cette version, la seule visée par le cahier des charges (souris, bords d'écran,
raccourcis clavier, F11). Le portage Linux et l'app Android ne sont pas modifiés.

### 1.2 Architecture actuelle (Windows)

```
MainWindow (WPF, fenêtre principale)
├── barre de titre WPF (32 px) + barre d'outils WPF (rangée de grille)
├── ContentHost ─ un BrowserTabView par onglet ─ WebView2 (HWND natif)
│                                                  └─ msedgewebview2.exe (processus Chromium)
├── TabSidebarWindow       ← fenêtre Win32 de premier niveau séparée (onglets)
├── WebViewOverlayWindow   ← fenêtre Win32 séparée, transparente, « click-through »
│                            (traînée du curseur + faux coins arrondis)
└── MusicOverlayWindow     ← mini-lecteur flottant (voulu)
PrivateBrowsingWindow      ← deuxième implémentation complète d'un navigateur
SettingsWindow, HistoryWindow, ExtensionsWindow, WelcomeWindow, ExtensionPopupWindow
```

Organisation : MVVM léger (CommunityToolkit.Mvvm), services statiques dans
`AppServices`, réglages JSON dans `%AppData%\Navigatueur\settings.json`,
historique/extensions/cache des listes dans `%LocalAppData%\Navigatueur`.

### 1.3 D'où vient la « séparation » entre application et WebView

Deux causes, toutes deux réelles :

1. **Le moteur est une application externe.** `WebView2EnvironmentService` crée
   l'environnement avec `browserExecutableFolder: null`, c'est-à-dire le runtime
   *Evergreen* « Microsoft Edge WebView2 Runtime », installé et mis à jour
   séparément par Windows. Sans lui, Navigatueur ne démarre pas d'onglet (et
   `EnsureLoadedAsync`, `async void`, plante sur l'exception).
2. **Le problème d'« airspace » de WPF.** WebView2 affiche la page dans un HWND
   natif ; aucun élément WPF de la *même* surface ne peut être dessiné par-dessus.
   Pour superposer la colonne d'onglets et la traînée du curseur, le code a créé
   des **fenêtres de premier niveau séparées** (`TabSidebarWindow`,
   `WebViewOverlayWindow`, et en v0.15 `ToolbarWindow`), recollées à la fenêtre
   principale à chaque déplacement. Conséquences constatées dans le code et
   l'historique : décalage pendant les déplacements/redimensionnements (masquées
   pendant le glisser en v0.16), coins « arrondis » simulés par des masques
   peints, synchronisation manuelle de l'image de fond entre fenêtres.

### 1.4 Fonctionnalités existantes (inventaire)

Conservées par la refonte : onglets (épinglage, sourdine, mise en veille LRU
3 onglets actifs + 15 min), groupes (création, glisser-déposer, renommage,
couleurs, repli, sauvegarde/réouverture), session restaurée, nouvelle fenêtre
(Ctrl+N), navigation privée, historique, téléchargements, extensions Chrome
(dont fenêtres popup), bloqueur de pubs (réseau + cosmétique), anti-hameçonnage
avec page d'avertissement, autorisations des sites mémorisées, zoom, traduction,
enregistrer sous (MHTML), recherche dans la page, Picture-in-Picture, couleur
`theme-color` du site, thème clair/sombre + accent, images de fond, curseur
personnalisé + traînée, mini-lecteur musique + icône de zone de notification,
mises à jour automatiques via GitHub, page nouvel onglet, raccourcis.

### 1.5 Limitations et défauts relevés

| Constat | Où |
|---|---|
| Fenêtres séparées pour superposer l'UI (airspace) | `TabSidebarWindow`, `WebViewOverlayWindow` |
| Dépendance au runtime WebView2 système ; plantage s'il manque | `WebView2EnvironmentService`, `BrowserTabView.EnsureLoadedAsync` |
| La recherche depuis la barre d'adresse ignore le moteur choisi (Bing codé en dur) | `UrlHelper.Normalize` |
| La navigation privée est une seconde implémentation, sans les nouveautés | `PrivateBrowsingWindow` |
| Glisser-déposer d'onglets et historique utilisent l'onglet de la fenêtre *principale*, même depuis une fenêtre secondaire | `TabSidebarWindow.OnTabDrop`, `HistoryWindow.OnEntryClick` |
| Pas de favoris | — |
| Pas de restauration du dernier onglet fermé, pas de F11 | — |
| Le plein écran HTML5 (vidéo) ne remplit que la zone de l'onglet | `BrowserTabView` |
| Le moniteur mémoire interroge WMI toutes les 5 s en permanence | `MemoryMonitorService` |
| La traînée dans la page passe par un message inter-processus à chaque mouvement de souris | `BrowserTabView.GetCursorInjectionScript` |
| Fenêtre maximisée débordant de l'écran (WindowChrome sans compensation) | `MainWindow` |
| Paramètres : une colonne de boutons, sans organisation | `SettingsWindow` |

À noter : le commentaire de `MainWindow.OnWindowPreviewKeyDown` affirme que les
raccourcis ne fonctionnent pas quand le focus est dans la page. C'est inexact :
la décompilation du contrôle WebView2 WPF (SDK 1.0.4314) montre que
`CoreWebView2Controller.AcceleratorKeyPressed` est relayé en `PreviewKeyDown` /
`KeyDown` WPF sur l'élément WebView2. La refonte s'appuie dessus.

---

## 2. Plan technique

### 2.1 Architecture cible

```
MainWindow  (une seule fenêtre Win32 de premier niveau)
├── ContentHost ─ BrowserTabView ─ WebView2 (HWND enfant, rendu natif Chromium)
└── OverlayLayer (Canvas)
    ├── OverlayHost « barre d'outils »  ┐  HWND *enfants* de la même fenêtre,
    ├── OverlayHost « onglets »         ├─ hébergeant chacun un arbre WPF, avec
    └── OverlayHost « chargement »      ┘  transparence par pixel (Windows 8+)
```

**Choix clé : des fenêtres enfants plutôt que des fenêtres séparées.**
`OverlayHost` est un `HwndHost` qui crée un `HwndSource` `WS_CHILD` de la fenêtre
principale. Une fenêtre enfant :

- appartient à la même fenêtre : elle se déplace et se redimensionne *avec* elle,
  sans décalage ni astuce de masquage, n'apparaît nulle part ailleurs ;
- peut être placée au-dessus du HWND de WebView2 dans l'ordre Z (ce que WPF ne
  sait pas faire dans une même surface) — `OverlayHost` force `HWND_TOP` à
  chaque positionnement ;
- supporte la transparence par pixel (`HwndSourceParameters.UsesPerPixelTransparency`,
  calques enfants Windows 8+) : coins arrondis et ombre réels. Si la plateforme
  ne la fournit pas, le panneau passe automatiquement en style plat opaque.

**Alternative écartée : `WebView2CompositionControl`.** Il supprime le problème
d'airspace mais capture la page par `GraphicsCaptureSession` : selon sa propre
documentation, fréquence d'images réduite et **contenus DRM (Netflix, Spotify…)
qui ne s'affichent pas**. Inacceptable pour un navigateur.

**Moteur intégré.** `WebView2EnvironmentService` utilise en priorité un runtime
WebView2 *Fixed Version* livré dans `WebView2Runtime\` à côté de l'exécutable,
et ne retombe sur le runtime système que s'il est absent. Le script de
publication et l'installateur intègrent ce dossier. Chromium garde ses processus
(navigateur, rendu, GPU, réseau) : c'est sa sandbox de sécurité, l'utilisateur ne
voit qu'une application.

### 2.2 Interface immersive

- **Détection des bords** : `EdgeRevealMonitor` lit la position du curseur
  (`GetCursorPos`, 25 ms) — la page appartient à un autre processus, WPF ne reçoit
  pas ses mouvements de souris. Il alimente deux automates indépendants
  (`EdgeRevealStateMachine`, Core, testés) : zone de déclenchement configurable,
  **temporisation d'apparition**, délai de disparition, zone d'hystérésis autour
  du panneau, aucun déclenchement bouton de souris enfoncé (sélection, glisser),
  ni si une autre fenêtre recouvre le bord, ni pendant une vidéo plein écran.
- **Superposition** : les panneaux glissent au-dessus de la page ; la page ne
  change jamais de taille en mode automatique. Quand les deux sont visibles, la
  colonne se cale sous la barre (`ShellLayout`, Core, testé).
- **Maintien** : un panneau reste affiché tant qu'il a le focus clavier, qu'un
  menu en est issu, qu'un onglet est en cours de glisser ou de renommage.
- **Modes** : immersif (les deux masqués), classique (les deux ancrés, la page est
  rétrécie d'autant), personnalisé (choix par panneau). F11 force le mode
  immersif plein écran.
- **Clavier** : Ctrl+L révèle la barre et sélectionne l'adresse, Échap revient à
  la page, Ctrl+Maj+A révèle la colonne avec navigation aux flèches.

### 2.3 Composants

| Action | Composant |
|---|---|
| **Supprimé** | `TabSidebarWindow`, `WebViewOverlayWindow`, `IWebViewOverlayHost` (remplacés par les panneaux enfants ; traînée du curseur dessinée dans la page en JS) |
| **Supprimé** | `PrivateBrowsingWindow` (une `MainWindow` en mode privé) |
| **Supprimé** | `SettingsWindow`, `HistoryWindow` (pages internes `navigatueur://settings` et `navigatueur://history`) |
| **Supprimé** | Réglages « position/taille de la barre d'adresse » (incompatibles avec la barre immersive) |
| **Réécrit** | `MainWindow`, `Theme.xaml`, `Icons.xaml`, `ThemeService`, page nouvel onglet |
| **Modifié** | `BrowserTabView(Model)`, `TabManagerService`, `MainWindowViewModel`, `WebView2EnvironmentService`, `UrlHelper`, `AppSettings`, `MemoryMonitorService`, `WelcomeWindow`, `ExtensionsWindow`, installateur |
| **Nouveau (Core)** | `InterfaceSettings`, `EdgeRevealStateMachine`, `ShellLayout`, `Shortcuts/*`, `InternalPages`, `BookmarkList`, `ClosedTabHistory` |
| **Nouveau (App)** | `Shell/OverlayHost`, `Shell/EdgeRevealMonitor`, `Shell/ImmersiveShell`, `Shell/WindowFrameController`, `Views/ToolbarPanel`, `Views/TabsPanel`, `InterfaceSettingsService`, `ShortcutService`, `BookmarkService`, `InternalPageBridge`, pages `settings.html`/`history.html` |
| **Conservé tel quel** | Bloqueur, anti-hameçonnage, autorisations, extensions, téléchargements, mises à jour, mini-lecteur musique |

### 2.4 Migration

- `settings.json` existant : les nouvelles clés ont des valeurs par défaut, les
  clés supprimées sont ignorées à la lecture (System.Text.Json). Testé.
- Premier lancement après mise à jour : l'écran d'accueil présente le mode
  immersif et permet de choisir le mode classique, pour qu'aucun utilisateur ne se
  retrouve devant une interface invisible sans explication.
- Les favoris sont stockés à part (`%LocalAppData%\Navigatueur\bookmarks.json`).

### 2.5 Risques

| Risque | Mitigation |
|---|---|
| Rien n'a pu être *exécuté* sous Windows depuis l'environnement de développement (Linux) | Compilation complète (XAML compris) avec le SDK WindowsDesktop ; logique extraite dans Core et testée ; pages internes testées dans Chromium ; comportements Win32/WPF vérifiés dans les sources de WPF et par décompilation de WebView2 ; liste de vérifications manuelles dans TESTS.md |
| Calques enfants transparents non disponibles / mal composés | Détection de `HwndSource.UsesPerPixelOpacity` après création ; repli automatique en panneaux plats opaques ; réglage pour forcer le style plat |
| Le HWND de WebView2 intercepte les bords de redimensionnement | La page est inset de 4 px en fenêtre normale ; les panneaux laissent passer (`HTTRANSPARENT`) les clics dans la bande de redimensionnement |
| Runtime Fixed Version : pas de mise à jour de sécurité automatique du moteur | Mises à jour livrées par le canal de mise à jour existant ; repli sur le runtime système s'il n'est pas embarqué |
| Polling du curseur | 25 ms seulement quand le curseur est sur la fenêtre, 250 ms sinon, arrêté fenêtre réduite ; trois appels Win32 triviaux |
