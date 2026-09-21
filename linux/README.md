# Navigatueur — version Linux

Portage Linux de Navigatueur, distribué en **AppImage** : un fichier unique,
exécutable sans droits administrateur, sans installateur et sans paquet système.

L'application Windows (`src/Navigatueur.App`) est en WPF et s'appuie sur
WebView2 ; ni l'un ni l'autre n'existe sous Linux. Cette version est donc une
application distincte, bâtie sur **Tauri 2 + WebKitGTK**, qui réutilise ce qui
était réellement portable du projet d'origine :

| Réutilisé depuis la version Windows | Où |
|---|---|
| `UrlHelper` (normalisation de la barre d'adresse) | `src-tauri/src/url_helper.rs` |
| `AppSettings` + sérialisation | `src-tauri/src/settings.rs` |
| `SearchEngineService` (les 5 moteurs) | `src-tauri/src/search.rs` |
| `HistoryService` | `src-tauri/src/history.rs` |
| `AdBlockService` + `PhishingProtectionService` | `src-tauri/src/blocklist.rs` |
| Listes de blocage (94k + 392k domaines) | *référencées en place*, cf. ci-dessous |
| Page « nouvel onglet », page d'avertissement | `ui/newtab.html`, `ui/warning.html` |
| Palette de `Theme.xaml`, icône `AppIcon.ico` | `ui/index.html`, `src-tauri/icons/` |

Les listes de blocage ne sont **pas** dupliquées : `tauri.conf.json` pointe sur
`src/Navigatueur.App/Resources/` et l'empaqueteur les copie dans l'AppImage au
moment du build. Mettre une liste à jour met donc à jour les deux versions.

Les modules portés conservent le comportement du C# ; `url_helper.rs` contient
même la matrice de tests exacte de `UrlHelperTests.cs` pour verrouiller la
parité. `cargo test` : 17 tests.

## Prérequis sur la machine cible

L'AppImage a besoin de **`libwebkit2gtk-4.1-0`** sur l'hôte. Vérification :

```sh
ldconfig -p | grep libwebkit2gtk-4.1 && echo OK || echo MANQUANT
```

C'est présent d'office sur quasiment tout bureau Debian/Ubuntu récent (GNOME et
de nombreuses applications en dépendent).

Pourquoi ce n'est pas embarqué, alors que le but est d'être portable :
WebKitGTK lance des processus auxiliaires (`WebKitNetworkProcess`,
`WebKitWebProcess`) depuis un chemin figé à la compilation. La variable
`WEBKIT_EXEC_PATH`, qui permettait autrefois de le rediriger, a été supprimée
en amont — vérifié avec `strings` sur la bibliothèque 2.52 livrée par Debian.
`linuxdeploy` copie bien les bibliothèques webkit dans l'AppDir, mais pas ces
exécutables : on obtiendrait une bibliothèque embarquée pilotant les auxiliaires
de l'hôte, donc un risque de version incompatible. `build-appimage.sh` retire
donc webkit du bundle pour que l'hôte fournisse un couple cohérent — c'est à la
fois plus fiable et 40 Mo de moins.

Si la machine cible n'a pas webkit et pas de sudo, la même technique que pour le
build reste applicable (dépaquetage des `.deb` dans `~/.local`), mais elle se
heurte au chemin figé ci-dessus : il faudrait alors passer par un espace de noms
utilisateur (`unshare --user --map-root-user --mount` + `mount --bind`). Non
implémenté ici.

## Installation (utilisateur final, sans sudo)

```sh
./packaging/install-user.sh Navigatueur_0.15.0_amd64.AppImage
```

Le binaire va dans `~/.local/bin`, le raccourci dans `~/.local/share/applications`.
Rien en dehors de `$HOME`. Désinstallation : `./packaging/uninstall-user.sh`
(ajouter `--purge` pour effacer aussi les données).

L'AppImage se lance aussi directement, sans installer quoi que ce soit :

```sh
chmod +x Navigatueur_0.15.0_amd64.AppImage
./Navigatueur_0.15.0_amd64.AppImage
```

Données utilisateur (conventions XDG, pas `%LocalAppData%`) :

- `~/.config/navigatueur/settings.json` — réglages
- `~/.local/share/navigatueur/history.json` — historique
- `~/.local/share/navigatueur/lists/` — cache des listes de blocage

## Compilation

La chaîne de build s'installe elle aussi **sans root** — c'est nécessaire sur
une machine verrouillée, où `apt` et `sudo` ne sont pas disponibles :

```sh
. packaging/env.sh              # Rust + sysroot webkit2gtk, hors système
(cd src-tauri && cargo test)    # 17 tests
packaging/build-appimage.sh     # -> target/release/bundle/appimage/*.AppImage
```

Utiliser `build-appimage.sh` plutôt que `cargo tauri build` directement : il
pose `APPIMAGE_EXTRACT_AND_RUN=1` (linuxdeploy et appimagetool sont eux-mêmes
des AppImages et réclament FUSE, absent des bacs à sable) et retire webkit du
bundle comme expliqué plus haut.

Outils installés sans root par ce chantier, tous sous `/Virtual/jmasse/toolchain` :
Rust 1.98.1 (`rustup`), `cargo-tauri`, `patchelf`, et un sysroot Debian 13
(`setup-sysroot.sh` + `fetch-deps.sh`) contenant webkit2gtk-4.1 et ses
dépendances transitives (ICU 76, libxml2, flite, manette, enchant, evdev…).

`packaging/env.sh` pointe vers un toolchain Rust et un sysroot `webkit2gtk-4.1`
privés. Le sysroot est produit par `setup-sysroot.sh`, qui dépaquette les `.deb`
Debian 13 dans un répertoire local — aucune installation système.

Point subtil : `PKG_CONFIG_SYSROOT_DIR` n'est **pas** défini. Il réécrirait tous
les chemins d'en-têtes vers le sysroot, y compris ceux de gtk/pango/cairo qui
viennent du système. Seuls les `.pc` de webkit ont leur `prefix` réécrit.

## Fonctionnalités

Inventaire fait à partir de la version Windows, fonctionnalité par fonctionnalité.

### Portées

**Onglets** — onglets multiples, épinglage, coupure du son, favicon, titre de
page, fermeture par clic du milieu, menu contextuel complet. Groupes d'onglets :
création, renommage en place, six couleurs, repli, suppression. Groupes
enregistrés : sauvegarde, réouverture, suppression. Glisser-déposer pour
réordonner, ou déposer au centre d'un onglet pour grouper les deux (comme
Chrome/Edge). Un nouvel onglet s'ouvre à côté de son parent et hérite de son
groupe.

