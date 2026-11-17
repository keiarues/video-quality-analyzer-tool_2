using System.Windows.Media;
using System.Windows;

namespace VideoQualityAnalyzer.App;

// 画面表示は選択中のパレット、画像出力は白背景の専用色を使う。
internal sealed record GraphPalette(Brush Surface, Brush Plot, Brush Text, Brush Grid, Brush Axis,
    Brush Connection, Brush Selection, Brush[] Series)
{
    public static GraphPalette Export => new(Brushes.White, Brushes.WhiteSmoke, UiTheme.OnLight,
        Brushes.LightGray, Brushes.DimGray, Brushes.Gray, Brushes.Black,
        [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod]);

    public static GraphPalette Screen => new(
        Get("GraphSurfaceBrush"), Get("GraphPlotBrush"), Get("GraphTextBrush"),
        Get("GraphGridBrush"), Get("GraphAxisBrush"), Get("GraphConnectionBrush"),
        Get("GraphSelectionBrush"),
        [Get("GraphSeries1Brush"), Get("GraphSeries2Brush"), Get("GraphSeries3Brush"),
         Get("GraphSeries4Brush"), Get("GraphSeries5Brush")]);

    private static Brush Get(string key) => (Brush)Application.Current.FindResource(key);
}
