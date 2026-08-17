<#
.SYNOPSIS
    Builds and launches Blare.

.DESCRIPTION
    `dotnet run` does not work for this project. WinUI 3's XAML compiler and the
    MRT resource tasks ship with Visual Studio, not the .NET SDK, so building
    through the plain SDK fails on Microsoft.Build.Packaging.Pri.Tasks. This
    script finds the real MSBuild through vswhere and uses that instead.

.EXAMPLE
    ./scripts/run.ps1
    ./scripts/run.ps1 -Configuration Release
    ./scripts/run.ps1 -Tray          # start minimised to the tray
    ./scripts/run.ps1 -BuildOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$Tray,

    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-MSBuildPath {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

    if (-not (Test-Path $vswhere)) {
        throw "vswhere not found. Visual Studio is required to build WinUI 3 projects."
    }

    $path = & $vswhere -latest -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1

    if (-not $path) {
        throw "MSBuild not found. Install the '.NET desktop development' and 'WinUI application development' workloads."
    }

    return $path
}

# A running instance holds a lock on its own DLLs and the build fails on copy.
Get-Process -Name 'Blare.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$msbuild = Get-MSBuildPath
Write-Host "Building Blare ($Configuration|$Platform)..." -ForegroundColor Cyan

& $msbuild "$repoRoot\src\Blare.App\Blare.App.csproj" `
    "/p:Configuration=$Configuration" `
    "/p:Platform=$Platform" `
    /verbosity:minimal /nologo

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

if ($BuildOnly) {
    Write-Host "Build succeeded." -ForegroundColor Green
    return
}

$exe = Join-Path $repoRoot "src\Blare.App\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\Blare.App.exe"

if (-not (Test-Path $exe)) {
    throw "Built, but no executable at $exe"
}

$arguments = if ($Tray) { @('--tray') } else { @() }
Start-Process -FilePath $exe -ArgumentList $arguments

Write-Host "Blare started." -ForegroundColor Green
Write-Host "Errors, if any: $env:LOCALAPPDATA\Blight\Blare\crash.log" -ForegroundColor DarkGray
