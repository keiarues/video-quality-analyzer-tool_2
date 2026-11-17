using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
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
}
