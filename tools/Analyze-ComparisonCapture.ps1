param(
    [Parameter(Mandatory)][string]$Path,
    [ValidateRange(4, 512)][int]$Size = 48,
    [int]$FirstFrame = 60,
    [int]$AfterLastFrame = 960
)

$ErrorActionPreference = 'Stop'
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path))
$frameBytes = $Size * $Size * 4
if ($bytes.Length % $frameBytes -ne 0) { throw 'キャプチャのサイズが指定した正方形のBGRAフレームに一致しません。' }
$seen = [System.Collections.Generic.HashSet[int]]::new()
$validSamples = 0
$reverseTransitions = 0
$previous = -1
$firstObserved = -1
$lastObserved = -1
for ($frame = 0; $frame -lt $bytes.Length / $frameBytes; $frame++) {
    $base = $frame * $frameBytes
    $number = 0
    $valid = $true
    for ($bit = 0; $bit -lt 16; $bit++) {
        $x = [int][math]::Floor((($bit % 4) + 0.5) * $Size / 4)
        $y = [int][math]::Floor((([math]::Floor($bit / 4)) + 0.5) * $Size / 4)
        $offset = $base + ($y * $Size + $x) * 4
        $blue = [int]$bytes[$offset]
        $green = [int]$bytes[$offset + 1]
        $red = [int]$bytes[$offset + 2]
        if ([math]::Abs($red - $green) -gt 25 -or [math]::Abs($red - $blue) -gt 25 -or
            ($red -gt 60 -and $red -lt 200)) { $valid = $false; break }
        if ($red -ge 200) { $number = $number -bor (1 -shl $bit) }
    }
    if (-not $valid -or $number -lt $FirstFrame -or $number -ge $AfterLastFrame) { continue }
    $validSamples++
    [void]$seen.Add($number)
    if ($previous -gt $number) { $reverseTransitions++ }
    $previous = $number
    if ($firstObserved -lt 0) { $firstObserved = $number }
    $lastObserved = $number
}
$expected = $AfterLastFrame - $FirstFrame
$missing = [System.Collections.Generic.List[int]]::new()
$longestGap = 0
$currentGap = 0
for ($number = $FirstFrame; $number -lt $AfterLastFrame; $number++) {
    if (-not $seen.Contains($number)) {
        $missing.Add($number)
        $currentGap++
        $longestGap = [math]::Max($longestGap, $currentGap)
    }
    else { $currentGap = 0 }
}
$report = [ordered]@{
    Method = 'FFmpeg gdigrabによる表示デスクトップのフレーム識別子の標本化'
    CaptureFile = [System.IO.Path]::GetFileName($Path)
    FirstFrame = $FirstFrame
    AfterLastFrame = $AfterLastFrame
    ExpectedFrames = $expected
    CapturedSamples = [int]($bytes.Length / $frameBytes)
    ValidMarkerSamples = $validSamples
    DistinctMarkerFrames = $seen.Count
    NotObservedFrames = $expected - $seen.Count
    NotObservedRate = ($expected - $seen.Count) / $expected
    NotObservedFrameNumbers = $missing.ToArray()
    LongestConsecutiveNotObserved = $longestGap
    ReverseTransitions = $reverseTransitions
    FirstObserved = $firstObserved
    LastObserved = $lastObserved
    PhysicalPanelScanoutMeasured = $false
}
$json = $report | ConvertTo-Json -Depth 4
$resultPath = [System.IO.Path]::ChangeExtension((Resolve-Path -LiteralPath $Path).Path, '.json')
[System.IO.File]::WriteAllText($resultPath, $json, [System.Text.UTF8Encoding]::new($false))
$json
