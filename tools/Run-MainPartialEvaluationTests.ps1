param([switch]$ShorterComparison)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Run-PartialEvaluationTests.ps1')
if ($LASTEXITCODE -ne 0) { throw '専用画面の試験に失敗しました。' }
$directory = Join-Path $workspace 'measurement-results/partial-evaluation'
$reference = Join-Path $directory '部分評価 試験.mkv'
$candidate = Join-Path $directory '部分評価 比較.mkv'
if ($ShorterComparison) {
    $ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
    & $ffmpeg -hide_banner -loglevel error -y -i $reference -t 2.5 -c:v ffv1 $candidate
    if ($LASTEXITCODE -ne 0) { throw '短い比較動画を生成できませんでした。' }
} else {
    Copy-Item -LiteralPath $reference -Destination $candidate -Force
}
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --verify-main-partial-evaluation $reference $candidate
if ($LASTEXITCODE -ne 0) { throw '主画面の部分評価試験に失敗しました。' }
Write-Output '主画面の部分評価の自動検証が完了しました。'
