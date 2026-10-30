using System.Text.Json;
using VideoQualityAnalyzer.Diagnostics;

namespace VideoQualityAnalyzer.Tests;

[TestClass]
public sealed class MeasurementLogTests
{
    [TestMethod]
    public async Task DisposeFlushesEventsAndPerformanceRemainsUnmeasured()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VideoQualityAnalyzer.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var log = new MeasurementLog(directory);
            await using (log)
            {
                log.Record("probe", "left", "video-id", "ok");
                log.RecordPerformance("both", "video-id", 60, "1440x1080", "ffv1", 1.5, 100);
            }
            var lines = await File.ReadAllLinesAsync(log.FilePath);
            Assert.HasCount(2, lines);
            using var first = JsonDocument.Parse(lines[0]);
            using var second = JsonDocument.Parse(lines[1]);
            Assert.AreEqual("probe", first.RootElement.GetProperty("phase").GetString());
            Assert.AreEqual(first.RootElement.GetProperty("run_id").GetString(), second.RootElement.GetProperty("run_id").GetString());
            Assert.AreEqual("presented_fps=unmeasured", second.RootElement.GetProperty("detail").GetString());
            Assert.AreEqual(JsonValueKind.Null, second.RootElement.GetProperty("presented_frame_id").ValueKind);
            Assert.AreEqual(1.5, second.RootElement.GetProperty("cpu_percent").GetDouble());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
