# 開発用テスト画面

通常利用の画面と区別するため、専用検証画面をこのフォルダーに置く。

| ファイル | 検証対象 | 起動引数 |
|---|---|---|
| FolderDropTestWindow.cs | フォルダー登録 | `--test-folder-drop` |
| FfmpegDownloadTestWindow.cs | FFmpegの取得 | `--test-ffmpeg-download` |
| CsvExportTestWindow.cs | 品質CSVの保存・読み戻し | `--test-csv-export --auto` |
| GraphPlaybackTestWindow.cs | グラフ表示中の再生 | `--graph-playback-test` |
| GraphImageSaveWindow.Verification.cs | グラフ画像保存画面のサンプル・画像出力 | `--test-graph-image --auto` |
| App.TestScreens.cs | 専用テスト画面の起動分岐 | `--test-*` など |
| App.Verification.cs | グラフ準備・評価・比較などの検証起動分岐 | `--verify-*` など |
| MainWindow/ | 主画面の検証専用partialクラス | `--verify-main-*` など |
| MainWindow/MainWindow.LibraryFilesVerification.cs | 同時追加の重複防止と読み込み失敗後の予約解除 | `--verify-concurrent-add` |

起動引数の詳細や入力ファイルは `App.TestScreens.cs`、`App.Verification.cs`、検証ツールを参照。名前空間と起動方法は変更していない。SDKの標準ファイル収集でビルドされるため、移動後も開発用引数で起動できる。この整理は配布物からテスト機能を除外するものではない。

通常利用でも使う画面・部品は用途別フォルダーに置く。配置は[アプリのソース配置](../README.md)を参照。通常画面の操作を確認するpartialクラスも通常画面と同じ名前空間で維持する。
