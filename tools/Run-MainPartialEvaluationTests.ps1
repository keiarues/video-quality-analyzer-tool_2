param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Run-PartialEvaluationTests.ps1')
if ($LASTEXITCODE -ne 0) { throw '専用画面の試験に失敗しました。' }
$directory = Join-Path $workspace 'measurement-results/partial-evaluation'
$reference = Join-Path $directory '部分評価 試験.mkv'
$candidate = Join-Path $directory '部分評価 比較.mkv'
Copy-Item -LiteralPath $reference -Destination $candidate -Force
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --verify-main-partial-evaluation $reference $candidate
if ($LASTEXITCODE -ne 0) { throw '主画面の部分評価試験に失敗しました。' }
Write-Output '主画面の部分評価の自動検証が完了しました。'
