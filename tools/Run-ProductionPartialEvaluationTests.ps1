param([switch]$Real)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $workspace 'tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
& $dotnet build (Join-Path $workspace 'src/VideoQualityAnalyzer.App') -c Release --no-restore -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
$directory = Join-Path $workspace 'measurement-results/production-partial'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
$reference = Join-Path $directory 'reference.mkv'
$candidate = Join-Path $directory 'candidate.mp4'
$reference10 = Join-Path $directory 'reference10.mkv'
$candidate10 = Join-Path $directory 'candidate10.mkv'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=64x64:rate=30:duration=3' -c:v ffv1 $reference
if ($LASTEXITCODE -ne 0) { throw '元動画の生成に失敗しました。' }
& $ffmpeg -hide_banner -loglevel error -y -i $reference -vf 'eq=brightness=0.02' -c:v libx264 -g 30 $candidate
if ($LASTEXITCODE -ne 0) { throw '比較動画の生成に失敗しました。' }
& $ffmpeg -hide_banner -loglevel error -y -i $reference -vf 'format=yuv420p10le' -c:v ffv1 $reference10
if ($LASTEXITCODE -ne 0) { throw '10bit元動画の生成に失敗しました。' }
& $ffmpeg -hide_banner -loglevel error -y -i $reference -vf 'eq=brightness=0.02,format=yuv420p10le' -c:v ffv1 $candidate10
if ($LASTEXITCODE -ne 0) { throw '10bit比較動画の生成に失敗しました。' }
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --test-production-partial --auto $reference $candidate $reference10 $candidate10
if ($LASTEXITCODE -ne 0) { throw '実評価APIの専用画面検証に失敗しました。production-partial/failure.txtを確認してください。' }
if ($Real) {
    foreach ($name in @('攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv', '攻殻機動隊_icq28.mp4')) {
        & $dotnet $app --verify-main-continuous-evaluation (Join-Path $workspace 'test-data/攻殻機動隊_reference.mkv') (Join-Path $workspace "test-data/$name")
        if ($LASTEXITCODE -ne 0) { throw '通常画面の実動画評価に失敗しました。' }
    }
    & $dotnet $app --verify-main-continuous-evaluation --expect-sequential $reference10 $candidate10
    if ($LASTEXITCODE -ne 0) { throw '通常画面の10bit評価に失敗しました。' }
}
Write-Output '実評価API：3指標・比較処理なし・10bitの先頭復号・中止・再実行が合格しました。'
