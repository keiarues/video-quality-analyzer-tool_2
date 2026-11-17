using System.Windows;
using VideoQualityAnalyzer.Media;
using System.IO;

namespace VideoQualityAnalyzer.App;

public partial class SettingsWindow
{
    private void VerifyStyle()
    {
        try
        {
            var modes = DisplayStyleBox.Items.OfType<System.Windows.Controls.ComboBoxItem>()
                .Select(item => item.Tag as string).ToArray();
            if (!modes.SequenceEqual(["None", "System", "Light", "Dark"]) ||
                !Equals(DisplayStyleBox.SelectedValue, DisplayStyleSettings.Load()))
                throw new InvalidOperationException("表示スタイルの選択肢または保存済み選択が不正です。");
            foreach (var mode in modes)
            {
                DisplayStyleBox.SelectedValue = mode;
                if (!Equals(DisplayStyleBox.SelectedValue, mode))
                    throw new InvalidOperationException("表示スタイルを選択できません。");
            }
            DisplayStyleBox.SelectedValue = DisplayStyleSettings.Load();
            UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight,
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            image.Render((System.Windows.Media.Visual)Content);
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/display-style"));
            Directory.CreateDirectory(directory);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(directory,
                "settings-" + DisplayStyleSettings.ActiveMode.ToLowerInvariant() + ".png"))) encoder.Save(stream);
            Console.WriteLine("設定画面: 4つの表示スタイルの選択と保存済み選択の復元が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
    }

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
