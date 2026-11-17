using System.Globalization;
using System.Text.Json;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

internal sealed record EvaluationRaster(string PixelFormat, double? Start, string Parameters)
{
    public static bool CanUsePipes(VideoDescriptor video, EvaluationRaster reference, EvaluationRaster candidate) =>
        OperatingSystem.IsWindows() && video.Width % 2 == 0 && video.Height % 2 == 0 &&
        reference.PixelFormat == "yuv420p" && candidate.PixelFormat == "yuv420p" &&
        reference.Start is not null && candidate.Start is not null;

    public static async Task<EvaluationRaster> ReadAsync(string probeExecutable, string path, CancellationToken token)
    {
        var arguments = new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=pix_fmt,start_time,color_space,color_range,color_primaries,color_transfer", "-of", "json", "-i", path };
        using var process = EvaluationProcess.Start(probeExecutable, arguments, null, "映像情報を取得できません。");
        using var registration = token.Register(() => EvaluationProcess.Kill(process));
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        finally { EvaluationProcess.Kill(process); await process.WaitForExitAsync(CancellationToken.None); await errors; }
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException("映像形式の取得に失敗しました。");
        using var json = JsonDocument.Parse(await output);
        var stream = json.RootElement.GetProperty("streams")[0];
        string Value(string key, string fallback = "unknown") => stream.TryGetProperty(key, out var value) ? value.GetString() ?? fallback : fallback;
        var range = Value("color_range") switch { "tv" => "limited", "pc" => "full", _ => "unknown" };
        var knownStart = double.TryParse(Value("start_time", "N/A"), NumberStyles.Float, CultureInfo.InvariantCulture, out var start) && double.IsFinite(start);
        return new(Value("pix_fmt"), knownStart ? start : null,
            $"setparams=colorspace={Value("color_space")}:range={range}:color_primaries={Value("color_primaries")}:color_trc={Value("color_transfer")}");
    }
}
