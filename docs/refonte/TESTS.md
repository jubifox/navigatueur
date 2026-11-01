# Refonte — tests et validation

## Ce qui est vérifié automatiquement

Ces vérifications tournent partout, y compris hors Windows ; l'intégration
continue les relance sur Windows à chaque modification :

| Vérification | Commande | Résultat |
|---|---|---|
| Compilation complète, XAML compris (SDK WindowsDesktop, ciblage Windows) | `dotnet build Navigatueur.sln -p:EnableWindowsTargeting=true` | 0 erreur, 0 avertissement |
| Logique Core (121 tests xUnit) | `dotnet test tests/Navigatueur.Core.Tests` | 121/121 |
| Pages internes dans Chromium headless (15 tests Playwright) | `node tests/pages/pages.test.js` | 15/15 |
| Références de ressources XAML (clés `StaticResource`/`DynamicResource`) | `python3 tests/xaml/check-resources.py` | 337 références, 0 problème |

Les tests des pages demandent Node et Playwright avec son Chromium
(`npm i -g playwright && npx playwright install chromium`).

### Ce que couvrent les tests Core

- **Apparition des bords** (`EdgeRevealStateMachine`) : temporisation avant
  apparition, passage furtif ignoré, bouton de souris enfoncé ignoré, maintien
  au survol, délai de disparition et annulation au retour, maintien par le focus
  ou un menu, fermeture au clic dans la page, suppression (vidéo plein écran),
  apparition clavier avec délai de grâce.
- **Mise en page** (`ShellLayout`) : page à 100 % en immersif, panneaux en
  superposition sans déplacer la page, colonne sous la barre quand les deux sont
  visibles, côté droit, mode classique (page rétrécie, sans ombre), mode
  personnalisé, bande de redimensionnement, bornes de l'animation.
- **Réglages d'interface** : résolution des trois modes, passage automatique en
  « personnalisé », noms inconnus et valeurs hors bornes réparés.
- **Raccourcis** : analyse/affichage (« Ctrl + Maj + T »), tous les raccourcis
  demandés présents par défaut, pas de doublon, personnalisation qui prend la
  combinaison à une autre commande, désactivation, entrées invalides ignorées.
- **Migration** : un `settings.json` v0.17.2 réel se charge (onglets, thème,
  moteur conservés ; nouvelles valeurs par défaut appliquées) ; `null` explicites
  réparés.
- **Adresses internes, favoris, onglets fermés, suggestions** de la barre
  d'adresse.

### Ce que couvrent les tests des pages

Un faux `window.chrome.webview` enregistre chaque message envoyé à
l'application et permet de lui répondre comme le fait `InternalPageBridge` :
rendu de chaque réglage depuis l'état, clé et valeur exactes envoyées par chaque
contrôle, thème clair/accent/teinte privée, enregistrement d'un raccourci au
clavier (succès, erreur, annulation, désactivation), favoris de la page nouvel
onglet, recherche passant par l'application, historique groupé par jour,
ouverture/suppression, titres affichés comme du texte (pas de HTML injecté),
refus de s'exécuter dans une iframe.

### Vérifié dans les sources plutôt que par l'exécution

- **Raccourcis avec le focus dans la page** : le contrôle WebView2 WPF (SDK
  1.0.4314, décompilé) relaie `AcceleratorKeyPressed` en `PreviewKeyDown` WPF sur
  l'élément WebView2 — les raccourcis de la fenêtre les reçoivent.
- **Fenêtres enfants** (sources de WPF 8) : `HwndHost` crée sa fenêtre dès la
  connexion à la fenêtre parente même masqué, la repositionne à chaque passe de
  mise en page (d'où la remise en tête de l'ordre Z), et `HwndSource` accepte la
  transparence par pixel pour une fenêtre `WS_CHILD` sous Windows 8+.
- **Clavier dans les panneaux** : schéma « puits clavier enfant » de WPF, où la
  fenêtre principale pré-traite chaque touche une seule fois puis la transmet au
  panneau.
