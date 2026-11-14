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

    [TestMethod]
    public void ConditionsKeepIndependentResultsAndReuseTheSameInstances()
    {
        var store = new EvaluationResultStore();
        var dictionary = store.Results;
        var key = ("id", QualityMetric.Ssim);
        var full = new QualityResult(QualityMetric.Ssim, 1, [new(0, 1)]);
        store.Results[key] = full;
        var selection = new EvaluationSelection(PartialEvaluationMode.TimeRange, 5, 60);
        store.Switch(selection);
        Assert.IsEmpty(store.Results);
        var partial = full with { Selection = selection };
        store.Results[key] = partial;
        store.Switch(EvaluationSelection.Full);
        Assert.AreSame(dictionary, store.Results);
        Assert.AreSame(full, store.Results[key]);
        store.Switch(selection);
        Assert.AreSame(partial, store.Results[key]);
        store.Switch(selection with { EndSeconds = 61 });
        Assert.IsEmpty(store.Results);
    }

    [TestMethod]
    public void RemovingVideoAndClearingAlsoInvalidateInactiveConditions()
    {
        var store = new EvaluationResultStore();
        var key = ("id", QualityMetric.Ssim);
        store.Results[key] = new(QualityMetric.Ssim, 1, [new(0, 1)]);
        var partial = new EvaluationSelection(PartialEvaluationMode.Periodic, PeriodSeconds: 60, LengthSeconds: 10);
        store.Switch(partial);
        store.Results[key] = new(QualityMetric.Ssim, 1, [new(0, 1)]);
        store.RemoveVideo("id");
        store.Switch(EvaluationSelection.Full);
        Assert.IsEmpty(store.Results);
        store.Results[key] = new(QualityMetric.Ssim, 1, [new(0, 1)]);
        store.Switch(partial);
        Assert.IsEmpty(store.Results);
        store.Clear();
        store.Switch(EvaluationSelection.Full);
        Assert.IsEmpty(store.Results);
    }

    [TestMethod]
    public void PartialCsvRecordsConditionAndOriginalFrameNumbers()
    {
        var video = Video();
        var selection = new EvaluationSelection(PartialEvaluationMode.TimeRange, 5, 60);
        var result = new QualityResult(QualityMetric.Ssim, 0.9, [new(150, 0.9)]) { Selection = selection };
        var results = new Dictionary<(string, QualityMetric), QualityResult> { [(video.Id, QualityMetric.Ssim)] = result };
        var summary = QualityCsvExporter.Summary(video, [new(video, "")], results);
        var frames = QualityCsvExporter.Frames(video, [new(video, "")], results);
        StringAssert.Contains(summary, "SSIM評価条件,SSIM集計フレーム数");
        StringAssert.Contains(summary, selection.Description + "（評価対象のみの平均）");
        StringAssert.Contains(frames, "評価条件");
        StringAssert.Contains(frames, "SSIM,150,5,");
        StringAssert.Contains(frames, selection.Description);
    }
}
