using System.IO;
using System.Windows;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class SettingsWindow
{
    private CancellationTokenSource? downloadCancellation;

    private async void Download_Click(object sender, RoutedEventArgs e) => await DownloadAsync();

    private async Task DownloadAsync()
    {
        if (downloadCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        downloadCancellation = cancellation;
        DownloadButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        CancelDownloadButton.Visibility = Visibility.Visible;
        ErrorText.Text = "";
        try
        {
            var folder = await new FfmpegDownloader().DownloadAsync(
                new Progress<string>(text => { if (!closed) DownloadStatus.Text = text; }), cancellation.Token);
            if (closed) return;
            FolderPathBox.Text = folder;
            DownloadStatus.Text = "ダウンロード完了。「保存」で確定し、次回起動時から使用します。";
        }
        catch (OperationCanceledException)
        { if (!closed) DownloadStatus.Text = "ダウンロードを中断しました。"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or InvalidOperationException)
        {
            if (!closed)
            {
                DownloadStatus.Text = "ダウンロードできませんでした。再実行してください。";
                ErrorText.Text = error.Message;
            }
        }
        finally
        {
            downloadCancellation = null;
            if (!closed)
            {
                DownloadButton.IsEnabled = true;
                SaveButton.IsEnabled = true;
                CancelDownloadButton.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e) => downloadCancellation?.Cancel();

}
