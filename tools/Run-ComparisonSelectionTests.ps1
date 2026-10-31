$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$videoDirectory = Join-Path $workspace 'test-data'
$outputDirectory = Join-Path $workspace 'measurement-results'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'experiments/VideoQualityAnalyzer.GpuPreview/bin/Release/net10.0-windows/VideoQualityAnalyzer.GpuPreview.dll'

function Video([string]$name) {
    return (Get-Item -LiteralPath (Join-Path $videoDirectory $name)).FullName
}

$reference = Video '攻殻機動隊_reference.mkv'
$h28 = Video '攻殻機動隊_icq28.mp4'
$h50 = Video '攻殻機動隊_icq50.mp4'
$a32 = Video '攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv'
$a48 = Video '攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv'
$cases = @(
    @{ Id = 'TC-01'; Left = $reference; Right = $h28 },
    @{ Id = 'TC-02'; Left = $reference; Right = $h50 },
    @{ Id = 'TC-03'; Left = $reference; Right = $a32 },
    @{ Id = 'TC-04'; Left = $reference; Right = $a48 },
    @{ Id = 'TC-05'; Left = $h28; Right = $h50 },
    @{ Id = 'TC-06'; Left = $a32; Right = $a48 }
)

foreach ($case in $cases) {
    $started = Get-Date
    Write-Host "$($case.Id) の選択変更を確認中"
    & $dotnet $app '--verify-selection=150' $case.Left $case.Right
    if ($LASTEXITCODE -ne 0) { throw "$($case.Id) の選択変更に失敗しました。" }
    $reportFile = Get-ChildItem -LiteralPath $outputDirectory -Filter 'comparison-selection-*.json' |
        Where-Object { $_.LastWriteTime -ge $started.AddSeconds(-1) } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $reportFile) { throw "$($case.Id) の検証結果がありません。" }
    $report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
    if ($report.Result -ne '合格' -or -not $report.StaticPixelsMatched -or
        -not $report.LeftSelectionPixelsMatched -or -not $report.OldResultDiscarded -or
        -not $report.PlaybackResumed -or -not $report.PausedSelectionStable -or
        -not $report.WipeAndHeatmapUpdated) {
        throw "$($case.Id) の選択変更が合格ではありません。"
    }
    Write-Host "  左右選択・連続選択・再生中・一時停止中切り替え: 合格"
}
