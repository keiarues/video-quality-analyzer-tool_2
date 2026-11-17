using System.Windows;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private EvaluatedPlaybackPlan? evaluatedPlaybackPlan;
    private bool updatingEvaluatedPlayback;
    private bool evaluatedPlaybackEnded;
    private bool evaluatedAudioPending;

    private void EvaluatedOnlyCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized || updatingEvaluatedPlayback) return;
        RefreshEvaluatedPlayback();
    }

    private void RefreshEvaluatedPlayback(bool seekOnChange = true)
    {
        if (!initialized) return;
        var position = CurrentPosition();
        EvaluatedPlaybackPlan? available = null;
        var reference = videos.FirstOrDefault(entry => entry.IsReference)?.Descriptor;
        var selection = evaluationResults.Selection;
        if (comparisonActive && reference is not null && selection.IsPartial && left.Video is { } leftVideo && right.Video is { } rightVideo)
        {
            var candidates = new[] { leftVideo, rightVideo }.Where(video => video.Id != reference.Id).ToArray();
            if (candidates.Length > 0 && candidates.All(video => qualityResults.Any(pair => pair.Key.VideoId == video.Id && pair.Value.IsReusableFor(selection))))
            {
                var common = Math.Min(EvaluationSelection.FrameCount(leftVideo), EvaluationSelection.FrameCount(rightVideo));
                var ranges = selection.CreatePlan(reference)!.Select(range => new EvaluationFrameRange(range.Start, Math.Min(range.End, common)))
                    .Where(range => range.End > range.Start).ToArray();
                if (ranges.Length > 0) available = new(ranges);
            }
        }
        updatingEvaluatedPlayback = true;
        try
        {
            EvaluatedOnlyCheck.IsEnabled = available is not null;
            EvaluatedOnlyCheck.ToolTip = available is null ? "部分評価を完了し、評価済みの比較動画をプレビューに選択してください。"
                : "現在のプレビュー動画で計算が完了した評価区間だけを再生します。未評価区間はスキップします。";
            if (available is null && EvaluatedOnlyCheck.IsChecked == true)
            {
                PausePlayback();
                EvaluatedOnlyCheck.IsChecked = false;
                SetWindowStatus("評価済みの部分評価結果がないため、区間再生を解除して停止しました。");
            }
        }
        finally { updatingEvaluatedPlayback = false; }
        var desired = EvaluatedOnlyCheck.IsChecked == true ? available : null;
        var changed = evaluatedPlaybackPlan is null ? desired is not null : desired is null || !evaluatedPlaybackPlan.Ranges.SequenceEqual(desired.Ranges);
        evaluatedPlaybackPlan = desired;
        comparisonPanel?.SetPlaybackRanges(desired?.Ranges);
        StopButton.ToolTip = desired is null ? "停止（先頭に戻る）" : "停止（評価区間の先頭に戻る）";
        if (!changed) return;
        evaluatedPlaybackEnded = false;
        if (seekOnChange && comparisonActive)
        {
            if (isPlaying) BeginEvaluatedAudioTransition(position);
            ApplySeek(position);
        }
    }

    private TimeSpan NormalizeEvaluatedSeek(TimeSpan position, int direction = 0)
    {
        if (evaluatedPlaybackPlan is null || left.Video is not { } video) return position;
        var frame = (long)Math.Round(position.TotalSeconds * video.AverageFrameRate);
        frame = direction < 0 ? evaluatedPlaybackPlan.BackwardOrFirst(frame) : evaluatedPlaybackPlan.ForwardOrLast(frame);
        return TimeSpan.FromSeconds(frame / video.AverageFrameRate);
    }

    private void BeginEvaluatedAudioTransition(TimeSpan target)
    {
        evaluatedAudioPending = true;
        LeftMedia.Pause();
        if (LeftMedia.Source is not null) LeftMedia.Position = target;
    }

    private void OnEvaluatedPlaybackFrameShown()
    {
        if (!evaluatedAudioPending || !isPlaying) return;
        evaluatedAudioPending = false;
        if (LeftMedia.Source is not null) { LeftMedia.Position = comparisonPanel!.Position; LeftMedia.Play(); }
    }
}
