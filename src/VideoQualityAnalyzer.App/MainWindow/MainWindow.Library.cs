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


}
