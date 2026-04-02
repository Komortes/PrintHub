param()

$ErrorActionPreference = "Stop"
$env:PRINTHUB_OPEN_URL_SUFFIX = "#printers"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $ScriptDir "run-printhub.ps1")
