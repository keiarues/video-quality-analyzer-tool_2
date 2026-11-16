using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class PartialEvaluationVersionTests
{
    [TestMethod]
    public void RawPartialResultCannotBeReusedAsFullBeforeItsSelectionIsAssigned()
    {
        var result = new QualityResult(QualityMetric.Ssim, 1, [new(30, 1)])
        { PartialVersion = QualityResult.CurrentPartialVersion, Method = QualityEvaluationMethod.PartialContinuous };
        Assert.IsFalse(result.IsReusableFor(EvaluationSelection.Full));
        var selection = new EvaluationSelection(PartialEvaluationMode.TimeRange, 1, 2);
        Assert.IsTrue((result with { Selection = selection }).IsReusableFor(selection));
    }

    [TestMethod]
    public void OldAndUnknownPartialResultsAreNeverRestored()
    {
        var store = new EvaluationResultStore();
        var selection = new EvaluationSelection(PartialEvaluationMode.TimeRange, 0, 1);
        var key = ("id", QualityMetric.Ssim);
        store.Switch(selection);
        var legacy = new QualityResult(QualityMetric.Ssim, 1, [new(0, 1)]) { Selection = selection };
        Assert.IsFalse(legacy.IsReusableFor(selection));
        store.Results[key] = legacy;
        store.Switch(EvaluationSelection.Full); store.Switch(selection);
        Assert.IsEmpty(store.Results);
        store.Results[key] = legacy with { PartialVersion = QualityResult.CurrentPartialVersion + 1, Method = QualityEvaluationMethod.PartialContinuous };
        store.Switch(selection);
        Assert.IsEmpty(store.Results);
        store.Results[key] = legacy with { PartialVersion = QualityResult.CurrentPartialVersion };
        store.Switch(selection);
        Assert.IsEmpty(store.Results);
    }

    [TestMethod]
    public void CurrentFallbackResultsAreReusableAndCsvRecordsTheMethod()
    {
        var store = new EvaluationResultStore();
        var selection = new EvaluationSelection(PartialEvaluationMode.TimeRange, 0, 1);
        var result = new QualityResult(QualityMetric.Ssim, 0.9, [new(0, 0.9)])
        { Selection = selection, PartialVersion = QualityResult.CurrentPartialVersion, Method = QualityEvaluationMethod.PartialSequential };
        var key = ("id", QualityMetric.Ssim);
        store.Switch(selection); store.Results[key] = result;
        store.Switch(EvaluationSelection.Full); store.Switch(selection);
        Assert.AreSame(result, store.Results[key]);
        var video = new VideoDescriptor("id", "video.mkv", 64, 64, "ffv1", 30, 30, TimeSpan.FromSeconds(1), false, 30, 1);
        StringAssert.Contains(QualityCsvExporter.Summary(video, [new(video, "")], store.Results), result.MethodDescription);
        StringAssert.Contains(QualityCsvExporter.Frames(video, [new(video, "")], store.Results), result.MethodDescription);
    }

    [TestMethod]
    public void FractionalFpsBoundarySkipsToNextIntervalAfterTickRounding()
    {
        const double fps = 30000d / 1001;
        for (var count = 1; count <= 300; count++)
        {
            var ranges = new EvaluationFrameRange[] { new(10, 10 + count), new(1000, 1005) };
            Assert.AreEqual(TimeSpan.FromSeconds(1000 / fps),
                PartialEvaluationProgress.ToSourceTime(ranges, fps, TimeSpan.FromSeconds(count / fps)));
        }
    }
}
