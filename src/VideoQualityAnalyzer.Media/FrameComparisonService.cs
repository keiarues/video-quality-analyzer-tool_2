using System.Diagnostics;
using System.Globalization;

namespace VideoQualityAnalyzer.Media;

public sealed record ComparisonFrame(int Width, int Height, long FrameNumber, byte[] Pixels);

public sealed record FrameDifferenceResult(int Width, int Height, byte[] Heatmap, double MeanAbsoluteRgbDifference);

// 指定フレーム番号をFFmpegで復号する。再生中のGPU経路とは独立して実行する。
public sealed class FrameComparisonService
{
    private readonly string ffmpegPath;
    private readonly FfprobeService probe;

    public FrameComparisonService(string ffmpegPath, string ffprobePath)
    {
        this.ffmpegPath = ffmpegPath;
        probe = new FfprobeService(ffprobePath);
    }

    public async Task<ComparisonFrame> ReadFrameAsync(string path, long frameNumber, CancellationToken token)
    {
        if (frameNumber < 0) throw new ArgumentOutOfRangeException(nameof(frameNumber));
        var descriptor = await probe.ProbeAsync(path, token);
        var size = checked(descriptor.Width * descriptor.Height * 4);
        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-i", path,
            "-map", "0:v:0", "-an", "-sn", "-dn", "-vf",
            $"select=eq(n\\,{frameNumber.ToString(CultureInfo.InvariantCulture)})",
            "-frames:v", "1", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1" })
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
        var errorTask = process.StandardError.ReadToEndAsync(token);
        var pixels = new byte[size];
        try
        {
            await process.StandardOutput.BaseStream.ReadExactlyAsync(pixels, token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"比較フレームの復号に失敗しました（終了コード {process.ExitCode}）。");
            return new ComparisonFrame(descriptor.Width, descriptor.Height, frameNumber, pixels);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("指定フレームが動画の範囲外か、復号できませんでした。", exception);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            try { await errorTask; } catch (OperationCanceledException) { }
        }
    }

    public static FrameDifferenceResult CreateHeatmap(ComparisonFrame left, ComparisonFrame right, int gain = 16)
    {
        if (gain < 1 || gain > 64) throw new ArgumentOutOfRangeException(nameof(gain));
        if (left.Width != right.Width || left.Height != right.Height)
            throw new InvalidDataException("比較する動画の解像度が異なります。");
        if (left.FrameNumber != right.FrameNumber)
            throw new InvalidDataException("比較するフレーム番号が異なります。");
        var heatmap = new byte[left.Pixels.Length];
        long totalDifference = 0;
        for (var offset = 0; offset < heatmap.Length; offset += 4)
        {
            var blue = Math.Abs(left.Pixels[offset] - right.Pixels[offset]);
            var green = Math.Abs(left.Pixels[offset + 1] - right.Pixels[offset + 1]);
            var red = Math.Abs(left.Pixels[offset + 2] - right.Pixels[offset + 2]);
            totalDifference += blue + green + red;
            var level = Math.Min(255, Math.Max(red, Math.Max(green, blue)) * gain);
            heatmap[offset] = 0;
            heatmap[offset + 1] = (byte)Math.Max(0, (level - 128) * 2);
            heatmap[offset + 2] = (byte)level;
            heatmap[offset + 3] = 255;
        }
        var average = (double)totalDifference / (left.Width * (long)left.Height * 3);
        return new FrameDifferenceResult(left.Width, left.Height, heatmap, average);
    }
}
