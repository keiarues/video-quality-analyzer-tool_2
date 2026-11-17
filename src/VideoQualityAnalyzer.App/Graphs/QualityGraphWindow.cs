using System.Windows;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 専用画面と主画面は同じグラフ部品を表示する。
public sealed class QualityGraphWindow : Window
{
    public QualityGraphWindow(VideoDescriptor[] videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results,
        Action<TimeSpan>? seek = null, Action<string>? select = null, bool verify = false,
        VideoDescriptor[]? allVideos = null, IReadOnlyDictionary<string, string>? groups = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "品質グラフ";
        Width = 1000;
        Height = 650;
        Content = new QualityGraphPanel(videos, results, seek, select, verify, allVideos, groups);
    }
}
