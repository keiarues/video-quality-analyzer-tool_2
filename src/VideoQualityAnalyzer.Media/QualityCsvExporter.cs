using System.Globalization;
using System.Text;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.Media;

public sealed record QualityCsvItem(VideoDescriptor Video, string Group);

public static class QualityCsvExporter
{
    public static string Summary(VideoDescriptor reference, IEnumerable<QualityCsvItem> videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results)
    {
        var builder = new StringBuilder("比較動画番号,元動画名,比較動画名,グループ,幅,高さ,fps,長さ秒,ビットレートkbps,ビットレート取得元,VMAF,SSIM,PSNR\r\n");
        var number = 0;
        foreach (var item in videos.Where(item => HasResult(item.Video, results)))
        {
            var video = item.Video;
            number++;
            var values = new List<string>
            {
                Integer(number), Escape(reference.Name), Escape(video.Name), Escape(item.Group),
                Integer(video.Width), Integer(video.Height), Number(video.AverageFrameRate),
                Number(video.Duration.TotalSeconds), Number(video.AverageBitrateKbps),
                Escape(SourceName(video.BitrateSource))
            };
            foreach (var metric in Enum.GetValues<QualityMetric>())
                values.Add(results.TryGetValue((video.Id, metric), out var result) ? Number(result.Average) : "");
            AppendRow(builder, values);
        }
        return builder.ToString();
    }

    public static string Frames(VideoDescriptor reference, IEnumerable<QualityCsvItem> videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results)
    {
        var builder = new StringBuilder("比較動画番号,元動画名,比較動画名,グループ,指標,フレーム番号,時刻秒,評価値\r\n");
        var number = 0;
        foreach (var item in videos.Where(item => HasResult(item.Video, results)))
        {
            var video = item.Video;
            number++;
            foreach (var metric in Enum.GetValues<QualityMetric>())
            {
                if (!results.TryGetValue((video.Id, metric), out var result)) continue;
                foreach (var frame in result.Frames.OrderBy(frame => frame.Number))
                    AppendRow(builder, [Integer(number), Escape(reference.Name), Escape(video.Name),
                        Escape(item.Group), metric.ToString().ToUpperInvariant(), Integer(frame.Number),
                        video.AverageFrameRate > 0 ? Number(frame.Number / video.AverageFrameRate) : "",
                        Number(frame.Value)]);
            }
        }
        return builder.ToString();
    }

    public static bool HasResults(IEnumerable<QualityCsvItem> videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results) =>
        videos.Any(item => HasResult(item.Video, results));

    public static async Task SaveAsync(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(true));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool HasResult(VideoDescriptor video,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results) =>
        Enum.GetValues<QualityMetric>().Any(metric => results.ContainsKey((video.Id, metric)));

    private static string SourceName(BitrateSource source) => source switch
    {
        BitrateSource.VideoStream => "映像ストリーム",
        BitrateSource.Container => "コンテナー",
        BitrateSource.FileSizeEstimate => "ファイルサイズ換算",
        _ => ""
    };

    private static string Number(double? value) => value is { } number && double.IsFinite(number)
        ? number.ToString("G17", CultureInfo.InvariantCulture) : "";
    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Escape(string value)
    {
        // ファイル名やグループ名が表計算ソフトの数式として実行されないようにする。
        var text = value.TrimStart();
        if (text.Length > 0 && "=+-@".Contains(text[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string> values) =>
        builder.Append(string.Join(',', values)).Append("\r\n");
}
