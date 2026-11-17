using System.Diagnostics;
using System.Globalization;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

// 同じCFR素材から対応する番号のフレームを連続取得する。再生時計と画面提示は呼び出し側が管理する。
public sealed class StreamingFramePairReader : IAsyncDisposable
{
    private readonly Process leftProcess;
    private readonly Process rightProcess;
    private readonly int frameBytes;
    private long nextFrameNumber;

    private StreamingFramePairReader(Process leftProcess, Process rightProcess,
        VideoDescriptor descriptor, long firstFrameNumber)
    {
        this.leftProcess = leftProcess;
        this.rightProcess = rightProcess;
        Descriptor = descriptor;
        frameBytes = checked(descriptor.Width * descriptor.Height * 4);
        nextFrameNumber = firstFrameNumber;
    }

    public VideoDescriptor Descriptor { get; }
    public double DecoderCpuSeconds => leftProcess.TotalProcessorTime.TotalSeconds +
        rightProcess.TotalProcessorTime.TotalSeconds;
    public long DecoderPeakWorkingSetBytes => leftProcess.PeakWorkingSet64 + rightProcess.PeakWorkingSet64;

    public static async Task<StreamingFramePairReader> OpenAsync(string ffmpegPath, string ffprobePath,
        string leftPath, string rightPath, long firstFrameNumber, CancellationToken token)
    {
        if (firstFrameNumber < 0) throw new ArgumentOutOfRangeException(nameof(firstFrameNumber));
        var probe = new FfprobeService(ffprobePath);
        var leftTask = probe.ProbeAsync(leftPath, token);
        var rightTask = probe.ProbeAsync(rightPath, token);
        await Task.WhenAll(leftTask, rightTask);
        var left = await leftTask;
        var right = await rightTask;
        if (left.Width != right.Width || left.Height != right.Height ||
            left.AverageFrameRate <= 0 || right.AverageFrameRate <= 0 ||
            Math.Abs(left.AverageFrameRate - right.AverageFrameRate) > 0.001 ||
            Math.Abs(left.AverageFrameRate - left.NominalFrameRate) > 0.001 ||
            Math.Abs(right.AverageFrameRate - right.NominalFrameRate) > 0.001)
            throw new InvalidDataException("再生比較には同じ解像度・CFRフレームレートの動画を選んでください。");

        var leftProcess = Start(ffmpegPath, leftPath, firstFrameNumber, left.AverageFrameRate);
        try
        {
            var rightProcess = Start(ffmpegPath, rightPath, firstFrameNumber, right.AverageFrameRate);
            return new StreamingFramePairReader(leftProcess, rightProcess, left, firstFrameNumber);
        }
        catch
        {
            Stop(leftProcess);
            throw;
        }
    }

    public async Task<(ComparisonFrame Left, ComparisonFrame Right)?> ReadNextAsync(CancellationToken token)
    {
        using var registration = token.Register(() => { StopProcess(leftProcess); StopProcess(rightProcess); });
        var leftPixels = new byte[frameBytes];
        var rightPixels = new byte[frameBytes];
        var leftTask = ReadFrameAsync(leftProcess, leftPixels, token);
        var rightTask = ReadFrameAsync(rightProcess, rightPixels, token);
        await Task.WhenAll(leftTask, rightTask);
        if (!await leftTask || !await rightTask) return null;
        var number = nextFrameNumber++;
        return (new ComparisonFrame(Descriptor.Width, Descriptor.Height, number, leftPixels),
            new ComparisonFrame(Descriptor.Width, Descriptor.Height, number, rightPixels));
    }

    private static async Task<bool> ReadFrameAsync(Process process, byte[] pixels, CancellationToken token)
    {
        var stream = process.StandardOutput.BaseStream;
        var first = await stream.ReadAsync(pixels.AsMemory(0, 1), token);
        if (first == 0)
        {
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"連続復号に失敗しました（終了コード {process.ExitCode}）。");
            return false;
        }
        await stream.ReadExactlyAsync(pixels.AsMemory(1), token);
        return true;
    }

    private static Process Start(string ffmpegPath, string path, long firstFrameNumber, double frameRate)
    {
        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        if (firstFrameNumber > 0)
        {
            // 入力側の正確なシークで対象時刻より前を捨て、同じ元フレーム番号から連続復号する。
            arguments.Add("-ss");
            arguments.Add((firstFrameNumber / frameRate).ToString("F9", CultureInfo.InvariantCulture));
        }
        arguments.AddRange(["-i", path, "-map", "0:v:0", "-an", "-sn", "-dn",
            "-fps_mode", "passthrough", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"]);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        var process = Process.Start(info) ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static void StopProcess(Process process)
    {
        EvaluationProcess.Kill(process);
    }

    private static void Stop(Process process)
    {
        StopProcess(process);
        process.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Stop(leftProcess);
        Stop(rightProcess);
        return ValueTask.CompletedTask;
    }
}
