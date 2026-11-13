; Inno Setup script for Navigatueur — builds a standard Windows installer
; (Start Menu shortcut, optional desktop shortcut, clean uninstall) from the
; self-contained publish output produced by packaging\windows\build-release.ps1.
; When that build bundled the WebView2 engine (publish\app\WebView2Runtime),
; it's installed with the app: one application, no separate runtime needed.

#define MyAppName "Navigatueur"
#define MyAppVersion "0.24.0"
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
; Tells Explorer the file and link associations below changed.
ChangesAssociations=yes

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

[Registry]
; Navigatueur as a browser Windows can pick (Paramètres > Applications > Applications
; par défaut, and "Comment voulez-vous ouvrir ceci ?"): links (http, https) and web
; files. Windows never lets an installer make itself the default; the user chooses.
; Everything is removed on uninstall.

; The document type both links and files open with.
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML"; ValueType: string; ValueName: ""; ValueData: "Document HTML Navigatueur"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML\Application"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML\Application"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML\Application"; ValueType: string; ValueName: "ApplicationCompany"; ValueData: "{#MyAppPublisher}"
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Classes\NavigatueurHTML\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

; The browser itself, with what it can open.
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"""
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Navigateur web immersif"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\{#MyAppExeName},0"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\StartMenu"; ValueType: string; ValueName: "StartMenuInternet"; ValueData: "{#MyAppName}"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\URLAssociations"; ValueType: string; ValueName: "http"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\URLAssociations"; ValueType: string; ValueName: "https"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".htm"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".html"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".shtml"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xht"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xhtml"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".svg"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "NavigatueurHTML"
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "Software\Clients\StartMenuInternet\{#MyAppName}\Capabilities"; Flags: uninsdeletevalue

; "Ouvrir avec" on those files.
Root: HKLM; Subkey: "Software\Classes\.htm\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.html\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.shtml\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.xht\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.xhtml\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.svg\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "NavigatueurHTML"; ValueData: ""; Flags: uninsdeletevalue

[Run]
; Chromium's sandboxed renderers (AppContainers) must be able to read the bundled engine.
; Program Files already grants this; re-granting is harmless and covers custom install folders.
Filename: "{sys}\icacls.exe"; Parameters: """{app}\WebView2Runtime"" /grant *S-1-15-2-1:(OI)(CI)(RX) *S-1-15-2-2:(OI)(CI)(RX) /T /Q"; Flags: runhidden; Check: HasBundledRuntime
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User data (settings, ad-block cache, WebView2 profile) lives under %LocalAppData%\Navigatueur
; and %AppData%\Navigatueur — intentionally NOT removed on uninstall, so a reinstall
; keeps the user's settings, saved groups, and permission choices.

[Code]
function HasBundledRuntime: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\WebView2Runtime\msedgewebview2.exe'));
end;
