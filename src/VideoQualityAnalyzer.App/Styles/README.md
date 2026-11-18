# 表示書式の共通ルール

表示書式は `UiTheme.xaml` を基準にし、各画面で独自のフォント種類・サイズ・文字色を増やさない。画面固有色は `Palettes/Dark.xaml` と `Palettes/Light.xaml` の同じリソースキーで管理する。

| 用途 | リソース | 値 |
| --- | --- | --- |
| フォント | UiFontFamily | Yu Gothic UI |
| 大・画面見出し | UiFontLarge | 20 |
| 中・本文、操作、一覧 | UiFontMedium | 14 |
| 小・補足、状態、目盛り | UiFontSmall | 12 |
| デフォルト文字 | TextBrush | パレットに応じた標準文字 |
| 強調・処理中や完了通知 | UiEmphasisBrush | 青 |
| 警告・中止や測定上の注意 | UiWarningBrush | パレットに応じた警告色 |
| 補足 | UiMutedBrush | 控えめな標準文字 |
| 明るい背景 | UiOnLightBrush | 暗い標準文字 |
| 強調色上のチェック | UiOnAccentBrush | 強調色と対比する文字 |

見出しは `UiTitleStyle` と `UiSectionStyle`、補足は `UiSmallTextStyle` を利用できる。グラフの系列色や映像ヒートマップは文字色ではないため、この文字色分類とは別に扱う。

派生Window・UserControlには基底型用の共通Styleを明示的に適用する。XAMLでは `Style="{StaticResource {x:Type Window}}"` またはUserControlを指定し、コードで作る画面でも同じ型キーのStyleを設定する。C#からの小文字サイズ・色の取得は `UiTheme` を利用する。

標準コントロールの表示には、保存した `None`・`System`・`Light`・`Dark` を起動時に `Application.ThemeMode` へ設定する。`None` は従来のWPF表示、`System` はWindowsのアプリ用配色を使う。ウィンドウの余白、文字、一覧選択行、画面内グラフは同じ明暗に対応するパレット辞書の値を `UiTheme.xaml` の共通スタイルから適用する。起動時にパレット辞書、次に共通スタイルを読み込む。新しい色用途が必要になった場合は、両パレットに同じキーを追加する。`UiOnLightBrush` は白背景PNG用の文字色である。

表示スタイルはオプション画面で保存し、次回起動時から反映する。未設定時の既定値は `System` とし、保存済みの選択は維持する。`System` の独自パレットは起動時のWindowsアプリ用配色に合わせる。`--test-display-style --auto --theme Light` のように4モードを専用画面で検証できる。`--test-fluent-theme` と `--test-dark-palette` は暗色部品とグラフの既存検証に使う。PNG画像出力は画像保存画面でライト・ダークを選択でき、初期値はライトである。

ライト配色は、Visual Studioの明色画面を参考に薄いグレーの余白とパネル、控えめな罫線を使う。標準部品のテンプレートは `ThemeMode` に任せ、画面で境界が必要なボタン・入力欄には `UiControlBorderBrush` を直接指定する。パネルは `UiPanelBorderBrush`、一覧は `UiListBackgroundBrush` と `UiGridLineBrush` を利用する。暗黙のButton等のStyleで標準テンプレートを置き換えない。

プレビューのタブ周辺と動画情報帯には `UiPreviewChromeBrush`・`UiVideoOverlayBrush` を使い、その上の文字色は `UiVideoTextBrush`・`UiVideoMutedBrush` で揃える。ライトでは明るいグレーと暗い文字、ダークでは暗い背景と明るい文字を指定する。タブは標準テンプレートを保ったまま `UiControlBorderBrush` の細い枠線を付ける。映像の余白は黒を維持する。

グラフの指標タブもプレビュータブと同じ `UiPreviewTabHeaderStyle`、背景色、枠線色を使う。プレビューの動画詳細・元フレーム位置の表示には左6pxの余白を付ける。

通常のボタン・入力欄・選択欄・タブは `UiControlMinHeight`（32）を最小高さにし、内容に応じて幅・高さが伸びるようにする。ボタンの内側余白は `UiButtonPadding`（横12・縦4）、入力欄は `UiInputPadding`（横8・縦4）とする。横並びのボタン間は8、内容群の間は12、パネル内側は16を基本にし、`UiSpace4`・`UiSpace8`・`UiSpace12`・`UiSpace16` を使う。映像の折りたたみやグラフの小型操作ボタンは用途に応じて小さくできる。

WPFの `Button`・`ComboBox`・`TextBox` などに独自の共通 `Style` を適用すると `ThemeMode` の部品書式が変わる場合がある。このアプリでは標準テンプレートを保つ寸法指定に、XAMLでは共通リソース、C#では `UiTheme.ApplyStandardSizing` を使う。配色・状態表示は `ThemeMode` と既存パレットに任せる。グラフ画像出力の18px・22pxは独立した仕様を維持する。

## グラフ画像出力の文字サイズ

画像保存画面の「画像のテーマ」はアプリ全体の `ThemeMode` と独立し、変更直後にプレビューと保存PNGへ反映する。ダーク出力には `Palettes/Dark.xaml` のグラフ用配色を使う。

画像出力は `QualityGraphImageRenderer` で、画像出力専用の小18px・中22pxを標準とする。さらに選択したフォント倍率（0.5・0.8・1・1.2・1.5・2倍、初期値1倍）を掛け、96dpi・1描画単位＝1pxで直接描く。ウィンドウの寸法や画面DPIによる文字サイズの変更、画像全体の縮小による文字の縮小は行わない。プレビューの表示倍率は出力画像を変更しない。
