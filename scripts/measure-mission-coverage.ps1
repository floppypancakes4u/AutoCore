param(
    [string]$CoverageFile = "",
    [double]$MinimumRate = 90.0
)

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($CoverageFile)) {
    $CoverageFile = (Get-ChildItem -Path "$repoRoot\TestResults\mission-cov" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1).FullName
}

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Run mission coverlet collect first."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package missing from coverage report."
    exit 1
}

# Runtime mission logic + requirement models + packets.
# Excluded: Mission.cs / MissionObjective.cs binary WAD Read() (asset I/O, needs clonebase.wad).
# CreateForTests paths are exercised by interact/kill/grant tests without counting WAD loader lines.
$patterns = @(
    '*\Managers\NpcInteractHandler.cs',
    '*\Managers\MissionKillProgress.cs',
    '*\Managers\IncompleteHandlerLog.cs',
    '*\Structures\CharacterQuest.cs',
    '*\Mission\Requirements\*',
    '*\Mission\MissionString.cs',
    '*\Packets\Sector\UseObjectPacket.cs',
    '*\Packets\Sector\AutoPatrolPacket.cs',
    '*\Packets\Sector\NpcMissionDialogPacket.cs',
    '*\Packets\Sector\MissionDialogResponsePacket.cs',
    '*\Packets\Sector\ObjectiveStatePacket.cs',
    '*\Packets\Sector\CompleteDynamicObjectivePacket.cs',
    '*\Packets\Sector\FailMissionPacket.cs',
    '*\Packets\Global\ConvoyMissionsRequestPacket.cs',
    '*\Packets\Global\ConvoyMissionsResponsePacket.cs'
)

$scoped = @($game.classes.class | Where-Object {
    $fn = $_.filename -replace '/', '\'
    if ($_.name -match '[<>]') { return $false }
    if ($fn -like '*\MissionDialogPacket.cs') { return $false }
    if ($fn -like '*\Mission\Mission.cs') { return $false }
    if ($fn -like '*\Mission\MissionObjective.cs') { return $false }
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
$perFile | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.File, $_.Rate, $_.Covered, $_.Lines) }

Write-Output 'Uncovered line samples (first 30 per weak file):'
foreach ($f in @($perFile | Where-Object { $_.Rate -lt $MinimumRate })) {
    Write-Output "  --- $($f.File) ---"
    $classGroup = @($scoped | Where-Object { $_.filename -eq $f.Full })
    $uncovered = @($classGroup | ForEach-Object { $_.lines.line } | Where-Object { [int]$_.hits -eq 0 } | Select-Object -First 30)
    foreach ($u in $uncovered) {
        Write-Output ("    L{0}" -f $u.number)
    }
}

$belowMinimum = @($perFile | Where-Object { $_.Rate -lt $MinimumRate })
if ($rate -lt $MinimumRate -or $belowMinimum.Count -gt 0) {
    Write-Error "Mission scoped coverage $rate% failed gate (minimum $MinimumRate% overall and per file)."
    exit 1
}

Write-Output "Mission scoped coverage gate passed ($rate% >= $MinimumRate%)."
exit 0
