using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoQualityAnalyzer.App;

internal static class UiTheme
{
    public const double EvaluationCardMinimumHeight = 158;
    public const double ActionButtonHeight = 32;
    public static double ControlMinHeight => (double)Application.Current.FindResource("UiControlMinHeight");
    public static double Space8 => (double)Application.Current.FindResource("UiSpace8");
    public static double Space12 => (double)Application.Current.FindResource("UiSpace12");
    public static double Space16 => (double)Application.Current.FindResource("UiSpace16");
    public static double Small => (double)Application.Current.FindResource("UiFontSmall");
    public static Brush ControlBorder => (Brush)Application.Current.FindResource("UiControlBorderBrush");
    public static Brush OnLight => (Brush)Application.Current.FindResource("UiOnLightBrush");
    public static Brush Emphasis => (Brush)Application.Current.FindResource("UiEmphasisBrush");
    public static Brush Warning => (Brush)Application.Current.FindResource("UiWarningBrush");

    // Styleを差し替えずに寸法だけ揃え、ThemeModeの標準外観を維持する。
    public static void ApplyStandardSizing(Control control)
    {
        control.MinHeight = ControlMinHeight;
        switch (control)
        {
            case Button button:
                button.Padding = (Thickness)Application.Current.FindResource("UiButtonPadding");
                button.BorderBrush = ControlBorder;
                break;
            case ComboBox or TextBox:
                control.Padding = (Thickness)Application.Current.FindResource("UiInputPadding");
                control.BorderBrush = ControlBorder;
                break;
        }
    }
}
