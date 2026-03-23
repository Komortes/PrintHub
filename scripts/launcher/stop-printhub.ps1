$ErrorActionPreference = "Stop"

function Resolve-PrintHubHome {
    if (-not [string]::IsNullOrWhiteSpace($env:PRINTHUB_HOME)) {
        return $env:PRINTHUB_HOME
    }

    return Join-Path $env:LOCALAPPDATA "PrintHub"
}

$env:PRINTHUB_HOME = Resolve-PrintHubHome
$PidFile = Join-Path $env:PRINTHUB_HOME "runtime/printhub.pid"

if (-not (Test-Path $PidFile)) {
    Write-Host "No PrintHub PID file was found at $PidFile"
    exit 0
}

$pidValue = Get-Content $PidFile -Raw

if ([string]::IsNullOrWhiteSpace($pidValue)) {
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    Write-Host "Removed empty PID file."
    exit 0
}

$process = Get-Process -Id ([int]$pidValue) -ErrorAction SilentlyContinue

if (-not $process) {
    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    Write-Host "Removed stale PID file for process $pidValue."
    exit 0
}

Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$process = Get-Process -Id ([int]$pidValue) -ErrorAction SilentlyContinue
if ($process) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
Write-Host "Stopped PrintHub process $pidValue."
