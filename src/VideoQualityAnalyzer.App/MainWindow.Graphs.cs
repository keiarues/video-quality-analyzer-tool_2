using System.Windows;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private QualityGraphPanel? qualityGraphPanel;

    private void SaveGraphImage_Click(object sender, RoutedEventArgs e)
    {
        var reference = videos.FirstOrDefault(entry => entry.Descriptor.Id == referenceId)?.Descriptor;
        if (reference is null)
        {
            MessageBox.Show(this, "基準動画を登録してください。", "グラフ画像の保存", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var allVideos = videos.Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray();
        var selectedVideos = SelectedOrAllVideoEntries().Where(entry => !entry.IsReference)
            .Select(entry => entry.Descriptor).ToArray();
        var allIds = allVideos.Select(video => video.Id).ToHashSet();
        var results = qualityResults.Where(entry => allIds.Contains(entry.Key.VideoId))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        if (results.Count == 0)
        {
            MessageBox.Show(this, "保存できる評価結果がありません。先に品質評価を実行してください。",
                "グラフ画像の保存", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var selectedMetric = qualityGraphPanel?.SelectedMetric ?? QualityMetric.Vmaf;
        if (!results.Keys.Any(key => key.Metric == selectedMetric))
            selectedMetric = Enum.GetValues<QualityMetric>().First(value => results.Keys.Any(key => key.Metric == value));
        var source = new GraphImageSaveSource(reference, selectedVideos, allVideos, results,
            videos.ToDictionary(entry => entry.Descriptor.Id, entry => entry.Group), selectedMetric);
        new GraphImageSaveWindow(source) { Owner = this }.ShowDialog();
    }

    private void InitializeEmbeddedGraphs()
    {
        qualityGraphPanel = new QualityGraphPanel([], qualityResults, seek: CommitGraphSeek, select: id =>
        {
            var entry = videos.FirstOrDefault(item => item.Descriptor.Id == id);
            if (entry is null) return;
            VideoList.SelectedItems.Clear();
            VideoList.SelectedItem = entry;
        }, separatePlaybackLayer: true, cacheStaticGraph: true, clearSelection: () =>
        {
            VideoList.UnselectAll();
            RefreshEmbeddedGraphs();
        });
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

    private bool graphsCollapsed;
    private System.Windows.GridLength savedGraphHeight;
    private System.Windows.GridLength savedPreviewHeight;

    private bool previewCollapsed;

    private void GraphToggle_Click(object sender, System.Windows.RoutedEventArgs e) => TogglePreviewArea(false);
    private void PreviewToggle_Click(object sender, System.Windows.RoutedEventArgs e) => TogglePreviewArea(true);

    private void TogglePreviewArea(bool collapsePreview)
    {
        var restore = previewCollapsed || graphsCollapsed;
        if (!restore)
        {
            savedGraphHeight = QualityGraphRow.Height;
            savedPreviewHeight = PreviewRow.Height;
        }
        previewCollapsed = !restore && collapsePreview;
        graphsCollapsed = !restore && !collapsePreview;
        PreviewRow.MinHeight = previewCollapsed ? 0 : 120;
        QualityGraphRow.MinHeight = graphsCollapsed ? 0 : 200;
        PreviewRow.Height = previewCollapsed ? new System.Windows.GridLength(0) :
            restore ? savedPreviewHeight : new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        QualityGraphRow.Height = graphsCollapsed ? new System.Windows.GridLength(0) :
            restore ? savedGraphHeight : new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        InitialPreviewTabs.Visibility = !previewCollapsed && !comparisonActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ComparisonHost.Visibility = !previewCollapsed && comparisonActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PlaybackControls.Visibility = previewCollapsed ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        QualityGraphHost.Visibility = graphsCollapsed ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        PreviewToggleButton.Visibility = previewCollapsed ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        GraphToggleButton.Visibility = graphsCollapsed ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        PreviewToggleButton.Content = "▲";
        GraphToggleButton.Content = "▼";
        PreviewToggleButton.ToolTip = graphsCollapsed ? "グラフを復元" : "プレビューを最小化";
        GraphToggleButton.ToolTip = previewCollapsed ? "プレビューを復元" : "グラフを最小化";
        QualityGraphResizeBar.IsEnabled = !previewCollapsed && !graphsCollapsed;
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
        if (graphsCollapsed || previewCollapsed) return;
        // 手動調整後も上下の比率を保持し、ウィンドウサイズに追従させる。
        var fixedHeight = PreviewLayout.RowDefinitions[1].ActualHeight +
            PreviewLayout.RowDefinitions[2].ActualHeight + PreviewLayout.RowDefinitions[3].ActualHeight;
        var maximum = Math.Max(QualityGraphRow.MinHeight, PreviewLayout.ActualHeight - fixedHeight - PreviewRow.MinHeight);
        var height = Math.Clamp(QualityGraphRow.ActualHeight - e.VerticalChange, QualityGraphRow.MinHeight, maximum);
        var available = PreviewRow.ActualHeight + QualityGraphRow.ActualHeight;
        PreviewRow.Height = new System.Windows.GridLength(Math.Max(1, available - height), System.Windows.GridUnitType.Star);
        QualityGraphRow.Height = new System.Windows.GridLength(Math.Max(1, height), System.Windows.GridUnitType.Star);
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
