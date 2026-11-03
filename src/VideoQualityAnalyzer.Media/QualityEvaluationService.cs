using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public enum QualityMetric { Vmaf, Ssim, Psnr }
public sealed record QualityFrame(long Number, double Value);
public sealed record QualityResult(QualityMetric Metric, double Average, IReadOnlyList<QualityFrame> Frames);

// 評価は外部FFmpegで行い、正常終了した結果だけを返す。
public sealed class QualityEvaluationService(string executable)
{
    public async Task<QualityResult> EvaluateAsync(VideoDescriptor reference, VideoDescriptor candidate,
        QualityMetric metric, IProgress<TimeSpan>? progress, CancellationToken token)
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
            foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(candidate.Path),
                "-i", Path.GetFullPath(reference.Path), "-filter_complex",
                "[0:v:0]setpts=PTS-STARTPTS[dist];[1:v:0]setpts=PTS-STARTPTS[ref];[dist][ref]" + filter,
                "-an", "-progress", "pipe:1", "-f", "null", "-" }) info.ArgumentList.Add(argument);
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
            return metric == QualityMetric.Vmaf
                ? ParseVmaf(await File.ReadAllTextAsync(Path.Combine(directory, "metric.json"), token))
                : ParseStats(metric, await File.ReadAllTextAsync(Path.Combine(directory, "metric.log"), token));
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
