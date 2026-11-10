# GPU実験の専用テスト画面

通常の実験画面・共用部品と区別し、操作別の検証画面を配置する。

- HeatmapBlurTestWindow.cs：ヒートマップのぼかし切り替え。
- SeekHandoffTestWindow.cs：シークと復号器の引き継ぎ。
- SelectionSwitchTestWindow.cs：動画選択の切り替え。
- WipeDragTestWindow.cs：ワイプ境界のドラッグ。

名前空間と起動方法は従来どおり。起動引数は親フォルダーの `App.xaml.cs` を参照。ComparisonPanelなど通常アプリでも共用する部品は移動しない。
