using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class PartialEvaluationProgressTests
{
    [TestMethod]
    public void TimeRangeStartsAtOriginalTimeAndClampsToEnd()
    {
        EvaluationFrameRange[] ranges = [new(150, 1800)];
        Assert.AreEqual(5d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.Zero).TotalSeconds);
        Assert.AreEqual(15d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(10)).TotalSeconds);
        Assert.AreEqual(60d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(90)).TotalSeconds);
        Assert.AreEqual(5d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(-1)).TotalSeconds);
    }

    [TestMethod]
    public void PeriodicBoundarySkipsUnevaluatedTimeAndUsesClippedLastInterval()
    {
        EvaluationFrameRange[] ranges = [new(0, 300), new(1800, 2100), new(3600, 3750)];
        Assert.AreEqual(60d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(10)).TotalSeconds);
        Assert.AreEqual(65d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(15)).TotalSeconds);
        Assert.AreEqual(120d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(20)).TotalSeconds);
        Assert.AreEqual(125d, PartialEvaluationProgress.ToSourceTime(ranges, 30, TimeSpan.FromSeconds(25)).TotalSeconds);
    }

    [TestMethod]
    public void UniformPlacementUsesActualRangesAtFractionalFrameRate()
    {
        EvaluationFrameRange[] ranges = [new(0, 300), new(1500, 1800), new(3000, 3300)];
        const double fps = 30000d / 1001;
        var source = PartialEvaluationProgress.ToSourceTime(ranges, fps, TimeSpan.FromSeconds(15));
        Assert.AreEqual((1500 + 15 * fps - 300) / fps, source.TotalSeconds, 0.000001);
        Assert.AreEqual(3300 / fps, PartialEvaluationProgress.ToSourceTime(ranges, fps, TimeSpan.FromHours(1)).TotalSeconds, 0.000001);
    }
}
