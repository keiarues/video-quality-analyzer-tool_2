using System.Globalization;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

internal static class QualityCsvSaveDialog
{
    internal static SaveFileDialog Create(DateTime savedAt, VideoDescriptor reference, bool frames,
        IReadOnlyList<QualityCsvItem> items,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results)
    {
        var graphName = frames ? "フレームグラフ" : "ファイルグラフ";
        var metrics = Enum.GetValues<QualityMetric>()
            .Where(metric => items.Any(item => results.ContainsKey((item.Video.Id, metric))))
            .Select(metric => metric.ToString().ToUpperInvariant());
        var evaluationType = string.Join("-", metrics);
        var fileName = $"{savedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}_" +
            $"{reference.Name}_{graphName}_{evaluationType}.csv";
        return new SaveFileDialog
        {
            Filter = "CSVファイル (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FileName = fileName
        };
    }
}
