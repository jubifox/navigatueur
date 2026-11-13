# Navigatueur

Navigateur web minimaliste et immersif pour **Windows**, **Android** et **Linux** :
le site occupe tout l'écran, la barre d'outils apparaît en approchant la souris du
bord supérieur, les onglets en approchant le bord gauche (ou droit).

## Télécharger

**Version actuelle : v0.24.0** · [Nouveautés de cette version](https://github.com/jubifox/navigatueur/releases/tag/v0.24.0) · [Toutes les versions](https://github.com/jubifox/navigatueur/releases)

Les liens ci-dessous téléchargent directement le fichier. Une fois installé,
Navigatueur propose lui-même les nouvelles versions (sauf le paquet .deb, voir plus bas).

### Windows

Pour Windows 10 (version 1809 ou plus récente) et Windows 11, 64 bits.

| Fichier | Explication |
|---|---|
| [**⬇ NavigatueurSetup-0.24.0.exe**](https://github.com/jubifox/navigatueur/releases/download/v0.24.0/NavigatueurSetup-0.24.0.exe) | Installateur. Lancez-le et suivez les étapes. Les versions suivantes sont proposées directement dans le navigateur. |

L'installateur n'est pas signé numériquement. Windows peut donc afficher « Windows
a protégé votre ordinateur » : cliquez sur **Informations complémentaires**, puis sur
**Exécuter quand même**.

### Android

Pour Android 8.0 ou plus récent. Il existe un fichier par type de processeur :
**dans le doute, prenez le premier.**

| Fichier | Explication |
|---|---|
| [**⬇ Navigatueur-Android-0.24.0-arm64-v8a.apk**](https://github.com/jubifox/navigatueur/releases/download/v0.24.0/Navigatueur-Android-0.24.0-arm64-v8a.apk) | **Le bon choix pour presque tous les téléphones et tablettes** (processeur 64 bits). |
| [⬇ Navigatueur-Android-0.24.0-armeabi-v7a.apk](https://github.com/jubifox/navigatueur/releases/download/v0.24.0/Navigatueur-Android-0.24.0-armeabi-v7a.apk) | Anciens appareils 32 bits. Prenez-le seulement si le premier fichier affiche « Application non compatible » ou une erreur d'analyse. |
| [⬇ Navigatueur-Android-0.24.0-x86_64.apk](https://github.com/jubifox/navigatueur/releases/download/v0.24.0/Navigatueur-Android-0.24.0-x86_64.apk) | Émulateurs Android sur ordinateur (Android Studio…) et rares appareils à processeur Intel. |

Installation : ouvrez le lien sur le téléphone, puis le fichier téléchargé. La
première fois, Android demande d'autoriser votre navigateur à installer des
applications. Si l'ancienne application *Navigatueur Mobile* (1.x) est installée,
désinstallez-la d'abord. Les mises à jour se font ensuite depuis l'application
(**Paramètres → À propos**).

### Linux

Pour Linux 64 bits (x86_64).

| Fichier | Explication |
|---|---|
| [**⬇ Navigatueur-Linux-0.24.0-x86_64.AppImage**](https://github.com/jubifox/navigatueur/releases/download/v0.24.0/Navigatueur-Linux-0.24.0-x86_64.AppImage) | **Recommandé.** Un seul fichier, pour toutes les distributions, sans droits administrateur. Les nouvelles versions sont proposées dans l'application. |
| [⬇ Navigatueur-Linux-0.24.0-amd64.deb](https://github.com/jubifox/navigatueur/releases/download/v0.24.0/Navigatueur-Linux-0.24.0-amd64.deb) | Paquet pour Debian, Ubuntu, Linux Mint… Il s'installe comme une application du système, mais pas de mise à jour automatique : installez le nouveau .deb à chaque version. |

- **AppImage** : rendez le fichier exécutable (clic droit → Propriétés → « Autoriser
  l'exécution », ou `chmod +x Navigatueur-Linux-0.24.0-x86_64.AppImage`), puis
  double-cliquez dessus. S'il ne démarre pas sur Ubuntu, installez FUSE :
  `sudo apt install libfuse2` (`libfuse2t64` sur Ubuntu 24.04 et plus récent).
- **.deb** : `sudo apt install ./Navigatueur-Linux-0.24.0-amd64.deb`

## Développement

| Plateforme | Dossier | Technique |
|---|---|---|
| **Windows** | `src/`, `tests/`, `installer/`, `packaging/windows/` | WPF (.NET 8) + moteur Chromium WebView2 |
| **Android** | `android/` | Kotlin + Jetpack Compose + GeckoView (moteur de Firefox) : vraies extensions (uBlock Origin…) |
| **Linux** | `linux/` | Electron (Chromium) : l'interface et les pages internes de la version Windows |

Une seule numérotation : chaque version (`vX.Y.Z`) publie l'installateur Windows,
les APK Android (un par type de processeur) et l'AppImage / le .deb Linux.

### Windows

Prérequis : Windows 10 1809 ou plus récent, SDK .NET 8.

```powershell
dotnet run --project src/Navigatueur.App          # lancer en développement
dotnet test tests/Navigatueur.Core.Tests          # tests unitaires
.\packaging\windows\build-release.ps1 -FixedRuntimeCab <cab>   # version publiée + installateur
```

La version publiée embarque son propre moteur Web (runtime WebView2 « Fixed
Version ») : aucune installation séparée de « Microsoft Edge WebView2 Runtime »
n'est nécessaire. Sans `-FixedRuntimeCab`, l'application utilise celui du système.

Chaque modification est compilée, testée, puis l'application réelle est lancée
et pilotée sur une machine Windows par GitHub Actions (`.github/workflows/windows.yml`,
scénario `tests/windows/smoke.ps1`) ; les captures d'écran de chaque étape sont
publiées en artefact et sur la branche `ci/windows-captures`.

Documentation de la refonte : [`docs/refonte/AUDIT-ET-PLAN.md`](docs/refonte/AUDIT-ET-PLAN.md)
et [`docs/refonte/TESTS.md`](docs/refonte/TESTS.md).

### Android

Application à part entière sur GeckoView 159 (moteur de Firefox), dans
[`android/`](android/). Les extensions de la version Windows (uBlock Origin,
SponsorBlock, Consent-O-Matic, Decentraleyes, Purple AdBlock, Violentmonkey,
BetterTTV) et Dark Reader s'installent depuis addons.mozilla.org au premier
lancement ; on peut en ajouter d'autres depuis le site.

```sh
cd android
./gradlew testDebugUnitTest     # règles partagées avec la version Windows
./gradlew assembleDebug         # APK de développement
```

La CI (`.github/workflows/android.yml`) lance l'application sur un émulateur
avec des captures (`tests/android/smoke.sh`). Les APK publiés sont signés avec la
clé des secrets du dépôt (voir `packaging/android/set-signing-secrets.ps1`).

### Linux

Electron (Chromium), dans [`linux/`](linux/README.md) : même interface immersive,
mêmes pages internes que la version Windows. AppImage (mise à jour automatique)
et paquet .deb ; CI avec captures sur écran virtuel (`.github/workflows/linux.yml`).
