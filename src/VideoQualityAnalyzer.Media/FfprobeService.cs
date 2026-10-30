using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public sealed class FfprobeService
{
    private readonly string probePath;

    public FfprobeService(string probePath)
    {
        this.probePath = probePath;
    }

    public static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("FFPROBE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "ffmpeg-9.0.2-essentials_build", "bin", "ffprobe.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, "ffprobe.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public async Task<VideoDescriptor> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["-v", "error", "-show_entries",
            "format=duration:stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,duration,nb_frames,sample_aspect_ratio",
            "-of", "json", "-i", path], cancellationToken);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(stream => GetString(stream, "codec_type") == "video");
        if (video.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("映像ストリームがありません。");

        var durationText = GetString(root.GetProperty("format"), "duration") ?? GetString(video, "duration");
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds))
            durationSeconds = 0;

        return new VideoDescriptor(
            Guid.NewGuid().ToString("N"),
            path,
            video.GetProperty("width").GetInt32(),
            video.GetProperty("height").GetInt32(),
            GetString(video, "codec_name") ?? "不明",
            ParseRate(GetString(video, "avg_frame_rate")),
            ParseRate(GetString(video, "r_frame_rate")),
            TimeSpan.FromSeconds(Math.Max(0, durationSeconds)),
            streams.Any(stream => GetString(stream, "codec_type") == "audio"),
            long.TryParse(GetString(video, "nb_frames"), out var totalFrames) && totalFrames > 0 ? totalFrames : null,
            ParseAspectRatio(GetString(video, "sample_aspect_ratio")));
    }

    public async Task<IReadOnlyList<TimeSpan>> ReadFrameTimesAsync(string path, TimeSpan position, CancellationToken cancellationToken)
    {
        var start = Math.Max(0, position.TotalSeconds - 1);
        var interval = $"{start.ToString("F3", CultureInfo.InvariantCulture)}%+3";
        var output = await RunAsync(["-v", "error", "-select_streams", "v:0", "-show_entries",
            "frame=best_effort_timestamp_time", "-of", "csv=p=0", "-read_intervals", interval, "-i", path], cancellationToken);
        var result = new List<TimeSpan>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = line.Trim().Split(',')[0];
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                result.Add(TimeSpan.FromSeconds(seconds));
        }
        result.Sort();
        return result;
    }

    public async Task<TimeSpan> ReadVideoStartTimeAsync(string path, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=start_time", "-of", "default=noprint_wrappers=1:nokey=1", "-i", path], cancellationToken);
        if (!double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            throw new InvalidDataException("映像の開始時刻を取得できませんでした。");
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(probePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
                throw new InvalidDataException($"FFprobeの処理に失敗しました（終了コード {process.ExitCode}）。{Sanitize(error)}");
            return output;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static double ParseRate(string? rate)
    {
        var parts = (rate ?? "0/1").Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && denominator != 0)
            return numerator / denominator;
        return 0;
    }

    private static double ParseAspectRatio(string? value)
    {
        var parts = (value ?? "").Split(':');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            numerator > 0 && denominator > 0)
            return numerator / denominator;
        return 1;
    }

    private static string Sanitize(string message)
    {
        // FFprobeの標準エラーにはファイルパスが含まれ得るため、詳細を画面やログへ流さない。
        return string.IsNullOrWhiteSpace(message) ? string.Empty : "詳細は元ファイルを確認してください。";
    }
}
