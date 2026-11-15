param([switch]$Quick, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $workspace 'tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if (-not $SkipBuild) {
    & $dotnet build (Join-Path $workspace 'src/VideoQualityAnalyzer.App') -c Release --no-restore -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
}
$directory = Join-Path $workspace 'measurement-results/input-seek'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
$reference = Join-Path $directory 'reference.mkv'
$candidate = Join-Path $directory 'candidate.mkv'
$offset = Join-Path $directory 'offset.mkv'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=64x64:rate=30:duration=3' -c:v ffv1 $reference
if ($LASTEXITCODE -ne 0) { throw '元動画の生成に失敗しました。' }
& $ffmpeg -hide_banner -loglevel error -y -i $reference -vf 'eq=brightness=0.02' -c:v ffv1 $candidate
if ($LASTEXITCODE -ne 0) { throw '比較動画の生成に失敗しました。' }
& $ffmpeg -hide_banner -loglevel error -y -i $candidate -c copy -output_ts_offset 5 $offset
if ($LASTEXITCODE -ne 0) { throw '開始時刻違いの生成に失敗しました。' }
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --test-input-seek --auto $reference $candidate $offset
if ($LASTEXITCODE -ne 0) { throw '生成動画の区間シーク試験に失敗しました。measurement-results/input-seek/failure.txtを参照してください。' }
Copy-Item -LiteralPath (Join-Path $directory 'results.json') -Destination (Join-Path $directory 'generated-results.json') -Force
if (-not $Quick) {
    $realReference = Join-Path $workspace 'test-data/攻殻機動隊_reference.mkv'
    $candidates = @('攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv', '攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv',
        '攻殻機動隊_icq28.mp4', '攻殻機動隊_icq50.mp4') | ForEach-Object { Join-Path $workspace "test-data/$_" }
    & $dotnet $app --test-input-seek --auto $realReference @candidates
    if ($LASTEXITCODE -ne 0) { throw '実動画の区間シーク試験に失敗しました。measurement-results/input-seek/failure.txtを参照してください。' }
    Copy-Item -LiteralPath (Join-Path $directory 'results.json') -Destination (Join-Path $directory 'real-results.json') -Force
}
Write-Output '区間入力シーク：左右の全対象フレームのSHA-256、評価値、事前中止・実行中止を確認しました。'
