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

## Ce qui fonctionne

Onglets multiples (une webview par onglet), barre d'adresse avec recherche selon
le moteur configuré, navigation précédent/suivant/actualiser/accueil, page
« nouvel onglet », raccourcis `Ctrl+T` / `Ctrl+W` / `Ctrl+L`, historique,
thème clair/sombre et couleur d'accent lus depuis les réglages, **blocage
anti-hameçonnage et anti-publicité au niveau navigation** avec page
d'avertissement et contournement explicite, filtrage cosmétique des bandeaux de
consentement et conteneurs publicitaires.

## Limites connues

À lire avant de considérer le portage comme terminé.

1. **Blocage des sous-ressources.** WebKitGTK ne transmet à Tauri que les
   navigations de premier niveau. Les requêtes de sous-ressources — les iframes
   publicitaires et traqueurs inclus dans une page par ailleurs désirée — ne
   passent donc jamais par le filtre Rust. C'est la moitié du travail que
   `WebResourceRequested` fait sur Windows. `ui/inject.js` compense côté page
   par un filtrage cosmétique, mais ce n'est pas un équivalent. Le vrai
   correctif est une *WebKit web extension* (un `.so` chargé dans le processus
   web) exposant `send-request` : c'est le prochain chantier.
2. **Pas encore d'interface de réglages ni d'historique.** Le backend est en
   place (`get_settings`, `save_settings`, `get_history`, `clear_history`) ;
   il manque les fenêtres qui les pilotent. `settings.json` s'édite à la main
   en attendant.
3. **Non portés :** navigation privée, gestion des extensions, overlay musique,
   gestionnaire de téléchargements, traînée de curseur, icône de zone de
   notification, groupes d'onglets sauvegardés, restauration de session.
   L'architecture multi-fenêtres de la version Windows (`ToolbarWindow`,
   `TabSidebarWindow`, `WebViewOverlayWindow`) contournait le problème
   d'*airspace* de WebView2 ; il n'existe pas ici, donc ces vues doivent être
   repensées plutôt que transposées.
4. **Non testé au lancement.** `cargo test` passe (17/17) et l'AppImage se
   construit et s'inspecte correctement, mais l'application n'a pas pu être
   lancée : l'environnement de développement est un bac à sable Flatpak dont
   le `/usr` est en lecture seule et sans webkit2gtk, donc les processus
   auxiliaires sont introuvables (voir « Prérequis »). Le premier lancement
   sur un vrai bureau reste à faire.
5. **Titres d'onglets.** Le nom d'hôte est affiché faute d'un événement de
   changement de titre exposé par Tauri ; il faudra le remonter via un script
   injecté.
