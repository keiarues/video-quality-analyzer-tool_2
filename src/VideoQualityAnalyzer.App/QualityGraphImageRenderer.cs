using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

internal sealed record QualityGraphImageSeries(VideoDescriptor Video, QualityResult Result,
    Brush Color, bool Selected, double Bitrate, string? Group);

// 出力の1単位を1pxとして直接描画する。画面サイズ・画面DPI・プレビュー倍率は参照しない。
internal static class QualityGraphImageRenderer
{
    internal const double SmallFontPixels = 18;
    internal const double MediumFontPixels = 22;

    public static BitmapSource Render(GraphImageTarget target, int width, int height, double fontScale,
        QualityMetric metric, IReadOnlyList<QualityGraphImageSeries> allSeries)
    {
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > 40_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "画像は各辺1～16384ピクセル、総画素数4000万以下にしてください。");
        if (!double.IsFinite(fontScale) || fontScale < 0.5 || fontScale > 2)
            throw new ArgumentOutOfRangeException(nameof(fontScale));
        var small = SmallFontPixels * fontScale;
        var medium = MediumFontPixels * fontScale;
        var typeface = new Typeface((FontFamily)Application.Current.FindResource("UiFontFamily"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Text(string value, double pixels, double maximumWidth = double.PositiveInfinity)
        {
            var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, pixels, UiTheme.OnLight, 1);
            if (double.IsFinite(maximumWidth))
            {
                text.MaxTextWidth = Math.Max(1, maximumWidth);
                text.MaxLineCount = 1;
                text.Trimming = TextTrimming.CharacterEllipsis;
            }
            return text;
        }
        const double margin = 12;
        if (width <= margin * 2) throw new InvalidOperationException("指定の文字サイズで描画するには画像の幅が不足しています。");
        var series = (target == GraphImageTarget.Frames ? allSeries.Where(item => item.Selected) : allSeries).ToArray();
        var title = Text(metric.ToString().ToUpperInvariant() + (target == GraphImageTarget.Frames
            ? " フレームグラフ：時刻（秒）と品質" : " ファイルグラフ：平均ビットレート（kbps）と平均品質"), medium, width - margin * 2);
        var legendTexts = series.Select(item => Text(target == GraphImageTarget.Frames ? item.Video.Name :
            $"{item.Video.Name}  {item.Result.Average:F4} / {item.Bitrate:N0} kbps", small)).ToArray();
        var contentWidth = width - margin * 2;
        var cellWidth = legendTexts.Length == 0 ? contentWidth : Math.Min(contentWidth, legendTexts.Max(text => text.WidthIncludingTrailingWhitespace) + 30);
        var columns = Math.Max(1, (int)(contentWidth / Math.Max(1, cellWidth)));
        columns = Math.Min(columns, Math.Max(1, series.Length));
        cellWidth = contentWidth / columns;
        var rowHeight = Text("品質", small).Height + 8;
        var legendHeight = Math.Ceiling(series.Length / (double)columns) * rowHeight;
        var legendTop = height - margin - legendHeight;
        var values = target == GraphImageTarget.Frames
            ? series.SelectMany(item => item.Result.Frames.Select(frame => frame.Value))
            : series.Select(item => item.Result.Average);
        var (minimum, maximum) = Range(values);
        var yLabels = Enumerable.Range(0, 6).Select(index => Text((minimum + (maximum - minimum) * index / 5).ToString("G4"), small)).ToArray();
        var axisName = Text(metric.ToString().ToUpperInvariant(), medium);
        var qualityGuide = Text("↑ 高画質", small);
        var left = margin + axisName.Height + 8 + yLabels.Max(text => text.WidthIncludingTrailingWhitespace) + 8;
        var top = margin + title.Height + qualityGuide.Height + 12;
        var bottom = legendTop - rowHeight - Text("秒", medium).Height - 16;
        var plot = new Rect(left, top, Math.Max(0, width - margin - left), Math.Max(0, bottom - top));
        if (plot.Width < 80 || plot.Height < 60)
            throw new InvalidOperationException("指定のフォントと凡例を配置するには画像サイズが不足しています。解像度を上げるかフォント倍率を下げてください。");
        var maximumX = target == GraphImageTarget.Files ? Math.Max(1, series.Select(item => item.Bitrate).DefaultIfEmpty(0).Max()) :
            Math.Max(0.001, allSeries.SelectMany(item => item.Result.Frames.Select(frame => item.Video.AverageFrameRate > 0
                ? frame.Number / item.Video.AverageFrameRate : 0)).DefaultIfEmpty(1).Max());
        var unit = target == GraphImageTarget.Files ? "kbps" : "秒";
        string XLabel(double value) => unit == "kbps" ? $"{value:N0} kbps" : $"{value:0.##}秒";
        var widestX = Text(XLabel(maximumX), small).WidthIncludingTrailingWhitespace;
        // 右端の目盛りも中心を基準に置ける幅を確保し、端でのラベルの重なりを防ぐ。
        plot.Width = Math.Max(0, plot.Width - widestX / 2);
        if (plot.Width < 80) throw new InvalidOperationException("目盛りを配置するには画像の幅が不足しています。");
        var xDivisions = Math.Clamp((int)(plot.Width / (widestX + 16)), 1, 5);
        var yDivisions = Math.Clamp((int)(plot.Height / (rowHeight * 1.5)), 1, 5);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            drawing.DrawText(title, new Point(margin, margin));
            drawing.DrawText(qualityGuide, new Point(left + 4, top - qualityGuide.Height - 4));
            drawing.DrawRectangle(Brushes.WhiteSmoke, null, plot);
            var gridPen = new Pen(Brushes.LightGray, 1);
            for (var index = 0; index <= xDivisions; index++)
            {
                var fraction = index / (double)xDivisions;
                var x = plot.Left + plot.Width * fraction;
                drawing.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var label = Text(XLabel(maximumX * fraction), small);
                drawing.DrawText(label, new Point(Math.Clamp(x - label.WidthIncludingTrailingWhitespace / 2,
                    margin, Math.Max(margin, width - margin - label.WidthIncludingTrailingWhitespace)), plot.Bottom + 6));
            }
            for (var index = 0; index <= yDivisions; index++)
            {
                var fraction = index / (double)yDivisions;
                var y = plot.Bottom - plot.Height * fraction;
                drawing.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                var label = Text((minimum + (maximum - minimum) * fraction).ToString("G4"), small);
                drawing.DrawText(label, new Point(plot.Left - label.WidthIncludingTrailingWhitespace - 8, y - label.Height / 2));
            }
            var axisPen = new Pen(Brushes.DimGray, 1);
            drawing.DrawLine(axisPen, plot.TopLeft, plot.BottomLeft);
            drawing.DrawLine(axisPen, plot.BottomLeft, plot.BottomRight);
            // 指標名を通常の横書きで組んでから反時計回りに90度回す。
            drawing.PushTransform(new MatrixTransform(0, -1, 1, 0, margin,
                plot.Top + (plot.Height + axisName.WidthIncludingTrailingWhitespace) / 2));
            drawing.DrawText(axisName, new Point(0, 0));
            drawing.Pop();
            var caption = Text(unit, medium);
            drawing.DrawText(caption, new Point(plot.Left + (plot.Width - caption.Width) / 2, plot.Bottom + rowHeight + 6));
            Point Position(double x, double value) => new(plot.Left + x / maximumX * plot.Width,
                plot.Top + (maximum - value) / (maximum - minimum) * plot.Height);
            drawing.PushClip(new RectangleGeometry(plot));
            if (target == GraphImageTarget.Frames)
            {
                foreach (var item in series.Where(item => item.Video.AverageFrameRate > 0))
                {
                    var geometry = new StreamGeometry();
                    using (var context = geometry.Open())
                    {
                        var first = true;
                        foreach (var frame in item.Result.Frames.Where(frame => double.IsFinite(frame.Value)))
                        {
                            var point = Position(frame.Number / item.Video.AverageFrameRate, frame.Value);
                            if (first) { context.BeginFigure(point, false, false); first = false; }
                            else context.LineTo(point, true, false);
                        }
                    }
                    geometry.Freeze(); drawing.DrawGeometry(null, new Pen(item.Color, 1), geometry);
                }
            }
            else
            {
                var previous = new Dictionary<string, Point>();
                foreach (var item in series.Where(item => double.IsFinite(item.Result.Average)))
                {
                    var point = Position(item.Bitrate, item.Result.Average);
                    if (item.Group is { } group)
                    {
                        if (previous.TryGetValue(group, out var last)) drawing.DrawLine(new Pen(Brushes.Gray, 1), last, point);
                        previous[group] = point;
                    }
                }
            }
            drawing.Pop();
            if (target == GraphImageTarget.Files)
                foreach (var item in series.Where(item => double.IsFinite(item.Result.Average)))
                {
                    var point = Position(item.Bitrate, item.Result.Average);
                    drawing.DrawEllipse(item.Color, item.Selected ? new Pen(Brushes.Black, 2) : null, point, 6, 6);
                    var label = Text($"{item.Result.Average:F4} / {item.Bitrate:N0} kbps", small, plot.Width);
                    var labelY = point.Y - label.Height - 8;
                    if (labelY < plot.Top) labelY = point.Y + 10;
                    var location = new Point(Math.Clamp(point.X + 10, plot.Left, Math.Max(plot.Left, plot.Right - label.Width)),
                        Math.Clamp(labelY, plot.Top, Math.Max(plot.Top, plot.Bottom - label.Height)));
                    drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(location, new Size(label.Width, label.Height)));
                    drawing.DrawText(label, location);
                }
            if (series.Length == 0)
                drawing.DrawText(Text("評価結果がありません。", small, plot.Width), new Point(plot.Left + 8, plot.Top + 8));
            // 凡例は出力サイズに合わせて折り返し、グラフを覆わないよう下部に配置する。
            for (var index = 0; index < series.Length; index++)
            {
                var x = margin + index % columns * cellWidth;
                var y = legendTop + index / columns * rowHeight;
                drawing.DrawEllipse(series[index].Color, null, new Point(x + 5, y + rowHeight / 2 - 4), 4, 4);
                drawing.DrawText(Text(legendTexts[index].Text, small, cellWidth - 24), new Point(x + 16, y));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static (double Minimum, double Maximum) Range(IEnumerable<double> values)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length == 0) return (0, 1);
        var minimum = finite.Min(); var maximum = finite.Max();
        var padding = Math.Max((maximum - minimum) * 0.05,
            Math.Max(Math.Max(Math.Abs(minimum), Math.Abs(maximum)) * 0.001, 0.000001));
        return (minimum - padding, maximum + padding);
    }
}
