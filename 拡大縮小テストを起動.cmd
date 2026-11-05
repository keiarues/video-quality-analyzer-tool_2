@echo off
cd /d "%~dp0"
set "DOTNET_ROOT=%~dp0tools\dotnet"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0tools\Start-ZoomPreview.ps1" %*
if errorlevel 1 pause
