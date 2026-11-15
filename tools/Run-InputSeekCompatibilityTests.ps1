param([switch]$Quick, [switch]$SkipBuild, [switch]$ContextOnly)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $workspace 'tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
if (-not $SkipBuild) {
    & "$env:DOTNET_ROOT/dotnet.exe" build (Join-Path $workspace 'tests/VideoQualityAnalyzer.Smoke') -c Release --no-restore -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw '検証ツールのビルドに失敗しました。' }
}
$directory = Join-Path $workspace 'measurement-results/input-seek-compatibility'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
$reference = Join-Path $directory 'reference.mkv'
$candidate = Join-Path $directory 'candidate.mp4'
if (-not $ContextOnly) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=256x144:rate=30:duration=4' -vf 'setparams=colorspace=bt709' -c:v ffv1 $reference
    if ($LASTEXITCODE -ne 0) { throw '生成元動画を作れませんでした。' }
    & $ffmpeg -hide_banner -loglevel error -y -i $reference -vf 'eq=brightness=0.03:contrast=0.9,setparams=colorspace=unknown' -c:v libx264 -crf 28 -g 30 -colorspace unknown $candidate
    if ($LASTEXITCODE -ne 0) { throw '生成比較動画を作れませんでした。' }
    & python (Join-Path $PSScriptRoot 'Verify-InputSeekCompatibility.py') $reference $candidate --quick --output (Join-Path $directory 'generated')
    if ($LASTEXITCODE -ne 0) { throw '生成素材の互換性検証に失敗しました。' }
}
& python (Join-Path $PSScriptRoot 'Verify-InputSeekCompatibility.py') $reference $candidate --context-only --output (Join-Path $directory 'generated')
if ($LASTEXITCODE -ne 0) { throw '生成素材の自然な文脈との照合に失敗しました。' }
if (-not $Quick) {
    $reference = Join-Path $workspace 'test-data/攻殻機動隊_reference.mkv'
    foreach ($entry in @(@('av1', '攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv'), @('hevc', '攻殻機動隊_icq28.mp4'))) {
        if (-not $ContextOnly) {
            & python (Join-Path $PSScriptRoot 'Verify-InputSeekCompatibility.py') $reference (Join-Path $workspace "test-data/$($entry[1])") --output (Join-Path $directory $entry[0])
            if ($LASTEXITCODE -ne 0) { throw "$($entry[0])の互換性検証に失敗しました。" }
        }
        & python (Join-Path $PSScriptRoot 'Verify-InputSeekCompatibility.py') $reference (Join-Path $workspace "test-data/$($entry[1])") --context-only --output (Join-Path $directory $entry[0])
        if ($LASTEXITCODE -ne 0) { throw "$($entry[0])の自然な文脈との照合に失敗しました。" }
    }
}
Write-Output '色タグ・time_baseの互換性、連結VMAFと区間初期化VMAFの比較を完了しました。差の有無は各results.jsonを確認してください。'
