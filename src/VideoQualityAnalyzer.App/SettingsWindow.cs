using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class SettingsWindow : Window
{
    public SettingsWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "設定";
        Width = 680;
        Height = 400;
        MinWidth = 420;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        var panel = new StackPanel { Margin = new Thickness(18) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var pathRow = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(pathRow);
        pathRow.Children.Add(new TextBlock { Text = "FFmpegフォルダーパス", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var saved = FfmpegSettings.Load();
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(status, 2);
        pathRow.Children.Add(status);
        var version = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(version);
        var path = new TextBox { Text = System.IO.File.Exists(saved) ? System.IO.Path.GetDirectoryName(saved) ?? "" : saved, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(path, 1);
        pathRow.Children.Add(path);
        var automatic = string.IsNullOrWhiteSpace(saved);
        var updatingPath = false;
        var generation = 0;
        CancellationTokenSource? versionCancellation = null;
        Closed += (_, _) => { generation++; versionCancellation?.Cancel(); };
        async void RefreshStatus()
        {
            versionCancellation?.Cancel();
            versionCancellation?.Dispose();
            versionCancellation = new CancellationTokenSource();
            var token = versionCancellation.Token;
            var current = ++generation;
            var input = automatic ? "" : path.Text ?? "";
            status.Text = "検出中…";
            version.Text = "";
            var detected = await Task.Run(() => FfmpegSettings.Detect(input));
            if (current != generation) return;
            status.Text = detected.Path is null ? "未検出" : detected.Source;
            status.ToolTip = detected.Source;
            if (detected.Path is null) return;
            if (automatic)
            {
                updatingPath = true;
                path.Text = System.IO.Path.GetDirectoryName(detected.Path) ?? "";
                updatingPath = false;
            }
            version.Text = "バージョン取得中…";
            var result = await FfmpegSettings.GetVersionAsync(detected.Path, token);
            if (current == generation) version.Text = result;
        }
        path.TextChanged += (_, _) =>
        {
            if (updatingPath) return;
            automatic = string.IsNullOrWhiteSpace(path.Text);
            RefreshStatus();
        };
        RefreshStatus();
        var browse = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(browse);

        var folder = new Button { Content = "フォルダーを選択" };

        browse.Children.Add(folder);
        var detect = new Button { Content = "再検出" };
        browse.Children.Add(detect);
        detect.Click += (_, _) => RefreshStatus();
        folder.Click += (_, _) => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) path.Text = dialog.FolderName; };
        panel.Children.Add(new TextBlock { Text = "同じフォルダーのffprobe.exeも使用します。空欄の場合はtools配下、次にPATHから自動検出します。\n保存した設定は次回起動時から反映されます。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("UiWarningBrush") };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        var save = new Button { Content = "保存", IsDefault = true };
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        save.Click += (_, _) =>
        {
            try { FfmpegSettings.Save(automatic ? "" : path.Text ?? ""); DialogResult = true; }
            catch (Exception exception) when (exception is ArgumentException or System.IO.IOException or UnauthorizedAccessException or NotSupportedException) { error.Text = exception.Message; }
        };
    }
}

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "バージョン情報";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "動画品質アナライザー", Style = (Style)Application.Current.FindResource("UiTitleStyle") });
        panel.Children.Add(new TextBlock { Text = $"バージョン {typeof(AboutWindow).Assembly.GetName().Version?.ToString(3)}", Margin = new Thickness(0, 12, 0, 12) });
        var close = new Button { Content = "閉じる", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
    }
}
