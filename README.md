# Navigatueur

Navigateur web minimaliste et immersif pour **Windows** : le site occupe tout
l'écran, la barre d'outils apparaît en approchant la souris du bord supérieur,
les onglets en approchant le bord gauche (ou droit).

| Plateforme | Dossier | Statut |
|---|---|---|
| **Windows** | `src/`, `tests/`, `installer/`, `packaging/windows/` | **Cible principale** — WPF (.NET 8) + moteur Chromium WebView2 |
| Linux | `linux/` | Optionnel — portage Tauri antérieur à la refonte, non maintenu au même niveau |
| Android | `android/` | Application mobile séparée |

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

## Linux (optionnel)

Le portage Linux (`linux/`, Tauri + WebKitGTK) n'est pas une priorité : il ne
reçoit pas les évolutions de la version Windows (interface immersive, favoris,
paramètres…), n'est pas compilé par l'intégration continue, et peut prendre du
retard ou être retiré. Voir [`linux/README.md`](linux/README.md).
