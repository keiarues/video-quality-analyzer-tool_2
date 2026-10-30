param([switch]$Stats)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$videoDirectory = Join-Path $workspace 'test-data'
$videos = Get-ChildItem -LiteralPath $videoDirectory -File

function Select-Video([string]$pattern) {
    $matches = @($videos | Where-Object { $_.Name -match $pattern })
    if ($matches.Count -ne 1) { throw "Expected one video matching: $pattern" }
    return $matches[0].FullName
}

$left = Select-Video 'av1.*48.*mkv$'
$rightH265 = Select-Video 'icq50.*mp4$'
$rightAv1 = Select-Video 'av1.*32.*mkv$'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$gpuApp = Join-Path $workspace 'experiments/VideoQualityAnalyzer.GpuPreview/bin/Release/net10.0-windows/VideoQualityAnalyzer.GpuPreview.dll'
if (-not (Test-Path -LiteralPath $gpuApp)) { throw 'Build the GPU experiment in Release first.' }

$arguments = @($gpuApp, '--direct')
if ($Stats) { $arguments += '--stats' }
$arguments += @($left, $rightH265, $rightAv1)
& $dotnet @arguments
exit $LASTEXITCODE
