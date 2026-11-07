using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class VideoDropFilesTests
{
    [TestMethod]
    public void FolderDropIncludesNestedVideosAndRemovesDuplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "子"));
        try
        {
            var video = Path.Combine(root, "動画.MP4");
            var nested = Path.Combine(root, "子", "動画.mkv");
            File.WriteAllText(video, "");
            File.WriteAllText(nested, "");
            File.WriteAllText(Path.Combine(root, "説明.txt"), "");
            var result = VideoDropFiles.Expand([root, video]);
            Assert.HasCount(2, result);
            CollectionAssert.Contains(result, video);
            CollectionAssert.Contains(result, nested);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void HundredVideosAskOnceAndCancellationReturnsNoFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < 101; index++)
                File.WriteAllText(Path.Combine(root, $"{index:D3}.mp4"), "");
            var calls = 0;
            Assert.HasCount(0, VideoDropFiles.Expand([root], () => { calls++; return false; }));
            Assert.AreEqual(1, calls);
            calls = 0;
            Assert.HasCount(101, VideoDropFiles.Expand([root, root], () => { calls++; return true; }));
            Assert.AreEqual(1, calls);
            File.Delete(Path.Combine(root, "100.mp4"));
            Assert.HasCount(100, VideoDropFiles.Expand([root], () => false) is { Length: 0 }
                ? VideoDropFiles.Expand([root], () => true) : []);
            File.Delete(Path.Combine(root, "099.mp4"));
            Assert.HasCount(99, VideoDropFiles.Expand([root], () => throw new InvalidOperationException()));
        }
        finally { Directory.Delete(root, true); }
    }}
