using System.Windows;
using System.Windows.Controls;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private bool TryRunStartupVerification()
    {
        if (Environment.GetCommandLineArgs().Contains("--verify-preview-collapse"))
        {
            try
            {
                var previewHeight = PreviewRow.Height;
                var graphHeight = QualityGraphRow.Height;
                PreviewToggle_Click(this, new RoutedEventArgs());
                if (!previewCollapsed || PreviewToggleButton.Visibility != Visibility.Collapsed || PreviewRow.Height.Value != 0 || PlaybackControls.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("プレビューを最小化できません。");
                GraphToggle_Click(this, new RoutedEventArgs());
                if (previewCollapsed || graphsCollapsed || PreviewRow.Height != previewHeight || QualityGraphRow.Height != graphHeight || PlaybackControls.Visibility != Visibility.Visible)
                    throw new InvalidOperationException("グラフ側からサイズを復元できません。");
                GraphToggle_Click(this, new RoutedEventArgs());
                if (!graphsCollapsed || GraphToggleButton.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("グラフを最小化できません。");
                PreviewToggle_Click(this, new RoutedEventArgs());
                if (graphsCollapsed || previewCollapsed || PreviewRow.Height != previewHeight || QualityGraphRow.Height != graphHeight)
                    throw new InvalidOperationException("プレビュー側からサイズを復元できません。");
                Console.WriteLine("プレビュー・グラフの最小化と反対側からの復元が合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); Application.Current.Shutdown(1); }
            return true;
        }
        if (Environment.GetCommandLineArgs().Contains("--verify-initial-preview"))
        {
            try
            {
                UpdateLayout();
                if (videos.Count != 0 || !InitialPreviewTabs.IsVisible || InitialPreviewTabs.Items.Count != 3 || InitialPreviewTabs.SelectedIndex != 0)
                    throw new InvalidOperationException("動画未登録時にプレビュータブが表示されません。");
                foreach (var index in new[] { 1, 2, 0 })
                {
                    InitialPreviewTabs.SelectedIndex = index;
                    UpdateLayout();
                    if (!InitialPreviewTabs.IsVisible) throw new InvalidOperationException("初期タブを切り替えられません。");
                }
                Console.WriteLine("初期プレビュー: 未登録時の3タブ表示と切り替えが合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
            return true;
        }
        if (Environment.GetCommandLineArgs().Contains("--verify-sidebar"))
        {
            try
            {
                SidebarColumn.Width = new GridLength(580);
                UpdateLayout();
                var width = SidebarColumn.ActualWidth;
                SidebarToggle_Click(this, new RoutedEventArgs());
                UpdateLayout();
                if (SidebarPanel.Visibility != Visibility.Collapsed || SidebarColumn.ActualWidth != 0)
                    throw new InvalidOperationException("一覧の最小化に失敗しました。");
                SidebarToggle_Click(this, new RoutedEventArgs());
                UpdateLayout();
                if (SidebarPanel.Visibility != Visibility.Visible || Math.Abs(SidebarColumn.ActualWidth - width) > 1)
                    throw new InvalidOperationException("一覧の幅の復元に失敗しました。");
                Application.Current.Shutdown(0);
            }
            catch { Application.Current.Shutdown(1); }
            return true;
        }
        return false;
    }
}
