using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed partial class QualityGraphPanel
{
    public TimeSpan PlayheadPosition => playbackPosition;
    internal int EvaluationProgressUpdateCount { get; private set; }
    internal TimeSpan LastEvaluationProgressPosition { get; private set; }
    public double PlayheadX => playhead.X1;
    public void SetPlaybackPosition(TimeSpan position)
    {
        if (playheadDragging || evaluationPosition is not null) return;
        playbackPosition = position;
        UpdatePlayhead();
    }

    public void BeginPlayheadDrag(double fraction)
    {
        if (maximumTime <= 0 || evaluationPosition is not null) return;
        playheadDragging = true;
        MovePlayheadDrag(fraction);
    }

    public void MovePlayheadDrag(double fraction)
    {
        if (!playheadDragging) return;
        playbackPosition = TimeSpan.FromSeconds(maximumTime * Math.Clamp(fraction, 0, 1));
        UpdatePlayhead();
        SeekPreviewed?.Invoke(playbackPosition);
    }

    public void EndPlayheadDrag()
    {
        if (!playheadDragging) return;
        playheadDragging = false;
        seekRequested?.Invoke(playbackPosition);
    }

    public void SetPlaybackSources(VideoDescriptor? left, VideoDescriptor? right, string? referenceId)
    {
        playbackLeft = left;
        playbackRight = right;
        playbackReferenceId = referenceId;
        Draw();
    }

    private static TextBlock MakePlaybackLabel(TextAlignment alignment) => new()
    {
        FontSize = UiTheme.Small, Foreground = UiTheme.OnLight, Background = Brushes.WhiteSmoke,
        TextAlignment = alignment, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false
    };

    private string PlaybackValue(VideoDescriptor? video)
    {
        if (video is null || video.Id == playbackReferenceId ||
            metric.SelectedItem is not TabItem { Tag: QualityMetric selected } ||
            !playbackResults.TryGetValue((video.Id, selected), out var result)) return "--";
        var number = (long)Math.Floor(playbackPosition.TotalSeconds * video.AverageFrameRate + 0.000001);
        // 長時間動画でも毎回全フレームを走査しない。
        var lower = 0;
        var upper = result.Frames.Count - 1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            var frame = result.Frames[middle];
            if (frame.Number == number) return frame.Value.ToString("F4");
            if (frame.Number < number) lower = middle + 1; else upper = middle - 1;
        }
        return "--";
    }

    private void UpdatePlayhead()
    {
        var area = PlotArea(timeline);
        playhead.Visibility = maximumTime > 0 ? Visibility.Visible : Visibility.Collapsed;
        playhead.X1 = playhead.X2 = area.Left + Math.Clamp(playbackPosition.TotalSeconds / Math.Max(0.001, maximumTime), 0, 1) * area.Width;
        playhead.Y1 = area.Top;
        playhead.Y2 = area.Bottom;
        leftPlaybackStatus.Foreground = playbackLeft is not null && videoColors.TryGetValue(playbackLeft.Id, out var leftColor)
            ? leftColor : UiTheme.OnLight;
        rightPlaybackStatus.Foreground = playbackRight is not null && videoColors.TryGetValue(playbackRight.Id, out var rightColor)
            ? rightColor : UiTheme.OnLight;
        leftPlaybackStatus.Text = $"{PlaybackValue(playbackLeft)}\n{playbackLeft?.Name ?? "--"}";
        rightPlaybackStatus.Text = $"{PlaybackValue(playbackRight)}\n{playbackRight?.Name ?? "--"}";
        playbackTimeLabel.Text = $"{playbackPosition.TotalSeconds:0.##}秒";
        var x = playhead.X1;
        void Place(TextBlock label, double start, double width, double bottom)
        {
            label.Visibility = playhead.Visibility;
            label.Width = Math.Max(0, width);
            label.Measure(new Size(label.Width, double.PositiveInfinity));
            Canvas.SetLeft(label, start);
            Canvas.SetTop(label, Math.Max(area.Top, bottom - label.DesiredSize.Height));
        }
        Place(leftPlaybackStatus, Math.Max(0, x - 226), Math.Min(220, Math.Max(0, x - 6)), area.Bottom - 4);
        Place(rightPlaybackStatus, x + 6, Math.Min(220, Math.Max(0, timeline.ActualWidth - x - 6)), area.Bottom - 4);
        Place(playbackTimeLabel, x + 6, Math.Min(220, Math.Max(0, timeline.ActualWidth - x - 6)),
            area.Bottom - rightPlaybackStatus.DesiredSize.Height - 8);
    }

}
