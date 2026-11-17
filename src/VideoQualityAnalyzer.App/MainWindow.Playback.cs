using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private void SetSlot(SlotState slot, VideoDescriptor? video)
    {
        if (slot.Video?.Id == video?.Id)
            return;

        slot.Av1.Close();
        slot.Media.Close();
        slot.Media.Source = null;
        slot.Video = video;
        slot.LastShownPosition = null;
        slot.Ready = false;
        slot.FpsSampleStamp = 0;
        slot.PlaybackFps = null;
        slot.OpenStarted = Stopwatch.GetTimestamp();
        slot.Name.Text = video is null ? (slot.Side == "left" ? "左: 未選択" : "右: 未選択") :
            (slot.Side == "left" ? "左: " : "右: ") + video.Name;
        if (video is null)
            return;
        if (!File.Exists(video.Path))
        {
            log.Record("open_failed", slot.Side, video.Id, "missing");
            return;
        }

        log.Record("open_requested", slot.Side, video.Id, "started");
        if (!comparisonActive) slot.Av1.Open(video, CurrentPosition(), isPlaying);
        // 左音声は既存のMediaElementで再生する。映像は画像側だけを表示する。
        if (slot.Side == "left" && video.HasAudio && video.Codec != "av1")
        {
            slot.Media.Source = new Uri(video.Path, UriKind.Absolute);
            slot.Media.Play();
        }
    }

    private void LeftMedia_Opened(object sender, RoutedEventArgs e) => MediaOpened(left);
    private void RightMedia_Opened(object sender, RoutedEventArgs e) => MediaOpened(right);

    private void MediaOpened(SlotState slot)
    {
        if (slot.Video is not { } video || slot.Media.Source is null ||
            !string.Equals(slot.Media.Source.LocalPath, video.Path, StringComparison.OrdinalIgnoreCase))
            return;

        // 映像の準備完了はFFmpeg側の最初のフレーム提示で判定する。
        slot.FpsSampleStamp = 0;
        slot.PlaybackFps = null;
        slot.Media.Position = ClampToVideo(CurrentPosition(), video);
        if (isPlaying)
            slot.Media.Play();
        else
            slot.Media.Pause();
        if (slot.Side == "right")
            slot.Media.IsMuted = true;
        var elapsed = Stopwatch.GetElapsedTime(slot.OpenStarted).TotalMilliseconds;
        log.Record("media_opened", slot.Side, video.Id, "ready_not_presented", elapsed,
            detail: "decode_mode=unknown;first_presented_at=unmeasured");
    }

    private void LeftMedia_Failed(object sender, ExceptionRoutedEventArgs e) => MediaFailed(left, e);
    private void RightMedia_Failed(object sender, ExceptionRoutedEventArgs e) => MediaFailed(right, e);

    private void MediaFailed(SlotState slot, ExceptionRoutedEventArgs e)
    {
        if (slot.Av1.IsOpen) return;
        slot.Ready = false;
        slot.PlaybackFps = null;
        log.Record("media_failed", slot.Side, slot.Video?.Id, "failed",
            detail: e.ErrorException?.GetType().Name ?? "unknown");
    }

    private void Media_Ended(object sender, RoutedEventArgs e)
    {
        log.Record("media_ended", ReferenceEquals(sender, LeftMedia) ? "left" : "right", null, "ended");
        StopPlayback();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (isPlaying) PausePlayback();
        else Play_Click(sender, e);
    }

    private void UpdatePlayPauseButton()
    {
        if (!initialized) return;
        PlayPauseButton.Content = isPlaying ? "⏸" : "▶";
        PlayPauseButton.ToolTip = isPlaying ? "一時停止" : "再生";
        System.Windows.Automation.AutomationProperties.SetName(PlayPauseButton, isPlaying ? "一時停止" : "再生");
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (left.Video is null)
            return;
        if (isPlaying)
            return;
        RefreshEvaluatedPlayback();
        if (evaluatedPlaybackEnded && evaluatedPlaybackPlan is { } plan)
            ApplySeek(TimeSpan.FromSeconds(plan.FirstFrame / left.Video.AverageFrameRate));
        if (comparisonActive && comparisonPanel is not null)
        {
            isPlaying = true;
            UpdatePlayPauseButton();
            if (evaluatedPlaybackPlan is not null && !comparisonPanel.IsReady) evaluatedAudioPending = true;
            comparisonPanel.Play();
            if (left.Media.Source is not null) { left.Media.Position = CurrentPosition(); if (!evaluatedAudioPending) left.Media.Play(); }
            return;
        }
        basePosition = ClampToCommon(basePosition);
        if (right.Video is not null)
        {
            right.Av1.Play(basePosition);
        }
        if (left.Video is not null)
        {
            left.Av1.Play(basePosition);
            if (left.Media.Source is not null)
            {
                left.Media.Position = ClampToVideo(basePosition, left.Video);
                left.Media.Play();
            }
        }
        playbackClock.Restart();
        isPlaying = true;
        UpdatePlayPauseButton();
        ResetFpsSamples();
        log.Record("play", "both", left.Video?.Id, "requested", detail: "presented_fps=unmeasured");
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => PausePlayback();

    private void PausePlayback()
    {
        if (!isPlaying)
            return;
        basePosition = ClampToCommon(CurrentPosition());
        comparisonPanel?.Pause();
        playbackClock.Stop();
        isPlaying = false;
        UpdatePlayPauseButton();
        ResetFpsSamples();
        left.Media.Pause();
        right.Media.Pause();
        left.Av1.Pause();
        right.Av1.Pause();
        log.Record("pause", "both", left.Video?.Id, "requested");
        UpdateTimeDisplay();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        playbackClock.Reset();
        isPlaying = false;
        UpdatePlayPauseButton();
        ResetFpsSamples();
        left.LastShownPosition = right.LastShownPosition = null;
        evaluatedPlaybackEnded = evaluatedAudioPending = false;
        basePosition = evaluatedPlaybackPlan is { } plan && left.Video is { } video ? TimeSpan.FromSeconds(plan.FirstFrame / video.AverageFrameRate) : TimeSpan.Zero;
        if (comparisonActive && comparisonPanel is not null) comparisonChangeTask = comparisonPanel.SeekAsync(basePosition, false);
        if (left.Av1.IsOpen) left.Av1.Seek(basePosition);
        if (left.Media.Source is not null) left.Media.Position = basePosition;
        if (right.Av1.IsOpen) right.Av1.Seek(basePosition);
        left.Media.Pause();
        right.Media.Pause();
        left.Av1.Pause();
        right.Av1.Pause();
        log.Record("stop", "both", left.Video?.Id, "requested");
        UpdateTimeDisplay();
    }

    private void BackFive_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() - TimeSpan.FromSeconds(5), -1);
    private void BackOne_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() - TimeSpan.FromSeconds(1), -1);
    private void ForwardOne_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() + TimeSpan.FromSeconds(1));
    private void ForwardFive_Click(object sender, RoutedEventArgs e) => ApplySeek(CurrentPosition() + TimeSpan.FromSeconds(5));

    private void ApplySeek(TimeSpan position, int direction = 0)
    {
        if (left.Video is null)
            return;
        left.LastShownPosition = right.LastShownPosition = null;
        evaluatedPlaybackEnded = false;
        basePosition = ClampToCommon(NormalizeEvaluatedSeek(position, direction));
        if (isPlaying && evaluatedPlaybackPlan is not null) BeginEvaluatedAudioTransition(basePosition);
        if (comparisonActive && comparisonPanel is not null) comparisonChangeTask = comparisonPanel.SeekAsync(basePosition, isPlaying);
        ResetFpsSamples();
        if (isPlaying)
            playbackClock.Restart();
        if (left.Av1.IsOpen) left.Av1.Seek(basePosition);
        if (left.Media.Source is not null) left.Media.Position = ClampToVideo(basePosition, left.Video);
        if (right.Av1.IsOpen) right.Av1.Seek(basePosition);
        log.Record("seek_requested", "both", left.Video.Id, "position_set_not_presented",
            detail: $"target_ms={basePosition.TotalMilliseconds:F0};presented_at=unmeasured");
        SetWindowStatus("シークを要求しました。実際の提示完了時刻は未測定です。");
        UpdateTimeDisplay();
    }

    private async void FrameStep_Click(object sender, RoutedEventArgs e) => await MoveFrameAsync(1);
    private async void FrameBack_Click(object sender, RoutedEventArgs e) => await MoveFrameAsync(-1);

    private async Task MoveFrameAsync(int direction)
    {
        PausePlayback();
        if (left.Video is not { } video)
            return;
        if (comparisonActive)
        {
            ApplySeek(evaluatedPlaybackPlan is { } plan
                ? TimeSpan.FromSeconds(plan.Move((long)Math.Round(CurrentPosition().TotalSeconds * video.AverageFrameRate), direction) / video.AverageFrameRate)
                : CurrentPosition() + TimeSpan.FromSeconds(direction / video.AverageFrameRate));
            return;
        }
        var token = frameTimesCancellation.Token;
        IReadOnlyList<TimeSpan>? times = null;
        if (probe is not null)
        {
            SetWindowStatus("フレーム時刻を取得中...");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                times = await probe.ReadFrameTimesAsync(video.Path, CurrentPosition(), timeout.Token);
                if (token.IsCancellationRequested || left.Video?.Id != video.Id)
                    return;
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                    return;
                SetWindowStatus("フレーム時刻の取得が時間切れになりました。");
            }
            catch (Exception exception)
            {
                SetWindowStatus("フレーム時刻を取得できません: " + exception.GetType().Name);
            }
        }

        var current = CurrentPosition();
        var next = direction > 0 ? times?.FirstOrDefault(time => time > current + TimeSpan.FromMilliseconds(1))
            : times?.Where(time => time < current - TimeSpan.FromMilliseconds(1)).Select(time => (TimeSpan?)time).LastOrDefault();
        if (next is not null && (direction > 0 ? next.Value > current : next.Value < current))
        {
            ApplySeek(next.Value);
            SetWindowStatus(direction > 0 ? "次のソースPTSへ移動しました。" : "前のソースPTSへ移動しました。");
        }
        else if (video.AverageFrameRate > 0)
        {
            ApplySeek(current + TimeSpan.FromSeconds(direction / video.AverageFrameRate));
            SetWindowStatus("平均fpsから1コマ分を近似しました。");
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initialized)
            LeftMedia.Volume = VolumeSlider.Value;
    }

    private void MuteCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (initialized)
            LeftMedia.IsMuted = MuteCheck.IsChecked == true;
        RightMedia.IsMuted = true;
    }

    private sealed class SlotState(string side, MediaElement media, Image av1Image, TextBlock name)
    {
        public string Side { get; } = side;
        public MediaElement Media { get; } = media;
        public Av1PreviewPlayer Av1 { get; } = new(av1Image);
        public TextBlock Name { get; } = name;
        public VideoDescriptor? Video { get; set; }
        public bool Ready { get; set; }
        public TimeSpan? LastShownPosition { get; set; }
        public long OpenStarted { get; set; }
        public long FpsSampleStamp { get; set; }
        public TimeSpan FpsSamplePosition { get; set; }
        public double? PlaybackFps { get; set; }
    }
}
