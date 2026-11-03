using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class VideoLibraryTests
{
    private static VideoEntry Entry(string name) => new(new VideoDescriptor(name, name, 64, 64,
        "ffv1", 30, 30, TimeSpan.FromSeconds(1), false, 30, 1));

    [TestMethod]
    public void ScoresSortNumericallyAndMissingValuesRemainLast()
    {
        var missing = Entry("missing");
        var small = Entry("small");
        small.SsimAverage = 2;
        var large = Entry("large");
        large.SsimAverage = 10;
        var ascending = VideoLibrary.Sort([missing, large, small], VideoSortOrder.Ssim);
        CollectionAssert.AreEqual(new[] { small, large, missing }, ascending);
        var descending = VideoLibrary.Sort([missing, small, large], VideoSortOrder.Ssim, true);
        CollectionAssert.AreEqual(new[] { large, small, missing }, descending);
    }

    [TestMethod]
    public void BitratesSortNumerically()
    {
        var small = new VideoEntry(Entry("small").Descriptor with { AverageBitrate = 2_000_000 });
        var large = new VideoEntry(Entry("large").Descriptor with { AverageBitrate = 10_000_000 });
        CollectionAssert.AreEqual(new[] { small, large }, VideoLibrary.Sort([large, small], VideoSortOrder.Bitrate));
        Assert.AreEqual(2d, small.AverageBitrateMbps);
    }

    [TestMethod]
    public void FrameRateDisplayMarksInferredVariableRate()
    {
        var constant = Entry("constant");
        var variable = new VideoEntry(constant.Descriptor with { AverageFrameRate = 24, NominalFrameRate = 30 });
        Assert.IsFalse(constant.Descriptor.IsVariableFrameRate);
        Assert.IsTrue(variable.Descriptor.IsVariableFrameRate);
        StringAssert.Contains(variable.FrameRateText, "可変");
    }

    [TestMethod]
    public void GroupChangesOnlyComparisonVideosAndCanBeCleared()
    {
        var reference = Entry("reference");
        reference.IsReference = true;
        var candidate = Entry("candidate");
        VideoLibrary.SetGroup([reference, candidate], " test ");
        Assert.AreEqual("", reference.Group);
        Assert.AreEqual("test", candidate.Group);
        VideoLibrary.SetGroup([candidate], "");
        Assert.AreEqual("", candidate.Group);
    }

    [TestMethod]
    public void SortingPreservesEntriesAndRestoresRegistrationOrder()
    {
        var first = Entry("b");
        var second = Entry("a");
        var sorted = VideoLibrary.Sort([first, second], VideoSortOrder.Name);
        Assert.AreSame(second, sorted[0]);
        var restored = VideoLibrary.Sort(sorted, VideoSortOrder.Registration);
        Assert.AreSame(first, restored[0]);
    }
}
