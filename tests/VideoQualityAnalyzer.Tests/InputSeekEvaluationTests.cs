using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class InputSeekEvaluationTests
{
    private static VideoDescriptor Video() => new("id", "test.mkv", 64, 64, "ffv1", 30, 30,
        TimeSpan.FromSeconds(3), false, 90, 1);

    [TestMethod]
    public void HashesMapBackToOriginalFrameNumbers()
    {
        var hash = new string('A', 64);
        var frames = InputSeekEvaluationService.ParseFrameHashes($"#format\n0,0,0,1,6144,{hash}\n0,1,1,1,6144,{hash}\n", [29, 87]);
        Assert.AreEqual(29L, frames[0].Number);
        Assert.AreEqual(87L, frames[1].Number);
        Assert.AreEqual(hash.ToLowerInvariant(), frames[1].Sha256);
        Assert.ThrowsExactly<InvalidDataException>(() => InputSeekEvaluationService.ParseFrameHashes("", [0]));
        Assert.ThrowsExactly<InvalidDataException>(() => InputSeekEvaluationService.ParseFrameHashes("0,0,0,1,1,invalid", [0]));
    }

    [TestMethod]
    public void InvalidTimelineAndUnsupportedMetricAreRejected()
    {
        var video = Video();
        InputSeekEvaluationService.Validate(video, video, [new(0, 3), new(87, 90)], QualityMetric.Ssim);
        Assert.ThrowsExactly<ArgumentException>(() => InputSeekEvaluationService.Validate(video, video, [new(89, 91)], QualityMetric.Ssim));
        Assert.ThrowsExactly<ArgumentException>(() => InputSeekEvaluationService.Validate(video, video, [new(29, 32), new(30, 33)], QualityMetric.Ssim));
        Assert.ThrowsExactly<ArgumentException>(() => InputSeekEvaluationService.Validate(video, video with { AverageFrameRate = double.NaN }, [new(0, 3)], QualityMetric.Ssim));
        Assert.ThrowsExactly<NotSupportedException>(() => InputSeekEvaluationService.Validate(video, video, [new(0, 3)], QualityMetric.Vmaf));
    }
}
