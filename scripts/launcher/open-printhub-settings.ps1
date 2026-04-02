param()

$ErrorActionPreference = "Stop"
$env:PRINTHUB_OPEN_URL_SUFFIX = "#settings"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $ScriptDir "run-printhub.ps1")
