namespace VideoQualityAnalyzer.Core;

public static class FrameDropCriteria
{
    // 期待枚数に対する異なる提示フレームの欠落率を3%以下とする。
    public static bool MeetsThreePercent(long expectedFrames, long observedUniqueFrames)
    {
        if (expectedFrames <= 0) throw new ArgumentOutOfRangeException(nameof(expectedFrames));
        if (observedUniqueFrames < 0 || observedUniqueFrames > expectedFrames)
            throw new ArgumentOutOfRangeException(nameof(observedUniqueFrames));
        return (expectedFrames - observedUniqueFrames) * 100m <= expectedFrames * 3m;
    }
}
