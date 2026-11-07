@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "DOTNET_ROOT=%~dp0tools\dotnet"
"%DOTNET_ROOT%\dotnet.exe" run --project "%~dp0src\VideoQualityAnalyzer.App" -c Release --no-restore -- --test-folder-drop "%~dp0test-data\フォルダドラッグテストファイル" %*
if errorlevel 1 (
    pause
    exit /b 1
)