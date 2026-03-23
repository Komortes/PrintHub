param(
    [string]$Runtime,
    [string]$Configuration = "Release",
    [string]$OutputDir,
    [string]$SelfContained = "false"
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RootDir "src/PrintHub.Api/PrintHub.Api.csproj"

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

Write-Host ""
Write-Host "Publish completed."
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
Write-Host "  $OutputDir\PrintHub.Api.exe"
