<#
.SYNOPSIS
  Gives GitHub the key that signs the Android APKs of each release.

.DESCRIPTION
  Android only installs an update over Navigatueur if it is signed with the
  same key as the installed copy. The key was generated on this PC, outside
  the repository:

    %USERPROFILE%\.navigatueur\android-signing\navigatueur-release.jks
    %USERPROFILE%\.navigatueur\android-signing\password.txt

  This script stores it in the repository's Actions secrets (encrypted, only
  readable by the workflows), where .github/workflows/release.yml picks it up.
  Run it once, with the GitHub CLI signed in (gh auth status).

  Keep a copy of the .jks file and its password somewhere safe: without them,
  a future version can't be installed over the current one.

  Since v0.23.0 the repository's secrets already hold the signing key (made
  on 2026-11-07, certificate SHA-256 F0:32:E7:3E:…:5F:87:85:17), and the
  copy kept by its owner is the one to restore from — not the older key that
  may still be in the folder above. Replacing the secrets with another key
  stops every installed copy from updating, so the script refuses unless
  -Replace is given.

.EXAMPLE
  pwsh packaging/android/set-signing-secrets.ps1 -Replace
#>
param([switch]$Replace)
$ErrorActionPreference = 'Stop'

if (-not $Replace) {
    throw "Les secrets du dépôt contiennent déjà la clé de signature Android (depuis la v0.23.0). La remplacer empêcherait les téléphones de se mettre à jour : relancez avec -Replace seulement si c'est vraiment voulu."
}
$repo = 'jubifox/navigatueur'
$dir = Join-Path $env:USERPROFILE '.navigatueur\android-signing'
$keystore = Join-Path $dir 'navigatueur-release.jks'
$passwordFile = Join-Path $dir 'password.txt'

if (-not (Test-Path $keystore) -or -not (Test-Path $passwordFile)) {
    throw "Clé introuvable dans $dir"
}

$password = (Get-Content $passwordFile -Raw).Trim()
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($keystore))

$base64 | gh secret set NAVIGATUEUR_KEYSTORE_B64 -R $repo
gh secret set NAVIGATUEUR_KEYSTORE_PASSWORD -R $repo --body $password
gh secret set NAVIGATUEUR_KEY_PASSWORD -R $repo --body $password
gh secret set NAVIGATUEUR_KEY_ALIAS -R $repo --body 'navigatueur'

Write-Host 'Secrets enregistrés. Les prochaines versions Android seront signées avec cette clé.'
