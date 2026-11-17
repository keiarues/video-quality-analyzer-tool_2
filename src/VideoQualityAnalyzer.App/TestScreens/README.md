# 開発用テスト画面

通常利用の画面と区別するため、専用検証画面をこのフォルダーに置く。

| ファイル | 検証対象 | 起動引数 |
|---|---|---|
| FolderDropTestWindow.cs | フォルダー登録 | `--test-folder-drop` |
| FfmpegDownloadTestWindow.cs | FFmpegの取得 | `--test-ffmpeg-download` |
| SettingsWindow.Verification.cs | 設定画面の無効な入力・保存拒否・自動検出・バージョン表示とダウンロード検証 | `--verify-settings-detection`、`--verify-ffmpeg-settings` |
| CsvExportTestWindow.cs | 品質CSVの保存・読み戻し | `--test-csv-export --auto` |
| GraphPlaybackTestWindow.cs | グラフ表示中の再生 | `--graph-playback-test` |
| GraphImageSaveWindow.Verification.cs | グラフ画像保存画面のサンプル・画像出力 | `--test-graph-image --auto` |
| DarkPaletteTestWindow.cs | 共通の暗色配色。実グラフ・操作部品・一覧と、メニュー・選択欄の展開画像を確認 | `--test-dark-palette --auto` |
| FluentThemeTestWindow.cs | Fluent Darkの標準部品と展開メニュー・コンボボックスの画像を確認 | `--test-fluent-theme --auto` |
| App.TestScreens.cs | 専用テスト画面の起動分岐 | `--test-*` など |
| App.Verification.cs | グラフ準備・評価・比較などの検証起動分岐 | `--verify-*` など |
| MainWindow/ | 主画面の検証専用partialクラス | `--verify-main-*` など |
| MainWindow/MainWindow.PreviewVerification.cs など | 主画面のプレビュー・評価とグラフ・比較・一覧の機能別検証 | `--verify-preview-controls`、`--verify-main-*` |
| MainWindow/MainWindow.LibraryFilesVerification.cs | 同時・重複追加、失敗時の予約解除、選択維持、個別・全削除中の登録 | `--verify-concurrent-add` |

起動引数の詳細や入力ファイルは `App.TestScreens.cs`、`App.Verification.cs`、検証ツールを参照。名前空間と起動方法は変更していない。SDKの標準ファイル収集でビルドされるため、移動後も開発用引数で起動できる。この整理は配布物からテスト機能を除外するものではない。

通常利用でも使う画面・部品は用途別フォルダーに置く。配置は[アプリのソース配置](../README.md)を参照。通常画面の操作を確認するpartialクラスも通常画面と同じ名前空間で維持する。
