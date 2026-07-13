param(
    [string]$CoverageFile = "",
    [double]$MinimumRate = 95.0
)

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($CoverageFile)) {
    $CoverageFile = (Get-ChildItem -Path "$repoRoot\TestResults\mission-phase-cov" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1).FullName
}

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Run coverlet collect into TestResults/mission-phase-cov first."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package missing from coverage report."
    exit 1
}

# Primary gate (must hit MinimumRate): pure + focused modules from this feature work.
# Large orchestration files (SectorMap / Reaction / full NpcInteractHandler) are reported
# separately — they contain extensive pre-existing surface outside this change.
$patterns = @(
    '*\Map\CharacterMapPresence.cs',
    '*\Managers\MissionWorldPhaseRules.cs',
    '*\Skills\SkillService.cs',
    '*\Skills\SkillElementTypes.cs',
    '*\Skills\SkillResponse.cs'
)

$secondaryPatterns = @(
    '*\Map\SectorMap.cs',
    '*\Managers\NpcInteractHandler.cs',
    '*\Entities\Reaction.cs'
)

$scoped = @($game.classes.class | Where-Object {
    $fn = $_.filename -replace '/', '\'
    if ($_.name -match '[<>]') { return $false }
    foreach ($p in $patterns) {
        if ($fn -like $p) { return $true }
    }
    return $false
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
} | Sort-Object Rate, File)

Write-Output 'Per-file breakdown:'
$perFile | ForEach-Object {
    Write-Output ("  {0,6:N1}%  {1,5}/{2,-5}  {3}" -f $_.Rate, $_.Covered, $_.Lines, $_.File)
}

$uncovered = @()
foreach ($cls in $scoped) {
    $fn = $cls.filename
    foreach ($line in $cls.lines.line) {
        if ([int]$line.hits -eq 0) {
            $uncovered += [PSCustomObject]@{ File = ($fn -replace '.*\\', '' -replace '.*/', ''); Line = $line.number; Class = $cls.name }
        }
    }
}

if ($uncovered.Count -gt 0) {
    Write-Output ''
    Write-Output "Uncovered lines (first 80):"
    $uncovered | Select-Object -First 80 | ForEach-Object {
        Write-Output ("  {0}:{1}  ({2})" -f $_.File, $_.Line, $_.Class)
    }
}

# Secondary report (informational — large files with pre-existing code).
$secondary = @($game.classes.class | Where-Object {
    $fn = $_.filename -replace '/', '\'
    if ($_.name -match '[<>]') { return $false }
    foreach ($p in $secondaryPatterns) {
        if ($fn -like $p) { return $true }
    }
    return $false
})
if ($secondary.Count -gt 0) {
    $sLines = @($secondary | ForEach-Object { $_.lines.line })
    $sCovered = @($sLines | Where-Object { [int]$_.hits -gt 0 })
    $sRate = if ($sLines.Count -gt 0) { [math]::Round(100.0 * $sCovered.Count / $sLines.Count, 2) } else { 0 }
    Write-Output ''
    Write-Output "Secondary (large orchestration files, informational): $sRate% ($($sCovered.Count)/$($sLines.Count))"
    $secondary | Group-Object filename | ForEach-Object {
        $fileLines = @($_.Group | ForEach-Object { $_.lines.line })
        $fileCovered = @($fileLines | Where-Object { [int]$_.hits -gt 0 }).Count
        $fr = if ($fileLines.Count -gt 0) { [math]::Round(100.0 * $fileCovered / $fileLines.Count, 1) } else { 100 }
        Write-Output ("  {0,6:N1}%  {1,5}/{2,-5}  {3}" -f $fr, $fileCovered, $fileLines.Count, ($_.Name -replace '.*\\', ''))
    }
}

if ($rate -lt $MinimumRate) {
    Write-Error "Primary coverage $rate% is below minimum $MinimumRate%."
    exit 2
}

Write-Output ''
Write-Output "OK: primary coverage $rate% >= $MinimumRate%."
exit 0
