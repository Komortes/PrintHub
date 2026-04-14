param(
    [string]$Runtime = "",
    [string]$ReleaseRoot = "",
    [string]$PublishDir = "",
    [bool]$IncludePublish = $true
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$PublishScript = Join-Path $RootDir "scripts/publish.ps1"

function Get-PrintHubVersion {
    if (-not [string]::IsNullOrWhiteSpace($env:PRINTHUB_VERSION)) {
        return $env:PRINTHUB_VERSION
    }

    $versionFile = Join-Path $RootDir "VERSION"
    if (Test-Path $versionFile) {
        return (Get-Content $versionFile -Raw).Trim()
    }

    return "0.1.0"
}

function Get-HostRuntime {
    switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        "Arm64" { $arch = "arm64" }
        "X64"   { $arch = "x64" }
        default { throw "Unsupported host architecture." }
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return "win-$arch"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        return "linux-$arch"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return "osx-$arch"
    }

    throw "Unsupported host OS."
}

function Copy-Directory([string]$SourceDir, [string]$TargetDir) {
    if (Test-Path $TargetDir) {
        Remove-Item $TargetDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $TargetDir | Out-Null
    Copy-Item (Join-Path $SourceDir "*") $TargetDir -Recurse -Force
}

if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = Get-HostRuntime
}

if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $RootDir "output/release/$Runtime"
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path $RootDir "output/publish/$Runtime"
}

$AppVersion = Get-PrintHubVersion
$StageName = "PrintHub-$AppVersion-$Runtime"
$StageDir = Join-Path $ReleaseRoot $StageName

New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

if ($IncludePublish -or -not (Test-Path $PublishDir)) {
    & $PublishScript -Runtime $Runtime -OutputDir $PublishDir
}

if (-not (Test-Path $PublishDir)) {
    throw "Publish output was not found: $PublishDir"
}

Copy-Directory $PublishDir (Join-Path $StageDir "payload")
New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "docs") | Out-Null
Copy-Item (Join-Path $RootDir "README.md") (Join-Path $StageDir "docs/README.md") -Force
Copy-Item (Join-Path $RootDir "CHANGELOG.md") (Join-Path $StageDir "docs/CHANGELOG.md") -Force
Copy-Item (Join-Path $RootDir "docs/api.md") (Join-Path $StageDir "docs/api.md") -Force
Copy-Item (Join-Path $RootDir "docs/user-guide.md") (Join-Path $StageDir "docs/user-guide.md") -Force

$Manifest = @{
    name = "PrintHub"
    version = $AppVersion
    runtime = $Runtime
    artifactName = $StageName
    builtAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    gitCommit = (& git -C $RootDir rev-parse --short HEAD 2>$null)
    selfContained = $true
}

if ([string]::IsNullOrWhiteSpace($Manifest.gitCommit)) {
    $Manifest.gitCommit = "unknown"
}

$Manifest | ConvertTo-Json | Set-Content (Join-Path $StageDir "RELEASE-MANIFEST.json")

$ArtifactPath = Join-Path $ReleaseRoot "$StageName.zip"
if (Test-Path $ArtifactPath) {
    Remove-Item $ArtifactPath -Force
}

Compress-Archive -Path $StageDir -DestinationPath $ArtifactPath -Force
Get-FileHash $ArtifactPath -Algorithm SHA256 | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($ArtifactPath))" |
        Set-Content "$ArtifactPath.sha256"
}

Write-Host ""
Write-Host "Release package completed."
Write-Host "  Version:   $AppVersion"
Write-Host "  Runtime:   $Runtime"
Write-Host "  Stage dir: $StageDir"
Write-Host "  Artifact:  $ArtifactPath"
Write-Host "  Checksum:  $ArtifactPath.sha256"
Write-Host "  Verify:    $RootDir\scripts\release\verify-release.ps1 -SourceDir $StageDir"
