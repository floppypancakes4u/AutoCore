param([string]$Configuration = "Release")

Set-Location $PSScriptRoot

Write-Host "=== ChromiumBridge Build (no MinHook link) ===" -ForegroundColor Cyan

$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path $vcvars)) {
    $vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat"
}
if (-not (Test-Path $vcvars)) {
    $vcvars = "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
}
if (-not (Test-Path $vcvars)) {
    throw "vcvarsall.bat not found (need VS 2022 x86 toolchain)"
}

# No MinHook / NODEFAULTLIB games — link the normal CRT so LoadLibrary is stable in-process.
Write-Host "Building ChromiumBridge.dll (Win32)..." -ForegroundColor Yellow
cmd /c "`"$vcvars`" x86 && cl.exe /nologo /LD /O2 /EHsc ChromiumBridge.cpp /link /DEF:ChromiumBridge.def /OUT:ChromiumBridge.dll" 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed building ChromiumBridge.dll" }

Write-Host "Build complete: $PSScriptRoot\ChromiumBridge.dll" -ForegroundColor Green