- **`WebView2CompositionControl`** écarté sur la foi de sa propre documentation
  (capture d'écran, DRM non affiché).

## Application réelle sous Windows (intégration continue)

`.github/workflows/windows.yml` compile la solution et lance les tests sur
`windows-latest`, puis **démarre l'application réelle** et la pilote avec
`tests/windows/smoke.ps1` : survol des bords, Ctrl+L et saisie d'une adresse,
Ctrl+T, Ctrl+Maj+A, Ctrl+Tab, Ctrl+W puis Ctrl+Maj+T, Ctrl+, (paramètres),
Ctrl+H, menu « … », F11, puis mode classique (colonne à droite, thème clair),
mode personnalisé et fenêtre privée. Chaque étape produit une capture ; le job
échoue si l'application se ferme seule ou écrit dans `crash.log`
(`%LocalAppData%\Navigatueur`, alimenté par tout plantage non géré), ou si
un changement attendu n'apparaît pas à l'écran (captures identiques avant et
après : barre au survol du bord haut, disparition, colonne au bord gauche,
Ctrl+H après un changement d'onglet, ouverture du menu « … », Échap qui le
referme, barre au bord haut en plein écran). Les captures sont publiées en
artefact et sur la branche `ci/windows-captures`.

Défauts trouvés par ces exécutions et corrigés :

| Constat sur Windows | Correction |
|---|---|
| Fenêtre maximisée débordant sous la barre des tâches ; zones de bord hors écran, donc pas d'apparition au survol | `WM_GETMINMAXINFO` limite l'agrandissement à la zone de travail ; zones de bord bornées à l'écran |
| Après un changement d'onglet, le clavier restait sur la page masquée (Ctrl+H perdu) | Le focus suit l'onglet actif, y compris s'il était en veille |
| Fenêtre privée de 1280×800 sur un écran de 768 px : barre d'outils hors écran | Taille et position de fenêtre bornées à l'écran |
| Onglets restaurés tous nommés « Nouvel onglet » | Titres conservés dans la session ; nom du site en attendant |
| Menu « … » insensible à Échap | Échap ferme les menus de la barre |
| Séparateurs de menu décalés | Style de séparateur propre aux menus |

## À vérifier à la main sous Windows

Les points marqués ✔ CI sont déjà couverts par le scénario automatique
ci-dessus (machine virtuelle Windows, écran 1024×768, sans GPU). Les autres
demandent une vraie session : souris physique, plusieurs écrans, haute
densité, son, vidéos protégées, glisser-déposer. Les points marqués ⚠ sont ceux
où un comportement n'a pu être confirmé que par la documentation.

### Démarrage et architecture
- [ ] Au premier lancement après mise à jour, l'écran d'accueil explique les
      bords et propose Immersif/Classique ; il ne réapparaît plus ensuite.
- [x] ✔ CI — Une seule fenêtre Navigatueur (Alt+Tab, barre des tâches) ; plus de fenêtre
      fantôme pour les onglets ou la traînée.
- [ ] Version publiée avec `-FixedRuntimeCab` : Paramètres → À propos indique
      « intégré à Navigatueur » ; l'app fonctionne après désinstallation du
      runtime WebView2 système (ou sur une machine qui ne l'a pas).
- [ ] Sans aucun runtime : l'onglet affiche « Le moteur Web n'a pas pu démarrer »
      au lieu de planter.

### Interface immersive
- [x] ✔ CI — Au lancement, la page occupe toute la fenêtre.
- [x] ✔ CI — Souris immobile au bord supérieur ~150 ms → la barre glisse par-dessus la
      page ; la page ne bouge pas. ⚠ Coins arrondis et ombre visibles (calques
      enfants transparents) ; sinon, la barre doit être plate et opaque, sans
      zones noires. En cas de rendu incorrect : Paramètres → Apparence →
      désactiver « Effets de profondeur » puis redémarrer (panneaux opaques
      classiques, sans calque transparent).
- [ ] Passer rapidement par le bord sans s'arrêter → rien n'apparaît.
- [ ] Sélectionner du texte en glissant jusqu'au bord → rien n'apparaît.
- [x] ✔ CI — Quitter la barre → elle disparaît après ~0,6 s ; y revenir avant annule.
- [ ] Cliquer dans la page pendant que la barre est visible → elle disparaît.
- [ ] Taper dans la barre d'adresse puis sortir la souris → la barre reste
      jusqu'à Entrée/Échap/clic dans la page.
- [x] ✔ CI — Bord gauche → colonne d'onglets ; avec la barre visible, la colonne commence
      sous la barre.
- [ ] Paramètres → côté Droite : la colonne apparaît au bord droit, le marqueur
      d'onglet actif passe à droite.
- [ ] Les deux panneaux visibles en même temps, utilisables l'un et l'autre.
- [ ] Menu « … », Favoris, Téléchargements ouverts → la barre ne disparaît pas.
- [ ] Vidéo YouTube en plein écran → la fenêtre passe en plein écran, aucun
      panneau n'apparaît aux bords ; Échap rétablit tout.
- [ ] Fenêtre restaurée : redimensionnement possible par les quatre bords,
      y compris quand la barre ou la colonne est affichée. ⚠
- [x] ✔ CI — Fenêtre maximisée : rien ne déborde de l'écran.
- [ ] Glisser la barre (zone vide) déplace la fenêtre ; double-clic → agrandir.

### Modes
- [x] ✔ CI — Classique : barre et colonne toujours visibles, la page est à côté (pas
      dessous). Bouton « colonne » de la barre : bascule ancrée/automatique.
- [ ] Personnalisé : barre ancrée + colonne automatique, et l'inverse.
- [ ] F11 en mode classique : plein écran immersif ; F11 à nouveau : retour.

### Clavier (interface masquée, focus dans la page)
- [ ] Ctrl+T, Ctrl+W, Ctrl+Tab, Ctrl+Maj+Tab, Ctrl+1…9.
- [x] ✔ CI — Ctrl+L : la barre apparaît, adresse sélectionnée ; Échap : retour à la page.
- [ ] Ctrl+R / F5, Alt+←/→, Ctrl+D (l'étoile s'allume, la barre s'affiche
      brièvement), Ctrl+H, Ctrl+J, Ctrl+,.
- [x] ✔ CI — Ctrl+Maj+T rouvre le dernier onglet fermé à sa place.
- [x] ✔ CI — Ctrl+Maj+A : la colonne apparaît, ↑/↓ parcourent, Entrée active, Suppr
      ferme, Échap referme.
- [ ] Paramètres → Raccourcis : remplacer Ctrl+T par une autre combinaison,
      l'utiliser, rétablir.
- [ ] Dans la barre d'adresse, une touche n'agit qu'une fois (Retour arrière
      n'efface qu'un caractère, Ctrl+T n'ouvre qu'un onglet). ⚠

### Onglets et groupes
- [ ] Glisser-déposer pour réordonner (trait d'insertion), au centre pour
      grouper (contour) ; renommer, recolorer, replier, enregistrer un groupe.
- [ ] Clic milieu sur un onglet le ferme ; son, sourdine, épingle.
- [ ] Élargir la colonne en tirant son bord ; la largeur est conservée.

### Paramètres et persistance
- [ ] Chaque réglage s'applique immédiatement (mode, côté, délais, thème clair,
      accent, animations, effets).
- [ ] Fermer/rouvrir : tout est conservé, y compris la position de la fenêtre
      et l'état maximisé.
- [ ] Démarrage « Page d'accueil » : la session n'est pas restaurée.
- [ ] Recherche depuis la barre d'adresse avec le moteur choisi (corrige
      l'ancien Bing forcé).

### Non-régression
- [ ] Bloqueur (réseau + cosmétique), désactivation par site et globale.
- [ ] Page d'avertissement anti-hameçonnage et « Continuer quand même ».
- [ ] Demande d'autorisation (caméra, micro…) : la barre apparaît au-dessus de
      la page (elle était auparavant cachée derrière WebView2).
- [ ] Téléchargements, Enregistrer sous, Traduire, Rechercher dans la page,
      Picture-in-Picture, zoom.
- [ ] Extensions : installation recommandée, activation, « Ouvrir ».
- [x] ✔ CI — Fenêtre privée (Ctrl+Maj+N) : teinte violette, badge « Privé », rien dans
      l'historique, profil supprimé à la fermeture.
- [ ] Nouvelle fenêtre (Ctrl+N), mini-lecteur musique et icône de notification.
- [ ] Mises à jour : point sur le bouton « … » et entrée de menu.

### Ressources
- [ ] Gestionnaire des tâches : processus Navigatueur + processus Chromium
      (normal) ; CPU ≈ 0 % au repos, interface masquée et souris immobile.
- [ ] Le menu « … » affiche la mémoire utilisée ; avec plus de 3 onglets, les
      plus anciens passent en veille (réglable dans Performances).
