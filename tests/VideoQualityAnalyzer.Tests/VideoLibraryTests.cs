using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class VideoLibraryTests
{
    private static VideoEntry Entry(string name) => new(new VideoDescriptor(name, name, 64, 64,
        "ffv1", 30, 30, TimeSpan.FromSeconds(1), false, 30, 1));

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
