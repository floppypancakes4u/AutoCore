param(
    [string[]]$CoverageFiles = @(),
    [double]$MinimumRate = 90.0
)

# Line-range gate for the player remote-pose smoothness change:
# - Vehicle.AdvanceNetworkPose + MultiplyQuaternion (+ test hooks)
# - SectorPlayerPoseTick (whole file)
#
# Usage:
#   powershell -File scripts/measure-player-pose-coverage.ps1 -CoverageFiles @(
#     'TestResults/player-pose-cov/game/.../coverage.cobertura.xml',
#     'TestResults/player-pose-cov/sector/.../coverage.cobertura.xml'
#   )

if ($CoverageFiles.Count -eq 0) {
    $CoverageFiles = @(Get-ChildItem -Path "$PSScriptRoot\..\TestResults" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 5 -ExpandProperty FullName)
}

if ($CoverageFiles.Count -eq 0) {
    Write-Error "No coverage files found."
    exit 1
}

Write-Output "Coverage files:"
$CoverageFiles | ForEach-Object { Write-Output ("  {0}" -f $_) }

# Resolve AdvanceNetworkPose line range from source.
$vehiclePath = Join-Path $PSScriptRoot "..\src\AutoCore.Game\Entities\Vehicle.cs"
$vehicleSrc = Get-Content $vehiclePath
$advStart = 0
$multEnd = 0
for ($i = 0; $i -lt $vehicleSrc.Count; $i++) {
    if ($vehicleSrc[$i] -match 'public bool AdvanceNetworkPose\(') {
        $advStart = $i + 1
    }
    if ($advStart -gt 0 -and $vehicleSrc[$i] -match 'private static Quaternion MultiplyQuaternion') {
        $depth = 0
        $seen = $false
        for ($j = $i; $j -lt $vehicleSrc.Count; $j++) {
            $line = $vehicleSrc[$j]
            if ($line -match '\{') { $depth += ([regex]::Matches($line, '\{')).Count; $seen = $true }
            if ($line -match '\}') { $depth -= ([regex]::Matches($line, '\}')).Count }
            if ($seen -and $depth -le 0) {
                $multEnd = $j + 1
                break
            }
        }
        break
    }
}

$hookStart = 0
$hookEnd = 0
for ($i = 0; $i -lt $vehicleSrc.Count; $i++) {
    if ($vehicleSrc[$i] -match 'SetAngularVelocityForTests') {
        $hookStart = $i + 1
        # include following velocity setter (usually next few lines)
        $hookEnd = [Math]::Min($vehicleSrc.Count, $i + 6)
        break
    }
}

if ($advStart -le 0 -or $multEnd -le 0) {
    Write-Error "Could not locate AdvanceNetworkPose/MultiplyQuaternion in Vehicle.cs"
    exit 1
}

Write-Output ("Vehicle.cs pose methods: lines {0}-{1}" -f $advStart, $multEnd)
if ($hookStart -gt 0) {
    Write-Output ("Vehicle.cs test hooks: lines {0}-{1}" -f $hookStart, $hookEnd)
}

# Merge hits: key = package|fileSuffix|lineNumber -> max hits
$hits = @{}

foreach ($cf in $CoverageFiles) {
    if (-not (Test-Path $cf)) {
        Write-Error "Missing coverage file: $cf"
        exit 1
    }
    [xml]$xml = Get-Content $cf
    foreach ($pkg in @($xml.coverage.packages.package)) {
        foreach ($cls in @($pkg.classes.class)) {
            if ($cls.name -match '[<>]') { continue }
            $fn = [string]$cls.filename
            foreach ($line in @($cls.lines.line)) {
                $n = [int]$line.number
                $h = [int]$line.hits
                $key = "{0}|{1}|{2}" -f $pkg.name, $fn, $n
                if (-not $hits.ContainsKey($key) -or $h -gt $hits[$key]) {
                    $hits[$key] = $h
                }
            }
        }
    }
}

function Get-ScopedRate {
    param(
        [string]$Package,
        [string]$FileEndsWith,
        [int]$StartLine,
        [int]$EndLine,
        [string]$Label
    )

    $matched = @()
    foreach ($key in $hits.Keys) {
        $parts = $key -split '\|', 3
        if ($parts.Count -lt 3) { continue }
        $pkg = $parts[0]
        $fn = $parts[1]
        $n = [int]$parts[2]
        if ($pkg -ne $Package) { continue }
        if (-not ($fn -like "*$FileEndsWith")) { continue }
        # Avoid GhostVehicle.cs matching *Vehicle.cs
        if ($FileEndsWith -eq 'Entities\Vehicle.cs' -and $fn -notlike '*\Entities\Vehicle.cs') { continue }
        if ($StartLine -gt 0 -and $n -lt $StartLine) { continue }
        if ($EndLine -gt 0 -and $n -gt $EndLine) { continue }
        $matched += [PSCustomObject]@{ Number = $n; Hits = $hits[$key] }
    }

    # Dedup line numbers (max hits already in map; multiple classes same file ok)
    $byLine = @{}
    foreach ($m in $matched) {
        if (-not $byLine.ContainsKey($m.Number) -or $m.Hits -gt $byLine[$m.Number]) {
            $byLine[$m.Number] = $m.Hits
        }
    }

    $nums = @($byLine.Keys | Sort-Object)
    $covered = @($nums | Where-Object { $byLine[$_] -gt 0 })
    $rate = if ($nums.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $nums.Count, 2) } else { 0 }

    # Write-Host so assignment of return value does not swallow the report line.
    Write-Host ("{0}: {1}% ({2}/{3} lines)" -f $Label, $rate, $covered.Count, $nums.Count)
    $uncovered = @($nums | Where-Object { $byLine[$_] -eq 0 })
    if ($uncovered.Count -gt 0) {
        Write-Host ("  uncovered lines: {0}" -f ($uncovered -join ', '))
    }
    if ($nums.Count -eq 0) {
        Write-Host ("  WARNING: no instrumented lines matched for {0}" -f $Label)
    }
    return ,$rate
}

$r1 = Get-ScopedRate -Package 'AutoCore.Game' -FileEndsWith 'Entities\Vehicle.cs' `
    -StartLine $advStart -EndLine $multEnd -Label 'Vehicle.AdvanceNetworkPose+MultiplyQuaternion'
$r2 = 100.0
if ($hookStart -gt 0) {
    $r2 = Get-ScopedRate -Package 'AutoCore.Game' -FileEndsWith 'Entities\Vehicle.cs' `
        -StartLine $hookStart -EndLine $hookEnd -Label 'Vehicle pose test hooks'
}
$r3 = Get-ScopedRate -Package 'AutoCore.Sector' -FileEndsWith 'SectorPlayerPoseTick.cs' `
    -StartLine 0 -EndLine 0 -Label 'SectorPlayerPoseTick.cs'

$failed = $false
if ($r1 -lt $MinimumRate) { Write-Host ("FAIL: pose methods {0}% < {1}%" -f $r1, $MinimumRate); $failed = $true }
if ($r2 -lt $MinimumRate) { Write-Host ("FAIL: test hooks {0}% < {1}%" -f $r2, $MinimumRate); $failed = $true }
if ($r3 -lt $MinimumRate) { Write-Host ("FAIL: SectorPlayerPoseTick {0}% < {1}%" -f $r3, $MinimumRate); $failed = $true }

if ($failed) {
    Write-Error "Player pose coverage gate failed (minimum $MinimumRate% per scoped unit)."
    exit 1
}

Write-Host ("Player pose coverage gate passed (>= {0}% on all scoped units)." -f $MinimumRate)
exit 0
