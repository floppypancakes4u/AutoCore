# PLATE: Forward args to tools/mission-live (python -m mission_live).
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsRest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$live = Join-Path $root "tools\mission-live"

if (-not (Test-Path $live)) {
    Write-Error "mission-live not found at $live"
}

Push-Location $live
try {
    python -m mission_live @ArgsRest
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
