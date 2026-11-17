using System.Windows;
using System.Windows.Media;

namespace VideoQualityAnalyzer.App;

internal static class UiTheme
{
    public const double EvaluationCardMinimumHeight = 158;
    public const double ActionButtonHeight = 32;
    public static double Small => (double)Application.Current.FindResource("UiFontSmall");
    public static Brush ControlBorder => (Brush)Application.Current.FindResource("UiControlBorderBrush");
    public static Brush OnLight => (Brush)Application.Current.FindResource("UiOnLightBrush");
    public static Brush Emphasis => (Brush)Application.Current.FindResource("UiEmphasisBrush");
    public static Brush Warning => (Brush)Application.Current.FindResource("UiWarningBrush");
}
