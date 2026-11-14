using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

// 無関係な方式の値を含めず、設定画面から取得した評価条件を結果の識別に使う。
public sealed record EvaluationSelection(PartialEvaluationMode? Mode = null, double StartSeconds = 0,
    double EndSeconds = 0, double PeriodSeconds = 0, double LengthSeconds = 0, int Count = 0)
{
    public static EvaluationSelection Full { get; } = new();
    public long? FrameLimit { get; init; }
    public bool IsPartial => Mode is not null;
    public static long FrameCount(VideoDescriptor video)
    {
        if (video.TotalFrames is > 0) return video.TotalFrames.Value;
        if (!double.IsFinite(video.AverageFrameRate) || video.AverageFrameRate <= 0 || video.Duration <= TimeSpan.Zero)
            throw new ArgumentException("動画の長さとフレームレートを確認してください。");
        return (long)Math.Ceiling(video.Duration.TotalSeconds * video.AverageFrameRate - 1e-7);
    }

    public EvaluationSelection WithComparisonTimeline(VideoDescriptor reference, IEnumerable<VideoDescriptor> candidates)
    {
        if (!IsPartial) return Full;
        var referenceFrames = FrameCount(reference);
        var commonFrames = candidates.Select(FrameCount).Append(referenceFrames).Min();
        return this with { FrameLimit = commonFrames < referenceFrames ? commonFrames : null };
    }

    public IReadOnlyList<EvaluationFrameRange>? CreatePlan(VideoDescriptor reference) => Mode is { } mode
        ? PartialEvaluationPlan.Create(FrameLimit is { } limit ? reference with
        {
            TotalFrames = limit,
            Duration = TimeSpan.FromSeconds(Math.Min(reference.Duration.TotalSeconds, limit / reference.AverageFrameRate))
        } : reference, mode, StartSeconds, EndSeconds, PeriodSeconds, LengthSeconds, Count) : null;
    public string Description => (Mode switch
    {
        PartialEvaluationMode.TimeRange => FormattableString.Invariant($"時間範囲指定：{StartSeconds:G}～{EndSeconds:G}秒"),
        PartialEvaluationMode.Periodic => FormattableString.Invariant($"周期指定：{PeriodSeconds:G}秒ごとに{LengthSeconds:G}秒"),
        PartialEvaluationMode.Uniform => FormattableString.Invariant($"均等配置：{LengthSeconds:G}秒×{Count}区間"),
        _ => "全編評価"
    }) + (FrameLimit is { } limit ? $"（共通範囲：{limit}フレーム）" : "");
}
