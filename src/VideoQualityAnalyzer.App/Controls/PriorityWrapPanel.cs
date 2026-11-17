using System.Windows;
using System.Windows.Controls;

namespace VideoQualityAnalyzer.App;

// 優先順位の小さいグループから下段へ送り、各段では元の順序を保つ。
public sealed class PriorityWrapPanel : Panel
{
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.RegisterAttached(
        "Priority", typeof(int), typeof(PriorityWrapPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static void SetPriority(UIElement element, int value) => element.SetValue(PriorityProperty, value);
    public static int GetPriority(UIElement element) => (int)element.GetValue(PriorityProperty);

    private List<List<UIElement>> GetRows(double width)
    {
        var first = InternalChildren.Cast<UIElement>().ToList();
        var overflow = new HashSet<UIElement>();
        foreach (var child in first.OrderBy(GetPriority).ToArray())
        {
            if (first.Sum(item => item.DesiredSize.Width) <= width || first.Count <= 1) break;
            first.Remove(child);
            overflow.Add(child);
        }
        var rows = new List<List<UIElement>> { first };
        var row = new List<UIElement>();
        double used = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (!overflow.Contains(child)) continue;
            if (row.Count > 0 && used + child.DesiredSize.Width > width)
            {
                rows.Add(row);
                row = [];
                used = 0;
            }
            row.Add(child);
            used += child.DesiredSize.Width;
        }
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var rows = GetRows(availableSize.Width);
        return new Size(rows.Max(row => row.Sum(child => child.DesiredSize.Width)),
            rows.Sum(row => row.Count == 0 ? 0 : row.Max(child => child.DesiredSize.Height)) + (rows.Count - 1) * 6);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        foreach (var row in GetRows(finalSize.Width))
        {
            var height = row.Count == 0 ? 0 : row.Max(child => child.DesiredSize.Height);
            double x = 0;
            foreach (var child in row)
            {
                // 拡大縮小のまとまりは各段の右端へ寄せる。
                if (child is VideoQualityAnalyzer.GpuPreview.ZoomControlBar)
                    x = Math.Max(x, finalSize.Width - row.Skip(row.IndexOf(child)).Sum(item => item.DesiredSize.Width));
                child.Arrange(new Rect(x, y + (height - child.DesiredSize.Height) / 2,
                    child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width;
            }
            y += height + 6;
        }
        return finalSize;
    }
}