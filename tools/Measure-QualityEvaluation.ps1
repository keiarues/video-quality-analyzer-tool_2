param(
    [ValidateSet('av1', 'hevc')][string]$CandidateCodec = 'av1',
    [ValidateSet('core', 'ablation', 'seek', 'seek-exact', 'metrics', 'fusion', 'vmaf-threads')][string]$Phase = 'core',
    [ValidateSet('Ssim', 'Psnr', 'Vmaf')][string]$Metric = 'Ssim',
    [ValidateRange(1, 5)][int]$Repeats = 3,
    [string]$ReferencePath,
    [string]$CandidatePath
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
if (-not $ReferencePath) { $ReferencePath = (Get-ChildItem (Join-Path $workspace 'test-data') -File -Filter '*reference.mkv' | Select-Object -First 1).FullName }
if (-not $CandidatePath) {
    $pattern = if ($CandidateCodec -eq 'av1') { '*qvbr 32*' } else { '*icq28.mp4' }
    $CandidatePath = (Get-ChildItem (Join-Path $workspace 'test-data') -File -Filter $pattern | Select-Object -First 1).FullName
}
if (-not (Test-Path -LiteralPath $ReferencePath -PathType Leaf) -or -not (Test-Path -LiteralPath $CandidatePath -PathType Leaf)) {
    throw '元動画と比較動画のパスを指定してください。'
}
& $dotnet build (Join-Path $workspace 'tests/VideoQualityAnalyzer.Smoke') -c Release --no-restore -m:1 /nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw '測定ツールのビルドに失敗しました。' }
$runner = Join-Path $workspace 'tests/VideoQualityAnalyzer.Smoke/bin/Release/net10.0/VideoQualityAnalyzer.Smoke.dll'
& $dotnet $runner $workspace --quality-timing $ReferencePath $CandidatePath --phase $Phase --metric $Metric --repeats $Repeats
if ($LASTEXITCODE -ne 0) { throw '品質評価の処理時間測定に失敗しました。' }
