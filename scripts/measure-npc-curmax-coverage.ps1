# Measures line coverage for Cur/Max NPC driver-attach modules (NPC.md §14.4).
# Gate: >= 95% overall on included types.
param(
    [double]$MinimumRate = 95.0
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

$outDir = Join-Path $repo 'TestResults\npc-curmax-modules'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$include = @(
    '[AutoCore.Game]AutoCore.Game.Map.ForeignNpcDriverWire',
    '[AutoCore.Game]AutoCore.Game.Packets.Sector.CreateCreaturePacket'
) -join '%2c'

$filter = @(
    'FullyQualifiedName~ForeignNpcDriverWireTests',
    'FullyQualifiedName~ForeignDriverCreateScopeTests',
    'FullyQualifiedName~CreateCreatureLayoutTests',
    'FullyQualifiedName~ForeignOwnerAttachReapplyTests',
    'FullyQualifiedName~VehicleWireTests'
) -join '|'

dotnet test (Join-Path $repo 'src\AutoCore.Game.Tests\AutoCore.Game.Tests.csproj') `
    --filter $filter `
    /p:CollectCoverage=true `
    /p:CoverletOutputFormat=cobertura `
    /p:CoverletOutput="$outDir\coverage" `
    /p:Include=$include `
    /p:Threshold=$MinimumRate `
    /p:ThresholdType=line `
    /p:ThresholdStat=total
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$cov = Get-ChildItem $outDir -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
if (-not $cov) {
    Write-Error "Coverage file not produced under $outDir"
    exit 1
}

[xml]$xml = Get-Content $cov.FullName
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }
$rows = @($game.classes.class | Where-Object { $_.name -notmatch '[<>]' } | ForEach-Object {
    $lines = @($_.lines.line)
    $hit = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    $rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $hit / $lines.Count, 1) } else { 100 }
    [PSCustomObject]@{ Class = $_.name; Rate = $rate; Hit = $hit; Total = $lines.Count }
})

$rows | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.Class, $_.Rate, $_.Hit, $_.Total) }
$total = ($rows | Measure-Object -Property Total -Sum).Sum
$hit = ($rows | Measure-Object -Property Hit -Sum).Sum
$overall = if ($total -gt 0) { [math]::Round(100.0 * $hit / $total, 2) } else { 0 }
Write-Output "Overall Cur/Max NPC modules: $overall% ($hit/$total)"

if ($overall -lt $MinimumRate) {
    Write-Error "Cur/Max NPC coverage $overall% failed gate (minimum $MinimumRate%)."
    exit 1
}

Write-Output "Cur/Max NPC coverage gate passed ($overall% >= $MinimumRate%)."
exit 0
