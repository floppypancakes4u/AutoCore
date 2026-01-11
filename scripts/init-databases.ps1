# AutoCore Database Initialization Script
# This script creates the required MySQL databases for AutoCore

param(
    [string]$MySQLHost = "localhost",
    [int]$MySQLPort = 3306,
    [string]$MySQLUser = "root",
    [string]$MySQLPassword = "",
    [string]$MySQLPath = ""
)

Write-Host "AutoCore Database Initialization" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
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
            Write-Host "     .\init-databases.ps1 -MySQLPath 'C:\Program Files\MariaDB 12.1\bin\mysql.exe'" -ForegroundColor Cyan
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

Write-Host "Creating databases..." -ForegroundColor Yellow

# SQL commands to create databases
$sqlCommands = @"
CREATE DATABASE IF NOT EXISTS autocore_auth CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS autocore_char CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS autocore_world CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
SELECT 'Databases created successfully!' AS Status;
"@

try {
    $sqlCommands | & $mysqlCmd $mysqlArgs 2>&1 | Out-Null
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[SUCCESS] Databases created successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Created databases:" -ForegroundColor Cyan
        Write-Host "  - autocore_auth" -ForegroundColor White
        Write-Host "  - autocore_char" -ForegroundColor White
        Write-Host "  - autocore_world" -ForegroundColor White
        Write-Host ""
        Write-Host "Note: Tables will be created automatically when you first run the server." -ForegroundColor Yellow
    } else {
        Write-Host "Error: Failed to create databases. Check your MySQL credentials." -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "Error: Failed to execute MySQL commands: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Database initialization complete!" -ForegroundColor Green
