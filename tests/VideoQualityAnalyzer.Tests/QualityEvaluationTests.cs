using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class QualityEvaluationTests
{
    [TestMethod]
    public void CsvEscapesNamesAndLeavesUnevaluatedMetricsBlank()
    {
        var video = new VideoQualityAnalyzer.Core.VideoDescriptor("id", "a,b.mkv", 64, 64,
            "ffv1", 30, 30, TimeSpan.FromSeconds(1), false, 30, 1);
        var results = new Dictionary<(string, QualityMetric), QualityResult>
        {
            [("id", QualityMetric.Ssim)] = new(QualityMetric.Ssim, 0.5, [new QualityFrame(3, 0.5)])
        };
        var item = new QualityCsvItem(video, "検証");
        StringAssert.Contains(QualityCsvExporter.Summary(video, [item], results), "\"a,b.mkv\"");
        StringAssert.Contains(QualityCsvExporter.Summary(video, [item], results), ",,0.5,");
        StringAssert.Contains(QualityCsvExporter.Frames(video, [item], results), "SSIM,3,0.10000000000000001,0.5");
    }

    [TestMethod]
    public void SsimUsesAllAndZeroBasedFrameNumbers()
    {
        var result = QualityEvaluationService.ParseStats(QualityMetric.Ssim,
            "n:1 Y:0.1 All:0.9 (10)\nn:2 Y:0.2 All:0.7 (5)");
        Assert.AreEqual(0.8, result.Average, 0.000001);
        Assert.AreEqual(0L, result.Frames[0].Number);
    }

    [TestMethod]
    public void PsnrExcludesInfinity()
    {
        var result = QualityEvaluationService.ParseStats(QualityMetric.Psnr,
            "n:1 psnr_avg:inf\nn:2 psnr_avg:30.0\nn:3 psnr_avg:40.0");
        Assert.AreEqual(35, result.Average);
        Assert.HasCount(2, result.Frames);
    }

    [TestMethod]
    public void EmptyStatisticsAreNotCompletedResults()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            QualityEvaluationService.ParseStats(QualityMetric.Psnr, "n:1 psnr_avg:inf"));
    }

    [TestMethod]
    public void VmafUsesPooledMean()
    {
        var result = QualityEvaluationService.ParseVmaf("""
            {"frames":[{"frameNum":0,"metrics":{"vmaf":80}},{"frameNum":1,"metrics":{"vmaf":90}}],
             "pooled_metrics":{"vmaf":{"mean":84.5}}}
            """);
        Assert.AreEqual(84.5, result.Average);
        Assert.HasCount(2, result.Frames);
    }
}
