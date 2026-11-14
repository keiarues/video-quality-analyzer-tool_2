using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class PartialEvaluationTests
{
    private static VideoDescriptor Video(double duration = 125) => new("id", "test.mkv", 64, 64, "ffv1", 30, 30,
        TimeSpan.FromSeconds(duration), false, (long)(duration * 30), 1);

    [TestMethod]
    public void TimeRangeUsesOriginalFramesAndExclusiveEnd()
    {
        var ranges = PartialEvaluationPlan.Create(Video(), PartialEvaluationMode.TimeRange, 1.01, 2, 0, 0, 0);
        CollectionAssert.AreEqual(new[] { new EvaluationFrameRange(31, 60) }, ranges.ToArray());
    }

    [TestMethod]
    public void PeriodicClipsLastInterval()
    {
        var ranges = PartialEvaluationPlan.Create(Video(), PartialEvaluationMode.Periodic, 0, 0, 60, 10, 0);
        CollectionAssert.AreEqual(new[] { new EvaluationFrameRange(0, 300), new(1800, 2100), new(3600, 3750) }, ranges.ToArray());
    }

    [TestMethod]
    public void UniformIncludesBothEndsAndSingleIntervalIsCentered()
    {
        var ranges = PartialEvaluationPlan.Create(Video(100), PartialEvaluationMode.Uniform, 0, 0, 0, 10, 3);
        CollectionAssert.AreEqual(new[] { new EvaluationFrameRange(0, 300), new(1350, 1650), new(2700, 3000) }, ranges.ToArray());
        Assert.AreEqual(new EvaluationFrameRange(1350, 1650),
            PartialEvaluationPlan.Create(Video(100), PartialEvaluationMode.Uniform, 0, 0, 0, 10, 1).Single());
    }

    [TestMethod]
    public void InvalidAndOverlappingSettingsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PartialEvaluationPlan.Create(Video(), PartialEvaluationMode.TimeRange, 10, 5, 0, 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => PartialEvaluationPlan.Create(Video(), PartialEvaluationMode.Periodic, 0, 0, 5, 10, 0));
        Assert.ThrowsExactly<ArgumentException>(() => PartialEvaluationPlan.Create(Video(), PartialEvaluationMode.Uniform, 0, 0, 0, 30, 6));
        Assert.ThrowsExactly<ArgumentException>(() => PartialEvaluationPlan.Create(Video(), PartialEvaluationMode.Periodic, 0, 0, double.NaN, 10, 0));
        Assert.ThrowsExactly<ArgumentException>(() => PartialEvaluationPlan.Create(Video() with { NominalFrameRate = 60 }, PartialEvaluationMode.TimeRange, 0, 1, 0, 0, 0));
    }
}
