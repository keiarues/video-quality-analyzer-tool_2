# 動画品質アナライザー プレビュー検証アプリ

Windows 11 / .NET 10 / WPFで、2本の動画を同時にプレビューするための試作です。実装範囲と合否基準は[移行検証仕様書](docs/CSharp_WPF_移行検証_仕様書.md)、操作を含む試験は[テスト仕様書](docs/CSharp_WPF_移行検証_テスト仕様書.md)を参照してください。

## 起動

手軽に試す場合は、作業フォルダーの`アプリを起動.cmd`をダブルクリックしてください。画面が開いたら「追加」から`test-data`内の動画を2本選び、「再生」を押します。動画ファイルを起動ファイルへドラッグ＆ドロップして登録することもできます。

作業フォルダーでPowerShellを開き、配置済みの.NET 10 SDKを使います。

```powershell
.\tools\dotnet\dotnet.exe restore .\VideoQualityAnalyzer.Wpf.sln --configfile .\NuGet.Config
.\tools\dotnet\dotnet.exe build .\VideoQualityAnalyzer.Wpf.sln -c Release --no-restore
.\tools\dotnet\dotnet.exe .\src\VideoQualityAnalyzer.App\bin\Release\net10.0-windows\VideoQualityAnalyzer.StageA.dll
```

`tools/ffmpeg-9.0.2-essentials_build/bin/ffprobe.exe`と`ffmpeg.exe`を自動検出します。FFmpegは全動画の共通プレビュー表示に必要です。

動画ファイルのフルパスを起動コマンドの後に並べると、起動時に登録できます。空白を含むパスは引用符で囲んでください。

## 操作

1. 「追加」またはドラッグ＆ドロップで動画を登録します。最初の動画が元動画です。一覧の動画を選んで「元動画に設定」から変更できます。
2. 一覧で比較動画を1本選ぶと元動画と並べて表示します。2本以上選んだ場合は一覧順の先頭2本を表示します。選択を外すと、元動画と直前または先頭の比較動画を表示します。
3. 再生、一時停止、停止、秒単位の移動、シークバー、1コマ送り、左側の音量・ミュートを操作できます。右側は常に消音です。
4. 1コマ送りは必要時にFFprobeで左動画のフレームPTSを取得します。取得できない場合は平均fpsで近似したことを表示します。
5. 動画の下には左右の「再生位置からの換算fps」を表示します。再生時刻付近には左右の現在フレーム位置と全フレーム数を表示します。現在位置は平均fpsと再生時刻からの推定です。全数はファイル内のメタデータがあればその値を使い、なければ長さと平均fpsから推定します。

すべての動画の映像は同じFFmpegのRGB変換を通し、WPFの画像として表示します。手元の可逆圧縮ファイルとAV1を左右に置き、初回フレームと再生位置の進行を画面で確認しました。RGBフレームをCPU側へ取り出す暫定方式のため、60fpsの達成は未確認です。rawvideo出力にはPTSがないため、プレビューの提示時刻は平均fpsから推定します。左音声は従来のMediaElementが開ける非AV1動画のみ対応し、AV1の音声は未実装です。

## 測定結果の読み方

- 画面のCPU使用率とメモリはアプリの実測値です。GPU利用率と実際のハードウェアデコード使用状況はまだ不明と表示します。
- 「再生位置からの換算fps」は各動画の再生位置が約1秒で進んだ量に平均fpsを掛けた参考値です。フレーム欠落や重複は検出しません。画面へ実際に提示された異なるソースフレームを正確に数えていないため、左右の実提示fps、提示PTS差、初回フレーム提示時刻、シーク完了時刻は「未測定」です。`MediaOpened`までの時間は準備時間として別に記録します。
- 診断ログは通常、`%LOCALAPPDATA%\VideoQualityAnalyzer.StageA\logs`にJSON Lines形式で保存します。動画のフルパスは記録せず、匿名のIDを使います。
- [測定結果](docs/benchmark-results.md)に実施状況と制約を記載しています。段階Aの性能合否には、テスト仕様書に従う実提示フレームの外部計測が必要です。

## 素材・選択規則・ログの確認

```powershell
.\tools\dotnet\dotnet.exe .\tests\VideoQualityAnalyzer.Smoke\bin\Release\net10.0\VideoQualityAnalyzer.Smoke.dll (Get-Location).Path
```

この確認は5本の素材情報取得、選択規則、診断ログの形式とパス非記録を調べます。画面上でのフレーム提示性能は測りません。

## 段階Bの初期検証

