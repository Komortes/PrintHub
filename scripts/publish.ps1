param(
    [string]$Runtime,
    [string]$Configuration = "Release",
    [string]$OutputDir,
    [string]$SelfContained = "true"
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RootDir "src/PrintHub.Api/PrintHub.Api.csproj"

function Install-SupportScripts {
    Copy-Item (Join-Path $RootDir "scripts/launcher/run-printhub.sh") (Join-Path $OutputDir "run-printhub.sh") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/stop-printhub.sh") (Join-Path $OutputDir "stop-printhub.sh") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/open-printhub-settings.sh") (Join-Path $OutputDir "open-printhub-settings.sh") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/open-printhub-printers.sh") (Join-Path $OutputDir "open-printhub-printers.sh") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/run-printhub.ps1") (Join-Path $OutputDir "run-printhub.ps1") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/stop-printhub.ps1") (Join-Path $OutputDir "stop-printhub.ps1") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/open-printhub-settings.ps1") (Join-Path $OutputDir "open-printhub-settings.ps1") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/open-printhub-printers.ps1") (Join-Path $OutputDir "open-printhub-printers.ps1") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/run-printhub.command") (Join-Path $OutputDir "run-printhub.command") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/stop-printhub.command") (Join-Path $OutputDir "stop-printhub.command") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/open-printhub-settings.command") (Join-Path $OutputDir "open-printhub-settings.command") -Force
    Copy-Item (Join-Path $RootDir "scripts/launcher/open-printhub-printers.command") (Join-Path $OutputDir "open-printhub-printers.command") -Force
    Copy-Item (Join-Path $RootDir "scripts/installers/install-printhub.sh") (Join-Path $OutputDir "install-printhub.sh") -Force
    Copy-Item (Join-Path $RootDir "scripts/installers/uninstall-printhub.sh") (Join-Path $OutputDir "uninstall-printhub.sh") -Force
    Copy-Item (Join-Path $RootDir "scripts/installers/install-printhub.ps1") (Join-Path $OutputDir "install-printhub.ps1") -Force
    Copy-Item (Join-Path $RootDir "scripts/installers/uninstall-printhub.ps1") (Join-Path $OutputDir "uninstall-printhub.ps1") -Force
    Copy-Item (Join-Path $RootDir "scripts/installers/install-printhub.command") (Join-Path $OutputDir "install-printhub.command") -Force
    Copy-Item (Join-Path $RootDir "scripts/installers/uninstall-printhub.command") (Join-Path $OutputDir "uninstall-printhub.command") -Force
}

function Get-DefaultRuntime {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

    if ($IsWindows) {
        switch ($architecture) {
            ([System.Runtime.InteropServices.Architecture]::Arm64) { return "win-arm64" }
            ([System.Runtime.InteropServices.Architecture]::X64) { return "win-x64" }
            default { throw "Unsupported Windows architecture: $architecture" }
        }
    }

    if ($IsMacOS) {
        switch ($architecture) {
            ([System.Runtime.InteropServices.Architecture]::Arm64) { return "osx-arm64" }
            ([System.Runtime.InteropServices.Architecture]::X64) { return "osx-x64" }
            default { throw "Unsupported macOS architecture: $architecture" }
        }
    }

    if ($IsLinux) {
        switch ($architecture) {
            ([System.Runtime.InteropServices.Architecture]::Arm64) { return "linux-arm64" }
            ([System.Runtime.InteropServices.Architecture]::X64) { return "linux-x64" }
            default { throw "Unsupported Linux architecture: $architecture" }
        }
    }

    throw "Unsupported platform."
}

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = Get-DefaultRuntime
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RootDir "output/publish/$Runtime"
}

Write-Host "Publishing PrintHub"
Write-Host "  Runtime:        $Runtime"
Write-Host "  Configuration:  $Configuration"
Write-Host "  Self-contained: $SelfContained"
Write-Host "  Output:         $OutputDir"

dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained $SelfContained `
    -o $OutputDir

Install-SupportScripts

Write-Host ""
Write-Host "Publish completed."
Write-Host ""
Write-Host "This publish is self-contained by default, so the target machine does not need the .NET runtime installed."
Write-Host ""
Write-Host "Default runtime data root:"
Write-Host "  macOS:   ~/Library/Application Support/PrintHub"
Write-Host "  Linux:   ~/.local/share/PrintHub"
Write-Host "  Windows: %LOCALAPPDATA%\PrintHub"
Write-Host ""
Write-Host "Override this location with:"
Write-Host "  PRINTHUB_HOME=C:\absolute\path"
Write-Host ""
Write-Host "Run the published app with:"
Write-Host "  $OutputDir\run-printhub.ps1"
Write-Host "Open Settings directly with:"
Write-Host "  $OutputDir\open-printhub-settings.ps1"
Write-Host "Open Printers directly with:"
Write-Host "  $OutputDir\open-printhub-printers.ps1"
Write-Host ""
Write-Host "Stop the background service with:"
Write-Host "  $OutputDir\stop-printhub.ps1"
Write-Host ""
Write-Host "Install for the current user with:"
Write-Host "  $OutputDir\install-printhub.ps1"
