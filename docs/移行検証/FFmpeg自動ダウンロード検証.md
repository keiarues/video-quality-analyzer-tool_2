# FFmpeg自動ダウンロードの検証

更新日: 2026-11-07

## 動作

- 設定画面の「フォルダーを選択」の下に「FFmpegの最新安定版をダウンロード」を追加。
- Gyanの `https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip` と `.sha256` を毎回取得する。開発版は取得しない。配布元の最新releaseビルドが対象。
- 開発時はソリューション直下、配布時はアプリ直下の `tools/ffmpeg/` 以下へ保存する。
- SHA256照合、ZIP展開、ffmpeg・ffprobeの存在と起動を確認してから確定する。配布物のライセンス等も含めて展開し、ハッシュ別のフォルダーに置く。既存の版は上書きしない。
- 完了後は設定欄にbinフォルダーを反映する。「保存」で確定し、次回起動から使用する。取得中の保存は無効。中断・画面終了時は取得をキャンセルし、一時ファイルを削除する。
- 通信失敗・ハッシュ不一致などは画面に表示する。配布元更新とダウンロードが重なり不一致になった場合は再実行する。

## 検証方法

専用テスト画面（`--test-ffmpeg-download --auto`）を先に用意して取得成功を確認し、設定画面へ組み込んだ。

成功したコマンド（リポジトリ直下で実行）:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PWD '.nuget-packages'
& tools/dotnet/dotnet.exe build src/VideoQualityAnalyzer.App/VideoQualityAnalyzer.App.csproj -c Release --no-restore -m:1 /nodeReuse:false
& tools/dotnet/dotnet.exe src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.StageA.dll --test-ffmpeg-download --auto
& tests/Run-Tests.ps1 -NoRestore
```

- Releaseビルド：警告0、エラー0。
- MSTest：32件合格。今回3件を追加し、ハッシュ不一致時の既存ファイル維持、不正ZIPの拒否、中断時の一時フォルダー削除を確認した。
- 制限付き環境ではDNS解決とテスト収集プロセスへの接続が失敗したため、通信・テストは通常権限で再実行し成功した。
- 人による画面の目視・マウス操作、取得した版での全動画の再生・品質評価・実提示fpsは未確認。

## 設定画面での最終確認

次の自動確認も通常権限で実行し、終了コード0で成功した。

```powershell
& tools/dotnet/dotnet.exe src/VideoQualityAnalyzer.App/bin/Release/net10.0-windows/VideoQualityAnalyzer.StageA.dll --verify-ffmpeg-settings
```

実際に取得されたのは `ffmpeg-9.0.2-essentials_build`。リポジトリ直下の `tools/ffmpeg/release-<SHA256>/` 以下への格納、ffmpeg・ffprobeの起動、設定欄へのbinパス反映、完了後の保存ボタン再有効化を確認した。設定ファイルへの保存はこの自動確認では行わず、利用者の既存設定を維持した。結果は実行先の `ffmpeg-settings-verification.txt` に出力する。