段階AでAV1が再生できなかったため、Media Foundation Source Readerによる初回フレームのデコード試験を追加しました。以下はAV1ファイルをD3D11デバイスマネージャー付きで読みます。

```powershell
$video = (Get-Item -LiteralPath 'test-data\攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv').FullName
.\tools\dotnet\dotnet.exe .\tests\VideoQualityAnalyzer.MediaFoundationProbe\bin\Release\net10.0-windows\VideoQualityAnalyzer.MediaFoundationProbe.dll --gpu $video
```

`--gpu`はGPUデバイスを渡す指定です。実際のハードウェアデコード使用とGPUへの直接提示はまだ未確認です。この試験は通常アプリのFFmpeg共通表示とは独立しています。

## GPU単一動画実験

段階Bの最初の描画実験は`experiments/VideoQualityAnalyzer.GpuPreview`にあります。手元の1440×1080動画の先頭300フレームをD3D11子ウィンドウへ渡します。

手元のAV1とH.265で2画面のGPU実験を操作する場合は、`GPU実験を起動.cmd`をダブルクリックしてください。右動画の候補2本を読み込み、切り替えと±5秒・先頭シークを試せます。表示領域が他のウィンドウに隠れないようにしてください。PowerShellから`& '.\GPU実験を起動.cmd' -Stats`と実行すると、Flip方式のDXGI提示統計も画面に表示します。

```powershell
$video = (Get-Item -LiteralPath 'test-data\攻殻機動隊_ av1 --qvbr 32 --vpp-yadif.mkv').FullName
.\tools\dotnet\dotnet.exe .\experiments\VideoQualityAnalyzer.GpuPreview\bin\Release\net10.0-windows\VideoQualityAnalyzer.GpuPreview.dll $video
```

通常起動はRGBフレームをCPU側へ取り出してからGPUへ渡す方式です。AV1の先頭300フレームが画面に出ることを再確認しました。画面の枚数は`Present`へ渡した数で、実提示fpsではありません。

`--direct`を指定すると、Media FoundationからDXGIサーフェスを受け取り、GPU内の別テクスチャへコピーしてからD3D11 VideoProcessorで変換します。CPUへフレームを取り出さずにAV1とH.265の先頭300フレームを画面表示できました。デコーダーのサーフェスをVideoProcessorへ直接渡すと`Present`で停止したため、GPU内コピーを挟んでいます。可逆圧縮の巨大ファイルはNV12出力設定で失敗しました。ハードウェアデコードの実使用、実提示fps、60fps評価は未完了です。

2本の動画パスと`--direct`を渡すと、左右それぞれのReaderを別作業で進め、共通時計以下の最新PTSを選んで描画します。開始PTSは各動画の先頭フレームを基準に揃えます。AV1＋H.265、AV1＋AV1、H.265＋H.265の先頭300フレームを画面確認しました。現在の同時再生は各開始・シーク後に最大900フレームを読み取る実験です。

```powershell
$left = (Get-Item -LiteralPath 'test-data\攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv').FullName
$right = (Get-Item -LiteralPath 'test-data\攻殻機動隊_icq50.mp4').FullName
.\tools\dotnet\dotnet.exe .\experiments\VideoQualityAnalyzer.GpuPreview\bin\Release\net10.0-windows\VideoQualityAnalyzer.GpuPreview.dll --direct $left $right
```

右動画の候補を追加する場合は、そのパスをコマンド末尾へ並べます。画面上部で右動画を切り替え、`-5秒`、`+5秒`、`先頭へ`で左右を同時にシークできます。切り替え中は左のReaderを維持し、右のGPU入力を作り直します。

画面の枚数は描画要求数、PTS差は最後に描画要求した左右のソースPTS差です。切り替え後の時間も初回の**描画要求**までであり、ディスプレイへの実提示時刻、実提示fps、同期精度ではありません。左音声はこのGPU実験に未接続で、可逆圧縮ファイルもMedia FoundationのNV12出力設定に失敗します。通常アプリのFFmpeg共通表示は引き続き利用できます。

`-Stats`はDXGIが報告する提示IDを各ソースPTSへ対応付け、直近約3秒の提示ID増分を観測時間で割った参考fpsを表示します。「対応なし」は提示IDをソースPTSに結び付けられなかった数、「同一PTS」は連続した提示IDに同じPTSが付いた数です。ウィンドウの遮蔽や複数画面環境では統計が不正確になる場合があり、観測時間は厳密な画面提示時刻ではありません。60.00fps素材と表示装置を使う30秒の規定測定、外部計測との照合はまだ行っていません。
