using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class FrameDropCriteriaTests
{
    [TestMethod]
    [DataRow(900L, 900L, true)]
    [DataRow(900L, 873L, true)]
    [DataRow(900L, 872L, false)]
    [DataRow(899L, 873L, true)]
    [DataRow(899L, 872L, false)]
    public void ThreePercentBoundaryUsesExpectedFrameCount(long expected, long observed, bool accepted)
    {
        Assert.AreEqual(accepted, FrameDropCriteria.MeetsThreePercent(expected, observed));
    }
}
