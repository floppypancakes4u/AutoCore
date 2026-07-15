@echo off
setlocal
set ROOT=%~dp0..
set LAUNCHER=%ROOT%\tools\ChromiumOverlay\ChromiumLauncher\bin\Debug\net8.0-windows\ChromiumLauncher.exe
if not exist "%LAUNCHER%" (
  echo ChromiumLauncher not built. Run:
  echo   powershell -File tools\ChromiumOverlay\build-all.ps1
  exit /b 1
)
REM Default client is resolved by the launcher (prefers Auto Assault.bak when present).
"%LAUNCHER%" %*
