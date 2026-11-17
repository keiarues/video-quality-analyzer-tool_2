using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private double expandedSidebarWidth = 700;
    private bool sidebarCollapsed;

    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!sidebarCollapsed)
        {
            expandedSidebarWidth = SidebarColumn.ActualWidth;
            SidebarPanel.Visibility = SidebarSplitter.Visibility = Visibility.Collapsed;
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            SidebarSplitterColumn.Width = new GridLength(20);
        }
        else
        {
            SidebarColumn.MinWidth = 300;
            SidebarColumn.Width = new GridLength(Math.Max(300, expandedSidebarWidth));
            SidebarSplitterColumn.Width = new GridLength(20);
            SidebarPanel.Visibility = SidebarSplitter.Visibility = Visibility.Visible;
        }
        sidebarCollapsed = !sidebarCollapsed;
        SidebarToggleButton.Content = sidebarCollapsed ? "▶" : "◀";
        SidebarToggleButton.ToolTip = sidebarCollapsed ? "一覧を復元" : "一覧を最小化";
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggleButton, (string)SidebarToggleButton.ToolTip);
    }

    private void VideoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 行・列見出し・スクロール操作は通常どおり扱い、一覧の空白だけで解除する。
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element != VideoList)
        {
            if (element is DataGridRow or System.Windows.Controls.Primitives.DataGridColumnHeader
                or System.Windows.Controls.Primitives.ScrollBar)
                return;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        if (element == VideoList) VideoList.UnselectAll();
    }

    private IEnumerable<VideoEntry> SelectedOrAllVideoEntries() =>
        VideoList.SelectedItems.Count == 0 ? videos : VideoList.SelectedItems.Cast<VideoEntry>();

    private void ClearLibrarySelection()
    {
        var wasSuppressed = suppressSelection;
        suppressSelection = true;
        try
        {
            VideoList.UnselectAll();
            ReferenceList.UnselectAll();
        }
        finally { suppressSelection = wasSuppressed; }
    }

    private void ReferenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelection || ReferenceList.SelectedItem is null) return;
        VideoList.UnselectAll();
    }

    private string? displayedReferenceId;

    private void RefreshLibraryLists()
    {
        var wasSuppressed = suppressSelection;
        suppressSelection = true;
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Where(entry => entry.Descriptor.Id != referenceId).ToArray();
        var reference = videos.FirstOrDefault(entry => entry.Descriptor.Id == referenceId);
        if (ReferenceList.Items.Count != (reference is null ? 0 : 1) || ReferenceList.Items.Cast<VideoEntry>().FirstOrDefault() != reference)
            ReferenceList.ItemsSource = reference is null ? Array.Empty<VideoEntry>() : new[] { reference };
        if (displayedReferenceId != referenceId)
            VideoList.ItemsSource = new System.Windows.Data.ListCollectionView(videos)
            { Filter = item => item is VideoEntry entry && entry.Descriptor.Id != referenceId };
        displayedReferenceId = referenceId;
        foreach (var entry in selected.Where(videos.Contains))
            if (!VideoList.SelectedItems.Contains(entry)) VideoList.SelectedItems.Add(entry);
        suppressSelection = wasSuppressed;
    }

    private void VideoList_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (!Enum.TryParse<VideoSortOrder>(e.Column.SortMemberPath, out var order)) return;
        sortDescending = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending;
        activeSort = order;
        foreach (var column in VideoList.Columns) column.SortDirection = null;
        e.Column.SortDirection = sortDescending ? System.ComponentModel.ListSortDirection.Descending
            : System.ComponentModel.ListSortDirection.Ascending;
        RefreshVideoOrder();
    }

    private void RefreshQualityValues()
    {
        foreach (var entry in videos)
        {
            entry.VmafAverage = qualityResults.TryGetValue((entry.Descriptor.Id, QualityMetric.Vmaf), out var vmaf) ? vmaf.Average : null;
            entry.SsimAverage = qualityResults.TryGetValue((entry.Descriptor.Id, QualityMetric.Ssim), out var ssim) ? ssim.Average : null;
            entry.PsnrAverage = qualityResults.TryGetValue((entry.Descriptor.Id, QualityMetric.Psnr), out var psnr) ? psnr.Average : null;
        }
        RefreshEmbeddedGraphs();
        RefreshEvaluatedPlayback();
    }

    private void RefreshVideoOrder()
    {
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().ToArray();
        var ordered = VideoLibrary.Sort(videos, activeSort, sortDescending);
        suppressSelection = true;
        for (var index = 0; index < ordered.Length; index++) videos.Move(videos.IndexOf(ordered[index]), index);
        VideoList.Items.Refresh();
        VideoList.SelectedItems.Clear();
        foreach (var entry in selected) VideoList.SelectedItems.Add(entry);
        suppressSelection = false;
        UpdateSelection();
    }
}
