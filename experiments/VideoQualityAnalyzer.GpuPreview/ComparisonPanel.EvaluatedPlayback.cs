using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonPanel
{
    private EvaluatedPlaybackPlan? evaluatedPlaybackPlan;
    public event Action<TimeSpan>? PlaybackSegmentStarting;

    // 設定変更で旧世代の再生・静止読込を止める。次のSeek/SetSourcesで新しい区間を適用する。
    public bool SetPlaybackRanges(IReadOnlyList<EvaluationFrameRange>? ranges)
    {
        if (ranges is null && evaluatedPlaybackPlan is null || ranges is not null && evaluatedPlaybackPlan is not null && ranges.SequenceEqual(evaluatedPlaybackPlan.Ranges)) return false;
        StopPlayback(keepRequestedFrame: true);
        loadCancellation?.Cancel();
        ++selectionGeneration;
        evaluatedPlaybackPlan = ranges is null ? null : new EvaluatedPlaybackPlan(ranges);
        return true;
    }
}
