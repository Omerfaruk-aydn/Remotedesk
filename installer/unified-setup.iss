#define MyAppName "SecureRemoteDesk"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "SecureRemoteDesk"
#define MyAppExeName "SecureRemoteDesk.exe"

[Setup]
AppId={{7E13F3A1-7F74-4DD4-95E5-05B72A44B203}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SecureRemoteDesk
DefaultGroupName=SecureRemoteDesk
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=SecureRemoteDesk-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\publish\desktop\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\SecureRemoteDesk"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\SecureRemoteDesk"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SecureRemoteDesk"; Flags: nowait postinstall skipifsilent
