#define MyAppName "SecureRemoteDesk Host"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "SecureRemoteDesk"
#define MyAppExeName "SecureRemoteDesk.Host.exe"

[Setup]
AppId={{A91B2F5B-7C79-4B2B-9C4C-46C73A12A201}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SecureRemoteDesk Host
DefaultGroupName=SecureRemoteDesk
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installers
OutputBaseFilename=SecureRemoteDesk-Host-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\publish\host\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\SecureRemoteDesk Host"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\SecureRemoteDesk Host"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SecureRemoteDesk Host"; Flags: nowait postinstall skipifsilent
