# Navigatueur pour Linux

La version Linux de Navigatueur, sur **Electron** (le moteur Chromium, le même que
WebView2 sous Windows). Elle remplace l'ancien portage Tauri/WebKitGTK, qui datait
d'avant la refonte v0.18.

## Ce qu'elle reprend de la version Windows

| Version Windows | Ici |
|---|---|
| Interface immersive : barre d'outils en haut, colonne d'onglets sur le côté, qui apparaissent au bord de l'écran ; modes Immersif / Classique / Personnalisé | `src/main/window.js`, `src/shell/` |
| Onglets épinglés, groupes nommés et colorés, glisser-déposer, aperçu au survol, mise en veille | `src/main/window.js`, `src/main/tab.js`, `src/shell/sidebar.js` |
| Pages internes (nouvel onglet, Paramètres, historique) | **les mêmes fichiers** : `src/Navigatueur.App/Resources/Pages`, servis par `navigatueur://home/`, avec le même pont (`window.chrome.webview`) |
| Règles de la barre d'adresse, suggestions, moteurs de recherche, raccourcis | `src/core/` (mêmes tests que `Navigatueur.Core`) |
| Image dans l'image automatique, mode sombre forcé (par site) | `src/main/tab.js` (mêmes scripts) |
| Protection contre l'hameçonnage (même liste), page d'avertissement | `src/main/protection.js` |
| Mises à jour depuis les versions GitHub | l'AppImage se remplace elle-même (electron-updater) |

Le blocage des publicités et des traqueurs utilise le moteur de **Ghostery**
(`@ghostery/adblocker-electron`), qui lit les mêmes listes qu'uBlock Origin
(EasyList, EasyPrivacy, listes d'uBlock, bandeaux de cookies). Les extensions Chrome
ne sont pas prises en charge ici : Electron n'en accepte qu'une petite partie.

## Installer

Chaque version publie sur GitHub :

- `Navigatueur-Linux-X.Y.Z-x86_64.AppImage` — un seul fichier, sans droits administrateur :
  `chmod +x Navigatueur-Linux-*.AppImage && ./Navigatueur-Linux-*.AppImage`. Il se met à jour tout seul.
- `Navigatueur-Linux-X.Y.Z-amd64.deb` — pour Debian/Ubuntu : `sudo apt install ./Navigatueur-Linux-*.deb`.

Les réglages et l'historique sont dans `~/.config/Navigatueur`.

## Développer

```sh
cd linux
npm ci
npm test        # règles partagées avec la version Windows
npm start       # lance l'application depuis les sources
npm run dist    # AppImage + .deb dans dist/ (sous Linux)
```

Lancé depuis les sources sous Windows, l'application range ses données dans
`%APPDATA%\Navigatueur-Linux-dev`, jamais dans celles de la version Windows.
La CI (`.github/workflows/linux.yml`) construit les paquets et fait tourner
l'application sur un écran virtuel, avec des captures (`tests/linux/smoke.sh`).
