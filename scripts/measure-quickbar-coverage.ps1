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
    Write-Error "Coverage file not found. Run quickbar coverage collect first."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
if (-not $game) {
    Write-Error "AutoCore.Game package not present in coverage file: $CoverageFile"
    exit 1
}

# Modules owned by the QuickBarUpdate persistence work.
$packet = @($game.classes.class | Where-Object {
    $_.name -eq 'AutoCore.Game.Packets.Sector.QuickBarUpdatePacket'
})
$service = @($game.classes.class | Where-Object {
    $_.name -eq 'AutoCore.Game.Skills.CharacterSkillService'
})
# Only the Sector partial (where HandleQuickBarUpdatePacket lives). Full TNLConnection is huge.
$sector = @($game.classes.class | Where-Object {
    $_.filename -like '*TNLConnection.Sector.cs' -and $_.name -notmatch '[<>]'
})

function Get-LineStats($classes) {
    $lines = @($classes | ForEach-Object { $_.lines.line })
    $covered = @($lines | Where-Object { [int]$_.hits -gt 0 })
    return @{
        Lines = $lines.Count
        Covered = $covered.Count
        Rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $lines.Count, 2) } else { 0 }
        LinesList = $lines
    }
}

$packetStats = Get-LineStats $packet
$serviceStats = Get-LineStats $service
$sectorStats = Get-LineStats $sector

# Handler method only (lines written for QuickBarUpdate) — Sector.cs overall is mostly unrelated.
$handlerLines = @($sector | ForEach-Object { $_.lines.line } | Where-Object {
    $n = [int]$_.number
    $n -ge 90 -and $n -le 112
})
$handlerCovered = @($handlerLines | Where-Object { [int]$_.hits -gt 0 })
$handlerRate = if ($handlerLines.Count -gt 0) {
    [math]::Round(100.0 * $handlerCovered.Count / $handlerLines.Count, 2)
} else { 0 }

# Scoped gate: packet + service + handler method (not entire Sector.cs).
$scopedLines = $packetStats.LinesList + $serviceStats.LinesList + $handlerLines
$scopedCovered = @($scopedLines | Where-Object { [int]$_.hits -gt 0 })
$scopedRate = if ($scopedLines.Count -gt 0) {
    [math]::Round(100.0 * $scopedCovered.Count / $scopedLines.Count, 2)
} else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "QuickBarUpdatePacket: $($packetStats.Rate)% ($($packetStats.Covered)/$($packetStats.Lines))"
Write-Output "CharacterSkillService: $($serviceStats.Rate)% ($($serviceStats.Covered)/$($serviceStats.Lines))"
Write-Output "HandleQuickBarUpdatePacket (Sector.cs:90-112): $handlerRate% ($($handlerCovered.Count)/$($handlerLines.Count))"
Write-Output "TNLConnection.Sector.cs overall (reference only): $($sectorStats.Rate)% ($($sectorStats.Covered)/$($sectorStats.Lines))"
Write-Output "Quickbar scoped line coverage: $scopedRate% ($($scopedCovered.Count)/$($scopedLines.Count))"

$failed = $false
if ($packetStats.Rate -lt $MinimumRate) {
    Write-Output "FAIL: QuickBarUpdatePacket below $MinimumRate%"
    $failed = $true
}
if ($serviceStats.Rate -lt $MinimumRate) {
    Write-Output "FAIL: CharacterSkillService below $MinimumRate%"
    $failed = $true
}
if ($handlerRate -lt $MinimumRate) {
    Write-Output "FAIL: HandleQuickBarUpdatePacket below $MinimumRate%"
    $failed = $true
}
if ($scopedRate -lt $MinimumRate) {
    Write-Output "FAIL: quickbar scoped coverage below $MinimumRate%"
    $failed = $true
}

# Uncovered handler lines for debugging.
$missed = @($handlerLines | Where-Object { [int]$_.hits -eq 0 })
if ($missed.Count -gt 0) {
    Write-Output "Uncovered handler lines:"
    $missed | ForEach-Object { Write-Output "  line $($_.number)" }
}

if ($failed) {
    Write-Error "Quickbar coverage gate failed (minimum $MinimumRate%)."
    exit 1
}

Write-Output "Quickbar coverage gate passed ($scopedRate% >= $MinimumRate%)."
# Note: CharacterSkillService lines 63-65 are the real DB Persist path (bypassed by PersistForTests).
# Entire TNLConnection.Sector.cs is intentionally not gated (hundreds of unrelated handlers).
exit 0
