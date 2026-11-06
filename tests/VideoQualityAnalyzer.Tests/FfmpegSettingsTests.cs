using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class FfmpegSettingsTests
{
    [TestMethod]
    public async Task DetectedExecutableReturnsActualVersion()
    {
        var detected = FfmpegSettings.Detect("");
        Assert.IsNotNull(detected.Path);
        var version = await FfmpegSettings.GetVersionAsync(detected.Path);
        Assert.StartsWith("ffmpeg version ", version);
    }

    [TestMethod]
    public void EmptySettingFindsBundledToolsRecursively()
    {
        var result = FfmpegSettings.Detect("");
        Assert.IsNotNull(result.Path);
        Assert.AreEqual("tools内の自動検出", result.Source);
        Assert.IsTrue(File.Exists(result.Path));
        Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(result.Path)!, "ffprobe.exe")));
    }

    [TestMethod]
    public void InvalidExplicitFolderDoesNotSilentlyUseAutomaticDetection()
    {
        var result = FfmpegSettings.Detect(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.IsNull(result.Path);
    }
}
