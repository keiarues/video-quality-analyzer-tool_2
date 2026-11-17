using System.Windows.Media;

namespace VideoQualityAnalyzer.App;

// 暗色案は専用テスト画面だけで選択する。
internal sealed record GraphPalette(Brush Surface, Brush Plot, Brush Text, Brush Grid, Brush Axis,
    Brush Connection, Brush Selection, Brush[] Series)
{
    public static GraphPalette Light => new(Brushes.White, Brushes.WhiteSmoke, UiTheme.OnLight,
        Brushes.LightGray, Brushes.DimGray, Brushes.Gray, Brushes.Black,
        [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod]);

    public static GraphPalette Dark => new(
        new SolidColorBrush(Color.FromRgb(32, 40, 55)),
        new SolidColorBrush(Color.FromRgb(23, 32, 45)),
        new SolidColorBrush(Color.FromRgb(241, 245, 250)),
        new SolidColorBrush(Color.FromRgb(64, 80, 102)),
        new SolidColorBrush(Color.FromRgb(151, 168, 190)),
        new SolidColorBrush(Color.FromRgb(151, 168, 190)),
        Brushes.White,
        [new SolidColorBrush(Color.FromRgb(145, 201, 255)),
         new SolidColorBrush(Color.FromRgb(255, 166, 120)),
         new SolidColorBrush(Color.FromRgb(119, 218, 172)),
         new SolidColorBrush(Color.FromRgb(205, 164, 255)),
         new SolidColorBrush(Color.FromRgb(255, 213, 122))]);
}