**Suspension mémoire** — au plus 3 onglets vivants, éviction LRU, mise en veille
après 15 minutes d'inactivité. Les onglets épinglés et ceux qui jouent du son
ne sont jamais évincés. Restauration de session complète (onglets, groupes,
onglet actif), tout suspendu sauf l'actif, avec sauvegarde automatique toutes
les 2 minutes et à la fermeture — géométrie de fenêtre comprise.

**Navigation** — barre d'adresse avec normalisation et complétion en ligne
(historique d'abord, puis 14 sites courants, plus court gagnant), précédent /
suivant / actualiser / arrêter / accueil, zoom (0,25–5,0), picture-in-picture,
traduction via le proxy Yandex, recherche dans la page.

**Raccourcis** — `Ctrl+T` `Ctrl+W` `Ctrl+L` `Ctrl+R` `Ctrl+F` `Ctrl+H`,
`Ctrl+Tab` / `Ctrl+Maj+Tab`, `Ctrl+1..8`, `Ctrl+9`, `Ctrl+ +/-/0`, `F5`,
`Alt+←` / `Alt+→`.

**Blocage** — anti-hameçonnage et anti-publicité au niveau navigation, page
d'avertissement avec contournement explicite limité à l'hôte et à la session,
désactivation du bloqueur par onglet, filtrage cosmétique par domaine issu
d'EasyList, et les scriptlets `set` d'uBO (c'est ce qui neutralise la charge
publicitaire de YouTube, que le blocage réseau ne peut pas atteindre puisqu'elle
vient du même CDN que la vidéo).

**Apparence** — thèmes clair/sombre, couleur d'accent par pastilles ou curseur
de teinte, arrière-plans d'interface et de nouvel onglet, taille de barre
d'adresse, traînée de curseur et curseur personnalisé, dégradé `theme-color`
du site dans l'interface.

**Pages** — paramètres, historique (groupé par jour, recherche, effacement),
nouvel onglet, avertissement. Moniteur de mémoire, vérificateur de mises à jour
GitHub, choix parmi les 5 moteurs de recherche.

### Impossibles sur WebKitGTK

- **Extensions Chromium.** `AddBrowserExtensionAsync` est propre à WebView2.
  WebKitGTK n'a aucun équivalent : il ne charge pas de CRX. Les quatre
  extensions recommandées (uBlock Origin, Consent-O-Matic, SponsorBlock,
  Decentraleyes) n'ont donc pas de portage possible.
- **Enregistrement MHTML.** Passait par `Page.captureSnapshot` du protocole
  DevTools, spécifique à Chromium.

### Existantes dans WebKitGTK mais non exposées par Tauri

- **Permissions de site** (caméra, micro, position…). WebKitGTK émet bien
  `permission-request`, mais Tauri ne le relaie pas. Les pages utilisent donc
  les réglages par défaut de WebKit.
- **Gestionnaire de téléchargements.** Même situation : le signal `download-started`
  existe côté WebKit, pas côté Tauri.
- **Détection de lecture audio** et **titre de document**. Le titre est déduit de
  l'hôte, l'indicateur audio n'est pas alimenté — le drapeau reste donc faux, ce
  qui prive l'éviction LRU de sa protection des onglets sonores.

### Non portées, par choix

- **Superposition musicale.** Reposait sur les SMTC de Windows. L'équivalent
  Linux est MPRIS via D-Bus : faisable, mais c'est un chantier à part entière.
- **Icône de zone de notification.** Demande `libappindicator` au build, que la
  machine cible n'a pas forcément.
- **Navigation privée** dans une fenêtre séparée, et **barre latérale d'onglets**
  (la disposition « Latérale » est acceptée dans les réglages mais rendue en haut).

### Une décision de sécurité

Plusieurs effets de la version Windows (couleur `theme-color`, détection audio,
traînée de curseur dans les pages) passaient par un pont `postMessage` entre la
page et l'hôte. Sur WebView2 ce pont est cantonné à l'application ; ici,
l'ouvrir signifierait déclarer les pages distantes dans une capacité Tauri, ce
qui donnerait à **n'importe quel site** l'accès à `save_settings`, à
l'historique et aux chemins de fichiers. Ce n'est pas fait.

La capacité ne déclare aucun champ `remote` : elle ne s'applique donc qu'aux
URL locales. Les pages internes (paramètres, historique, nouvel onglet,
avertissement) ont l'IPC même chargées dans un onglet ; un onglet affichant un
site distant n'a rien. La traînée de curseur est redessinée entièrement côté
page, ce qui supprime au passage l'aller-retour IPC par mouvement de souris que
la version Windows payait.

## Limites connues

1. **Blocage des sous-ressources.** WebKitGTK ne transmet à Tauri que les
   navigations de premier niveau. Les requêtes de sous-ressources — iframes
   publicitaires et traqueurs inclus dans une page par ailleurs désirée — ne
   passent jamais par le filtre Rust. Le filtrage cosmétique par domaine
   compense une partie de l'effet visuel, mais pas la consommation réseau. Le
   vrai correctif est une *WebKit web extension* (un `.so` chargé dans le
   processus web) exposant `send-request`.
2. **Non testé au lancement.** 41 tests passent et l'AppImage se construit, mais
   l'environnement de développement est un bac à sable Flatpak sans webkit2gtk :
   l'application ne peut pas y être lancée (voir « Prérequis »).
