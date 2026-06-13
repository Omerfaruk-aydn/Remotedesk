#define MyAppName "SecureRemoteDesk Viewer"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "SecureRemoteDesk"
#define MyAppExeName "SecureRemoteDesk.Viewer.exe"

[Setup]
AppId={{C62935D4-9CF2-49FD-8D9B-4D7D2770B202}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SecureRemoteDesk Viewer
DefaultGroupName=SecureRemoteDesk
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=SecureRemoteDesk-Viewer-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\publish\viewer\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\SecureRemoteDesk Viewer"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\SecureRemoteDesk Viewer"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SecureRemoteDesk Viewer"; Flags: nowait postinstall skipifsilent
