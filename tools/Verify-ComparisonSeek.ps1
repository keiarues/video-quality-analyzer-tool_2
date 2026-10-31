param([int[]]$Frames = @(0, 1, 150, 151, 450, 451, 833, 900))

$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ffmpeg = Join-Path $PSScriptRoot 'ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe'
$output = Join-Path $workspace 'measurement-results'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$baseline = Join-Path $output 'seek-verification-baseline.raw'
$candidate = Join-Path $output 'seek-verification-candidate.raw'
$names = @(
    '攻殻機動隊_reference.mkv', '攻殻機動隊_icq28.mp4', '攻殻機動隊_icq50.mp4',
    '攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv',
    '攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv'
)
try {
    foreach ($name in $names) {
        $path = Join-Path (Join-Path $workspace 'test-data') $name
        if (-not (Test-Path -LiteralPath $path)) { throw "動画が見つかりません: $name" }
        foreach ($frame in $Frames) {
            if ($frame -lt 0) { throw 'フレーム番号は0以上にしてください。' }
            $seconds = ($frame * 1001.0 / 30000.0).ToString('F9',
                [System.Globalization.CultureInfo]::InvariantCulture)
            & $ffmpeg -hide_banner -loglevel error -i $path -map 0:v:0 -an -sn -dn `
                -vf "select=eq(n\,$frame)" -frames:v 1 -pix_fmt bgra -f rawvideo -y $baseline
            if ($LASTEXITCODE -ne 0) { throw "従来の復号に失敗: $name #$frame" }
            & $ffmpeg -hide_banner -loglevel error -ss $seconds -i $path -map 0:v:0 -an -sn -dn `
                -frames:v 1 -pix_fmt bgra -f rawvideo -y $candidate
            if ($LASTEXITCODE -ne 0) { throw "近傍シークに失敗: $name #$frame" }
            $old = Get-Item -LiteralPath $baseline
            $new = Get-Item -LiteralPath $candidate
            if ($old.Length -ne 1440 * 1080 * 4 -or $new.Length -ne $old.Length -or
                (Get-FileHash -LiteralPath $baseline).Hash -ne (Get-FileHash -LiteralPath $candidate).Hash) {
                throw "元フレームの画素が一致しません: $name #$frame"
            }
        }
        Write-Host "$name : $($Frames.Count)位置で画素一致"
    }
}
finally {
    Remove-Item -LiteralPath $baseline, $candidate -ErrorAction SilentlyContinue
}
