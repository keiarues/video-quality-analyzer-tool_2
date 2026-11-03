using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace VideoQualityAnalyzer.Diagnostics;

public sealed record MeasurementEvent(
    string RunId,
    string Phase,
    string Side,
    string? VideoId,
    long TimestampMonotonic,
    double? ElapsedMs,
    string Result,
    string? Detail,
    double? SourceFps = null,
    string? Resolution = null,
    string? Codec = null,
    string DecodeMode = "unknown",
    double? SourcePtsMs = null,
    string? PresentedFrameId = null,
    int? DroppedFrames = null,
    double? CpuPercent = null,
    double? GpuPercent = null,
    double? MemoryMb = null);

public sealed class MeasurementLog : IAsyncDisposable
{
    private readonly Channel<MeasurementEvent> events = Channel.CreateBounded<MeasurementEvent>(
        new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task writer;
    private readonly string runId = Guid.NewGuid().ToString("N");
    private long droppedEvents;

    public string FilePath { get; }

    public MeasurementLog(string? outputDirectory = null)
    {
        var directory = outputDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoQualityAnalyzer.StageA", "logs");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (UnauthorizedAccessException)
        {
            directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
        }
        FilePath = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{runId[..8]}.jsonl");
        writer = Task.Run(WriteAsync);
    }

    public void Record(string phase, string side, string? videoId, string result, double? elapsedMs = null, string? detail = null)
    {
        var item = new MeasurementEvent(runId, phase, side, videoId,
            Stopwatch.GetTimestamp(), elapsedMs, result, detail);
        if (!events.Writer.TryWrite(item))
            Interlocked.Increment(ref droppedEvents);
    }

    public void RecordPerformance(string side, string? videoId, double? sourceFps, string? resolution,
        string? codec, double? cpuPercent, double? memoryMb)
    {
        var item = new MeasurementEvent(runId, "resource_sample", side, videoId,
            Stopwatch.GetTimestamp(), null, "sample", "presented_fps=unmeasured",
            sourceFps, resolution, codec, CpuPercent: cpuPercent, MemoryMb: memoryMb);
        if (!events.Writer.TryWrite(item))
            Interlocked.Increment(ref droppedEvents);
    }

    private async Task WriteAsync()
    {
        await using var stream = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 65536, useAsync: true);
        await using var writer = new StreamWriter(stream);
        await foreach (var item in events.Reader.ReadAllAsync())
            await writer.WriteLineAsync(JsonSerializer.Serialize(item, JsonOptions));

        if (Interlocked.Read(ref droppedEvents) is var dropped && dropped > 0)
        {
            var item = new MeasurementEvent(runId, "log_overflow", "none", null,
                Stopwatch.GetTimestamp(), null, "dropped", dropped.ToString());
            await writer.WriteLineAsync(JsonSerializer.Serialize(item, JsonOptions));
        }
    }

    public async ValueTask DisposeAsync()
    {
        events.Writer.TryComplete();
        await writer.ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
