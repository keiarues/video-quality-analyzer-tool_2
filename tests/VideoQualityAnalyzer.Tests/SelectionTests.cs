using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class SelectionTests
{
    private static readonly VideoDescriptor[] Videos = [Create("ref"), Create("a"), Create("b")];

    [TestMethod]
    public void EmptyVideosPreserveGeneration()
    {
        Assert.AreEqual(new PreviewSelection(null, null, 42), SelectionService.Resolve([], null, [], null, 42));
    }

    [TestMethod]
    [DataRow(null, "a")]
    [DataRow("b", "b")]
    [DataRow("missing", "a")]
    [DataRow("ref", "a")]
    public void NoSelectionKeepsValidPreviousRight(string? previous, string expected)
    {
        Assert.AreEqual(new PreviewSelection("ref", expected, 3), SelectionService.Resolve(Videos, "ref", [], previous, 3));
    }

    [TestMethod]
    public void SingleComparisonKeepsReferenceOnLeft()
    {
        Assert.AreEqual(new PreviewSelection("ref", "b", 4), SelectionService.Resolve(Videos, "ref", ["b"], "a", 4));
    }

    [TestMethod]
    public void MultipleSelectionsUseVideoOrder()
    {
        Assert.AreEqual(new PreviewSelection("a", "b", 5), SelectionService.Resolve(Videos, "ref", ["b", "a"], null, 5));
    }

    [TestMethod]
    public void MissingReferenceFallsBackToFirstVideo()
    {
        Assert.AreEqual(new PreviewSelection("ref", "a", 6), SelectionService.Resolve(Videos, "missing", ["missing"], null, 6));
    }

    [TestMethod]
    public void ReferenceOnlyHasNoRightVideo()
    {
        Assert.AreEqual(new PreviewSelection("ref", null, 7), SelectionService.Resolve([Videos[0]], "ref", ["ref"], null, 7));
    }

    [TestMethod]
    public void ValidPreviewOverridesChangeOnlyRequestedSides()
    {
        var resolved = SelectionService.Resolve(Videos, "ref", [], null, 8);
        Assert.AreEqual(new PreviewSelection("b", "a", 8),
            SelectionService.ApplyOverrides(resolved, Videos, "b", null));
        Assert.AreEqual(new PreviewSelection("ref", "b", 8),
            SelectionService.ApplyOverrides(resolved, Videos, null, "b"));
    }

    [TestMethod]
    public void MissingPreviewOverridesPreserveResolvedVideos()
    {
        var resolved = new PreviewSelection("ref", "a", 9);
        Assert.AreEqual(resolved, SelectionService.ApplyOverrides(resolved, Videos, "missing", "deleted"));
    }

    private static VideoDescriptor Create(string id) => new(id, $"{id}.mkv", 1440, 1080, "ffv1", 60, 60, TimeSpan.FromSeconds(10), false, 600, 1);
}
