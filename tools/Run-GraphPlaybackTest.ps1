param([switch]$Prepare, [switch]$Auto, [switch]$VerifyOperations)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.dll'
$cache = Join-Path $workspace 'measurement-results/graph-playback/ssim.json'
if ($Prepare) {
    $reference = (Get-ChildItem -LiteralPath (Join-Path $workspace 'test-data') -Filter '*reference.mkv').FullName
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $workspace 'test-data') -File |
        Where-Object { $_.Extension -in '.mp4', '.mkv' -and $_.FullName -ne $reference } |
        Select-Object -ExpandProperty FullName)
    & $dotnet $app --prepare-graph-test $cache $reference @candidates
} else {
    $arguments = @($app, '--graph-playback-test', $cache)
    if ($Auto) { $arguments += '--auto' }
    if ($VerifyOperations) { $arguments += '--verify-operations' }
    & $dotnet @arguments
}
if ($LASTEXITCODE -ne 0) { throw "グラフ検証に失敗しました。保存先の.error.txtも確認してください。" }
