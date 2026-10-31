param(
    [ValidateSet('TC-01', 'TC-02', 'TC-03', 'TC-04', 'TC-05', 'TC-06')]
    [string]$Case = 'TC-02',
    [ValidateSet('wipe', 'heatmap')][string]$Mode = 'wipe',
    [switch]$KeepRaw
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$videoDirectory = Join-Path $workspace 'test-data'
$outputDirectory = Join-Path $workspace 'measurement-results'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'experiments/VideoQualityAnalyzer.GpuPreview/bin/Release/net10.0-windows/VideoQualityAnalyzer.GpuPreview.dll'
$ffmpeg = Join-Path $workspace 'tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'

function Video([string]$name) { return (Get-Item -LiteralPath (Join-Path $videoDirectory $name)).FullName }
$reference = Video '攻殻機動隊_reference.mkv'
$h28 = Video '攻殻機動隊_icq28.mp4'
$h50 = Video '攻殻機動隊_icq50.mp4'
$a32 = Video '攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv'
$a48 = Video '攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv'
$cases = @{
    'TC-01' = @($reference, $h28)
    'TC-02' = @($reference, $h50)
    'TC-03' = @($reference, $a32)
    'TC-04' = @($reference, $a48)
    'TC-05' = @($h28, $h50)
    'TC-06' = @($a32, $a48)
}
$pair = $cases[$Case]

if (-not ('ComparisonCaptureWindow' -as [type])) { Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ComparisonCaptureWindow
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr order,
        int x, int y, int width, int height, uint flags);
}
'@
}

$info = [System.Diagnostics.ProcessStartInfo]::new($dotnet)
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WorkingDirectory = $workspace
foreach ($argument in @($app, "--benchmark-capture-$Mode=0", $pair[0], $pair[1])) {
    [void]$info.ArgumentList.Add($argument)
}
$appProcess = [System.Diagnostics.Process]::Start($info)
try {
    $targetPath = Join-Path $outputDirectory "comparison-capture-target-$($appProcess.Id).json"
    for ($attempt = 0; $attempt -lt 100 -and -not (Test-Path -LiteralPath $targetPath); $attempt++) {
        if ($appProcess.HasExited) { throw '比較アプリがキャプチャ準備前に終了しました。' }
        Start-Sleep -Milliseconds 50
    }
    if (-not (Test-Path -LiteralPath $targetPath)) { throw 'キャプチャ座標を取得できませんでした。' }
    $target = Get-Content -LiteralPath $targetPath -Raw | ConvertFrom-Json
    if ($target.Width -ne $target.Height -or $target.Width -lt 48) { throw 'マーカーの画面サイズが不正です。' }
    $appProcess.Refresh()
    $window = $appProcess.MainWindowHandle
    if ($window -eq [IntPtr]::Zero) { throw '比較アプリの画面ハンドルが見つかりません。' }
    [void][ComparisonCaptureWindow]::ShowWindow($window, 9)
    [void][ComparisonCaptureWindow]::SetWindowPos($window, [IntPtr]::new(-1), 0, 0, 0, 0, 0x43)
    [void][ComparisonCaptureWindow]::SetForegroundWindow($window)
    Start-Sleep -Milliseconds 100
    Write-Host '撮影対象ウィンドウを最上位に配置しました。'
    Write-Host "$Case $Mode 画面座標 X=$($target.X), Y=$($target.Y), サイズ=$($target.Width)"
    $rawPath = Join-Path $outputDirectory "comparison-capture-$Case-$Mode-$([guid]::NewGuid().ToString('N')).raw"
    $captureInfo = [System.Diagnostics.ProcessStartInfo]::new($ffmpeg)
    $captureInfo.UseShellExecute = $false
    $captureInfo.CreateNoWindow = $true
    $captureInfo.RedirectStandardError = $true
    foreach ($argument in @('-hide_banner', '-loglevel', 'error', '-f', 'gdigrab', '-framerate', '120',
        '-offset_x', [string]$target.X, '-offset_y', [string]$target.Y,
        '-video_size', "$($target.Width)x$($target.Height)", '-draw_mouse', '0', '-i', 'desktop',
        '-t', '33', '-frames:v', '3960', '-pix_fmt', 'bgra', '-f', 'rawvideo', '-y', $rawPath)) {
        [void]$captureInfo.ArgumentList.Add($argument)
    }
    $capture = [System.Diagnostics.Process]::Start($captureInfo)
    try {
        $errorTask = $capture.StandardError.ReadToEndAsync()
        while (-not $capture.WaitForExit(500)) {
            if ((Get-Item -LiteralPath $rawPath).Length -gt 100MB) {
                $capture.Kill($true)
                throw '撮影データが100MBを超えたため停止しました。'
            }
        }
        if ($capture.ExitCode -ne 0) { throw "画面キャプチャに失敗しました: $($errorTask.Result)" }
    }
    finally { $capture.Dispose() }
    $appProcess.WaitForExit()
    if ($appProcess.ExitCode -ne 0) { throw '比較アプリの測定が失敗しました。' }
    $json = & (Join-Path $PSScriptRoot 'Analyze-ComparisonCapture.ps1') -Path $rawPath -Size $target.Width
    $report = $json | ConvertFrom-Json
    Write-Host "$Case $Mode デスクトップ画像で識別した元フレーム: $($report.DistinctMarkerFrames)/$($report.ExpectedFrames)"
    if ($report.ValidMarkerSamples -eq 0 -or $report.FirstObserved -gt 62 -or
        $report.LastObserved -lt 957 -or $report.ReverseTransitions -gt 0) {
        throw '画面撮影が2～32秒の対象区間を十分に覆っていません。生データを残しました。'
    }
    Write-Host "  欠落率の上限: $([math]::Round($report.NotObservedRate * 100, 2))%"
    if (-not $KeepRaw) { Remove-Item -LiteralPath $rawPath }
    $json
}
finally { $appProcess.Dispose() }
