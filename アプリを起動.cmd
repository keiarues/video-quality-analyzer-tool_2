@echo off
cd /d "%~dp0"
set "DOTNET_ROOT=%~dp0tools\dotnet"
"%DOTNET_ROOT%\dotnet.exe" "%~dp0src\VideoQualityAnalyzer.App\bin\Release\net10.0-windows\VideoQualityAnalyzer.StageA.dll" %*
if errorlevel 1 pause
