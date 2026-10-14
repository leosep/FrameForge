; Requires Inno Setup 6 (https://jrsoftware.org/isinfo.php)
[Setup]
AppId={{B7E2C1A4-5F3D-4C8E-9A61-0A1B2C3D4E5F}
AppName=FrameForge
AppVersion=1.0.2
AppPublisher=leandrosepulveda.com
AppPublisherURL=https://leandrosepulveda.com
DefaultDirName={autopf}\FrameForge
DefaultGroupName=FrameForge
OutputDir=..\dist
OutputBaseFilename=FrameForge-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\FrameForge.exe
UninstallDisplayName=FrameForge — leandrosepulveda.com
VersionInfoCompany=leandrosepulveda.com
VersionInfoCopyright=Created by leandrosepulveda.com
VersionInfoProductName=FrameForge
VersionInfoProductVersion=1.0.2

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.desktopicon=Crear acceso directo en el escritorio
english.desktopicon=Create a desktop icon
spanish.runapp=Abrir FrameForge
english.runapp=Launch FrameForge

[Tasks]
Name: "desktopicon"; Description: "{cm:desktopicon}"; Flags: unchecked

[Files]
; Excludes: the debug .pdb must not ship in the installer
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\FrameForge"; Filename: "{app}\FrameForge.exe"
Name: "{group}\Sitio de leandrosepulveda.com"; Filename: "https://leandrosepulveda.com"
Name: "{autodesktop}\FrameForge"; Filename: "{app}\FrameForge.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FrameForge.exe"; Description: "{cm:runapp}"; Flags: nowait postinstall skipifsilent

; Clean uninstall: logs are deleted, but backup.json/cmdbackup.json are
; intentionally kept in {commonappdata}\FrameForge in case you reinstall and
; want to revert. Delete them by hand if you won't use FrameForge again.
[UninstallDelete]
Type: files; Name: "{commonappdata}\FrameForge\logs\*.log"
Type: dirifempty; Name: "{commonappdata}\FrameForge\logs"
