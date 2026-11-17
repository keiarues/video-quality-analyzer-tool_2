using System.Windows;
using VideoQualityAnalyzer.Media;
using System.IO;

namespace VideoQualityAnalyzer.App;

public partial class SettingsWindow
{
    private async Task VerifyDetectionAsync()
    {
        try
        {
            FolderPathBox.Text = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            await RefreshStatusAsync();
            if (DetectionStatus.Text != "未検出" || VersionText.Text.Length != 0)
                throw new InvalidOperationException("無効なフォルダーの検出表示が不正です。");
            var saved = FfmpegSettings.Load();
            Save_Click(this, new RoutedEventArgs());
            if (FfmpegSettings.Load() != saved || ErrorText.Text.Length == 0)
                throw new InvalidOperationException("無効な入力が保存されたか、入力エラーが表示されませんでした。");
            ErrorText.Text = "";

            FolderPathBox.Text = "";
            await RefreshStatusAsync();
            if (FfmpegSettings.Detect(FolderPathBox.Text).Path is null ||
                DetectionStatus.Text == "未検出" || !VersionText.Text.StartsWith("ffmpeg version ", StringComparison.Ordinal) ||
                !DownloadButton.IsEnabled || !SaveButton.IsEnabled)
                throw new InvalidOperationException("自動検出またはバージョン表示が不正です。");
            Console.WriteLine("設定画面: 無効な入力の保存拒否、自動検出の表示、バージョン取得が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }

    private async Task VerifyDownloadAsync()
    {
        await DownloadAsync();
        var success = DownloadStatus.Text.StartsWith("ダウンロード完了", StringComparison.Ordinal)
            && FolderPathBox.Text.StartsWith(FfmpegDownloader.InstallRoot, StringComparison.OrdinalIgnoreCase)
            && FfmpegSettings.Detect(FolderPathBox.Text).Path is not null
            && DownloadButton.IsEnabled && SaveButton.IsEnabled;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ffmpeg-settings-verification.txt"),
            (success ? "成功" : "失敗") + "\n" + FolderPathBox.Text + "\n" + DownloadStatus.Text + "\n" + ErrorText.Text);
        Application.Current.Shutdown(success ? 0 : 1);
    }
}
