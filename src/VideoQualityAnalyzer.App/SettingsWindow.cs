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
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(18) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "FFmpegのファイルパス、またはフォルダーパス" });
        var path = new TextBox { Text = FfmpegSettings.Load(), Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(path);
        var browse = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(browse);
        var file = new Button { Content = "ファイルを選択" };
        var folder = new Button { Content = "フォルダーを選択" };
        browse.Children.Add(file);
        browse.Children.Add(folder);
        file.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe" }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; };
        folder.Click += (_, _) => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) path.Text = dialog.FolderName; };
        panel.Children.Add(new TextBlock { Text = "同じフォルダーのffprobe.exeも使用します。空欄で自動検出に戻します。\n保存した設定は次回起動時から反映されます。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("UiWarningBrush") };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        var save = new Button { Content = "保存", IsDefault = true };
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        save.Click += (_, _) =>
        {
            try { FfmpegSettings.Save(path.Text); DialogResult = true; }
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
