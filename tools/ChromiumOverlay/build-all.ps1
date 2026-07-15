param([string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== ChromiumOverlay build-all ($Configuration) ===" -ForegroundColor Cyan

Write-Host "`n[1/4] ChromiumBridge.dll" -ForegroundColor Yellow
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "ChromiumBridge\build.ps1")
if ($LASTEXITCODE -ne 0) { throw "bridge build failed" }

Write-Host "`n[2/4] ChromiumHost" -ForegroundColor Yellow
dotnet build (Join-Path $root "ChromiumHost\ChromiumHost.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "host build failed" }

Write-Host "`n[3/4] ChromiumLauncher" -ForegroundColor Yellow
dotnet build (Join-Path $root "ChromiumLauncher\ChromiumLauncher.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "launcher build failed" }

Write-Host "`n[4/4] Tests" -ForegroundColor Yellow
dotnet test (Join-Path $root "ChromiumOverlay.Tests\ChromiumOverlay.Tests.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

$launcherOut = Join-Path $root "ChromiumLauncher\bin\$Configuration\net8.0-windows"
$hostOut = Join-Path $root "ChromiumHost\bin\$Configuration\net8.0-windows\win-x64"
if (-not (Test-Path (Join-Path $hostOut "ChromiumHost.exe"))) {
    $hostOut = Join-Path $root "ChromiumHost\bin\$Configuration\net8.0-windows"
}
$hostStage = Join-Path $launcherOut "host"
$bridgeDll = Join-Path $root "ChromiumBridge\ChromiumBridge.dll"

Write-Host "`nCopying host (x64 subfolder) + bridge into launcher output..." -ForegroundColor Yellow
if (Test-Path $hostStage) { Remove-Item $hostStage -Recurse -Force }
New-Item -ItemType Directory -Path $hostStage | Out-Null
Copy-Item (Join-Path $hostOut "*") $hostStage -Recurse -Force
Copy-Item $bridgeDll $launcherOut -Force

Write-Host "`nDone. Run:" -ForegroundColor Green
Write-Host "  $launcherOut\ChromiumLauncher.exe --game-path `"C:\Program Files (x86)\NetDevil\Auto Assault`""
