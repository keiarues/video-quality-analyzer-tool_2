param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.StageA.dll'
$smoke = Join-Path $workspace 'tests/VideoQualityAnalyzer.Smoke/bin/Release/net10.0/VideoQualityAnalyzer.Smoke.dll'

if (-not $NoBuild) {
    & $dotnet build (Join-Path $workspace 'VideoQualityAnalyzer.Wpf.sln') -c Release --no-restore -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
}
& (Join-Path $workspace 'tests/Run-Tests.ps1') -NoRestore
if ($LASTEXITCODE -ne 0) { throw 'MSTestに失敗しました。' }
& $dotnet $smoke $workspace --quality-test
if ($LASTEXITCODE -ne 0) { throw '品質評価の実計算試験に失敗しました。' }
& $dotnet $app --verify-groups
if ($LASTEXITCODE -ne 0) { throw 'グループ設定画面の試験に失敗しました。' }
$red = Join-Path $workspace 'measurement-results/quality-smoke/red.mkv'
$blue = Join-Path $workspace 'measurement-results/quality-smoke/blue.mkv'
& $dotnet $app --verify-evaluation $red $blue
if ($LASTEXITCODE -ne 0) { throw '評価結果の再利用・グラフ画面の試験に失敗しました。' }
$videos = Get-ChildItem -LiteralPath (Join-Path $workspace 'test-data') -File | Select-Object -ExpandProperty FullName
& $dotnet $app --verify-main-features @videos
if ($LASTEXITCODE -ne 0) { throw '主画面の選択・結果破棄の試験に失敗しました。' }
& $dotnet $app --verify-main-graphs @videos
if ($LASTEXITCODE -ne 0) { throw '主画面グラフの試験に失敗しました。' }
& $dotnet $app --verify-main-comparison @videos
if ($LASTEXITCODE -ne 0) { throw '主画面に統合した比較機能の試験に失敗しました。' }
& (Join-Path $workspace 'tools/Run-ComparisonOperations.ps1') -MainApp
if ($LASTEXITCODE -ne 0) { throw '比較操作試験に失敗しました。' }
Write-Output '完成版への組み込み機能の自動確認がすべて完了しました。'
