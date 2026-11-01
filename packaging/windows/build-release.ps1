<#
.SYNOPSIS
  Builds a self-contained Navigatueur release that ships its own web engine.

.DESCRIPTION
  1. Publishes Navigatueur.App (Release, win-x64, self-contained) to publish\app.
  2. If a WebView2 "Fixed Version" runtime CAB is given, unpacks it into
     publish\app\WebView2Runtime. WebView2EnvironmentService uses that folder
     when it exists, so the installed browser no longer depends on the
     separately installed "Microsoft Edge WebView2 Runtime".
  3. Builds the installer with Inno Setup (installer\Navigatueur.iss) if ISCC
     is available.

  Get the CAB (x64) from:
  https://developer.microsoft.com/microsoft-edge/webview2/#download-section
  ("Fixed Version" > x64). Pick a recent version: unlike the system runtime,
  a bundled engine only gets security fixes when Navigatueur ships a new
  release with a newer CAB.

.EXAMPLE
  .\packaging\windows\build-release.ps1 -FixedRuntimeCab "$HOME\Downloads\Microsoft.WebView2.FixedVersionRuntime.130.0.2849.80.x64.cab"
#>
param(
    [string] $FixedRuntimeCab,
    [switch] $SkipInstaller,
    # Offline builds only: the app then downloads its built-in extensions on first start.
    [switch] $SkipExtensions
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$publishDir = Join-Path $root 'publish\app'
$runtimeDir = Join-Path $publishDir 'WebView2Runtime'

Write-Host '==> Publishing Navigatueur (Release, win-x64, self-contained)'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish (Join-Path $root 'src\Navigatueur.App\Navigatueur.App.csproj') `
    -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

if (-not $SkipExtensions) {
    # The extensions that are part of Navigatueur ship with it: their packages go
    # next to the app (publish\app\Extensions), where BuiltInExtensions installs
    # them from on first start and updates installed copies from. Same catalog
    # as the app reads. Each is downloaded from its authors' own release, and a
    # package whose checksum doesn't match stops the build.
    Write-Host '==> Bundling the built-in extensions'
    $catalogPath = Join-Path $root 'src\Navigatueur.App\Resources\Extensions\builtin-extensions.json'
    # The catalog has // comment lines, which Windows PowerShell's ConvertFrom-Json rejects.
    $catalog = (Get-Content $catalogPath -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*//' }) -join "`n" | ConvertFrom-Json
    $packagesDir = Join-Path $publishDir 'Extensions'
    New-Item -ItemType Directory -Force -Path $packagesDir | Out-Null

    $previousProgress = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        # Web Store entries (webStoreId) may not be redistributed: the app installs those itself.
        foreach ($extension in $catalog.extensions | Where-Object { -not $_.webStoreId }) {
            $target = Join-Path $packagesDir $extension.package
            Invoke-WebRequest $extension.url -OutFile $target -UseBasicParsing
            $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne $extension.sha256) {
                throw "$($extension.name): checksum mismatch (got $hash, expected $($extension.sha256))"
            }
            Write-Host "    $($extension.name) $($extension.version)"
        }
    }
    finally {
        $ProgressPreference = $previousProgress
    }
}

if ($FixedRuntimeCab) {
    if (-not (Test-Path $FixedRuntimeCab)) { throw "CAB not found: $FixedRuntimeCab" }

    Write-Host '==> Bundling the WebView2 Fixed Version runtime'
    $staging = Join-Path ([IO.Path]::GetTempPath()) ("nv-webview2-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        & expand.exe $FixedRuntimeCab -F:* $staging | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'expand.exe failed' }

        # The CAB holds one folder, Microsoft.WebView2.FixedVersionRuntime.<version>.<arch>.
        $exe = Get-ChildItem $staging -Recurse -Filter 'msedgewebview2.exe' | Select-Object -First 1
        if (-not $exe) { throw 'msedgewebview2.exe not found in the CAB' }

        New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
        Copy-Item (Join-Path $exe.DirectoryName '*') $runtimeDir -Recurse -Force
    }
    finally {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Chromium's sandboxed renderer processes run as AppContainers and must be
    # able to read the engine's files. Program Files already allows this; a
    # portable copy elsewhere needs it granted explicitly.
    & icacls.exe $runtimeDir /grant '*S-1-15-2-1:(OI)(CI)(RX)' '*S-1-15-2-2:(OI)(CI)(RX)' /T /Q | Out-Null

    $version = (Get-Item (Join-Path $runtimeDir 'msedgewebview2.exe')).VersionInfo.ProductVersion
    Write-Host "    bundled engine: $version"
}
else {
    Write-Warning 'No -FixedRuntimeCab given: this build will rely on the system "Microsoft Edge WebView2 Runtime".'
}

if (-not $SkipInstaller) {
    $iscc = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $default = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path $default) { $iscc = Get-Item $default }
    }

    if ($iscc) {
        Write-Host '==> Building the installer'
        & $iscc.Source (Join-Path $root 'installer\Navigatueur.iss')
        if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
    }
    else {
        Write-Warning 'Inno Setup (ISCC.exe) not found: installer not built. publish\app is ready to zip or install.'
    }
}

Write-Host "==> Done: $publishDir"
