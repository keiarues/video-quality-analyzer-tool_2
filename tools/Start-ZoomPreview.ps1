param([string]$LeftPath, [string]$RightPath, [switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$app = Join-Path $workspace 'experiments/VideoQualityAnalyzer.GpuPreview/bin/Release/net10.0-windows/VideoQualityAnalyzer.GpuPreview.dll'
if (-not (Test-Path -LiteralPath $app)) { throw 'テスト画面をRelease構成でビルドしてください。' }
if (-not $CheckOnly) {
    Add-Type -AssemblyName System.Windows.Forms
    function Select-Video([string]$title) {
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        try {
            $dialog.Title = $title
            $dialog.InitialDirectory = Join-Path $workspace 'test-data'
            $dialog.Filter = '動画ファイル|*.mkv;*.mp4;*.avi;*.mov;*.webm;*.m4v;*.ts|すべてのファイル|*.*'
            if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
            return $dialog.FileName
        } finally { $dialog.Dispose() }
    }
    if (-not $LeftPath) { $LeftPath = Select-Video '比較する左の動画を選択' }
    if (-not $LeftPath) { exit 0 }
    if (-not $RightPath) { $RightPath = Select-Video '比較する右の動画を選択' }
    if (-not $RightPath) { exit 0 }
}
foreach ($video in @($LeftPath, $RightPath)) {
    if (-not $video -or -not (Test-Path -LiteralPath $video -PathType Leaf)) { throw '左右の動画ファイルが見つかりません。' }
}
if ($CheckOnly) { Write-Output '起動先と左右の動画ファイルを確認しました。'; exit 0 }
& $dotnet $app '--compare-frame=0' '--zoom-test' $LeftPath $RightPath
exit $LASTEXITCODE
