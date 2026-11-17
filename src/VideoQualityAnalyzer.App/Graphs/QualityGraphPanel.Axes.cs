using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed partial class QualityGraphPanel
{
    private static FrameworkElementFactory MakeLegendHeader()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "凡例（移動可）");
        text.SetValue(TextBlock.ForegroundProperty, UiTheme.OnLight);
        text.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        return text;
    }

    private static DockPanel MakeGraph(string title, Canvas canvas)
    {
        var panel = new DockPanel { Margin = new Thickness(6) };
        var label = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label);
        panel.Children.Add(canvas);
        return panel;
    }

    private static double Bitrate(VideoDescriptor video) => video.AverageBitrateKbps ?? 0;

    // 軸・目盛りと描画データは同じ領域を基準に配置する。
    private Rect PlotArea(Canvas canvas) => new(88, 28,
        Math.Max(1, canvas.ActualWidth - 114), Math.Max(1, canvas.ActualHeight - 82));

    private void DrawAxes(Canvas canvas, Rect area, double maximumX, double minimumY, double maximumY,
        string unit, QualityMetric selected)
    {
        const int divisions = 5;
        for (var index = 0; index <= divisions; index++)
        {
            var fraction = index / (double)divisions;
            var x = area.Left + fraction * area.Width;
            var y = area.Bottom - fraction * area.Height;
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = area.Top, Y2 = area.Bottom,
                Stroke = Brushes.LightGray, StrokeThickness = 1, Tag = "グリッド", IsHitTestVisible = false });
            canvas.Children.Add(new Line { X1 = area.Left, X2 = area.Right, Y1 = y, Y2 = y,
                Stroke = Brushes.LightGray, StrokeThickness = 1, Tag = "グリッド", IsHitTestVisible = false });
            AddTick(canvas, (maximumX * fraction).ToString(unit == "kbps" ? "N0" : "0.##") + (unit == "kbps" ? " kbps" : "秒"), x, area.Bottom + 6, true);
            AddTick(canvas, (minimumY + (maximumY - minimumY) * fraction).ToString("G4"), area.Left - 7, y, false);
        }
        canvas.Children.Add(new Polyline { Stroke = Brushes.DimGray, StrokeThickness = 1,
            IsHitTestVisible = false, Tag = "軸", Points = new PointCollection
                { new(area.Left, area.Top), new(area.Left, area.Bottom), new(area.Right, area.Bottom) } });
        var axisName = new TextBlock { Text = selected.ToString().ToUpperInvariant(),
            FontSize = (double)FindResource("UiFontMedium"), Foreground = UiTheme.OnLight,
            RenderTransform = new RotateTransform(-90), Tag = "縦軸名", IsHitTestVisible = false };
        axisName.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(axisName, 6);
        Canvas.SetTop(axisName, area.Top + (area.Height + axisName.DesiredSize.Width) / 2);
        canvas.Children.Add(axisName);
        var qualityGuide = new TextBlock { Text = "↑ 高画質", FontSize = UiTheme.Small,
            Foreground = UiTheme.OnLight, Tag = "品質方向", IsHitTestVisible = false };
        Canvas.SetLeft(qualityGuide, area.Left + 4);
        Canvas.SetTop(qualityGuide, 2);
        canvas.Children.Add(qualityGuide);
        var caption = new TextBlock { Text = unit, FontSize = (double)FindResource("UiFontMedium"), Foreground = UiTheme.OnLight, IsHitTestVisible = false };
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, area.Left + (area.Width - caption.DesiredSize.Width) / 2);
        Canvas.SetTop(caption, area.Bottom + 28);
        canvas.Children.Add(caption);
    }

    private void AddTick(Canvas canvas, string text, double x, double y, bool horizontal)
    {
        var label = new TextBlock { Text = text, Foreground = UiTheme.OnLight, FontSize = UiTheme.Small,
            Tag = "目盛り", IsHitTestVisible = false };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, horizontal ? Math.Clamp(x - label.DesiredSize.Width / 2, 0,
            Math.Max(0, canvas.ActualWidth - label.DesiredSize.Width)) : x - label.DesiredSize.Width);
        Canvas.SetTop(label, horizontal ? y : y - label.DesiredSize.Height / 2);
        canvas.Children.Add(label);
    }
}
