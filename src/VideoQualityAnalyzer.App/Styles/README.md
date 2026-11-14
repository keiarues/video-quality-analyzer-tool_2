# 表示書式の共通ルール

表示書式は `UiTheme.xaml` を基準にし、各画面で独自のフォント種類・サイズ・文字色を増やさない。

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

暗い画面の文字と、白いグラフ・入力部品の文字を分けて読みやすさを保つ。ツールチップにも同じフォントを使用する。新しい文字用途が必要になった場合は、この表と辞書に用途を追加してから各画面へ適用する。

## グラフ画像出力の文字サイズ

画像出力は `QualityGraphImageRenderer` で、画像出力専用の小18px・中22pxを標準とする。さらに選択したフォント倍率（0.5・0.8・1・1.2・1.5・2・3倍、初期値1倍）を掛け、96dpi・1描画単位＝1pxで直接描く。ウィンドウの寸法や画面DPIによる文字サイズの変更、画像全体の縮小による文字の縮小は行わない。プレビューの表示倍率は出力画像を変更しない。
