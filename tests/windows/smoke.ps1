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

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    // Titles of the engine's own top-level windows (the picture-in-picture window is one).
    public static string[] EngineWindowTitles() {
        var titles = new System.Collections.Generic.List<string>();
        EnumWindows((hwnd, _) => {
            if (!IsWindowVisible(hwnd)) return true;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            try {
                if (!System.Diagnostics.Process.GetProcessById((int)pid).ProcessName.StartsWith("msedgewebview2", StringComparison.OrdinalIgnoreCase)) return true;
            } catch (ArgumentException) { return true; }
            var text = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, text, 256);
            if (text.Length > 0) titles.Add(text.ToString());
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }
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

# A colourful picture, so captures show whether the panels' background lines up with the page's.
function New-Backdrop([string] $Path) {
    $bitmap = New-Object System.Drawing.Bitmap 1600, 1000
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $rect = New-Object System.Drawing.Rectangle 0, 0, 1600, 1000
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 40, 90, 220)), ([System.Drawing.Color]::FromArgb(255, 230, 70, 120)), 35
    $graphics.FillRectangle($brush, $rect)
    for ($i = 0; $i -lt 8; $i++) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 220, 80)), 14
        $graphics.DrawEllipse($pen, 100 + $i * 170, 120 + ($i % 3) * 230, 260, 260)
    }
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}

function Write-Settings([string] $Mode, [string] $Side = 'Left', [string] $Theme = 'Dark', [string] $NewTabButton = 'Bottom', [string] $Backdrop = $null, [switch] $ForceDark) {
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
        Interface      = [ordered]@{ Mode = $Mode; SidebarSide = $Side; NewTabButtonPlacement = $NewTabButton; HasSeenIntro = $true }
    }
    if ($Backdrop) { $settings['NewTabBackgroundImagePath'] = $Backdrop }
    if ($ForceDark) { $settings['IsForceDarkEnabled'] = $true }
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

# Average brightness (0-255) of a region of a capture, sampled on a grid.
function Get-Brightness([string] $File, [int] $X0, [int] $Y0, [int] $X1, [int] $Y1) {
    $bitmap = New-Object System.Drawing.Bitmap $File
    $sum = 0.0; $count = 0
    for ($y = $Y0; $y -lt $Y1; $y += 12) {
        for ($x = $X0; $x -lt $X1; $x += 12) {
            $pixel = $bitmap.GetPixel($x, $y)
            $sum += ($pixel.R + $pixel.G + $pixel.B) / 3.0
            $count++
        }
    }
    $bitmap.Dispose()
    return [int]($sum / [Math]::Max(1, $count))
}

