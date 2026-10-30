param([switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace '.nuget-packages'
$dotnet = Join-Path $workspace 'tools/dotnet/dotnet.exe'
$project = Join-Path $PSScriptRoot 'VideoQualityAnalyzer.Tests/VideoQualityAnalyzer.Tests.csproj'
$resultDirectory = Join-Path $PSScriptRoot 'VideoQualityAnalyzer.Tests/TestResults'
$testArguments = @('test', $project, '-c', 'Release', '--logger', 'trx', '--blame-hang-timeout', '60s', '-m:1', '/nodeReuse:false')
if ($NoRestore) { $testArguments += '--no-restore' }
$started = Get-Date
& $dotnet @testArguments
$testExitCode = $LASTEXITCODE
if ($testExitCode -ne 0) { exit $testExitCode }

$result = Get-ChildItem -LiteralPath $resultDirectory -Filter '*.trx' -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $started } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -eq $result) {
    Write-Error '新しいテスト結果がありません。テストが実行されたか確認してください。'
    exit 1
}

[xml]$report = Get-Content -LiteralPath $result.FullName -Raw
$summary = $report.TestRun.ResultSummary
$counters = $summary.Counters
if ($summary.outcome -notin @('Passed', 'Completed') -or
    [int]$counters.executed -lt 1 -or
    [int]$counters.passed -ne [int]$counters.total -or
    [int]$counters.failed -ne 0) {
    Write-Error "テスト結果を確認してください: 実行 $($counters.executed) 件、失敗 $($counters.failed) 件、結果 $($summary.outcome)"
    exit 1
}
Write-Output "テスト結果を確認しました: 実行 $($counters.executed) 件、合格 $($counters.passed) 件。"
