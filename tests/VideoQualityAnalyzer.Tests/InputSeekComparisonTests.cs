using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class InputSeekComparisonTests
{
    private static InputSeekRun Run(QualityFrame[] values, EvaluationFrameDigest[] reference,
        EvaluationFrameDigest[] candidate) =>
        new(new(QualityMetric.Ssim, values.Average(frame => frame.Value), values), reference, candidate, TimeSpan.Zero);

    [TestMethod]
    public void IdenticalFramesAndValuesPass()
    {
        var hashes = new[] { new EvaluationFrameDigest(0, 4, "hash") };
        var run = Run([new(0, 0.5)], hashes, hashes);

        var comparison = InputSeekComparison.Create(run, run);

        Assert.IsTrue(comparison.Passed);
        Assert.AreEqual(0, comparison.ReferencePixelMismatches);
        Assert.AreEqual(0, comparison.CandidatePixelMismatches);
        Assert.AreEqual(0, comparison.ValueMismatches);
        Assert.AreEqual(0d, comparison.MaximumValueDifference);
    }

    [TestMethod]
    public void MissingFramesAndChangedHashesAreCounted()
    {
        var shared = new EvaluationFrameDigest(0, 4, "same");
        var expected = Run([new(0, 0.5), new(1, 0.8)], [shared, new(1, 4, "other")], [shared]);
        var actual = Run([new(0, 0.500002), new(2, 0.8)], [shared], [new(0, 4, "changed")]);

        var comparison = InputSeekComparison.Create(expected, actual);

        Assert.IsFalse(comparison.Passed);
        Assert.AreEqual(1, comparison.ReferencePixelMismatches);
        Assert.AreEqual(1, comparison.CandidatePixelMismatches);
        Assert.AreEqual(3, comparison.ValueMismatches);
        Assert.AreEqual(double.PositiveInfinity, comparison.MaximumValueDifference);
    }
}
