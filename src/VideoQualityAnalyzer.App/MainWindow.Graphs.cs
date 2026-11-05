using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private QualityGraphPanel? qualityGraphPanel;

    private void InitializeEmbeddedGraphs()
    {
        qualityGraphPanel = new QualityGraphPanel([], qualityResults, seek: CommitGraphSeek, select: id =>
        {
            var entry = videos.FirstOrDefault(item => item.Descriptor.Id == id);
            if (entry is null) return;
            VideoList.SelectedItems.Clear();
            VideoList.SelectedItem = entry;
        }, separatePlaybackLayer: true, cacheStaticGraph: true);
        qualityGraphPanel.SeekPreviewed += position =>
        {
            seekSliderDragging = true;
            seekTimer.Stop();
            pendingSeekPosition = position;
            ++sliderSeekGeneration;
            // グラフ操作中も共通シークバーには指定位置を表示する。
            programmaticSlider = true;
            SeekSlider.Value = Math.Clamp(position.TotalSeconds, SeekSlider.Minimum, SeekSlider.Maximum);
            programmaticSlider = false;
            UpdateTimeDisplay();
        };
        QualityGraphHost.Content = qualityGraphPanel;
    }

    private void QualityGraphResize_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)) return;
        QualityGraphResize_DragDelta(sender, new System.Windows.Controls.Primitives.DragDeltaEventArgs(0,
            e.Key == System.Windows.Input.Key.Up ? -10 : 10));
        e.Handled = true;
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
        qualityGraphPanel?.SetPlaybackSources(left.Video, right.Video, referenceId);
        qualityGraphPanel?.UpdateData(SelectedOrAllVideoEntries()
            .Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray(),
            videos.Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray(),
            videos.ToDictionary(entry => entry.Descriptor.Id, entry => entry.Group));
    }
}
