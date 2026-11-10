# 開発用テスト画面

通常利用の画面と区別するため、専用検証画面をこのフォルダーに置く。

| ファイル | 検証対象 | 起動引数 |
|---|---|---|
| FolderDropTestWindow.cs | フォルダー登録 | `--test-folder-drop` |
| FfmpegDownloadTestWindow.cs | FFmpegの取得 | `--test-ffmpeg-download` |
| GraphPlaybackTestWindow.cs | グラフ表示中の再生 | `--graph-playback-test` |

起動引数の詳細や入力ファイルは既存の `App.xaml.cs` と検証ツールを参照。名前空間と起動方法は変更していない。SDKの標準ファイル収集でビルドされるため、移動後も開発用引数で起動できる。この整理は配布物からテスト機能を除外するものではない。

通常利用でも使う画面・部品は親フォルダーに残す。通常画面の操作を確認するpartialクラスも通常画面と同じ名前空間で維持する。
