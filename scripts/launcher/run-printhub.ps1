param(
    [switch]$NoBrowser,
    [string]$OpenUrlSuffix
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppExe = Join-Path $ScriptDir "PrintHub.Api.exe"
$AppDll = Join-Path $ScriptDir "PrintHub.Api.dll"
$DefaultPort = 5051
$StartTimeoutSeconds = if ($env:PRINTHUB_START_TIMEOUT_SECONDS) { [int]$env:PRINTHUB_START_TIMEOUT_SECONDS } else { 30 }

function Resolve-PrintHubHome {
    if (-not [string]::IsNullOrWhiteSpace($env:PRINTHUB_HOME)) {
        return $env:PRINTHUB_HOME
    }

    return Join-Path $env:LOCALAPPDATA "PrintHub"
}

function Resolve-PrintHubPort {
    if (-not [string]::IsNullOrWhiteSpace($env:PRINTHUB_PORT)) {
        return [int]$env:PRINTHUB_PORT
    }

    $settingsFile = Join-Path $env:PRINTHUB_HOME "data/settings.json"
    if (Test-Path $settingsFile) {
        try {
            $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json
            if ($settings.port) {
                return [int]$settings.port
            }
        }
        catch {
        }
    }

    return $DefaultPort
}

function Resolve-PrintHubUrl {
    if (-not [string]::IsNullOrWhiteSpace($env:PRINTHUB_URL)) {
        return $env:PRINTHUB_URL
    }

    $hostName = if ($env:PRINTHUB_HOST) { $env:PRINTHUB_HOST } else { "127.0.0.1" }
    $port = Resolve-PrintHubPort
    return "http://$hostName`:$port"
}

function Test-PrintHubHealth {
    try {
        Invoke-RestMethod -Uri "$script:AppUrl/health" -Method Get -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-ForPrintHubHealth {
    $attemptCount = $StartTimeoutSeconds * 2

    for ($attempt = 1; $attempt -le $attemptCount; $attempt++) {
        if (Test-PrintHubHealth) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    return $false
}

function Open-PrintHubBrowser {
    if ($NoBrowser -or $env:PRINTHUB_OPEN_BROWSER -eq "false") {
        return
    }

    $suffix = if (-not [string]::IsNullOrWhiteSpace($OpenUrlSuffix)) {
        $OpenUrlSuffix
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:PRINTHUB_OPEN_URL_SUFFIX)) {
        $env:PRINTHUB_OPEN_URL_SUFFIX
    }
    else {
        ""
    }

    Start-Process "$script:AppUrl$suffix" | Out-Null
}

$env:PRINTHUB_HOME = Resolve-PrintHubHome
$RuntimeDir = Join-Path $env:PRINTHUB_HOME "runtime"
$null = New-Item -ItemType Directory -Force -Path $RuntimeDir
$PidFile = Join-Path $RuntimeDir "printhub.pid"
$StdOutLog = Join-Path $RuntimeDir "launcher.stdout.log"
$StdErrLog = Join-Path $RuntimeDir "launcher.stderr.log"
$script:AppUrl = Resolve-PrintHubUrl
$env:ASPNETCORE_URLS = $script:AppUrl

if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) {
    $env:ASPNETCORE_ENVIRONMENT = "Production"
}

if (Test-PrintHubHealth) {
    Write-Host "PrintHub is already running at $script:AppUrl"
    Open-PrintHubBrowser
    exit 0
}

if (Test-Path $PidFile) {
    $existingPid = Get-Content $PidFile -Raw
    if ($existingPid) {
        $existingProcess = Get-Process -Id ([int]$existingPid) -ErrorAction SilentlyContinue
        if ($existingProcess) {
            Write-Host "Existing PrintHub process found ($existingPid). Waiting for health endpoint..."
            if (Wait-ForPrintHubHealth) {
                Write-Host "PrintHub is available at $script:AppUrl"
                Open-PrintHubBrowser
                exit 0
            }
        }
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

if (Test-Path $AppExe) {
    $process = Start-Process -FilePath $AppExe `
        -WorkingDirectory $ScriptDir `
        -RedirectStandardOutput $StdOutLog `
        -RedirectStandardError $StdErrLog `
        -WindowStyle Hidden `
        -PassThru
}
elseif (Test-Path $AppDll) {
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "`"$AppDll`"" `
        -WorkingDirectory $ScriptDir `
        -RedirectStandardOutput $StdOutLog `
        -RedirectStandardError $StdErrLog `
        -WindowStyle Hidden `
        -PassThru
}
else {
    throw "PrintHub executable was not found in $ScriptDir"
}

$process.Id | Set-Content $PidFile

if (-not (Wait-ForPrintHubHealth)) {
    throw "PrintHub started but did not become healthy at $script:AppUrl within $StartTimeoutSeconds seconds. Check $StdOutLog and $StdErrLog."
}

Write-Host "PrintHub is running at $script:AppUrl"
Write-Host "PID file: $PidFile"
Write-Host "Runtime home: $env:PRINTHUB_HOME"

Open-PrintHubBrowser
