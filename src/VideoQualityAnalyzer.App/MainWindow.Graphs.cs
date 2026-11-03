using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private QualityGraphPanel? qualityGraphPanel;

    private void InitializeEmbeddedGraphs()
    {
        qualityGraphPanel = new QualityGraphPanel([], qualityResults, seek: ApplySeek, select: id =>
        {
            var entry = videos.FirstOrDefault(item => item.Descriptor.Id == id);
            if (entry is null) return;
            VideoList.SelectedItems.Clear();
            VideoList.SelectedItem = entry;
        });
        QualityGraphHost.Content = qualityGraphPanel;
    }

    private void RefreshEmbeddedGraphs()
    {
        qualityGraphPanel?.UpdateData(VideoList.SelectedItems.Cast<VideoEntry>()
            .Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray(),
            videos.Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray(),
            videos.ToDictionary(entry => entry.Descriptor.Id, entry => entry.Group));
    }
}
