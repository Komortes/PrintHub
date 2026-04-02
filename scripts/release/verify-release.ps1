param(
    [string]$SourceDir,
    [switch]$KeepFiles
)

$ErrorActionPreference = "Stop"

function Resolve-SourceDir {
    param([string]$BaseDir)

    if (Test-Path (Join-Path $BaseDir "install-printhub.ps1")) {
        return $BaseDir
    }

    if (Test-Path (Join-Path $BaseDir "payload/install-printhub.ps1")) {
        return (Join-Path $BaseDir "payload")
    }

    throw "Could not find install-printhub.ps1 under: $BaseDir"
}

function Test-Health([string]$Url) {
    try {
        Invoke-RestMethod -Uri "$Url/health" -Method Get -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-UntilDown([string]$Url) {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if (-not (Test-Health $Url)) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Save-RuntimeArtifacts([string]$Url, [string]$ArtifactsDir) {
    New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

    if (Test-Health $Url) {
        try { Invoke-WebRequest -Uri "$Url/health" -Method Get -TimeoutSec 5 -OutFile (Join-Path $ArtifactsDir "health.json") | Out-Null } catch {}
        try { Invoke-WebRequest -Uri "$Url/printers/diagnostics" -Method Get -TimeoutSec 5 -OutFile (Join-Path $ArtifactsDir "printers-diagnostics.json") | Out-Null } catch {}
        try { Invoke-WebRequest -Uri "$Url/diagnostics/report" -Method Get -TimeoutSec 5 -OutFile (Join-Path $ArtifactsDir "diagnostics-report.txt") | Out-Null } catch {}
        try { Invoke-WebRequest -Uri "$Url/diagnostics/support-bundle" -Method Get -TimeoutSec 10 -OutFile (Join-Path $ArtifactsDir "support-bundle.zip") | Out-Null } catch {}
    }
}

function Write-TailIfExists([string]$Label, [string]$Path, [int]$Lines = 40) {
    if (-not (Test-Path $Path)) {
        return
    }

    Write-Host ""
    Write-Host "---- $Label ($Path) ----"
    Get-Content $Path -Tail $Lines -ErrorAction SilentlyContinue
}

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    throw "Usage: ./scripts/release/verify-release.ps1 -SourceDir <publish-dir-or-release-stage-dir>"
}

$ResolvedSourceDir = Resolve-SourceDir $SourceDir
$VerifyRoot = Join-Path $env:TEMP ("printhub-release-verify-" + [guid]::NewGuid().ToString("N"))
$InstallDir = Join-Path $VerifyRoot "PrintHub"
$VerifyLogsDir = Join-Path $VerifyRoot "verify-logs"
$VerifyArtifactsDir = Join-Path $VerifyRoot "verify-artifacts"
$InstallLog = Join-Path $VerifyLogsDir "install.log"
$RunLog = Join-Path $VerifyLogsDir "run.log"
$StopLog = Join-Path $VerifyLogsDir "stop.log"
$ProgramsDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PrintHub"
$PreviousPrintHubHome = $env:PRINTHUB_HOME
$PreviousPrintHubPort = $env:PRINTHUB_PORT
$PreviousOpenBrowser = $env:PRINTHUB_OPEN_BROWSER

try {
    New-Item -ItemType Directory -Force -Path $VerifyRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $VerifyLogsDir | Out-Null
    New-Item -ItemType Directory -Force -Path $VerifyArtifactsDir | Out-Null

    $env:PRINTHUB_HOME = Join-Path $VerifyRoot "home"
    $env:PRINTHUB_PORT = [string](Get-Random -Minimum 5400 -Maximum 5800)
    $env:PRINTHUB_OPEN_BROWSER = "false"
    $AppUrl = "http://127.0.0.1:$($env:PRINTHUB_PORT)"

    & (Join-Path $ResolvedSourceDir "install-printhub.ps1") -InstallDir $InstallDir *> $InstallLog

    if (-not (Test-Path $InstallDir)) {
        throw "PrintHub install directory was not created."
    }

    if (-not (Test-Path (Join-Path $ProgramsDir "PrintHub Settings.lnk"))) {
        throw "PrintHub Settings shortcut was not created."
    }

    if (-not (Test-Path (Join-Path $ProgramsDir "PrintHub Printers.lnk"))) {
        throw "PrintHub Printers shortcut was not created."
    }

    & (Join-Path $InstallDir "run-printhub.ps1") -NoBrowser *> $RunLog

    if (-not (Test-Health $AppUrl)) {
        throw "PrintHub did not become healthy at $AppUrl."
    }

    Save-RuntimeArtifacts $AppUrl $VerifyArtifactsDir

    & (Join-Path $InstallDir "stop-printhub.ps1") *> $StopLog

    if (-not (Wait-UntilDown $AppUrl)) {
        throw "PrintHub did not stop cleanly at $AppUrl."
    }

    Write-Host ""
    Write-Host "Release verification succeeded."
    Write-Host ""
    Write-Host "Source:       $ResolvedSourceDir"
    Write-Host "Install root: $VerifyRoot"
    Write-Host "Health URL:   $AppUrl/health"
    Write-Host "Artifacts:    $VerifyArtifactsDir"
}
catch {
    Save-RuntimeArtifacts $AppUrl $VerifyArtifactsDir

    Write-Host ""
    Write-Host "Release verification failed."
    Write-Host "Verify workspace:  $VerifyRoot"
    Write-Host "Health URL:       $AppUrl/health"
    Write-Host "App runtime home: $env:PRINTHUB_HOME"
    Write-TailIfExists "install log" $InstallLog
    Write-TailIfExists "run log" $RunLog
    Write-TailIfExists "stop log" $StopLog
    Write-TailIfExists "launcher stdout" (Join-Path $env:PRINTHUB_HOME "runtime/launcher.stdout.log")
    Write-TailIfExists "launcher stderr" (Join-Path $env:PRINTHUB_HOME "runtime/launcher.stderr.log")
    Write-TailIfExists "file log" (Join-Path $env:PRINTHUB_HOME "data/logs/printhub.log")
    throw
}
finally {
    $env:PRINTHUB_HOME = $PreviousPrintHubHome
    $env:PRINTHUB_PORT = $PreviousPrintHubPort
    $env:PRINTHUB_OPEN_BROWSER = $PreviousOpenBrowser

    if (-not $KeepFiles -and (Test-Path $VerifyRoot)) {
        Remove-Item $VerifyRoot -Recurse -Force
    }
}
