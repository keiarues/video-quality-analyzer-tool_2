using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class SsimTestCacheTests
{
    [TestMethod]
    public async Task SavedFramesSurviveReloadAndChangedVideoIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var videoPath = Path.Combine(directory, "video.mkv");
            await File.WriteAllTextAsync(videoPath, "test");
            var video = new VideoDescriptor("test", videoPath, 64, 64, "test", 30, 30, TimeSpan.FromSeconds(1), false, 30, 1);
            var stamp = TestVideoStamp.Read(videoPath);
            var result = new QualityResult(QualityMetric.Ssim, .95, [new(0, .9), new(1, 1)]);
            var cache = new SsimTestCache(1, video, stamp, [new(video, stamp, result)]);
            var path = Path.Combine(directory, "ssim.json");
            await cache.SaveAsync(path);
            var loaded = await SsimTestCache.LoadAsync(path);
            Assert.AreEqual(.95, loaded.Entries[0].Result.Average);
            CollectionAssert.AreEqual(result.Frames.ToArray(), loaded.Entries[0].Result.Frames.ToArray());
            await File.AppendAllTextAsync(videoPath, "changed");
            try { await SsimTestCache.LoadAsync(path); Assert.Fail("変更動画の結果が利用されました。"); }
            catch (InvalidDataException) { }
            await File.WriteAllTextAsync(path, "broken");
            try { await SsimTestCache.LoadAsync(path); Assert.Fail("破損結果が利用されました。"); }
            catch (System.Text.Json.JsonException) { }
        }
        finally { Directory.Delete(directory, true); }
    }
}
