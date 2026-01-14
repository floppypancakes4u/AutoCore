# AutoCore Client Setup Script
# This script helps configure the Windows hosts file to redirect the Auto Assault client to your local server

param(
    [string]$ServerDomain = "auth.autoassault.com",
    [string]$ServerIP = "127.0.0.1",
    [switch]$Remove
)

$hostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"

Write-Host "AutoCore Client Setup" -ForegroundColor Cyan
Write-Host "====================" -ForegroundColor Cyan
Write-Host ""

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Error: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host ""
    Write-Host "To run as Administrator:" -ForegroundColor Yellow
    Write-Host "  1. Right-click PowerShell" -ForegroundColor Yellow
    Write-Host "  2. Select 'Run as Administrator'" -ForegroundColor Yellow
    Write-Host "  3. Navigate to this directory and run the script again" -ForegroundColor Yellow
    exit 1
}

if ($Remove) {
    Write-Host "Removing hosts file entry for $ServerDomain..." -ForegroundColor Yellow
    
    if (Test-Path $hostsPath) {
        $content = Get-Content $hostsPath
        $newContent = $content | Where-Object { $_ -notmatch "^\s*$ServerIP\s+$ServerDomain" }
        
        if ($content.Count -eq $newContent.Count) {
            Write-Host "Entry not found in hosts file." -ForegroundColor Yellow
        } else {
            $newContent | Set-Content $hostsPath
            Write-Host "[SUCCESS] Removed hosts file entry for $ServerDomain" -ForegroundColor Green
        }
    } else {
        Write-Host "Hosts file not found at $hostsPath" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Adding hosts file entry..." -ForegroundColor Yellow
    Write-Host "  Domain: $ServerDomain" -ForegroundColor Gray
    Write-Host "  IP: $ServerIP" -ForegroundColor Gray
    Write-Host ""
    
    if (-not (Test-Path $hostsPath)) {
        Write-Host "Error: Hosts file not found at $hostsPath" -ForegroundColor Red
        exit 1
    }
    
    # Check if entry already exists
    $content = Get-Content $hostsPath
    $existingEntry = $content | Where-Object { $_ -match "^\s*$ServerIP\s+$ServerDomain" }
    
    if ($existingEntry) {
        Write-Host "Entry already exists in hosts file:" -ForegroundColor Yellow
        Write-Host "  $existingEntry" -ForegroundColor Gray
        Write-Host ""
        Write-Host "No changes needed." -ForegroundColor Green
    } else {
        # Add the entry
        $entry = "$ServerIP`t$ServerDomain"
        Add-Content -Path $hostsPath -Value $entry
        
        Write-Host "[SUCCESS] Added hosts file entry!" -ForegroundColor Green
        Write-Host ""
        Write-Host "The Auto Assault client will now connect to your local server." -ForegroundColor Cyan
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Make sure your AutoCore server is running" -ForegroundColor White
Write-Host "  2. Launch Auto Assault" -ForegroundColor White
Write-Host "  3. Log in with:" -ForegroundColor White
Write-Host "     Username: admin" -ForegroundColor Gray
Write-Host "     Password: admin123" -ForegroundColor Gray
Write-Host ""
Write-Host "To remove this entry later, run:" -ForegroundColor Yellow
Write-Host "  .\setup-client.ps1 -Remove" -ForegroundColor Cyan
Write-Host ""

# Flush DNS cache
Write-Host "Flushing DNS cache..." -ForegroundColor Yellow
ipconfig /flushdns | Out-Null
Write-Host "[SUCCESS] DNS cache flushed" -ForegroundColor Green












