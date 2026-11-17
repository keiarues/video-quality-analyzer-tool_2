using System.Diagnostics;
using System.Globalization;
using System.Buffers;

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
        var useFastSeek = frameNumber > 0 && descriptor.AverageFrameRate > 0 &&
            Math.Abs(descriptor.AverageFrameRate - descriptor.NominalFrameRate) < 0.001;
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error" };
        if (useFastSeek)
        {
            // CFR素材は時刻から対象フレームへ移動する。VFR素材は従来の番号指定を保つ。
            arguments.Add("-ss");
            arguments.Add((frameNumber / descriptor.AverageFrameRate).ToString("F9", CultureInfo.InvariantCulture));
        }
        arguments.AddRange(["-i", path, "-map", "0:v:0", "-an", "-sn", "-dn"]);
        if (!useFastSeek)
            arguments.AddRange(["-vf", $"select=eq(n\\,{frameNumber.ToString(CultureInfo.InvariantCulture)})"]);
        arguments.AddRange(["-frames:v", "1", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"]);
        foreach (var argument in arguments)
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
            EvaluationProcess.Kill(process);
            try { await errorTask; } catch (OperationCanceledException) { }
        }
    }

    public static FrameDifferenceResult CreateHeatmap(ComparisonFrame left, ComparisonFrame right,
        int gain = 16, int blurRadius = 0)
    {
        if (gain < 1 || gain > 64) throw new ArgumentOutOfRangeException(nameof(gain));
        if (blurRadius < 0 || blurRadius > 8) throw new ArgumentOutOfRangeException(nameof(blurRadius));
        if (left.Width != right.Width || left.Height != right.Height)
            throw new InvalidDataException("比較する動画の解像度が異なります。");
        if (left.FrameNumber != right.FrameNumber)
            throw new InvalidDataException("比較するフレーム番号が異なります。");
        if (left.Pixels.Length != checked(left.Width * left.Height * 4) ||
            right.Pixels.Length != left.Pixels.Length)
            throw new InvalidDataException("比較フレームの画素数が正しくありません。");
        return blurRadius == 0 ? CreateUnblurredHeatmap(left, right, gain) :
            CreateBlurredHeatmap(left, right, gain, blurRadius);
    }

    private static FrameDifferenceResult CreateUnblurredHeatmap(ComparisonFrame left, ComparisonFrame right, int gain)
    {
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

    private static FrameDifferenceResult CreateBlurredHeatmap(ComparisonFrame left, ComparisonFrame right,
        int gain, int radius)
    {
        var width = left.Width;
        var height = left.Height;
        var heatmap = new byte[left.Pixels.Length];
        var horizontal = ArrayPool<short>.Shared.Rent(checked(width * height * 3));
        var columns = new int[checked(width * 3)];
        try
        {
            // 左右へ同じボックスぼかしを適用した場合の符号付き差を、絶対値にする前に平均する。
            for (var y = 0; y < height; y++)
            {
                var blue = 0;
                var green = 0;
                var red = 0;
                for (var sourceX = 0; sourceX <= Math.Min(radius, width - 1); sourceX++)
                    AddDifference(y, sourceX, 1);
                for (var x = 0; x < width; x++)
                {
                    var target = (y * width + x) * 3;
                    horizontal[target] = (short)blue;
                    horizontal[target + 1] = (short)green;
                    horizontal[target + 2] = (short)red;
                    if (x - radius >= 0) AddDifference(y, x - radius, -1);
                    if (x + radius + 1 < width) AddDifference(y, x + radius + 1, 1);
                }

                void AddDifference(int row, int column, int sign)
                {
                    var offset = (row * width + column) * 4;
                    blue += sign * (left.Pixels[offset] - right.Pixels[offset]);
                    green += sign * (left.Pixels[offset + 1] - right.Pixels[offset + 1]);
                    red += sign * (left.Pixels[offset + 2] - right.Pixels[offset + 2]);
                }
            }

            for (var row = 0; row <= Math.Min(radius, height - 1); row++)
                AddHorizontalRow(row, 1);

            double totalDifference = 0;
            for (var y = 0; y < height; y++)
            {
                var verticalCount = Math.Min(height - 1, y + radius) - Math.Max(0, y - radius) + 1;
                for (var x = 0; x < width; x++)
                {
                    var source = x * 3;
                    var offset = (y * width + x) * 4;
                    var horizontalCount = Math.Min(width - 1, x + radius) - Math.Max(0, x - radius) + 1;
                    var divisor = horizontalCount * verticalCount;
                    var blue = Math.Abs(columns[source]) / (double)divisor;
                    var green = Math.Abs(columns[source + 1]) / (double)divisor;
                    var red = Math.Abs(columns[source + 2]) / (double)divisor;
                    totalDifference += blue + green + red;
                    var level = (int)Math.Min(255, Math.Max(red, Math.Max(green, blue)) * gain);
                    heatmap[offset] = 0;
                    heatmap[offset + 1] = (byte)Math.Max(0, (level - 128) * 2);
                    heatmap[offset + 2] = (byte)level;
                    heatmap[offset + 3] = 255;
                }
                if (y - radius >= 0) AddHorizontalRow(y - radius, -1);
                if (y + radius + 1 < height) AddHorizontalRow(y + radius + 1, 1);
            }
            return new FrameDifferenceResult(width, height, heatmap,
                totalDifference / (width * (long)height * 3));

            void AddHorizontalRow(int row, int sign)
            {
                var source = row * width * 3;
                for (var i = 0; i < columns.Length; i++)
                    columns[i] += sign * horizontal[source + i];
            }
        }
        finally { ArrayPool<short>.Shared.Return(horizontal); }
    }
}
