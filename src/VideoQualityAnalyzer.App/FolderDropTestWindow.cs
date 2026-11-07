using System.IO;
using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public sealed class FolderDropTestWindow : Window
{
    private readonly ListView list = new();
    private readonly TextBlock status = new();
    private readonly Dictionary<string, TestEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool loading;

    public sealed record TestEntry(string Name, string Group, string Path);

    public FolderDropTestWindow(string? testFolder, bool automatic)
    {
        Title = "フォルダドラッグ・自動グループ設定テスト";
        Width = 950; Height = 650;
        Style = (Style)FindResource(typeof(Window));
        AllowDrop = true;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "フォルダをドロップしてください。動画の情報取得・再生は行いません。",
            Style = (Style)FindResource("UiSectionStyle")
        });
        header.Children.Add(new TextBlock
        {
            Text = "動画のあるフォルダ名で分類／単体ファイルは未設定／登録済みは維持／100本以上で確認",
            Style = (Style)FindResource("UiSmallTextStyle")
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var load = new Button { Content = "用意した120本を読み込む", Margin = new Thickness(0, 8, 8, 8), IsEnabled = testFolder is not null };
        load.Click += async (_, _) => { if (testFolder is not null) await ImportAsync([testFolder]); };
        var clear = new Button { Content = "一覧をクリア", Margin = new Thickness(0, 8, 0, 8) };
        clear.Click += (_, _) => { if (!loading) { entries.Clear(); Refresh(); } };
        buttons.Children.Add(load); buttons.Children.Add(clear);
        header.Children.Add(buttons); header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "ファイル名", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 220 });
        view.Columns.Add(new GridViewColumn { Header = "グループ", DisplayMemberBinding = new System.Windows.Data.Binding("Group"), Width = 150 });
        view.Columns.Add(new GridViewColumn { Header = "ファイルパス", DisplayMemberBinding = new System.Windows.Data.Binding("Path"), Width = 500 });
        list.View = view; panel.Children.Add(list); Content = panel;
        DragOver += (_, e) => { e.Effects = !loading && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { e.Handled = true; if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportAsync(paths); };
        if (automatic) Loaded += async (_, _) =>
        {
            try
            {
                if (testFolder is null) throw new InvalidOperationException("テストフォルダ未指定");
                var calls = 0;
                await ImportAsync([testFolder], () => { calls++; return false; });
                if (entries.Count != 0 || calls != 1) throw new InvalidOperationException("中止の検証失敗");
                calls = 0;
                await ImportAsync([testFolder], () => { calls++; return true; });
                if (entries.Count != 120 || calls != 1 || entries.Values.Any(entry =>
                    entry.Group != new DirectoryInfo(System.IO.Path.GetDirectoryName(entry.Path)!).Name))
                    throw new InvalidOperationException("グループの検証失敗");
                await ImportAsync(entries.Keys.Take(1).ToArray(), () => true);
                if (entries.Count != 120 || entries.Values.Any(entry => entry.Group.Length == 0))
                    throw new InvalidOperationException("重複維持の検証失敗");
                File.WriteAllText(System.IO.Path.Combine(testFolder, "自動検証結果.log"), "成功：中止0本、続行120本、親フォルダ名による分類、重複時のグループ維持");
                Application.Current.Shutdown(0);
            }
            catch (Exception error)
            {
                if (testFolder is not null) File.WriteAllText(System.IO.Path.Combine(testFolder, "自動検証結果.log"), error.ToString());
                Application.Current.Shutdown(1);
            }
        };
    }

    private async Task ImportAsync(string[] paths, Func<bool>? confirmation = null)
    {
        if (loading) return;
        loading = true;
        status.Text = "候補を探索中…";
        try
        {
            var canceled = false;
            var files = await Task.Run(() => VideoDropFiles.Expand(paths, () => Dispatcher.Invoke(() =>
            {
                var accepted = confirmation?.Invoke() ?? MessageBox.Show(this,
                    "読み込む動画が100本以上あります。処理を続けますか？",
                    "動画の一括登録", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
                canceled = !accepted;
                return accepted;
            })));
            foreach (var file in files)
            {
                if (entries.ContainsKey(file)) continue;
                var fromFolder = paths.Any(path => Directory.Exists(path) &&
                    file.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path)) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                entries.Add(file, new TestEntry(System.IO.Path.GetFileName(file),
                    fromFolder ? new DirectoryInfo(System.IO.Path.GetDirectoryName(file)!).Name : "", file));
            }
            Refresh();
            if (canceled) status.Text = $"今回の登録を中止しました（登録済み {entries.Count}本）。";
        }
        catch (Exception error) { status.Text = "読み込み失敗：" + error.Message; }
        finally { loading = false; }
    }

    private void Refresh()
    {
        list.ItemsSource = entries.Values.ToArray();
        status.Text = $"登録済み {entries.Count}本／グループ {entries.Values.Where(entry => entry.Group.Length > 0).Select(entry => entry.Group).Distinct().Count()}個";
    }
}
