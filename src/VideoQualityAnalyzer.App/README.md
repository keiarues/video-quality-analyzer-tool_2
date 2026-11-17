# アプリのソース配置

| フォルダー | 内容 |
| --- | --- |
| `MainWindow/` | 主画面のXAML、再生・選択・評価のpartialクラス |
| `Graphs/` | 品質グラフ、軸・凡例・ペイン操作、画像保存画面 |
| `Windows/` | 設定、評価、グループ、CSV保存など利用者向け画面 |
| `Controls/` | 評価範囲・指標の入力部品、配置部品 |
| `Playback/` | 通常プレビューのFFmpeg再生部品 |
| `Styles/` | 共通テーマのXAMLとC#ヘルパー |
| `TestScreens/` | 開発用の専用検証画面、起動分岐、主画面の検証専用partialクラス |

直下には `App.xaml`、コードビハインド、プロジェクトファイルを置く。移動したC#型の名前空間は `VideoQualityAnalyzer.App` のままで、WPFのXAMLとコードビハインドは同じフォルダーに置く。`TestScreens/GraphImageSaveWindow.Verification.cs` は画像保存画面のpartialクラスだが、検証用のため `TestScreens/` に置く。

SDKの標準ファイル収集でビルドするため、個別の `Compile` / `Page` 指定は不要。`App.xaml` からのテーマ参照 `Styles/UiTheme.xaml` は従来どおり。
