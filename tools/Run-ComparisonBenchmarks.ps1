param(
    [ValidateRange(1, 10)][int]$Repeats = 1,
    [ValidateSet('wipe', 'heatmap')][string[]]$Modes = @('wipe', 'heatmap')
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$videoDirectory = Join-Path $workspace 'test-data'
$outputDirectory = Join-Path $workspace 'measurement-results'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'experiments/VideoQualityAnalyzer.GpuPreview/bin/Release/net10.0-windows/VideoQualityAnalyzer.GpuPreview.dll'
if (-not (Test-Path -LiteralPath $app)) { throw 'Release版の比較アプリを先にビルドしてください。' }

function Video([string]$name) {
    $path = Join-Path $videoDirectory $name
    if (-not (Test-Path -LiteralPath $path)) { throw "動画が見つかりません: $name" }
    return (Get-Item -LiteralPath $path).FullName
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

$summary = [System.Collections.Generic.List[object]]::new()
foreach ($mode in $Modes) {
    foreach ($case in $cases) {
        for ($run = 1; $run -le $Repeats; $run++) {
            $started = Get-Date
            Write-Host "$($case.Id) $mode $run/$Repeats を30秒測定中"
            & $dotnet $app "--benchmark-$mode=0" $case.Left $case.Right
            if ($LASTEXITCODE -ne 0) { throw "$($case.Id) $mode の比較アプリが失敗しました。" }
            $reportFile = Get-ChildItem -LiteralPath $outputDirectory -Filter "comparison-$mode-*.json" |
                Where-Object { $_.LastWriteTime -ge $started.AddSeconds(-1) } |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($null -eq $reportFile) { throw "$($case.Id) $mode の測定結果がありません。" }
            $report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
            $summary.Add([pscustomobject]@{
                Case = $case.Id
                Mode = $mode
                Run = $run
                Expected = $report.ExpectedFrames
                Submitted = $report.SubmittedFrames
                LateDropped = $report.LateDroppedFrames
                CompositionObserved = $report.CompositionObservedUniqueFrames
                CompositionMissingRate = $report.CompositionMissingRate
                WindowVisible = $report.WindowVisibleThroughout
                ActualPresentationMeasured = $report.ActualScreenPresentationMeasured
                Report = $reportFile.Name
            })
            Write-Host "  描画要求 $($report.SubmittedFrames)/$($report.ExpectedFrames)、WPF合成観測 $($report.CompositionObservedUniqueFrames)/$($report.ExpectedFrames)"
        }
    }
}
$summaryPath = Join-Path $outputDirectory ("comparison-summary-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$summary | Export-Csv -LiteralPath $summaryPath -NoTypeInformation -Encoding utf8
Write-Host "集計: $summaryPath"
