using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VideoQualityAnalyzer.App;

internal sealed class QualityGraphLegendController
{
    private readonly Border box;
    private readonly ScrollViewer scroll;
    private readonly Canvas overlay;
    private readonly Canvas graph;
    private readonly Func<Rect> plotArea;
    private bool positioned;

    public QualityGraphLegendController(Border box, ScrollViewer scroll, Canvas overlay, Canvas graph,
        Thumb drag, Button collapse, Func<Rect> plotArea)
    {
        this.box = box;
        this.scroll = scroll;
        this.overlay = overlay;
        this.graph = graph;
        this.plotArea = plotArea;
        overlay.SizeChanged += (_, _) =>
        {
            if (positioned) Clamp(Canvas.GetLeft(box), Canvas.GetTop(box));
            else Place();
        };
        drag.DragDelta += (_, e) => Clamp(Canvas.GetLeft(box) + e.HorizontalChange,
            Canvas.GetTop(box) + e.VerticalChange);
        collapse.Click += (_, _) =>
        {
            var collapsed = scroll.Visibility == Visibility.Visible;
            scroll.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            collapse.Content = collapsed ? "＋" : "−";
            collapse.ToolTip = collapsed ? "凡例を展開" : "凡例を折りたたむ";
            Clamp(Canvas.GetLeft(box), Canvas.GetTop(box));
        };
    }

    private void Clamp(double x, double y)
    {
        box.Width = Math.Min(350, Math.Max(1, overlay.ActualWidth - 12));
        scroll.MaxHeight = Math.Min(180, Math.Max(0, overlay.ActualHeight - 50));
        box.Measure(new Size(box.Width, double.PositiveInfinity));
        Canvas.SetLeft(box, Math.Clamp(x, 0, Math.Max(0, overlay.ActualWidth - box.DesiredSize.Width)));
        Canvas.SetTop(box, Math.Clamp(y, 0, Math.Max(0, overlay.ActualHeight - box.DesiredSize.Height)));
    }

    public void Place()
    {
        Clamp(0, 0);
        var area = plotArea();
        var width = box.DesiredSize.Width;
        var height = box.DesiredSize.Height;
        var left = area.Left;
        var right = Math.Max(left, area.Right - width);
        var top = area.Top;
        var bottom = Math.Max(top, area.Bottom - height);
        // 同点なら右下を優先。四隅と各辺の中央を比較する。
        Point[] candidates = [new(right, bottom), new(left, bottom), new(right, top), new(left, top),
            new((left + right) / 2, bottom), new(right, (top + bottom) / 2),
            new(left, (top + bottom) / 2), new((left + right) / 2, top)];
        double Score(Point position)
        {
            var bounds = new Rect(position, new Size(width, height));
            double score = 0;
            foreach (var child in graph.Children.OfType<FrameworkElement>())
            {
                if (child is Line line && !Equals(line.Tag, "グリッド"))
                {
                    // 接続線は線上の点で評価し、斜線の外接矩形を過大評価しない。
                    for (var step = 0; step <= 24; step++)
                        if (bounds.Contains(new Point(line.X1 + (line.X2 - line.X1) * step / 24,
                            line.Y1 + (line.Y2 - line.Y1) * step / 24))) score += 2;
                }
                else if (child is Ellipse || child is TextBlock { Tag: "点の評価値" })
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var childBounds = new Rect(Canvas.GetLeft(child), Canvas.GetTop(child), child.DesiredSize.Width, child.DesiredSize.Height);
                    if (!bounds.IntersectsWith(childBounds)) continue;
                    var overlap = Rect.Intersect(bounds, childBounds);
                    score += overlap.Width * overlap.Height + (child is Ellipse ? 1000 : 100);
                }
            }
            return score;
        }
        var best = candidates.OrderBy(Score).First();
        Clamp(best.X, best.Y);
        positioned = true;
    }
}
