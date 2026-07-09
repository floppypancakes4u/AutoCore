param(
    [string]$CoverageFile = (Get-ChildItem -Path "$PSScriptRoot\..\TestResults" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName,
    [double]$MinimumRate = 90.0
)

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Run: dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter FullyQualifiedName~Inventory --collect:`"XPlat Code Coverage`" --results-directory TestResults"
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
$scoped = @($game.classes.class | Where-Object {
    ($_.filename -like '*\Inventory\*' -and $_.filename -notlike '*InventoryPersistence.cs') -or
    ($_.filename -like '*\Entities\Vehicle.cs') -or
    ($_.filename -like '*\Packets\Sector\Inventory*') -or
    ($_.filename -like '*\Packets\Sector\ItemDrop*')
} | Where-Object { $_.name -notmatch '[<>]' -and $_.filename -notmatch 'DebugLog' })

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
        File = $_.Name
        Lines = $fileLines.Count
        Covered = $fileCovered
        Rate = if ($fileLines.Count -gt 0) { [math]::Round(100.0 * $fileCovered / $fileLines.Count, 1) } else { 100 }
    }
} | Sort-Object Rate)

Write-Output 'Per-file breakdown:'
$perFile | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.File, $_.Rate, $_.Covered, $_.Lines) }

$belowMinimum = @($perFile | Where-Object { $_.Rate -lt $MinimumRate })
if ($belowMinimum.Count -gt 0) {
    Write-Output "Files below $MinimumRate%:"
    $belowMinimum | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.File, $_.Rate, $_.Covered, $_.Lines) }
}

if ($rate -lt $MinimumRate -or $belowMinimum.Count -gt 0) {
    Write-Error "Inventory scoped coverage $rate% failed gate (minimum $MinimumRate% overall and per file)."
    exit 1
}

Write-Output "Inventory scoped coverage gate passed ($rate% >= $MinimumRate%)."
exit 0
