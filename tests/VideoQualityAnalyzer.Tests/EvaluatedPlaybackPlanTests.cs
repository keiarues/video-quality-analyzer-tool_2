using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class EvaluatedPlaybackPlanTests
{
    [TestMethod]
    public void GapsAndExclusiveEndsAreNeverPlaybackTargets()
    {
        var plan = new EvaluatedPlaybackPlan([new(6, 12), new(30, 36), new(84, 90)]);
        Assert.IsTrue(plan.Contains(6)); Assert.IsTrue(plan.Contains(89));
        Assert.IsFalse(plan.Contains(12)); Assert.IsFalse(plan.Contains(90));
        Assert.AreEqual(6L, plan.ForwardOrLast(0));
        Assert.AreEqual(30L, plan.ForwardOrLast(12));
        Assert.AreEqual(89L, plan.ForwardOrLast(100));
        Assert.AreEqual(11L, plan.BackwardOrFirst(29));
        Assert.AreEqual(6L, plan.BackwardOrFirst(-1));
    }

    [TestMethod]
    public void FrameStepsCrossGapsInBothDirectionsAndClampAtEnds()
    {
        var plan = new EvaluatedPlaybackPlan([new(6, 12), new(30, 36)]);
        Assert.AreEqual(30L, plan.Move(11, 1));
        Assert.AreEqual(11L, plan.Move(30, -1));
        Assert.AreEqual(6L, plan.Move(6, -1));
        Assert.AreEqual(35L, plan.Move(35, 1));
    }

    [TestMethod]
    public void InvalidRangesAreRejectedAndCallerChangesCannotAlterThePlan()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EvaluatedPlaybackPlan([]));
        Assert.ThrowsExactly<ArgumentException>(() => new EvaluatedPlaybackPlan([new(2, 2)]));
        Assert.ThrowsExactly<ArgumentException>(() => new EvaluatedPlaybackPlan([new(2, 10), new(9, 12)]));
        EvaluationFrameRange[] ranges = [new(6, 12)];
        var plan = new EvaluatedPlaybackPlan(ranges); ranges[0] = new(0, 90);
        Assert.AreEqual(6L, plan.FirstFrame); Assert.AreEqual(11L, plan.LastFrame);
    }
}
