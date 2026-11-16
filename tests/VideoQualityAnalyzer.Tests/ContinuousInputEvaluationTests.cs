using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class ContinuousInputEvaluationTests
{
    private static VideoDescriptor Video() => new("id", "存在しない試験入力.mkv", 64, 64, "ffv1", 30, 30,
        TimeSpan.FromSeconds(3), false, 90, 1);

    [TestMethod]
    public async Task CanceledRequestDoesNotStartExternalProcesses()
    {
        var service = new ContinuousInputEvaluationService("存在しないffmpeg", "存在しないffprobe");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.CompareAsync(Video(), Video(), [new(0, 3)], QualityMetric.Vmaf, false, null, canceled.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.EvaluatePartialAsync(Video(), Video(), [new(0, 3)], QualityMetric.Vmaf, null, canceled.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new QualityEvaluationService("存在しないffmpeg").EvaluatePartialAsync(Video(), Video(), QualityMetric.Ssim, [new(0, 3)], null, canceled.Token));
    }

    [TestMethod]
    public async Task InvalidRequestsAreRejectedBeforeOpeningInputs()
    {
        var service = new ContinuousInputEvaluationService("存在しないffmpeg", "存在しないffprobe");
        var odd = Video() with { Width = 63 };
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            service.CompareAsync(odd, odd, [new(0, 3)], QualityMetric.Vmaf, false, null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.CompareAsync(Video(), Video(), [new(89, 91)], QualityMetric.Ssim, false, null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.CompareAsync(Video(), Video(), [new(0, 3)], (QualityMetric)99, false, null, CancellationToken.None));
    }
}
