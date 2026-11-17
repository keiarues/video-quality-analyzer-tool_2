using System.Diagnostics;

namespace VideoQualityAnalyzer.Media;

// 全コーデックで同じRGB変換を通すためのプレビュー用リーダー。
public sealed class FfmpegRgbReader : IDisposable
{
    private readonly Process process;
    private readonly Stream output;
    private readonly int frameBytes;
    private readonly double frameRate;
    private readonly TimeSpan start;
    private long frameIndex;

    public FfmpegRgbReader(string path, int width, int height, double frameRate, TimeSpan start)
    {
        var executable = FindExecutable() ?? throw new FileNotFoundException("FFmpegが見つかりません。");
        frameBytes = checked(width * height * 4);
        this.frameRate = frameRate > 0 ? frameRate : 30;
        this.start = start;
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-hide_banner");
        info.ArgumentList.Add("-loglevel"); info.ArgumentList.Add("error");
        info.ArgumentList.Add("-ss"); info.ArgumentList.Add(start.TotalSeconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-i"); info.ArgumentList.Add(path);
        info.ArgumentList.Add("-map"); info.ArgumentList.Add("0:v:0");
        info.ArgumentList.Add("-an"); info.ArgumentList.Add("-sn"); info.ArgumentList.Add("-dn");
        info.ArgumentList.Add("-fps_mode"); info.ArgumentList.Add("passthrough");
        info.ArgumentList.Add("-pix_fmt"); info.ArgumentList.Add("bgr0");
        info.ArgumentList.Add("-f"); info.ArgumentList.Add("rawvideo");
        info.ArgumentList.Add("pipe:1");
        process = Process.Start(info) ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
        output = process.StandardOutput.BaseStream;
        _ = process.StandardError.ReadToEndAsync();
    }

    public RgbFrame? ReadNext()
    {
        var pixels = new byte[frameBytes];
        var offset = 0;
        while (offset < pixels.Length)
        {
            var count = output.Read(pixels, offset, pixels.Length - offset);
            if (count == 0)
            {
                if (offset == 0) return null;
                throw new InvalidDataException("FFmpegの映像フレームが途中で終了しました。");
            }
            offset += count;
        }
        // rawvideoはPTSを保持しない。表示時刻はフレーム番号からの推定値。
        var position = start + TimeSpan.FromSeconds(frameIndex++ / frameRate);
        return new RgbFrame(position, pixels);
    }

    public void Dispose()
    {
        EvaluationProcess.Kill(process);
        process.Dispose();
    }

    public static string? FindExecutable() => string.IsNullOrEmpty(FfmpegSettings.ActivePath) ? null : FfmpegSettings.ActivePath;
}
