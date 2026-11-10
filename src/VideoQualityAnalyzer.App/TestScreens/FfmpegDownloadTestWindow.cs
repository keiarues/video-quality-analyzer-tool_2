using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class FfmpegDownloadTestWindow : Window
{
    public FfmpegDownloadTestWindow(bool automatic)
    {
        Title = "FFmpeg自動ダウンロード検証";
        Width = 650; Height = 240;
        Style = (Style)FindResource(typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(18) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var button = new Button { Content = "最新安定版を取得して検証" };
        panel.Children.Add(button); panel.Children.Add(status); Content = panel;
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var path = await new FfmpegDownloader().DownloadAsync(new Progress<string>(text => status.Text = text));
                status.Text = "取得成功：" + path;
                if (automatic) Application.Current.Shutdown(0);
            }
            catch (Exception error)
            {
                status.Text = error.ToString();
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg-download-error.txt"), error.ToString());
                if (automatic) Application.Current.Shutdown(1);
            }
            finally { button.IsEnabled = true; }
        };
        if (automatic) Loaded += (_, _) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
