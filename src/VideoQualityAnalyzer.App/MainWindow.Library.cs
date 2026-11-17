using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
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

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "動画を追加",
            Filter = "動画ファイル|*.mkv;*.mp4;*.mov;*.avi;*.webm;*.m4v|すべてのファイル|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
            await AddFilesAsync(dialog.FileNames);
    }

    private async Task AddFilesAsync(IEnumerable<string> paths, string[]? droppedFolders = null)
    {
        if (probe is null)
        {
            SetWindowStatus("動画を読み込むにはFFmpegとFFprobeが必要です。オプション → 設定でフォルダーを指定し、再起動してください。");
            return;
        }
        if (evaluationCancellation is not null)
        {
            SetWindowStatus("評価中は動画を追加できません。", evaluation: true);
            return;
        }

        pendingVideoLoads++;
        try
        {
        foreach (var input in paths)
        {
            var path = System.IO.Path.GetFullPath(input);
            if (videos.Any(item => string.Equals(item.Descriptor.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!File.Exists(path))
            {
                SetWindowStatus("ファイルが見つかりません: " + System.IO.Path.GetFileName(path));
                continue;
            }

            SetWindowStatus("動画情報を取得中: " + System.IO.Path.GetFileName(path));
            try
            {
                var descriptor = await probe.ProbeAsync(path, CancellationToken.None);
                var entry = new VideoEntry(descriptor);
                videos.Add(entry);
                referenceId ??= descriptor.Id;
                entry.IsReference = referenceId == descriptor.Id;
                if (!entry.IsReference && droppedFolders is not null)
                    entry.Group = VideoDropFiles.GetGroup(path, droppedFolders);
                VideoList.Items.Refresh();
                log.Record("file_added", "none", descriptor.Id, "ok",
                    detail: $"codec={descriptor.Codec};resolution={descriptor.Width}x{descriptor.Height};fps={descriptor.AverageFrameRate:F3}");
            }
            catch (Exception exception)
            {
                SetWindowStatus($"動画情報を取得できません: {System.IO.Path.GetFileName(path)} ({exception.GetType().Name})");
                log.Record("probe_failed", "none", null, "failed", detail: exception.GetType().Name);
            }
        }

        ClearLibrarySelection();
        UpdateSelection();
        if (activeSort != VideoSortOrder.Registration) RefreshVideoOrder();
        ClearLibrarySelection();
        var groupCount = videos.Where(entry => !entry.IsReference && !string.IsNullOrWhiteSpace(entry.Group))
            .Select(entry => entry.Group).Distinct().Count();
        SetWindowStatus($"読み込み済み：{videos.Count}ファイル／{groupCount}グループ");
        }
        finally { pendingVideoLoads--; }
    }

    private void SetReference_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        if (VideoList.SelectedItem is not VideoEntry entry)
            return;
        if (referenceId != entry.Descriptor.Id && evaluationResults.HasResults &&
            !ConfirmEvaluationReset("基準動画を変更すると、計算済みの評価値がすべて削除されます。続行しますか？")) return;
        if (referenceId != entry.Descriptor.Id) evaluationResults.Clear();
        referenceId = entry.Descriptor.Id;
        foreach (var item in videos)
            item.IsReference = item.Descriptor.Id == referenceId;
        RefreshQualityValues();
        suppressSelection = true;
        VideoList.Items.Refresh();
        suppressSelection = false;
        ClearLibrarySelection();
        UpdateSelection();
        ClearLibrarySelection();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        var removed = VideoList.SelectedItems.Cast<VideoEntry>().Concat(ReferenceList.SelectedItems.Cast<VideoEntry>()).Distinct().ToArray();
        RemoveVideos(removed);
    }

    private void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        RemoveVideos(videos.ToArray());
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void RemoveVideos(VideoEntry[] removed)
    {
        if (removed.Any(item => item.Descriptor.Id == referenceId) && evaluationResults.HasResults &&
            !ConfirmEvaluationReset("基準動画を削除すると、計算済みの評価値がすべて削除されます。続行しますか？")) return;
        suppressSelection = true;
        foreach (var item in removed)
        {
            videos.Remove(item);
            log.Record("file_removed", "none", item.Descriptor.Id, "ok");
        }
        if (!videos.Any(item => item.Descriptor.Id == referenceId))
        {
            evaluationResults.Clear();
            referenceId = videos.FirstOrDefault()?.Descriptor.Id;
        }
        foreach (var item in removed) evaluationResults.RemoveVideo(item.Descriptor.Id);
        foreach (var item in videos)
            item.IsReference = item.Descriptor.Id == referenceId;
        RefreshQualityValues();
        VideoList.Items.Refresh();
        suppressSelection = false;
        UpdateSelection();
    }

    private Func<string, bool>? confirmEvaluationResetForVerification;

    private bool ConfirmEvaluationReset(string message) => confirmEvaluationResetForVerification?.Invoke(message) ??
        MessageBox.Show(this, message, "評価値の削除", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

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

    private void SetGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Where(entry => !entry.IsReference).ToArray();
        if (selected.Length == 0) { SetWindowStatus("グループを設定する比較動画を選択してください。"); return; }
        if (new GroupWindow(selected, existingGroups: videos.Where(entry => !entry.IsReference).Select(entry => entry.Group)) { Owner = this }.ShowDialog() == true) RefreshVideoOrder();
    }

    private void ClearGroup_Click(object sender, RoutedEventArgs e)
    {
        var selected = VideoList.SelectedItems.Cast<VideoEntry>().Where(entry => !entry.IsReference).ToArray();
        if (selected.Length == 0) { SetWindowStatus("グループを解除する比較動画を選択してください。"); return; }
        VideoLibrary.SetGroup(selected, "");
        RefreshVideoOrder();
        SetWindowStatus($"{selected.Length}本のグループ設定を解除しました。");
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

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
            e.Handled = true;
            try
            {
                var folders = paths.Where(Directory.Exists).ToArray();
                var files = await Task.Run(() => VideoDropFiles.Expand(paths, () =>
                    Dispatcher.Invoke(() => !closing && MessageBox.Show(this,
                        "読み込む動画が100本以上あります。処理を続けますか？\n「いいえ」を選ぶと今回の登録を中止します。",
                        "動画の一括登録", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) == MessageBoxResult.Yes)));
                if (!closing && files.Length > 0) await AddFilesAsync(files, folders);
            }
            catch (Exception exception)
            {
                SetWindowStatus($"フォルダーを読み込めません: {exception.GetType().Name}");
            }
        }
    }

}
