param([string]$Configuration = "Release")

Set-Location $PSScriptRoot

Write-Host "=== PathAHook Build ===" -ForegroundColor Cyan

# Prefer sibling SpeedHook vendored MinHook (repo layout); fall back to legacy AutoLogin path.
$minhookRoot = Join-Path $PSScriptRoot "..\..\AutoCore.DevTool\SpeedHook\minhook"
if (-not (Test-Path $minhookRoot)) {
    $minhookRoot = Join-Path $PSScriptRoot "..\..\AutoLogin\AuthHook\minhook"
}
$minhookInclude = Join-Path $minhookRoot "include"
$minhookLib = Join-Path $minhookRoot "build\Release\minhook.x32.lib"

if (-not (Test-Path $minhookLib)) {
    Write-Host "`n[1/2] Building MinHook..." -ForegroundColor Yellow
    $minhookBuild = Join-Path $minhookRoot "build"
    cmake -S $minhookRoot -B $minhookBuild -A Win32
    if ($LASTEXITCODE -ne 0) { throw "cmake configure MinHook failed" }
    cmake --build $minhookBuild --config Release
    if ($LASTEXITCODE -ne 0) { throw "cmake build MinHook failed" }
} else {
    Write-Host "`n[1/2] Reusing MinHook at $minhookLib" -ForegroundColor Yellow
}

Write-Host "`n[2/2] Building PathAHook.dll (Win32)..." -ForegroundColor Yellow
$vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path $vcvars)) {
    $vcvars = "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat"
}
if (-not (Test-Path $vcvars)) {
    throw "vcvarsall.bat not found (need VS 2022 x86 toolchain)"
}

$libDir = Split-Path $minhookLib
cmd /c "`"$vcvars`" x86 && cl.exe /nologo /LD /O2 /EHsc /I `"$minhookInclude`" PathAHook.cpp /link /LIBPATH:`"$libDir`" minhook.x32.lib /NODEFAULTLIB:msvcrt.lib /DEF:PathAHook.def /OUT:PathAHook.dll" 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed building PathAHook.dll" }

Write-Host "`nBuild complete: $PSScriptRoot\PathAHook.dll" -ForegroundColor Green
