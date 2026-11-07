Set-Location $PSScriptRoot
& ./tools/dotnet/dotnet.exe run --project src/VideoQualityAnalyzer.App -c Release --no-restore -- --test-folder-drop "$PSScriptRoot/test-data/フォルダドラッグテストファイル"
