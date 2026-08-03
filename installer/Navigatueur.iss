; Inno Setup script for Navigatueur — builds a standard Windows installer
; (Start Menu shortcut, optional desktop shortcut, clean uninstall) from the
; self-contained publish output.

#define MyAppName "Navigatueur"
#define MyAppVersion "0.12.1"
#define MyAppPublisher "Navigatueur"
#define MyAppExeName "Navigatueur.exe"
#define MyPublishDir "..\publish\app"

[Setup]
AppId={{6C7F0F0E-2B7E-4C7B-9E0A-3E7B2D2A8F31}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\publish\installer
OutputBaseFilename=NavigatueurSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis supplémentaires :"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data (settings, ad-block cache, WebView2 profile) lives under %LocalAppData%\Navigatueur
; and %AppData%\Navigatueur — intentionally NOT removed on uninstall, so a reinstall
; keeps the user's settings, saved groups, and permission choices.
