# Streams GhostObjectDiag server lines + PathAHook GhostApply client lines to stdout.
$ErrorActionPreference = 'Continue'
$live = 'C:\Users\josh\.grok\sessions\C%3A%5CUsers%5Cjosh%5CDocuments%5CGitHub%5CAutoCore\019f5d27-a3b9-7332-bed5-da22a0576581\terminal\call-c8782186-5d40-4a0e-bba5-b1104fbe0214-230.log'
$hits = Join-Path $env:TEMP 'AutoCorePathA\hits.jsonl'
$fallbackServer = 'C:\Users\josh\Documents\GitHub\AutoCore\server-ghost-object-diag.log'
$deadline = (Get-Date).AddHours(2)
$paths = @($live, $hits, $fallbackServer)
$positions = @{}
foreach ($k in $paths) {
    $positions[$k] = 0
    if (Test-Path $k) { $positions[$k] = (Get-Item $k).Length }
}
Write-Output "MONITOR_START live=$live hits=$hits"

while ((Get-Date) -lt $deadline) {
    foreach ($path in $paths) {
        if (-not (Test-Path $path)) { continue }
        $len = (Get-Item $path).Length
        $pos = $positions[$path]
        if ($len -le $pos) { continue }
        try {
            $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
            try {
                [void]$fs.Seek($pos, 'Begin')
                $sr = New-Object IO.StreamReader($fs)
                while (($line = $sr.ReadLine()) -ne $null) {
                    if ($path -eq $hits) {
                        if ($line -match 'GhostApply|GhostOnAdd|CRASH|SetupPathA') {
                            Write-Output ("CLIENT " + $line)
                        }
                    }
                    else {
                        if ($line -match 'GhostObjectDiag|BecameDamagable|ScopeAlways|PackInitial|CreateGhost') {
                            Write-Output ("SERVER " + $line)
                        }
                    }
                }
                $positions[$path] = $fs.Position
            }
            finally {
                $fs.Close()
            }
        }
        catch {
            # locked; retry
        }
    }
    Start-Sleep -Milliseconds 400
}
