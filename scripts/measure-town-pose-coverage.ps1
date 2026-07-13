param(
    [string]$CoverageFile = "",
    [double]$MinimumRate = 95.0
)

# Line coverage gate for town on-foot logout resume fixes:
# - Character.CaptureWorldStateToDb (town vs field vs map-null pose selection)
# - Creature.HandleMovement (pose without ghost + target branches)
# - TNLConnection.EndCharacterSession / SafeTeardownStep (double-disconnect race)
#
# Usage:
#   $out = 'TestResults/town-pose-cov'
#   dotnet test src/AutoCore.Game.Tests `
#     --filter "FullyQualifiedName~CharacterWorldStateCaptureTests|FullyQualifiedName~CharacterWorldStatePersistenceTests|FullyQualifiedName~CreatureMovementPoseTests|FullyQualifiedName~VehicleCombatStatePersistenceTests" `
#     --collect:"XPlat Code Coverage" --results-directory $out `
#     -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
#   powershell -File scripts/measure-town-pose-coverage.ps1 -CoverageFile (Get-ChildItem $out -Recurse -Filter coverage.cobertura.xml | Sort LastWriteTime -Desc | Select -First 1).FullName

if ([string]::IsNullOrWhiteSpace($CoverageFile)) {
    $CoverageFile = (Get-ChildItem -Path "$PSScriptRoot\..\TestResults\town-pose-cov" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1).FullName
}

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Collect town-pose coverage first (see script header)."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package not present in coverage file: $CoverageFile"
    exit 1
}

$focus = @(
    @{ Type = 'AutoCore.Game.Entities.Character'; Method = 'CaptureWorldStateToDb' },
    @{ Type = 'AutoCore.Game.Entities.Creature'; Method = 'HandleMovement' },
    @{ Type = 'AutoCore.Game.TNL.TNLConnection'; Method = 'EndCharacterSession' },
    @{ Type = 'AutoCore.Game.TNL.TNLConnection'; Method = 'SafeTeardownStep' }
)

$methods = @()
foreach ($f in $focus) {
    $cls = @($game.classes.class | Where-Object { $_.name -eq $f.Type })
    foreach ($c in $cls) {
        foreach ($m in @($c.methods.method | Where-Object { $_.name -eq $f.Method })) {
            $methods += [PSCustomObject]@{
                Type = $c.name
                Method = $m.name
                Lines = @($m.lines.line)
            }
        }
    }
}

if ($methods.Count -eq 0) {
    Write-Error "No focused methods found in coverage XML."
    exit 1
}

$allLines = @($methods | ForEach-Object { $_.Lines })
$covered = @($allLines | Where-Object { [int]$_.hits -gt 0 })
$rate = if ($allLines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $allLines.Count, 2) } else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "Focused methods: $($methods.Count)"
Write-Output "Lines valid: $($allLines.Count)"
Write-Output "Lines covered: $($covered.Count)"
Write-Output "Town-pose focused line coverage: $rate%"

$perMethod = @($methods | ForEach-Object {
    $fileCovered = @($_.Lines | Where-Object { [int]$_.hits -gt 0 }).Count
    $total = $_.Lines.Count
    [PSCustomObject]@{
        Name = ("{0}::{1}" -f ($_.Type -replace 'AutoCore.Game\.', ''), $_.Method)
        Lines = $total
        Covered = $fileCovered
        Rate = if ($total -gt 0) { [math]::Round(100.0 * $fileCovered / $total, 1) } else { 100 }
        Uncovered = @($_.Lines | Where-Object { [int]$_.hits -eq 0 } | ForEach-Object { $_.number }) -join ','
    }
} | Sort-Object Rate, Name)

Write-Output 'Per-method breakdown:'
$perMethod | ForEach-Object {
    $extra = if ($_.Uncovered) { " uncovered=$($_.Uncovered)" } else { "" }
    Write-Output ("  {0}: {1}% ({2}/{3}){4}" -f $_.Name, $_.Rate, $_.Covered, $_.Lines, $extra)
}

$below = @($perMethod | Where-Object { $_.Rate -lt $MinimumRate -and $_.Lines -gt 0 })
if ($rate -lt $MinimumRate -or $below.Count -gt 0) {
    Write-Error "Town-pose coverage gate failed (focused=$rate%, minimum $MinimumRate%)."
    if ($below.Count -gt 0) {
        Write-Output "Methods below $MinimumRate%:"
        $below | ForEach-Object { Write-Output ("  {0}: {1}%" -f $_.Name, $_.Rate) }
    }
    exit 1
}

Write-Output "Town-pose coverage gate passed (focused $rate% >= $MinimumRate%)."
exit 0
