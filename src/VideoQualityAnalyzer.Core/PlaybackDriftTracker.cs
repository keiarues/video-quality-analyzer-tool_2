namespace VideoQualityAnalyzer.Core;

// 位置の推定差が続いた場合だけ、右側の再同期を要求する。
public sealed class PlaybackDriftTracker
{
    private DateTimeOffset? started;

    public bool ShouldResync(TimeSpan difference, DateTimeOffset now)
    {
        if (difference.Duration() < TimeSpan.FromMilliseconds(100))
        {
            Reset();
            return false;
        }

        started ??= now;
        if (now - started < TimeSpan.FromSeconds(1)) return false;
        Reset();
        return true;
    }

    public void Reset() => started = null;
}
