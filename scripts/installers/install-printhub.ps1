param(
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "Programs\PrintHub"
}

$ProgramsDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\PrintHub"

function New-Shortcut {
    param(
        [string]$Path,
        [string]$TargetPath,
        [string]$Arguments,
        [string]$WorkingDirectory
    )

    $wshShell = New-Object -ComObject WScript.Shell
    $shortcut = $wshShell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Save()
}

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $ScriptDir "*") $InstallDir -Recurse -Force

@'
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0run-printhub.ps1" %*
'@ | Set-Content (Join-Path $InstallDir "Start PrintHub.cmd") -Encoding Ascii

@'
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0open-printhub-settings.ps1" %*
'@ | Set-Content (Join-Path $InstallDir "Open PrintHub Settings.cmd") -Encoding Ascii

@'
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0open-printhub-printers.ps1" %*
'@ | Set-Content (Join-Path $InstallDir "Open PrintHub Printers.cmd") -Encoding Ascii

@'
@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0stop-printhub.ps1" %*
'@ | Set-Content (Join-Path $InstallDir "Stop PrintHub.cmd") -Encoding Ascii

New-Item -ItemType Directory -Force -Path $ProgramsDir | Out-Null

$powershellPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"

New-Shortcut `
    -Path (Join-Path $ProgramsDir "PrintHub.lnk") `
    -TargetPath $powershellPath `
    -Arguments "-ExecutionPolicy Bypass -File `"$InstallDir\run-printhub.ps1`"" `
    -WorkingDirectory $InstallDir

New-Shortcut `
    -Path (Join-Path $ProgramsDir "PrintHub Settings.lnk") `
    -TargetPath $powershellPath `
    -Arguments "-ExecutionPolicy Bypass -File `"$InstallDir\open-printhub-settings.ps1`"" `
    -WorkingDirectory $InstallDir

New-Shortcut `
    -Path (Join-Path $ProgramsDir "PrintHub Printers.lnk") `
    -TargetPath $powershellPath `
    -Arguments "-ExecutionPolicy Bypass -File `"$InstallDir\open-printhub-printers.ps1`"" `
    -WorkingDirectory $InstallDir

New-Shortcut `
    -Path (Join-Path $ProgramsDir "Stop PrintHub.lnk") `
    -TargetPath $powershellPath `
    -Arguments "-ExecutionPolicy Bypass -File `"$InstallDir\stop-printhub.ps1`"" `
    -WorkingDirectory $InstallDir

New-Shortcut `
    -Path (Join-Path $ProgramsDir "Uninstall PrintHub.lnk") `
    -TargetPath $powershellPath `
    -Arguments "-ExecutionPolicy Bypass -File `"$InstallDir\uninstall-printhub.ps1`"" `
    -WorkingDirectory $InstallDir

Write-Host "PrintHub was installed for the current user."
Write-Host "  App files:   $InstallDir"
Write-Host "  Start menu:  $ProgramsDir"
Write-Host "  Settings:    $ProgramsDir\PrintHub Settings.lnk"
Write-Host "  Printers:    $ProgramsDir\PrintHub Printers.lnk"
