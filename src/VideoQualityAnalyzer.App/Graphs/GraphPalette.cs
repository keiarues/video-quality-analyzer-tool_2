using System.Windows.Media;
using System.Windows;

namespace VideoQualityAnalyzer.App;

// 画面表示は選択中のパレット、画像出力は保存画面で選んだ配色を使う。
public enum GraphImageTheme { Light, Dark }

internal sealed record GraphPalette(Brush Surface, Brush Plot, Brush Text, Brush Grid, Brush Axis,
    Brush Connection, Brush Selection, Brush[] Series)
{
    public static GraphPalette Export => new(Brushes.White, Brushes.WhiteSmoke, UiTheme.OnLight,
        Brushes.LightGray, Brushes.DimGray, Brushes.Gray, Brushes.Black,
        [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod]);

    private static readonly Lazy<ResourceDictionary> DarkExportResources = new(() => new ResourceDictionary
    { Source = new Uri("Styles/Palettes/Dark.xaml", UriKind.Relative) });

    public static GraphPalette ForExport(GraphImageTheme theme)
    {
        if (theme == GraphImageTheme.Light) return Export;
        if (theme != GraphImageTheme.Dark) throw new ArgumentOutOfRangeException(nameof(theme));
        Brush Dark(string key) => (Brush)DarkExportResources.Value[key];
        return new(Dark("GraphSurfaceBrush"), Dark("GraphPlotBrush"), Dark("GraphTextBrush"),
            Dark("GraphGridBrush"), Dark("GraphAxisBrush"), Dark("GraphConnectionBrush"),
            Dark("GraphSelectionBrush"),
            [Dark("GraphSeries1Brush"), Dark("GraphSeries2Brush"), Dark("GraphSeries3Brush"),
             Dark("GraphSeries4Brush"), Dark("GraphSeries5Brush")]);
    }

    public static GraphPalette Screen => new(
        Get("GraphSurfaceBrush"), Get("GraphPlotBrush"), Get("GraphTextBrush"),
        Get("GraphGridBrush"), Get("GraphAxisBrush"), Get("GraphConnectionBrush"),
        Get("GraphSelectionBrush"),
        [Get("GraphSeries1Brush"), Get("GraphSeries2Brush"), Get("GraphSeries3Brush"),
         Get("GraphSeries4Brush"), Get("GraphSeries5Brush")]);

    private static Brush Get(string key) => (Brush)Application.Current.FindResource(key);
}
