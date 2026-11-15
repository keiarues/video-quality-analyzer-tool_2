namespace VideoQualityAnalyzer.Media;

// FFmpegへ渡した区間の連結時刻を、元動画のフレーム位置に戻す。
public static class PartialEvaluationProgress
{
    public static TimeSpan ToSourceTime(IReadOnlyList<EvaluationFrameRange> ranges, double frameRate, TimeSpan processed)
    {
        if (ranges.Count == 0 || !double.IsFinite(frameRate) || frameRate <= 0)
            throw new ArgumentException("評価区間とフレームレートを確認してください。");
        var remaining = Math.Max(0, processed.TotalSeconds * frameRate);
        foreach (var range in ranges)
        {
            var count = range.End - range.Start;
            if (count <= 0) throw new ArgumentException("評価区間が不正です。");
            if (remaining < count)
                return TimeSpan.FromSeconds((range.Start + remaining) / frameRate);
            remaining -= count;
        }
        return TimeSpan.FromSeconds(ranges[^1].End / frameRate);
    }
}
