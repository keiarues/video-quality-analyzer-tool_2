using System.Windows;
using VideoQualityAnalyzer.Media;

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

            FolderPathBox.Text = "";
            await RefreshStatusAsync();
            if (FfmpegSettings.Detect(FolderPathBox.Text).Path is null ||
                DetectionStatus.Text == "未検出" || !VersionText.Text.StartsWith("ffmpeg version ", StringComparison.Ordinal) ||
                !DownloadButton.IsEnabled || !SaveButton.IsEnabled)
                throw new InvalidOperationException("自動検出またはバージョン表示が不正です。");
            Console.WriteLine("設定画面: 無効な入力と自動検出の表示、バージョン取得が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Application.Current.Shutdown(1);
        }
    }
}