function Test-PictureInPicture {
    return [bool]([SmokeNative]::EngineWindowTitles() | Where-Object { $_ -match 'icture|incrustation' })
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

    # Hover card: rest on the second row, the tab just left (pictured while it was on screen).
    Move-Mouse 12 $cy 900
    $column = Save-Shot 'colonne-avant-survol'
    Move-Mouse 140 102 1400
    $card = Save-Shot 'survol-onglet-apercu'
    Assert-Changed $column $card 'la carte d aperçu doit apparaître au survol d un onglet'
    Move-Mouse $cx $cy 1600

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
    Write-Host '== Mode classique, colonne à droite, thème clair, sites assombris, fond d image, bouton sous les onglets'
    # Where Settings copies a picked picture: the new-tab page only loads images from there.
    New-Item -ItemType Directory -Force -Path (Join-Path $dataDir 'Backgrounds') | Out-Null
    $backdrop = Join-Path (Join-Path $dataDir 'Backgrounds') 'newtab.png'
    New-Backdrop $backdrop
    Write-Settings -Mode 'Classic' -Side 'Right' -Theme 'Light' -NewTabButton 'AfterTabs' -Backdrop $backdrop -ForceDark
    $app = Start-Navigatueur
    Save-Shot 'classique-droite-clair' | Out-Null
    # Wikipedia (2nd tab) has no dark theme of its own here: the engine darkens it.
    Send-Keys @($VK_CONTROL, 0x32)
    Start-Sleep -Seconds 6
    $darkShot = Save-Shot 'classique-wikipedia-assombri'
    $pageBrightness = Get-Brightness $darkShot 20 70 ($W - 300) ($H - 70)
    Write-Host "  luminosité moyenne de la page : $pageBrightness"
    if ($pageBrightness -gt 110) {
        $script:failures += "Mode sombre forcé : Wikipédia devrait être sombre (luminosité moyenne $pageBrightness)"
    }
    Send-Keys @($VK_CONTROL, (VK 'T'))
    Start-Sleep -Seconds 3
    Save-Shot 'classique-fond-aligne-nouvel-onglet' | Out-Null
    Assert-Alive $app 'mode classique'
    Stop-Process -Id $app.Id -Force
    Start-Sleep -Seconds 2

    # ---------------------------------------- automatic picture-in-picture
    Write-Host '== Image dans l image automatique'
    # A page whose video carries a (very quiet) sound once clicked: the engine
    # only lets a page play sound after a real click on it.
    $videoPage = Join-Path $env:TEMP 'navigatueur-video.html'
    Set-Content -Path $videoPage -Encoding utf8 -Value @'
<!doctype html><html><head><title>Vidéo prête</title></head>
<body style="margin:0;background:#111;color:#fff;font:28px Segoe UI" onclick="start()">
<p style="position:fixed;top:16px;left:20px;margin:0">Cliquer pour lancer la vidéo</p>
<video id="v" style="width:100vw;height:100vh" playsinline></video>
<canvas id="c" width="640" height="360" hidden></canvas>
<script>
var started = false;
function start() {
  if (started) return; started = true;
  var c = document.getElementById('c'), x = c.getContext('2d'), n = 0;
  (function frame() { n++; x.fillStyle = 'hsl(' + (n % 360) + ',70%,45%)'; x.fillRect(0, 0, 640, 360);
    x.fillStyle = '#fff'; x.font = 'bold 90px Segoe UI'; x.fillText(n, 40, 220); requestAnimationFrame(frame); })();
  var audio = new AudioContext(), tone = audio.createOscillator(), gain = audio.createGain(), out = audio.createMediaStreamDestination();
  gain.gain.value = 0.002; tone.connect(gain); gain.connect(out); tone.start();
  var stream = c.captureStream(30); stream.addTrack(out.stream.getAudioTracks()[0]);
  var v = document.getElementById('v'); v.srcObject = stream;
  v.play().then(function () { document.title = 'Vidéo en lecture'; }, function (e) { document.title = 'Échec : ' + e.name; });
}
</script></body></html>
'@
    Write-Settings -Mode 'Immersive'
    $settingsPath = Join-Path $settingsDir 'settings.json'
    $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $json.Tabs = @([ordered]@{ Url = ([Uri]$videoPage).AbsoluteUri; IsPinned = $false }, [ordered]@{ Url = 'https://example.com/'; IsPinned = $false })
    $json.Groups = @()
    $json.ActiveTabIndex = 0
    $json | ConvertTo-Json -Depth 6 | Set-Content $settingsPath -Encoding utf8
    $app = Start-Navigatueur
    Click-At $cx $cy
    Start-Sleep -Seconds 4
    $app.Refresh()
    Write-Host "  titre de la fenêtre : $($app.MainWindowTitle)"
    Save-Shot 'video-en-lecture' | Out-Null

    Send-Keys @($VK_CONTROL, (VK 'T'))
    Start-Sleep -Seconds 3
    Save-Shot 'pip-auto-nouvel-onglet' | Out-Null
    if (-not (Test-PictureInPicture)) {
        $script:failures += "En quittant l onglet de la vidéo, elle aurait dû passer en image dans l image (fenêtres du moteur : $([SmokeNative]::EngineWindowTitles() -join ', '))"
    }

    Send-Keys @($VK_CONTROL, 0x31)
    Start-Sleep -Seconds 2
    Save-Shot 'pip-retour-sur-onglet' | Out-Null
    if (Test-PictureInPicture) {
        $script:failures += 'En revenant sur l onglet, la vidéo aurait dû revenir dans la page'
    }

    # Closing a tab shuts its page down: a video left in picture-in-picture goes with it.
    Send-Keys @($VK_CONTROL, 0x33)
    Start-Sleep -Seconds 3
    $pipAgain = Test-PictureInPicture
    Write-Host "  image dans l image après être passé au 3e onglet : $pipAgain"
    # Middle click on the video's row, the first one in the tab column. Out of the
    # address bar first, so the toolbar folds away and the column starts at the top.
    Send-Keys @($VK_ESCAPE)
    Move-Mouse $cx $cy 1500
    Move-Mouse 12 $cy 900
    Move-Mouse 140 66 600
    [SmokeNative]::mouse_event(0x0020, 0, 0, 0, [UIntPtr]::Zero)
    [SmokeNative]::mouse_event(0x0040, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 3
    Save-Shot 'pip-onglet-ferme' | Out-Null
    if ($pipAgain -and (Test-PictureInPicture)) {
        $script:failures += 'Fermer l onglet de la vidéo aurait dû fermer son image dans l image'
    }
    Move-Mouse $cx $cy 1200
    Assert-Alive $app 'image dans l image'
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
