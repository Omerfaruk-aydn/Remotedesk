# Windows Installers

This folder builds the unified installer executable:

- `SecureRemoteDesk-Setup.exe`

## Requirements

- .NET 8 SDK
- Inno Setup 6 with `ISCC.exe` available in PATH or installed at `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

## Build

```powershell
.\infra\scripts\build-windows-installers.ps1
```

Output:

```text
artifacts/installers/SecureRemoteDesk-Setup.exe
```

To also produce separate role-specific installers:

```powershell
.\infra\scripts\build-windows-installers.ps1 -IncludeSeparateRoleInstallers
```
