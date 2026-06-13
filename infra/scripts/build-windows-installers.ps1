param(
    [ValidateSet("win-x64")]
    [string] $Runtime = "win-x64",
    [string] $Configuration = "Release",
    [switch] $IncludeSeparateRoleInstallers
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$artifacts = Join-Path $root "artifacts"
$publishRoot = Join-Path $artifacts "publish"
$installerRoot = Join-Path $artifacts "installers"
$toolsRoot = Join-Path $root ".tools"

function Resolve-Tool {
    param(
        [string] $Name,
        [string[]] $Fallbacks
    )

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    foreach ($fallback in $Fallbacks) {
        if (Test-Path -LiteralPath $fallback) {
            return $fallback
        }
    }

    throw "Required tool '$Name' was not found."
}

$dotnet = Resolve-Tool "dotnet" @(
    (Join-Path $toolsRoot "dotnet\dotnet.exe")
)
$iscc = Resolve-Tool "ISCC.exe" @(
    (Join-Path $toolsRoot "InnoSetup\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

New-Item -ItemType Directory -Force -Path $publishRoot, $installerRoot | Out-Null
Remove-Item -LiteralPath (Join-Path $publishRoot "desktop") -Recurse -Force -ErrorAction SilentlyContinue

& $dotnet publish (Join-Path $root "apps\desktop-windows\src\SecureRemoteDesk.Desktop.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $publishRoot "desktop")
if ($LASTEXITCODE -ne 0) {
    throw "Desktop publish failed."
}

& $iscc (Join-Path $root "installer\unified-setup.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Unified installer compile failed."
}

if ($IncludeSeparateRoleInstallers) {
    Remove-Item -LiteralPath (Join-Path $publishRoot "host") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $publishRoot "viewer") -Recurse -Force -ErrorAction SilentlyContinue

    & $dotnet publish (Join-Path $root "apps\host-windows\src\SecureRemoteDesk.Host.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $publishRoot "host")
    if ($LASTEXITCODE -ne 0) {
        throw "Host publish failed."
    }

    & $dotnet publish (Join-Path $root "apps\viewer-windows\src\SecureRemoteDesk.Viewer.csproj") `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $publishRoot "viewer")
    if ($LASTEXITCODE -ne 0) {
        throw "Viewer publish failed."
    }

    & $iscc (Join-Path $root "installer\host-setup.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Host installer compile failed."
    }
    & $iscc (Join-Path $root "installer\viewer-setup.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Viewer installer compile failed."
    }
}

Write-Host "Installers created:"
Write-Host (Join-Path $installerRoot "SecureRemoteDesk-Setup.exe")
if ($IncludeSeparateRoleInstallers) {
    Write-Host (Join-Path $installerRoot "SecureRemoteDesk-Host-Setup.exe")
    Write-Host (Join-Path $installerRoot "SecureRemoteDesk-Viewer-Setup.exe")
}
