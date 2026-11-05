@echo off
cd /d "%~dp0"
set "DOTNET_ROOT=%~dp0tools\dotnet"
"%DOTNET_ROOT%\dotnet.exe" "%~dp0src\VideoQualityAnalyzer.App\bin\Release\net10.0-windows\VideoQualityAnalyzer.StageA.dll" --graph-playback-test "%~dp0measurement-results\graph-playback\ssim.json" %*
if errorlevel 1 pause
