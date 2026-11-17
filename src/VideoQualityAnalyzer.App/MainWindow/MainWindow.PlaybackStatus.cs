using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private void DisplayTimer_Tick(object? sender, EventArgs e)
    {
        if (comparisonActive)
        {
            playbackDrift.Reset();
            UpdateResourceSample();
            UpdatePerformanceDisplay();
            return;
        }
        UpdateResourceSample();
        if (isPlaying && CurrentPosition() >= CommonDuration() && CommonDuration() > TimeSpan.Zero)
        {
            StopPlayback();
            return;
        }

        if (isPlaying && left.Ready && right.Ready)
        {
            // Position差は提示PTS差ではない。再同期は持続した大きなずれだけに限定する。
            var difference = (SlotPosition(left) - SlotPosition(right)).Duration();
            if (playbackDrift.ShouldResync(difference, DateTimeOffset.UtcNow))
            {
                if (right.Av1.IsOpen)
                    right.Av1.Seek(ClampToVideo(CurrentPosition(), right.Video!));
                else
                    right.Media.Position = ClampToVideo(CurrentPosition(), right.Video!);
                log.Record("resync", "right", right.Video?.Id, "position_estimate",
                    detail: $"position_difference_ms={difference.TotalMilliseconds:F0};presented_pts_difference=unmeasured");
            }
        }
        else playbackDrift.Reset();
        UpdatePerformanceDisplay();
    }

    private void UpdateResourceSample()
    {
        var now = Stopwatch.GetTimestamp();
        if (previousSampleStamp != 0 && Stopwatch.GetElapsedTime(previousSampleStamp, now) < TimeSpan.FromSeconds(1))
            return;

        using var process = Process.GetCurrentProcess();
        var totalCpuTime = process.TotalProcessorTime;
        if (previousSampleStamp != 0)
        {
            var seconds = Stopwatch.GetElapsedTime(previousSampleStamp, now).TotalSeconds;
            cpuPercent = Math.Clamp(100 * (totalCpuTime - previousCpuTime).TotalSeconds /
                (seconds * Environment.ProcessorCount), 0, 100);
        }
        previousSampleStamp = now;
        previousCpuTime = totalCpuTime;
        memoryMb = process.WorkingSet64 / 1024d / 1024d;
        var video = left.Video;
        log.RecordPerformance("both", video?.Id, video?.AverageFrameRate,
            video is null ? null : $"{video.Width}x{video.Height}", video?.Codec, cpuPercent, memoryMb);
    }

    private TimeSpan CurrentPosition()
    {
        if (comparisonActive && comparisonPanel is not null) return comparisonPanel.Position;
        if (!isPlaying)
            return basePosition;
        if (left.Ready && left.Video?.HasAudio == true)
            return ClampToCommon(SlotPosition(left));
        return ClampToCommon(basePosition + playbackClock.Elapsed);
    }

    private TimeSpan SlotPosition(SlotState slot) =>
        comparisonActive && comparisonPanel is not null ? comparisonPanel.Position :
        slot.Av1.IsOpen ? slot.Av1.Position : slot.Media.Position;

    private TimeSpan CommonDuration()
    {
        if (left.Video is null)
            return TimeSpan.Zero;
        if (right.Video is null || right.Video.Duration == TimeSpan.Zero)
            return left.Video.Duration;
        if (left.Video.Duration == TimeSpan.Zero)
            return right.Video.Duration;
        return left.Video.Duration < right.Video.Duration ? left.Video.Duration : right.Video.Duration;
    }

    private TimeSpan ClampToCommon(TimeSpan time)
    {
        var duration = CommonDuration();
        if (time < TimeSpan.Zero)
            return TimeSpan.Zero;
        return duration > TimeSpan.Zero && time > duration ? duration : time;
    }

    private static TimeSpan ClampToVideo(TimeSpan time, VideoDescriptor video) =>
        video.Duration > TimeSpan.Zero && time > video.Duration ? video.Duration : time;

    // 操作イベントは即時反映し、フレーム通知は同じ入口へまとめる。
    private void UpdateTimeDisplay() => UpdateFrameStatus();

    private void RequestPlaybackRefresh()
    {
        if (closing || playbackRefreshQueued) return;
        playbackRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            playbackRefreshQueued = false;
            if (!closing) UpdateTimeDisplay();
        }));
    }

    private void UpdatePerformanceDisplay()
    {
        if (!initialized) return;
        MeasurementText.Text = $"CPU {(cpuPercent.HasValue ? $"{cpuPercent.Value:F1}%" : "計測中")} / メモリ {memoryMb:F0} MiB / GPU 不明　｜　" +
            $"実提示fps: 未測定（WPF観測: 左 {FormatObservedFps(comparisonPanel?.ObservedLeftFps)} / 右 {FormatObservedFps(comparisonPanel?.ObservedRightFps)}）　｜　" +
            $"左右PTS差（CFR換算）: {(comparisonActive && comparisonPanel?.RelativePtsDifferenceMilliseconds is { } difference ? $"{difference:F1} ms" : "未測定")}　｜　" +
            $"遅延破棄: {(comparisonActive && comparisonPanel is not null ? $"{comparisonPanel.LateDroppedFrames:N0}" : "未測定")}";

    }

    private TimeSpan DisplayedPlaybackPosition() => comparisonActive && comparisonPanel is not null
        ? comparisonPanel.Position : left.LastShownPosition ?? right.LastShownPosition ?? CurrentPosition();

    private void UpdatePlaybackControls()
    {
        if (!initialized) return;
        UpdatePlayPauseButton();
        var position = pendingSeekPosition ?? DisplayedPlaybackPosition();
        qualityGraphPanel?.SetPlaybackPosition(position);
        var duration = CommonDuration();
        TimeText.Text = $"{position:hh\\:mm\\:ss} / {duration:hh\\:mm\\:ss}";
        programmaticSlider = true;
        SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
        if (!seekSliderPointerActive && !seekSliderDragging && pendingSeekPosition is null)
            SeekSlider.Value = Math.Min(SeekSlider.Maximum, position.TotalSeconds);
        programmaticSlider = false;
    }

    private void UpdateFrameStatus()
    {
        if (!initialized) return;
        UpdatePlaybackControls();
        var framePrefix = comparisonActive ? "元フレーム位置（0始まり）: " : "フレーム位置（反映時刻から換算）: ";
        LeftFrameStatus.Text = framePrefix + FormatFramePosition(left);
        RightFrameStatus.Text = framePrefix + FormatFramePosition(right);
        if (comparisonActive) comparisonPanel?.SetFrameStatus(LeftFrameStatus.Text, RightFrameStatus.Text);
    }

    private string FormatObservedFps(double? fps) =>
        comparisonActive && isPlaying && fps.HasValue ? $"{fps.Value:F1}" : "—";

    private string FormatFramePosition(SlotState slot)
    {
        if ((!slot.Ready && !(comparisonActive && comparisonPanel?.CurrentFrameNumber is not null)) || slot.Video is not { AverageFrameRate: > 0 } video)
            return "—";
        var total = video.TotalFrames ?? (video.Duration > TimeSpan.Zero
            ? (long)Math.Round(video.Duration.TotalSeconds * video.AverageFrameRate) : 0);
        var position = comparisonActive && comparisonPanel?.CurrentFrameNumber is { } number ? number :
            Math.Max(1, (long)Math.Floor((slot.LastShownPosition ?? SlotPosition(slot)).TotalSeconds * video.AverageFrameRate) + 1);
        if (total > 0)
            position = Math.Min(position, total);
        return total > 0
            ? $"{position:N0} / {total:N0}{(video.TotalFrames is null ? "（全数推定）" : "（全数はメタデータ）")}"
            : $"{position:N0} / 不明";
    }

    private void UpdateInfo()
    {
        LeftDetails.Text = FormatVideoDetails(left.Video);
        RightDetails.Text = FormatVideoDetails(right.Video);
    }

    private static string FormatVideoDetails(VideoDescriptor? video) => video is null ? "" :
        $"{video.Codec} / {(video.AverageBitrate is { } bitrate ? $"{bitrate / 1000.0:N0} kbps" : "— kbps")} / {video.Width}×{video.Height} / {video.AverageFrameRate:F3} fps / 音声 {(video.HasAudio ? "あり" : "なし")}";

}
