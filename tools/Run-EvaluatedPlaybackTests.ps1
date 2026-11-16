param([switch]$Main, [switch]$Real)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $workspace 'tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
& $dotnet build (Join-Path $workspace 'src/VideoQualityAnalyzer.App') -c Release --no-restore -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。' }
$directory = Join-Path $workspace 'measurement-results/evaluated-playback'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$reference = Join-Path $directory 'reference.mkv'
$candidate = Join-Path $directory 'candidate.mp4'
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=64x64:rate=30:duration=3' -f lavfi -i 'sine=frequency=440:duration=3' -c:v ffv1 -c:a pcm_s16le -shortest $reference
if ($LASTEXITCODE -ne 0) { throw '試験元動画の生成に失敗しました。' }
& $ffmpeg -hide_banner -loglevel error -y -i $reference -an -c:v libx264 -g 30 $candidate
if ($LASTEXITCODE -ne 0) { throw '試験比較動画の生成に失敗しました。' }
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --test-evaluated-playback $reference $candidate
if ($LASTEXITCODE -ne 0) { throw '専用画面の評価区間再生試験に失敗しました。' }
if ($Main) {
    $unassessed = Join-Path $directory 'unassessed.mp4'
    Copy-Item -LiteralPath $candidate -Destination $unassessed -Force
    & $dotnet $app --verify-evaluated-playback $reference $candidate $unassessed
    if ($LASTEXITCODE -ne 0) { throw '通常画面の評価区間再生試験に失敗しました。' }
}
if ($Real) {
    $realReference = Join-Path $workspace 'test-data/攻殻機動隊_reference.mkv'
    $av1 = Join-Path $workspace 'test-data/攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv'
    $hevc = Join-Path $workspace 'test-data/攻殻機動隊_icq28.mp4'
    foreach ($pair in @(@($av1, $hevc), @($hevc, $av1))) {
        & $dotnet $app --test-evaluated-playback $realReference $pair[0]
        if ($LASTEXITCODE -ne 0) { throw '実動画の専用画面試験に失敗しました。' }
        & $dotnet $app --verify-evaluated-playback $realReference $pair[0] $pair[1]
        if ($LASTEXITCODE -ne 0) { throw '実動画の通常画面試験に失敗しました。' }
    }
}
Write-Output '評価区間のみの再生：3方式・未評価区間スキップ・末尾停止・静止画素・一時停止・通常シークを確認しました。'
