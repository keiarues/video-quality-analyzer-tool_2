# 表示書式の共通ルール

表示書式は `UiTheme.xaml` を基準にし、各画面で独自のフォント種類・サイズ・文字色を増やさない。画面固有色は `Palettes/Dark.xaml` と `Palettes/Light.xaml` の同じリソースキーで管理する。

| 用途 | リソース | 値 |
| --- | --- | --- |
| フォント | UiFontFamily | Yu Gothic UI |
| 大・画面見出し | UiFontLarge | 20 |
| 中・本文、操作、一覧 | UiFontMedium | 13 |
| 小・補足、状態、目盛り | UiFontSmall | 11 |
| デフォルト・暗い背景 | TextBrush | 明るい標準文字 |
| 強調・処理中や完了通知 | UiEmphasisBrush | 青 |
| 警告・中止や測定上の注意 | UiWarningBrush | 黄 |
| 補足 | UiMutedBrush | 控えめな標準文字 |
| 明るい背景 | UiOnLightBrush | 暗い標準文字 |

見出しは `UiTitleStyle` と `UiSectionStyle`、補足は `UiSmallTextStyle` を利用できる。グラフの系列色や映像ヒートマップは文字色ではないため、この文字色分類とは別に扱う。

派生Window・UserControlには基底型用の共通Styleを明示的に適用する。XAMLでは `Style="{StaticResource {x:Type Window}}"` またはUserControlを指定し、コードで作る画面でも同じ型キーのStyleを設定する。C#からの小文字サイズ・色の取得は `UiTheme` を利用する。

標準コントロールの暗色表示は `Application.ThemeMode="Dark"` のFluentテーマを利用する。ウィンドウの余白、文字、一覧選択行、画面内グラフはパレット辞書の値を `UiTheme.xaml` の共通スタイルから適用する。`App.xaml` ではパレット辞書を共通スタイルより先に読み込む。`UiOnLightBrush` とグラフの画像出力色は白背景PNG用である。新しい色用途が必要になった場合は、両パレットに同じキーを追加する。

`--test-fluent-theme` は標準コントロールとウィンドウ余白、`--test-dark-palette` は独自グラフと標準コントロールを確認する。PNG画像出力は従来の白背景を維持する。現在読み込むのは暗色パレットで、`None`・`System`・`Light` への切り替えと明色画面の検証は次の作業である。

## グラフ画像出力の文字サイズ

画像出力は `QualityGraphImageRenderer` で、画像出力専用の小18px・中22pxを標準とする。さらに選択したフォント倍率（0.5・0.8・1・1.2・1.5・2倍、初期値1倍）を掛け、96dpi・1描画単位＝1pxで直接描く。ウィンドウの寸法や画面DPIによる文字サイズの変更、画像全体の縮小による文字の縮小は行わない。プレビューの表示倍率は出力画像を変更しない。
