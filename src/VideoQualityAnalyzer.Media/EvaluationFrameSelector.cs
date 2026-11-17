using System.Globalization;

namespace VideoQualityAnalyzer.Media;

internal static class EvaluationFrameSelector
{
    public static string Sequential(IReadOnlyList<EvaluationFrameRange> ranges) =>
        $"trim=end_frame={ranges[^1].End},select='" +
        string.Join("+", ranges.Select(range => $"between(n,{range.Start},{range.End - 1})")) + "'";

    public static string Seek(IReadOnlyList<EvaluationFrameRange> ranges, double streamStart, double frameRate) =>
        $"select='between(round((t-{streamStart.ToString("R", CultureInfo.InvariantCulture)})*" +
        $"{frameRate.ToString("R", CultureInfo.InvariantCulture)}),{ranges[0].Start},{ranges[^1].End - 1})'";
}
