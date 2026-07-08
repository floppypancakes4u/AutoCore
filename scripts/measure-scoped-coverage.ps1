param(
    [string]$CoverageFile = (Get-ChildItem -Path "$PSScriptRoot\..\TestResults" -Recurse -Filter coverage.cobertura.xml | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
)

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
$scoped = @($game.classes.class | Where-Object {
    ($_.filename -like '*\Inventory\*' -and $_.filename -notlike '*InventoryPersistence.cs') -or
    ($_.filename -like '*\Entities\Vehicle.cs') -or
    ($_.filename -like '*\Packets\Sector\Inventory*')
} | Where-Object { $_.name -notmatch '[<>]' -and $_.filename -notmatch 'DebugLog' })

$lines = @($scoped | ForEach-Object { $_.lines.line })
$covered = @($lines | Where-Object { [int]$_.hits -gt 0 })
$rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $lines.Count, 2) } else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "Scoped classes: $($scoped.Count)"
Write-Output "Lines valid: $($lines.Count)"
Write-Output "Lines covered: $($covered.Count)"
Write-Output "Scoped line coverage: $rate%"

$uncovered = @($scoped | Where-Object { $_.name -notmatch '[<>]' } | Group-Object filename | ForEach-Object {
    $lines = @($_.Group | ForEach-Object { $_.lines.line })
    $coveredCount = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    [PSCustomObject]@{
        File = $_.Name
        Lines = $lines.Count
        Covered = $coveredCount
        Rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $coveredCount / $lines.Count, 1) } else { 100 }
    }
} | Sort-Object Rate)

Write-Output 'Per-file breakdown:'
$uncovered | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.File, $_.Rate, $_.Covered, $_.Lines) }

$uncoveredLow = @($uncovered | Where-Object { $_.Rate -lt 90 })
if ($uncoveredLow.Count -gt 0 -and $rate -lt 90) {
    Write-Output 'Files below 90%:'
    $uncoveredLow | ForEach-Object { Write-Output ("  {0}: {1}%" -f $_.File, $_.Rate) }
}
