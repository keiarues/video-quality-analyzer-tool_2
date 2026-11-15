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
$directory = Join-Path $workspace 'measurement-results/continuous-input'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
$reference = Join-Path $directory 'reference.mkv'
$candidate = Join-Path $directory 'candidate.mp4'
$offset = Join-Path $directory 'offset.mkv'
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=64x64:rate=30:duration=3' -vf 'setparams=colorspace=bt709' -c:v ffv1 $reference
if ($LASTEXITCODE -ne 0) { throw '元動画を生成できませんでした。' }
& $ffmpeg -hide_banner -loglevel error -y -i $reference -vf 'eq=brightness=0.02,setparams=colorspace=unknown' -c:v libx264 -crf 28 -g 30 -colorspace unknown $candidate
if ($LASTEXITCODE -ne 0) { throw '比較動画を生成できませんでした。' }
& $ffmpeg -hide_banner -loglevel error -y -i $candidate -c copy -output_ts_offset 5 $offset
if ($LASTEXITCODE -ne 0) { throw '開始PTS違いの素材を生成できませんでした。' }
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
& $dotnet $app --test-continuous-input --auto $reference $candidate $offset
if ($LASTEXITCODE -ne 0) { throw '生成素材の連続入力試験に失敗しました。measurement-results/continuous-input/failure.txtを参照してください。' }
Copy-Item -LiteralPath (Join-Path $directory 'results.json') -Destination (Join-Path $directory 'generated-results.json') -Force
Copy-Item -LiteralPath (Join-Path $directory 'cancellation.json') -Destination (Join-Path $directory 'generated-cancellation.json') -Force
if (-not $Quick) {
    $reference = Join-Path $workspace 'test-data/攻殻機動隊_reference.mkv'
    $av1 = Join-Path $workspace 'test-data/攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv'
    $hevc = Join-Path $workspace 'test-data/攻殻機動隊_icq28.mp4'
    & $dotnet $app --test-continuous-input --auto $reference $av1 $hevc
    if ($LASTEXITCODE -ne 0) { throw '実動画の連続入力試験に失敗しました。measurement-results/continuous-input/failure.txtを参照してください。' }
    Copy-Item -LiteralPath (Join-Path $directory 'results.json') -Destination (Join-Path $directory 'real-results.json') -Force
    Copy-Item -LiteralPath (Join-Path $directory 'cancellation.json') -Destination (Join-Path $directory 'real-cancellation.json') -Force
}
$reports = @((Join-Path $directory 'generated-results.json'))
if (-not $Quick) { $reports += Join-Path $directory 'real-results.json' }
foreach ($path in $reports) {
    $rows = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($rows.Count -lt 1) { throw '照合結果がありません。' }
    foreach ($row in $rows) {
        if (-not $row.Passed -or -not $row.DirectReferenceVerified -or $row.Values -ne $row.Frames) {
            throw '評価値・画素または対象フレーム数の照合に失敗しました。'
        }
        if ($row.Case -like '性能*' -and (-not $row.IntermediateProgress -or $row.UiTimerTicks -le 0)) {
            throw '性能測定中の中間進捗またはUIタイマーの更新を確認できませんでした。'
        }
    }
}
Write-Output '中間キャッシュなしの連続入力：3指標・画素・元時刻進捗・中止・再実行を確認しました。'
