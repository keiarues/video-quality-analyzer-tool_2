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

    private void QualityGraphResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // グラフ行だけを指定し、残りの高さは映像行に吸収させる。
        var fixedHeight = PreviewLayout.RowDefinitions[1].ActualHeight +
            PreviewLayout.RowDefinitions[2].ActualHeight + PreviewLayout.RowDefinitions[3].ActualHeight;
        var maximum = Math.Max(QualityGraphRow.MinHeight, PreviewLayout.ActualHeight - fixedHeight - PreviewRow.MinHeight);
        var height = Math.Clamp(QualityGraphRow.ActualHeight - e.VerticalChange, QualityGraphRow.MinHeight, maximum);
        QualityGraphRow.Height = new System.Windows.GridLength(height);
        e.Handled = true;
    }

    private void RefreshEmbeddedGraphs()
    {
        qualityGraphPanel?.UpdateData(VideoList.SelectedItems.Cast<VideoEntry>()
            .Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray(),
            videos.Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray(),
            videos.ToDictionary(entry => entry.Descriptor.Id, entry => entry.Group));
    }
}
