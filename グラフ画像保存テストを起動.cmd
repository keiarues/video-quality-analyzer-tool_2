@echo off
cd /d "%~dp0"
set "DOTNET_ROOT=%~dp0tools\dotnet"
"%DOTNET_ROOT%\dotnet.exe" build "%~dp0src\VideoQualityAnalyzer.App\VideoQualityAnalyzer.App.csproj" -c Release --no-restore -m:1 /nodeReuse:false
if errorlevel 1 goto failed
"%DOTNET_ROOT%\dotnet.exe" "%~dp0src\VideoQualityAnalyzer.App\bin\Release\net10.0-windows\VideoQualityAnalyzer.dll" --test-graph-image %*
if errorlevel 1 goto failed
exit /b 0
:failed
pause
exit /b 1
