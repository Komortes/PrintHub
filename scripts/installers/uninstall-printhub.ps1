param(
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\PrintHub"
}

$ProgramsDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PrintHub"

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}

if (Test-Path $ProgramsDir) {
    Remove-Item $ProgramsDir -Recurse -Force
}

Write-Host "PrintHub was removed from:"
Write-Host "  $InstallDir"
