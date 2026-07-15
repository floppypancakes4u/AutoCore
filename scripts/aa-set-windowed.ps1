# PLATE: Toggle Auto Assault MODE_WINDOWED in exe\vog.ini (required for layered CEF overlays).
# Usage:
#   powershell -File scripts\aa-set-windowed.ps1              # enable windowed
#   powershell -File scripts\aa-set-windowed.ps1 -Fullscreen  # exclusive FS
#   powershell -File scripts\aa-set-windowed.ps1 -GamePath "C:\...\Auto Assault.bak"

param(
    [string]$GamePath = "",
    [switch]$Fullscreen
)

$ErrorActionPreference = "Stop"
if (-not $GamePath) {
    $bak = "C:\Program Files (x86)\NetDevil\Auto Assault.bak"
    $stock = "C:\Program Files (x86)\NetDevil\Auto Assault"
    if ($env:AA_INSTALL) { $GamePath = $env:AA_INSTALL }
    elseif (Test-Path (Join-Path $bak "exe\vog.ini")) { $GamePath = $bak }
    else { $GamePath = $stock }
}

$ini = Join-Path $GamePath "exe\vog.ini"
if (-not (Test-Path $ini)) { throw "vog.ini not found: $ini" }

$val = if ($Fullscreen) { "0" } else { "1" }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
Copy-Item $ini "$ini.bak-$stamp" -Force

$lines = Get-Content $ini
$found = $false
$out = foreach ($line in $lines) {
    if ($line -match '^\s*MODE_WINDOWED\s*=') {
        $found = $true
        "MODE_WINDOWED=$val;"
    } else { $line }
}
if (-not $found) { $out += "MODE_WINDOWED=$val;" }
$out | Set-Content $ini -Encoding ASCII

Write-Host "Updated $ini"
Write-Host "  MODE_WINDOWED=$val  $(if ($val -eq '1') { '(windowed — layered overlays can show)' } else { '(exclusive FS — layered overlays usually invisible)' })"
