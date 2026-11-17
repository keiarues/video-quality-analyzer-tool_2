using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class PlaybackDriftTrackerTests
{
    [TestMethod]
    public void ResynchronizesOnlyAfterLargeDifferenceContinuesForOneSecond()
    {
        var tracker = new PlaybackDriftTracker();
        var start = DateTimeOffset.UnixEpoch;

        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(99), start));
        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(100), start));
        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(-150), start.AddMilliseconds(999)));
        Assert.IsTrue(tracker.ShouldResync(TimeSpan.FromMilliseconds(150), start.AddSeconds(1)));
        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(150), start.AddSeconds(1.1)));
    }

    [TestMethod]
    public void ResetAndSmallDifferenceDiscardThePreviousInterval()
    {
        var tracker = new PlaybackDriftTracker();
        var start = DateTimeOffset.UnixEpoch;

        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(150), start));
        tracker.Reset();
        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(150), start.AddSeconds(2)));
        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(50), start.AddSeconds(3)));
        Assert.IsFalse(tracker.ShouldResync(TimeSpan.FromMilliseconds(150), start.AddSeconds(4)));
        Assert.IsTrue(tracker.ShouldResync(TimeSpan.FromMilliseconds(150), start.AddSeconds(5)));
    }
}
