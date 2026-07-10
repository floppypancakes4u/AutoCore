param(
    [string]$CoverageFile = "",
    [double]$MinimumRate = 90.0
)

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($CoverageFile)) {
    $CoverageFile = (Get-ChildItem -Path "$repoRoot\TestResults" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1).FullName
}

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package missing from coverage report."
    exit 1
}

# Only modules introduced/rewritten for map-prop combat + kill progress + invincible/faction.
$patterns = @(
    '*\Entities\GraphicsObject.cs',
    '*\Entities\ReactionObjectStateEffects.cs',
    '*\Managers\MissionKillProgress.cs'
)

$scoped = @($game.classes.class | Where-Object {
    $fn = $_.filename -replace '/', '\'
    $nameOk = $_.name -notmatch '[<>]'
    $fileOk = $false
    foreach ($p in $patterns) {
        if ($fn -like $p) { $fileOk = $true; break }
    }
    $nameOk -and $fileOk
})

$lines = @($scoped | ForEach-Object { $_.lines.line })
$covered = @($lines | Where-Object { [int]$_.hits -gt 0 })
$rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $lines.Count, 2) } else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "Scoped classes: $($scoped.Count)"
Write-Output "Lines valid: $($lines.Count)"
Write-Output "Lines covered: $($covered.Count)"
Write-Output "Scoped line coverage: $rate%"

$perFile = @($scoped | Group-Object filename | ForEach-Object {
    $fileLines = @($_.Group | ForEach-Object { $_.lines.line })
    $fileCovered = @($fileLines | Where-Object { [int]$_.hits -gt 0 }).Count
    [PSCustomObject]@{
        File = ($_.Name -replace '.*\\', '' -replace '.*/', '')
        Full = $_.Name
        Lines = $fileLines.Count
        Covered = $fileCovered
        Rate = if ($fileLines.Count -gt 0) { [math]::Round(100.0 * $fileCovered / $fileLines.Count, 1) } else { 100 }
    }
} | Sort-Object Rate)

Write-Output 'Per-file breakdown:'
$perFile | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.File, $_.Rate, $_.Covered, $_.Lines) }

# Uncovered lines by file (for gap filling)
Write-Output 'Uncovered line samples (first 25 per weak file):'
foreach ($f in @($perFile | Where-Object { $_.Rate -lt $MinimumRate })) {
    Write-Output "  --- $($f.File) ---"
    $classGroup = @($scoped | Where-Object { $_.filename -eq $f.Full })
    $uncovered = @($classGroup | ForEach-Object { $_.lines.line } | Where-Object { [int]$_.hits -eq 0 } | Select-Object -First 25)
    foreach ($u in $uncovered) {
        Write-Output ("    L{0}" -f $u.number)
    }
}

$belowMinimum = @($perFile | Where-Object { $_.Rate -lt $MinimumRate })
if ($rate -lt $MinimumRate -or $belowMinimum.Count -gt 0) {
    Write-Error "Mission-combat scoped coverage $rate% failed gate (minimum $MinimumRate% overall and per file)."
    exit 1
}

Write-Output "Mission-combat scoped coverage gate passed ($rate% >= $MinimumRate%)."
exit 0
