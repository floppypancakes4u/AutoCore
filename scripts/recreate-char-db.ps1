# AutoCore Character Database Recreation Script
# This script drops and recreates the autocore_char database
# Use this when you need to reset the character database schema

param(
    [string]$MySQLHost = "localhost",
    [int]$MySQLPort = 3306,
    [string]$MySQLUser = "root",
    [string]$MySQLPassword = "Jcr321321!",
    [string]$MySQLPath = ""
)

Write-Host "AutoCore Character Database Recreation" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Find MySQL executable
$mysqlCmd = $null

# If path provided, use it
if ($MySQLPath) {
    if (Test-Path $MySQLPath) {
        $mysqlCmd = $MySQLPath
        Write-Host "Using MySQL at: $MySQLPath" -ForegroundColor Gray
    } else {
        Write-Host "Error: MySQL path not found: $MySQLPath" -ForegroundColor Red
        exit 1
    }
} else {
    # Try to find MySQL in PATH first
    try {
        $null = Get-Command "mysql" -ErrorAction Stop
        $mysqlCmd = "mysql"
    } catch {
        # Check common installation locations
        $commonPaths = @(
            "C:\Program Files\MariaDB 12.1\bin\mysql.exe",
            "C:\Program Files\MariaDB 12.0\bin\mysql.exe",
            "C:\Program Files\MariaDB 11.5\bin\mysql.exe",
            "C:\Program Files\MariaDB 11.4\bin\mysql.exe",
            "C:\Program Files\MariaDB 11.3\bin\mysql.exe",
            "C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe",
            "C:\Program Files\MySQL\MySQL Server 8.1\bin\mysql.exe",
            "C:\Program Files\MySQL\MySQL Server 8.2\bin\mysql.exe",
            "C:\Program Files\MySQL\MySQL Server 8.3\bin\mysql.exe",
            "C:\Program Files (x86)\MariaDB 12.1\bin\mysql.exe",
            "C:\Program Files (x86)\MariaDB 12.0\bin\mysql.exe",
            "C:\Program Files (x86)\MySQL\MySQL Server 8.0\bin\mysql.exe"
        )
        
        foreach ($path in $commonPaths) {
            if (Test-Path $path) {
                $mysqlCmd = $path
                Write-Host "Found MySQL at: $path" -ForegroundColor Gray
                break
            }
        }
        
        if (-not $mysqlCmd) {
            Write-Host "Error: MySQL command line client not found." -ForegroundColor Red
            Write-Host ""
            Write-Host "Please either:" -ForegroundColor Yellow
            Write-Host "  1. Add MySQL to your PATH, or" -ForegroundColor Yellow
            Write-Host "  2. Specify the path using -MySQLPath parameter:" -ForegroundColor Yellow
            Write-Host "     .\recreate-char-db.ps1 -MySQLPath 'C:\Program Files\MariaDB 12.1\bin\mysql.exe'" -ForegroundColor Cyan
            exit 1
        }
    }
}

# Build MySQL connection string
$mysqlArgs = @(
    "-h", $MySQLHost
    "-P", $MySQLPort.ToString()
    "-u", $MySQLUser
)

if ($MySQLPassword) {
    $mysqlArgs += "-p$MySQLPassword"
} else {
    $mysqlArgs += "--password="
}

Write-Host "WARNING: This will DELETE all character data!" -ForegroundColor Red
Write-Host "Press Ctrl+C to cancel, or any key to continue..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host ""
Write-Host "Dropping and recreating autocore_char database..." -ForegroundColor Yellow

# SQL commands to drop and recreate database
$sqlCommands = @"
DROP DATABASE IF EXISTS autocore_char;
CREATE DATABASE autocore_char CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
SELECT 'Database recreated successfully!' AS Status;
"@

try {
    $output = $sqlCommands | & $mysqlCmd $mysqlArgs 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[SUCCESS] Database recreated successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "The autocore_char database has been dropped and recreated." -ForegroundColor Cyan
        Write-Host "Tables will be created automatically when you next run the server." -ForegroundColor Yellow
    } else {
        Write-Host "Error: Failed to recreate database." -ForegroundColor Red
        Write-Host "MySQL output:" -ForegroundColor Red
        Write-Host $output
        exit 1
    }
} catch {
    Write-Host "Error: Failed to execute MySQL commands: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Database recreation complete!" -ForegroundColor Green

