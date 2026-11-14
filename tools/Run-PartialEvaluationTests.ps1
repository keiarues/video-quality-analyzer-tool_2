param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $workspace 'tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
& $dotnet build (Join-Path $workspace 'src/VideoQualityAnalyzer.App') -c Release --no-restore -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
$directory = Join-Path $workspace 'measurement-results/partial-evaluation'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$video = Join-Path $directory '部分評価 試験.mkv'
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=64x64:rate=30:duration=3' -c:v ffv1 $video
if ($LASTEXITCODE -ne 0) { throw '試験動画を生成できませんでした。' }
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --test-partial-evaluation --auto $video $video
if ($LASTEXITCODE -ne 0) { throw '部分評価専用画面の自動試験に失敗しました。' }
Write-Output '部分評価3方式：バー範囲・ステータス・入力連動・動画変更・同一動画SSIM・対象フレーム数・元フレーム番号を確認しました。'
