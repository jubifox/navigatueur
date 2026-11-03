<#
.SYNOPSIS
  Drives the real Navigatueur on Windows like a user would and screenshots each step.

.DESCRIPTION
  Used by .github/workflows/windows.yml on a GitHub-hosted Windows machine:
  starts the built app with a known session, moves the mouse to the screen
  edges, presses the shortcuts, and saves a PNG per step to -OutDir. Fails
  if the app exits on its own or writes anything to its crash log.

  Can also be run by hand on a dev machine (it overwrites the Navigatueur
  settings of the current Windows user — back them up first).

.EXAMPLE
  pwsh tests/windows/smoke.ps1 -Exe src\Navigatueur.App\bin\Release\net8.0-windows10.0.19041.0\Navigatueur.exe -OutDir captures
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$Exe = (Resolve-Path $Exe).Path

Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class SmokeNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
"@
[SmokeNative]::SetProcessDPIAware() | Out-Null

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$W = $screen.Width
$H = $screen.Height
Write-Host "Écran : ${W}x${H}"

$settingsDir = Join-Path $env:APPDATA 'Navigatueur'
$dataDir = Join-Path $env:LOCALAPPDATA 'Navigatueur'
$crashLog = Join-Path $dataDir 'crash.log'
$script:step = 0
$script:failures = @()

function Write-Settings([string] $Mode, [string] $Side = 'Left', [string] $Theme = 'Dark') {
    New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
    $videoGroup = '6f1c2a9e-3b4d-4e5f-8a7b-1c2d3e4f5a6b'
    $settings = [ordered]@{
        WasMaximized   = $true
        Tabs           = @(
            [ordered]@{ Url = 'https://example.com/'; IsPinned = $false },
            [ordered]@{ Url = 'https://en.wikipedia.org/wiki/Web_browser'; IsPinned = $false },
            [ordered]@{ Url = 'https://navigatueur.home/index.html'; IsPinned = $false },
            [ordered]@{ Url = 'https://www.youtube.com/'; IsPinned = $false; GroupId = $videoGroup },
            [ordered]@{ Url = 'https://www.twitch.tv/'; IsPinned = $false; GroupId = $videoGroup }
        )
        # A group with a glyph in its folder icon, so the captures show one.
        Groups         = @([ordered]@{ Id = $videoGroup; Name = 'Vidéo'; ColorHex = '#E5484D'; IsCollapsed = $false; Icon = 'Play' })
        ActiveTabIndex = 1
        ThemeMode      = $Theme
        SearchEngine   = 'DuckDuckGo'
        Interface      = [ordered]@{ Mode = $Mode; SidebarSide = $Side; HasSeenIntro = $true }
    }
    $settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $settingsDir 'settings.json') -Encoding utf8
}

function Save-Shot([string] $Name) {
    $script:step++
    $file = Join-Path $OutDir ('{0:D2}-{1}.png' -f $script:step, $Name)
    $bitmap = New-Object System.Drawing.Bitmap $W, $H
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "  capture $([IO.Path]::GetFileName($file))"
    return $file
}

# Two captures that should differ (a panel appeared, a page opened) but are
# byte-identical mean nothing happened on screen.
function Assert-Changed([string] $Before, [string] $After, [string] $Expectation) {
    if ((Get-FileHash $Before).Hash -eq (Get-FileHash $After).Hash) {
        $script:failures += "Rien n'a changé à l'écran : $Expectation ($([IO.Path]::GetFileName($After)))"
        Write-Host "  ÉCHEC : $Expectation"
    }
}

