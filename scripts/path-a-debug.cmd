@echo off
setlocal
set ROOT=%~dp0..
set EXE=%ROOT%\tools\PathADebug\bin\Debug\net8.0-windows\PathADebug.exe
if not exist "%EXE%" (
  echo Building PathADebug...
  dotnet build "%ROOT%\tools\PathADebug\PathADebug.csproj" -c Debug -v q
  if errorlevel 1 exit /b 1
)
"%EXE%" %*
endlocal
