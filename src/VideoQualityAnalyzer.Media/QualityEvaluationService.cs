using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public enum QualityMetric { Vmaf, Ssim, Psnr }
public sealed record QualityFrame(long Number, double Value);
public sealed record QualityEvaluationTiming(TimeSpan FfmpegElapsed, TimeSpan AggregationElapsed, TimeSpan FfmpegCpuTime);
public sealed record QualityResult(QualityMetric Metric, double Average, IReadOnlyList<QualityFrame> Frames)
{
    public EvaluationSelection Selection { get; init; } = EvaluationSelection.Full;
}

// 評価は外部FFmpegで行い、正常終了した結果だけを返す。
public sealed class QualityEvaluationService(string executable, Action<QualityEvaluationTiming>? timing = null)
{
    public async Task<QualityResult> EvaluateAsync(VideoDescriptor reference, VideoDescriptor candidate,
        QualityMetric metric, IProgress<TimeSpan>? progress, CancellationToken token)
        => await EvaluateCoreAsync(reference, candidate, metric, progress, token, null);

    public async Task<QualityResult> EvaluatePartialAsync(VideoDescriptor reference, VideoDescriptor candidate,
        QualityMetric metric, IReadOnlyList<EvaluationFrameRange> ranges, IProgress<TimeSpan>? progress, CancellationToken token)
    {
        var snapshot = ranges.ToArray();
        if (reference.IsVariableFrameRate || candidate.IsVariableFrameRate || snapshot.Length == 0 ||
            snapshot.Any(range => range.Start < 0 || range.End <= range.Start) ||
            snapshot.Zip(snapshot.Skip(1)).Any(pair => pair.First.End > pair.Second.Start) ||
            snapshot[^1].End > (candidate.TotalFrames ?? (long)Math.Ceiling(candidate.Duration.TotalSeconds * candidate.AverageFrameRate)) ||
            snapshot[^1].End > (reference.TotalFrames ?? (long)Math.Ceiling(reference.Duration.TotalSeconds * reference.AverageFrameRate)))
            throw new ArgumentException("評価区間が動画の範囲外、重複、またはCFRではありません。");
        return await EvaluateCoreAsync(reference, candidate, metric, progress, token, snapshot);
    }

    private async Task<QualityResult> EvaluateCoreAsync(VideoDescriptor reference, VideoDescriptor candidate,
        QualityMetric metric, IProgress<TimeSpan>? progress, CancellationToken token, EvaluationFrameRange[]? ranges)
    {
        token.ThrowIfCancellationRequested();
        if (reference.Width != candidate.Width || reference.Height != candidate.Height ||
            Math.Abs(reference.AverageFrameRate - candidate.AverageFrameRate) > 0.001)
            throw new InvalidOperationException("評価する動画の解像度とフレームレートをそろえてください。");
        var directory = Path.Combine(Path.GetTempPath(), "VideoQualityAnalyzer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var filter = metric switch
            {
                QualityMetric.Vmaf => "libvmaf=log_fmt=json:log_path=metric.json:shortest=1",
                QualityMetric.Ssim => "ssim=stats_file=metric.log:shortest=1",
                _ => "psnr=stats_file=metric.log:shortest=1"
            };
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory
            };
            var selection = ranges is null ? "setpts=PTS-STARTPTS" :
                $"trim=end_frame={ranges[^1].End},select='" +
                string.Join("+", ranges.Select(range => $"between(n,{range.Start},{range.End - 1})")) +
                $"',setpts=N/({reference.AverageFrameRate.ToString("R", CultureInfo.InvariantCulture)}*TB)";
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(candidate.Path),
                "-i", Path.GetFullPath(reference.Path), "-filter_complex",
                $"[0:v:0]{selection}[dist];[1:v:0]{selection}[ref];[dist][ref]" + filter,
                "-an", "-progress", "pipe:1", "-f", "null", "-" }) info.ArgumentList.Add(argument);
            var ffmpegClock = timing is null ? null : Stopwatch.StartNew();
            using var process = Process.Start(info) ?? throw new InvalidOperationException("FFmpegを起動できません。");
            using var registration = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            });
            var errors = DrainErrorsAsync(process.StandardError);
            try
            {
                while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                    if (line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out var microseconds))
                        progress?.Report(TimeSpan.FromTicks(microseconds * 10));
                await process.WaitForExitAsync(token);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await errors;
            }
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(metric == QualityMetric.Vmaf
                    ? "VMAF評価に失敗しました。FFmpegのlibvmaf対応と動画を確認してください。"
                    : "品質評価に失敗しました。動画とFFmpegを確認してください。");
            ffmpegClock?.Stop();
            var aggregationClock = timing is null ? null : Stopwatch.StartNew();
            void ReportTiming()
            {
                aggregationClock?.Stop();
                if (timing is not null)
                    timing(new(ffmpegClock!.Elapsed, aggregationClock!.Elapsed, process.TotalProcessorTime));
            }
            var result = metric == QualityMetric.Vmaf
                ? ParseVmaf(await File.ReadAllTextAsync(Path.Combine(directory, "metric.json"), token))
                : ParseStats(metric, await File.ReadAllTextAsync(Path.Combine(directory, "metric.log"), token));
            if (ranges is null) { ReportTiming(); return result; }
            long Map(long number)
            {
                foreach (var range in ranges)
                {
                    if (number < range.End - range.Start) return range.Start + number;
                    number -= range.End - range.Start;
                }
                throw new InvalidDataException("評価結果のフレーム番号が対象区間を超えています。");
            }
            result = result with { Frames = result.Frames.Select(frame => frame with { Number = Map(frame.Number) }).ToArray() };
            ReportTiming();
            return result;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task DrainErrorsAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0) { }
    }

    public static QualityResult ParseStats(QualityMetric metric, string text)
    {
        var frames = new List<QualityFrame>();
        var key = metric == QualityMetric.Ssim ? "All" : "psnr_avg";
        foreach (var line in text.Split('\n'))
        {
            var number = Regex.Match(line, @"n:\s*(\d+)");
            var value = Regex.Match(line, key + @":\s*([^\s]+)");
            if (number.Success && value.Success && double.TryParse(value.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
                frames.Add(new QualityFrame(long.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture) - 1, parsed));
        }
        if (frames.Count == 0) throw new InvalidDataException("集計可能な評価値がありません（PSNR無限大は集計対象外です）。");
        return new QualityResult(metric, frames.Average(frame => frame.Value), frames);
    }

    public static QualityResult ParseVmaf(string text)
    {
        using var json = JsonDocument.Parse(text);
        var frames = json.RootElement.GetProperty("frames").EnumerateArray().Select(frame =>
            new QualityFrame(frame.GetProperty("frameNum").GetInt64(),
                frame.GetProperty("metrics").GetProperty("vmaf").GetDouble())).ToArray();
        if (frames.Length == 0) throw new InvalidDataException("VMAFの評価値がありません。");
        var average = json.RootElement.TryGetProperty("pooled_metrics", out var pooled)
            ? pooled.GetProperty("vmaf").GetProperty("mean").GetDouble() : frames.Average(frame => frame.Value);
        return new QualityResult(QualityMetric.Vmaf, average, frames);
    }
}