function Send-Keys([byte[]] $Keys) {
    foreach ($key in $Keys) { [SmokeNative]::keybd_event($key, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
    for ($i = $Keys.Length - 1; $i -ge 0; $i--) { [SmokeNative]::keybd_event($Keys[$i], 0, 2, [UIntPtr]::Zero); Start-Sleep -Milliseconds 30 }
}

function Move-Mouse([int] $X, [int] $Y, [int] $SettleMs = 0) {
    [SmokeNative]::SetCursorPos($X, $Y) | Out-Null
    if ($SettleMs -gt 0) { Start-Sleep -Milliseconds $SettleMs }
}

function Click-At([int] $X, [int] $Y) {
    Move-Mouse $X $Y 150
    [SmokeNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [SmokeNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Assert-Alive($Process, [string] $When) {
    $Process.Refresh()
    if ($Process.HasExited) {
        $script:failures += "Navigatueur s'est fermé tout seul ($When), code $($Process.ExitCode)"
        throw "Navigatueur exited during: $When"
    }
}

function Start-Navigatueur {
    Get-Process Navigatueur -ErrorAction SilentlyContinue | Stop-Process -Force
    $process = Start-Process -FilePath $Exe -PassThru
    for ($i = 0; $i -lt 90 -and $process.MainWindowHandle -eq 0; $i++) {
        Start-Sleep -Seconds 1
        $process.Refresh()
        if ($process.HasExited) { break }
    }
    Assert-Alive $process 'démarrage'
    if ($process.MainWindowHandle -eq 0) { throw 'No main window after 90 s' }
    [SmokeNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Move-Mouse ([int]($W / 2)) ([int]($H / 2))
    # Let WebView2 start and the first pages load.
    Start-Sleep -Seconds 15
    Assert-Alive $process 'chargement des pages'
    return $process
}

$VK_CONTROL = 0x11; $VK_SHIFT = 0x10; $VK_MENU = 0x12; $VK_ESCAPE = 0x1B; $VK_RETURN = 0x0D
$VK_TAB = 0x09; $VK_F11 = 0x7A; $VK_OEM_COMMA = 0xBC
function VK([char] $Letter) { [byte][char]::ToUpperInvariant($Letter) }

Remove-Item $crashLog -ErrorAction SilentlyContinue
$cx = [int]($W / 2)
$cy = [int]($H / 2)

try {
    # ---------------------------------------------------- immersive mode
    Write-Host '== Mode immersif'
    Write-Settings -Mode 'Immersive'
    $app = Start-Navigatueur
    $start = Save-Shot 'immersif-demarrage'

    Move-Mouse $cx 0 900
    $top = Save-Shot 'bord-haut-barre-outils'
    Assert-Changed $start $top 'la barre doit apparaître au bord supérieur'

    Move-Mouse $cx $cy 1600
    $hidden = Save-Shot 'souris-au-centre-barre-masquee'
    Assert-Changed $top $hidden 'la barre doit disparaître quand la souris s en va'

    # 12 px from the edge: inside the tab column's (wider) trigger zone, outside the toolbar's.
    Move-Mouse 12 $cy 900
    $left = Save-Shot 'bord-gauche-colonne-onglets'
    Assert-Changed $hidden $left 'la colonne doit apparaître à 12 px du bord gauche'

    Move-Mouse 1 1 1000
    Save-Shot 'coin-les-deux-panneaux' | Out-Null

    Move-Mouse $cx $cy 1600
    Assert-Alive $app 'survol des bords'

    Send-Keys @($VK_CONTROL, (VK 'L'))
    Start-Sleep -Milliseconds 900
    Save-Shot 'ctrl-l-barre-adresse' | Out-Null

    foreach ($ch in 'example.org'.ToCharArray()) {
        if ($ch -eq '.') { Send-Keys @(0xBE) } else { Send-Keys @((VK $ch)) }
    }
    Start-Sleep -Milliseconds 400
    Save-Shot 'saisie-adresse-suggestions' | Out-Null
    Send-Keys @($VK_RETURN)
    Start-Sleep -Seconds 4
    Save-Shot 'apres-entree' | Out-Null

    Send-Keys @($VK_CONTROL, (VK 'T'))
    Start-Sleep -Seconds 3
    Save-Shot 'ctrl-t-nouvel-onglet' | Out-Null
    Send-Keys @($VK_ESCAPE)
    Start-Sleep -Milliseconds 800

    Send-Keys @($VK_CONTROL, $VK_SHIFT, (VK 'A'))
    Start-Sleep -Milliseconds 900
    Save-Shot 'ctrl-maj-a-onglets-clavier' | Out-Null
    Send-Keys @($VK_ESCAPE)
    Start-Sleep -Milliseconds 800

    Send-Keys @($VK_CONTROL, $VK_TAB)
    Start-Sleep -Seconds 2
    Save-Shot 'ctrl-tab' | Out-Null

    Send-Keys @($VK_CONTROL, (VK 'W'))
    Start-Sleep -Seconds 1
    Send-Keys @($VK_CONTROL, $VK_SHIFT, (VK 'T'))
    Start-Sleep -Seconds 3
    Save-Shot 'ctrl-maj-t-onglet-restaure' | Out-Null

    Send-Keys @($VK_CONTROL, $VK_OEM_COMMA)
    Start-Sleep -Seconds 4
    $settingsShot = Save-Shot 'parametres'

    Send-Keys @($VK_CONTROL, (VK 'H'))
    Start-Sleep -Seconds 3
    $historyShot = Save-Shot 'historique'
    Assert-Changed $settingsShot $historyShot 'Ctrl+H doit ouvrir l historique après un changement d onglet'

    Move-Mouse $cx 0 900
    Click-At ($W - 150) 24
    Start-Sleep -Milliseconds 900
    $menuShot = Save-Shot 'menu-outils'
    Assert-Changed $historyShot $menuShot 'le menu « … » doit s ouvrir'
    Send-Keys @($VK_ESCAPE)
    Start-Sleep -Milliseconds 500
    $escShot = Save-Shot 'menu-ferme-par-echap'
    Assert-Changed $menuShot $escShot 'Échap doit fermer le menu « … »'

    # The bar's buttons take clicks over its full height: one at the screen's
    # very top edge, where the toolbar is summoned, must still hit « … ».
    Click-At ($W - 150) 1
    Start-Sleep -Milliseconds 900
    $edgeMenuShot = Save-Shot 'menu-outils-clic-au-bord'
    Assert-Changed $escShot $edgeMenuShot 'un clic tout en haut de l écran doit ouvrir le menu « … »'
    Send-Keys @($VK_ESCAPE)
    Start-Sleep -Milliseconds 500
    Move-Mouse $cx $cy 1600

    Send-Keys @($VK_F11)
    Start-Sleep -Seconds 2
    $fullShot = Save-Shot 'f11-plein-ecran'
    Move-Mouse $cx 0 900
    $fullTopShot = Save-Shot 'f11-bord-haut'
    Assert-Changed $fullShot $fullTopShot 'en plein écran, la barre doit apparaître au bord supérieur'
    Move-Mouse $cx $cy 1200
    Send-Keys @($VK_F11)
    Start-Sleep -Seconds 2

    Assert-Alive $app 'raccourcis'
    Stop-Process -Id $app.Id -Force
    Start-Sleep -Seconds 2

    # ------------------------------------------------------ classic mode
    Write-Host '== Mode classique, colonne à droite, thème clair'
    Write-Settings -Mode 'Classic' -Side 'Right' -Theme 'Light'
    $app = Start-Navigatueur
    Save-Shot 'classique-droite-clair' | Out-Null
    Assert-Alive $app 'mode classique'
    Stop-Process -Id $app.Id -Force
    Start-Sleep -Seconds 2

    # --------------------------------------------- custom + private window
    Write-Host '== Mode personnalisé + fenêtre privée'
    Write-Settings -Mode 'Custom'
    $settingsPath = Join-Path $settingsDir 'settings.json'
    $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $json.Interface | Add-Member -NotePropertyName ToolbarAutoHide -NotePropertyValue $false -Force
    $json.Interface | Add-Member -NotePropertyName SidebarAutoHide -NotePropertyValue $true -Force
    $json | ConvertTo-Json -Depth 6 | Set-Content $settingsPath -Encoding utf8
    $app = Start-Navigatueur
    Save-Shot 'personnalise-barre-ancree' | Out-Null
    Send-Keys @($VK_CONTROL, $VK_SHIFT, (VK 'N'))
    Start-Sleep -Seconds 12
    Save-Shot 'fenetre-privee' | Out-Null
    Assert-Alive $app 'fenêtre privée'
}
catch {
    $script:failures += "Échec du scénario : $($_.Exception.Message)"
    try { Save-Shot 'echec' } catch { }
}
finally {
    Get-Process Navigatueur -ErrorAction SilentlyContinue | Stop-Process -Force
}

if (Test-Path $crashLog) {
    Copy-Item $crashLog (Join-Path $OutDir 'crash.log')
    Write-Host '== crash.log'
    Get-Content $crashLog | Write-Host
    $script:failures += 'Navigatueur a écrit dans crash.log (voir ci-dessus)'
}

if ($script:failures.Count -gt 0) {
    $script:failures | ForEach-Object { Write-Host "ÉCHEC : $_" }
    exit 1
}

Write-Host "Scénario terminé sans erreur, $($script:step) captures."
