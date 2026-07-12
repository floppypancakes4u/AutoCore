param(
    [string]$CoverageFile = "",
    [double]$MinimumRate = 90.0
)

if ([string]::IsNullOrWhiteSpace($CoverageFile)) {
    $CoverageFile = (Get-ChildItem -Path "$PSScriptRoot\..\TestResults" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1).FullName
}

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Run the XP coverage collect command first."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package not present in coverage file: $CoverageFile"
    exit 1
}

# XP economy: ExperienceService + kill award + progress persistence + GiveXP / CharacterLevel packets.
$scoped = @($game.classes.class | Where-Object {
    $_.filename -like '*\Experience\*.cs' -or
    $_.filename -like '*\Packets\Sector\GiveXPPacket.cs' -or
    $_.filename -like '*\Packets\Sector\CharacterLevelPacket.cs'
} | Where-Object { $_.name -notmatch '[<>]' })

$lines = @($scoped | ForEach-Object { $_.lines.line })
$covered = @($lines | Where-Object { [int]$_.hits -gt 0 })
$rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $lines.Count, 2) } else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "Scoped classes: $($scoped.Count)"
Write-Output "Lines valid: $($lines.Count)"
Write-Output "Lines covered: $($covered.Count)"
Write-Output "XP scoped line coverage: $rate%"

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
    Write-Error "XP scoped coverage $rate% failed gate (minimum $MinimumRate% overall and per file)."
    exit 1
}

Write-Output "XP scoped coverage gate passed ($rate% >= $MinimumRate%)."
exit 0
