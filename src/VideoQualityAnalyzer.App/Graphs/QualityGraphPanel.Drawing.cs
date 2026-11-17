using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed partial class QualityGraphPanel
{
    private void AttachPlaybackVisuals()
    {
        playbackCanvas.Children.Add(playhead);
        playbackCanvas.Children.Add(leftPlaybackStatus);
        playbackCanvas.Children.Add(rightPlaybackStatus);
        playbackCanvas.Children.Add(playbackTimeLabel);
    }

    private void SelectVideo(string id)
    {
        selectedIds.Clear();
        selectedIds.Add(id);
        selectVideoRequested?.Invoke(id);
        Draw();
    }

    private void Draw()
    {
        displayedVideoIds.Clear();
        videoColors.Clear();
        timeline.Children.Clear();
        playbackOverlay.Children.Clear();
        averages.Children.Clear();
        legend.Children.Clear();
        if (metric.SelectedItem is not TabItem { Tag: QualityMetric selected }) return;
        var available = (allVideos ?? videos).Where(video => playbackResults.ContainsKey((video.Id, selected))).ToArray();
        if (available.Length == 0)
        {
            var durations = new[] { playbackLeft, playbackRight }.OfType<VideoDescriptor>()
                .Where(video => video.Duration > TimeSpan.Zero).Select(video => video.Duration.TotalSeconds).ToArray();
            maximumTime = evaluationVideo?.Duration.TotalSeconds ?? (durations.Length > 0 ? durations.Min() : 0);
            if (maximumTime > 0) DrawAxes(timeline, PlotArea(timeline), maximumTime, 0, 1, "秒", selected);
            AttachPlaybackVisuals();
            UpdatePlayhead();
            legendController.Place();
            return;
        }
        // フレーム値とファイル平均値は変動幅が異なるため、表示対象ごとに軸を求める。
        var frameVideos = available.Where(video => selectedIds.Contains(video.Id)).ToArray();
        var (min, max) = QualityGraphScale.VerticalRange(frameVideos.SelectMany(video =>
            playbackResults[(video.Id, selected)].Frames.Select(frame => frame.Value)));
        var (fileMin, fileMax) = QualityGraphScale.VerticalRange(available.Select(video => playbackResults[(video.Id, selected)].Average));
        maximumTime = available.Max(video => playbackResults[(video.Id, selected)].Frames.Max(frame => frame.Number) / video.AverageFrameRate);
        if (available.Any(video => playbackResults[(video.Id, selected)].Selection.IsPartial))
            maximumTime = available.Max(video => video.Duration.TotalSeconds);
        if (evaluationVideo is not null) maximumTime = evaluationVideo.Duration.TotalSeconds;
        var rates = available.Select(Bitrate).ToArray();
        var maxRate = Math.Max(1, rates.Max());
        var timeArea = PlotArea(timeline);
        var fileArea = PlotArea(averages);
        DrawAxes(timeline, timeArea, maximumTime, min, max, "秒", selected);
        DrawAxes(averages, fileArea, maxRate, fileMin, fileMax, "kbps", selected);
        var previousPoints = new Dictionary<string, Point>();
        for (var index = 0; index < available.Length; index++)
        {
            var video = available[index];
            var result = playbackResults[(video.Id, selected)];
            var color = colors[index % colors.Length];
            videoColors[video.Id] = color;
            var line = new Polyline { Stroke = color, StrokeThickness = 1, ToolTip = video.Name };
            var isSelected = selectedIds.Contains(video.Id);
            long? previousNumber = null;
            foreach (var frame in result.Frames)
            {
                if (result.Selection.IsPartial && previousNumber is { } previous && frame.Number != previous + 1)
                {
                    if (isSelected) timeline.Children.Add(line);
                    line = new Polyline { Stroke = color, StrokeThickness = 1, ToolTip = video.Name };
                }
                line.Points.Add(new Point(timeArea.Left + frame.Number / video.AverageFrameRate / Math.Max(0.001, maximumTime) * timeArea.Width,
                    timeArea.Top + (max - frame.Value) / (max - min) * timeArea.Height));
                previousNumber = frame.Number;
            }
            if (isSelected) { timeline.Children.Add(line); displayedVideoIds.Add(video.Id); }
            var point = new Ellipse { Width = 12, Height = 12, Fill = color,
                Stroke = isSelected ? palette.Selection : null, StrokeThickness = 2,
                Tag = video.Id, ToolTip = $"{video.Name}: {result.Average:F4} / {rates[index]:N0} kbps\n{result.Selection.Description}" +
                    (result.Selection.IsPartial ? "（評価対象のみの平均）" : ""), Cursor = System.Windows.Input.Cursors.Hand };
            Canvas.SetLeft(point, fileArea.Left + rates[index] / maxRate * fileArea.Width - 6);
            Canvas.SetTop(point, fileArea.Top + (fileMax - result.Average) / (fileMax - fileMin) * fileArea.Height - 6);
            point.MouseLeftButtonDown += (_, e) => { SelectVideo(video.Id); e.Handled = true; };
            if (groups is not null && groups.TryGetValue(video.Id, out var group))
            {
                var center = new Point(Canvas.GetLeft(point) + 6, Canvas.GetTop(point) + 6);
                if (previousPoints.TryGetValue(group, out var previous))
                    averages.Children.Add(new Line { X1 = previous.X, Y1 = previous.Y,
                        X2 = center.X, Y2 = center.Y, Stroke = palette.Connection, StrokeThickness = 1, IsHitTestVisible = false });
                previousPoints[group] = center;
            }
            averages.Children.Add(point);
            var label = new TextBlock { Text = $"{result.Average:F4} / {rates[index]:N0} kbps",
                FontSize = UiTheme.Small, Foreground = palette.Text, Background = palette.Plot,
                IsHitTestVisible = false, Tag = "点の評価値" };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(Canvas.GetLeft(point) + 14, fileArea.Left,
                Math.Max(fileArea.Left, averages.ActualWidth - label.DesiredSize.Width - 4)));
            Canvas.SetTop(label, Math.Clamp(Canvas.GetTop(point) - label.DesiredSize.Height,
                0, Math.Max(0, averages.ActualHeight - label.DesiredSize.Height)));
            averages.Children.Add(label);
            var legendItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 10, 2),
                ToolTip = $"{video.Name} / {result.Average:F4} / {rates[index]:N0} kbps" };
            legendItem.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = color, Margin = new Thickness(0, 0, 5, 0) });
            legendItem.Children.Add(new TextBlock { Text = video.Name, FontSize = UiTheme.Small,
                Foreground = palette.Text, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis });
            legendItem.Children.Add(new TextBlock { Text = $"  {result.Average:F4} / {rates[index]:N0} kbps",
                FontSize = UiTheme.Small, Foreground = palette.Text });
            legendItem.Background = Brushes.Transparent;
            legendItem.Cursor = System.Windows.Input.Cursors.Hand;
            legendItem.MouseLeftButtonDown += (_, e) => { SelectVideo(video.Id); e.Handled = true; };
            legend.Children.Add(legendItem);
        }
        legendController.Place();
        AttachPlaybackVisuals();
        UpdatePlayhead();
    }
}
