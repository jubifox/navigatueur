# Navigatueur

Navigateur web minimaliste et immersif pour **Windows**, **Android** et **Linux** :
le site occupe tout l'écran, la barre d'outils apparaît en approchant la souris du
bord supérieur, les onglets en approchant le bord gauche (ou droit).

| Plateforme | Dossier | Technique |
|---|---|---|
| **Windows** | `src/`, `tests/`, `installer/`, `packaging/windows/` | WPF (.NET 8) + moteur Chromium WebView2 |
| **Android** | `android/` | Kotlin + Jetpack Compose + GeckoView (moteur de Firefox) : vraies extensions (uBlock Origin…) |
| **Linux** | `linux/` | Electron (Chromium) : l'interface et les pages internes de la version Windows |

Une seule numérotation : chaque version (`vX.Y.Z`) publie l'installateur Windows,
les APK Android (un par type de processeur) et l'AppImage / le .deb Linux.

## Windows

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

## Android

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

## Linux

Electron (Chromium), dans [`linux/`](linux/README.md) : même interface immersive,
mêmes pages internes que la version Windows. AppImage (mise à jour automatique)
et paquet .deb ; CI avec captures sur écran virtuel (`.github/workflows/linux.yml`).
