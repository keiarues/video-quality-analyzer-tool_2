using System.Globalization;
using System.Text;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public static class QualityCsvExporter
{
    public static string Summary(IEnumerable<VideoDescriptor> videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results)
    {
        var builder = new StringBuilder("ファイル名,幅,高さ,fps,長さ秒,ビットレートkbps（サイズから換算）,ファイルサイズ,VMAF,SSIM,PSNR\r\n");
        foreach (var video in videos)
        {
            var bytes = File.Exists(video.Path) ? new FileInfo(video.Path).Length : 0;
            var values = new List<string> { Escape(video.Name), video.Width.ToString(CultureInfo.InvariantCulture),
                video.Height.ToString(CultureInfo.InvariantCulture), Number(video.AverageFrameRate),
                Number(video.Duration.TotalSeconds), video.Duration.TotalSeconds > 0 ? Escape((bytes * 8d / video.Duration.TotalSeconds / 1000).ToString("N0", CultureInfo.GetCultureInfo("ja-JP"))) : "",
                bytes.ToString(CultureInfo.InvariantCulture) };
            foreach (var metric in Enum.GetValues<QualityMetric>())
                values.Add(results.TryGetValue((video.Id, metric), out var result) ? Number(result.Average) : "");
            builder.AppendLine(string.Join(',', values));
        }
        return builder.ToString();
    }

    public static string Frames(IEnumerable<VideoDescriptor> videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results)
    {
        var builder = new StringBuilder("ファイル名,指標,フレーム番号,時刻秒,評価値\r\n");
        foreach (var video in videos)
        foreach (var metric in Enum.GetValues<QualityMetric>())
            if (results.TryGetValue((video.Id, metric), out var result))
                foreach (var frame in result.Frames)
                    builder.AppendLine(string.Join(',', Escape(video.Name), metric.ToString().ToUpperInvariant(),
                        frame.Number.ToString(CultureInfo.InvariantCulture),
                        video.AverageFrameRate > 0 ? Number(frame.Number / video.AverageFrameRate) : "", Number(frame.Value)));
        return builder.ToString();
    }

    private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
