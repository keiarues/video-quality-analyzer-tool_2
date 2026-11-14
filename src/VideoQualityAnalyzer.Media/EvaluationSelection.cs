using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

// 無関係な方式の値を含めず、設定画面から取得した評価条件を結果の識別に使う。
public sealed record EvaluationSelection(PartialEvaluationMode? Mode = null, double StartSeconds = 0,
    double EndSeconds = 0, double PeriodSeconds = 0, double LengthSeconds = 0, int Count = 0)
{
    public static EvaluationSelection Full { get; } = new();
    public bool IsPartial => Mode is not null;
    public IReadOnlyList<EvaluationFrameRange>? CreatePlan(VideoDescriptor reference) => Mode is { } mode
        ? PartialEvaluationPlan.Create(reference, mode, StartSeconds, EndSeconds, PeriodSeconds, LengthSeconds, Count) : null;
    public string Description => Mode switch
    {
        PartialEvaluationMode.TimeRange => FormattableString.Invariant($"時間範囲指定：{StartSeconds:G}～{EndSeconds:G}秒"),
        PartialEvaluationMode.Periodic => FormattableString.Invariant($"周期指定：{PeriodSeconds:G}秒ごとに{LengthSeconds:G}秒"),
        PartialEvaluationMode.Uniform => FormattableString.Invariant($"均等配置：{LengthSeconds:G}秒×{Count}区間"),
        _ => "全編評価"
    };
}
