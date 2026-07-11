# Tail server-live.log for null-wheels retest signals (share-read with launcher).
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = Get-Location }
$path = Join-Path $root 'server-live.log'
$lastLen = 0
$deadline = (Get-Date).AddHours(2)

Write-Output "[WATCH] tailing $path until $deadline"
while ((Get-Date) -lt $deadline) {
    if (-not (Test-Path $path)) {
        Start-Sleep -Milliseconds 500
        continue
    }

    $fi = Get-Item $path
    if ($fi.Length -gt $lastLen) {
        $fs = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            [void]$fs.Seek($lastLen, [System.IO.SeekOrigin]::Begin)
            $sr = New-Object System.IO.StreamReader($fs)
            while (($line = $sr.ReadLine()) -ne $null) {
                if ($line -match 'CreateVehicle wire|NESTED_WHEEL|wheelOk|GhostVehicle|owner=|authenticated|Listening|Error|Exception|Invalid Packet|disconnect|logged in|MapInfo|forcing IsActive|EnsureDefault|Outgoing Packet: CreateVehicle') {
                    Write-Output $line
                }
            }
            $lastLen = $fs.Position
            $sr.Dispose()
        }
        finally {
            $fs.Dispose()
        }
    }

    # Prefer port check: process name can differ under redirection / shell hosts.
    $listening = $false
    try {
        $listening = [bool](Get-NetUDPEndpoint -LocalPort 27001 -ErrorAction SilentlyContinue)
        if (-not $listening) {
            $listening = [bool](Get-NetTCPConnection -LocalPort 2106 -State Listen -ErrorAction SilentlyContinue)
        }
    } catch {
        $listening = [bool](Get-Process -Name 'AutoCore.Launcher' -ErrorAction SilentlyContinue)
    }
    if (-not $listening) {
        # Grace period: avoid false exit while process is still binding.
        Start-Sleep -Seconds 2
        $still = Get-Process -Name 'AutoCore.Launcher' -ErrorAction SilentlyContinue
        $port = $false
        try { $port = [bool](Get-NetUDPEndpoint -LocalPort 27001 -ErrorAction SilentlyContinue) } catch {}
        if (-not $still -and -not $port) {
            Write-Output '[WATCH] AutoCore.Launcher / port 27001 gone'
            break
        }
    }

    Start-Sleep -Milliseconds 400
}
