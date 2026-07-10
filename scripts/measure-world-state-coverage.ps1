param(
    [string]$CoverageFile = "",
    [double]$MinimumRate = 90.0
)

if ([string]::IsNullOrWhiteSpace($CoverageFile)) {
    $CoverageFile = (Get-ChildItem -Path "$PSScriptRoot\..\TestResults\world-state-msbuild" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1).FullName
}

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Run coverlet collect for world-state first."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package not present in coverage file: $CoverageFile"
    exit 1
}

# New/updated world-state surface (methods only — avoids diluting by unrelated Character/Vehicle code).
$focusMethodNames = @(
    'CaptureWorldStateToDb',
    'PersistFromCharacter',
    'Save',
    'SaveWithStore',
    'ApplyToCharacter',
    'ApplyToVehicle',
    'EndCharacterSession',
    'OnConnectionTerminated',
    'get_WorldStatePersistenceForTests',
    'get_WorldStatePersistence',
    'get_Instance',
    'get_ContextFactoryForTests',
    'set_ContextFactoryForTests',
    'set_WorldStatePersistenceForTests',
    'AttachTestDataForTests',
    'SetLastTownIdForTests',
    'GetDbPositionXForTests',
    'GetDbPositionYForTests',
    'GetDbPositionZForTests',
    'GetDbRotationXForTests',
    'GetDbRotationWForTests',
    'GetDbRotationYForTests',
    'TransferCharacterToMap',
    'FindCharacter',
    'FindVehicle',
    'SaveChanges',
    '.ctor'
)

$focusTypes = @(
    'AutoCore.Game.Managers.CharacterWorldStatePersistence',
    'AutoCore.Game.Managers.CharacterWorldStatePersistence/CharContextWorldStateStore',
    'AutoCore.Game.Structures.CharacterWorldStateSnapshot',
    'AutoCore.Game.Entities.Character',
    'AutoCore.Game.Entities.Vehicle',
    'AutoCore.Game.Managers.MapManager',
    'AutoCore.Game.TNL.TNLConnection'
)

$methods = @()
foreach ($c in @($game.classes.class | Where-Object { $_.name -notmatch '[<>]' -and ($focusTypes -contains $_.name -or $_.name -like 'AutoCore.Game.Managers.CharacterWorldStatePersistence*') })) {
    if (-not $c.methods) { continue }
    foreach ($m in @($c.methods.method)) {
        $include = $false
        if ($c.name -like '*CharacterWorldState*' -or $c.name -like '*CharacterWorldStateSnapshot*') {
            $include = $true
        }
        elseif ($focusMethodNames -contains $m.name) {
            $include = $true
        }
        if ($include) {
            $methods += [PSCustomObject]@{
                Type = $c.name
                Method = $m.name
                Lines = @($m.lines.line)
            }
        }
    }
}

$allLines = @($methods | ForEach-Object { $_.Lines })
$covered = @($allLines | Where-Object { [int]$_.hits -gt 0 })
$rate = if ($allLines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $allLines.Count, 2) } else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "Focused methods: $($methods.Count)"
Write-Output "Lines valid: $($allLines.Count)"
Write-Output "Lines covered: $($covered.Count)"
Write-Output "World-state focused line coverage: $rate%"

$perMethod = @($methods | ForEach-Object {
    $fileCovered = @($_.Lines | Where-Object { [int]$_.hits -gt 0 }).Count
    $total = $_.Lines.Count
    [PSCustomObject]@{
        Name = ("{0}::{1}" -f ($_.Type -replace 'AutoCore.Game\.',''), $_.Method)
        Lines = $total
        Covered = $fileCovered
        Rate = if ($total -gt 0) { [math]::Round(100.0 * $fileCovered / $total, 1) } else { 100 }
        Uncovered = @($_.Lines | Where-Object { [int]$_.hits -eq 0 } | ForEach-Object { $_.number }) -join ','
    }
} | Sort-Object Rate, Name)

Write-Output 'Per-method breakdown (lowest first):'
$perMethod | ForEach-Object {
    $extra = if ($_.Uncovered) { " uncovered=$($_.Uncovered)" } else { "" }
    Write-Output ("  {0}: {1}% ({2}/{3}){4}" -f $_.Name, $_.Rate, $_.Covered, $_.Lines, $extra)
}

# Also report full CharacterWorldStatePersistence file coverage
$cwpClasses = @($game.classes.class | Where-Object {
    $_.filename -like '*CharacterWorldStatePersistence.cs' -or
    $_.filename -like '*CharacterWorldStateSnapshot.cs'
} | Where-Object { $_.name -notmatch '[<>]' })

$cwpLines = @($cwpClasses | ForEach-Object { $_.lines.line })
$cwpCovered = @($cwpLines | Where-Object { [int]$_.hits -gt 0 })
$cwpRate = if ($cwpLines.Count -gt 0) { [math]::Round(100.0 * $cwpCovered.Count / $cwpLines.Count, 2) } else { 100 }
Write-Output ""
Write-Output "New files (Persistence + Snapshot) line coverage: $cwpRate% ($($cwpCovered.Count)/$($cwpLines.Count))"

$below = @($perMethod | Where-Object { $_.Rate -lt $MinimumRate -and $_.Lines -gt 0 })
if ($rate -lt $MinimumRate -or $cwpRate -lt $MinimumRate) {
    Write-Error "World-state coverage gate failed (focused=$rate% new-files=$cwpRate%, minimum $MinimumRate%)."
    if ($below.Count -gt 0) {
        Write-Output "Methods below $MinimumRate%:"
        $below | ForEach-Object { Write-Output ("  {0}: {1}%" -f $_.Name, $_.Rate) }
    }
    exit 1
}

Write-Output "World-state coverage gate passed (focused $rate% >= $MinimumRate%, new files $cwpRate% >= $MinimumRate%)."
exit 0
