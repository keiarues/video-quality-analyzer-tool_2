using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class SettingsWindow : Window
{
    private CancellationTokenSource? downloadCancellation;
    private bool closed;
    private bool automatic;
    private bool updatingPath;
    private int generation;
    private CancellationTokenSource? versionCancellation;

    public SettingsWindow(bool verifyDownload = false)
    {
        InitializeComponent();
        if (DesignerProperties.GetIsInDesignMode(this)) return;
        var saved = FfmpegSettings.Load();
        FolderPathBox.Text = File.Exists(saved) ? Path.GetDirectoryName(saved) ?? "" : saved;
        automatic = string.IsNullOrWhiteSpace(saved);
        FolderPathBox.TextChanged += (_, _) =>
        {
            if (updatingPath) return;
            automatic = string.IsNullOrWhiteSpace(FolderPathBox.Text);
            RefreshStatus();
        };
        Closed += (_, _) => { closed = true; generation++; versionCancellation?.Cancel(); downloadCancellation?.Cancel(); };
        Loaded += (_, _) => RefreshStatus();
        if (verifyDownload) Loaded += async (_, _) =>
        {
            await DownloadAsync();
            var success = DownloadStatus.Text.StartsWith("ダウンロード完了", StringComparison.Ordinal)
                && FolderPathBox.Text.StartsWith(FfmpegDownloader.InstallRoot, StringComparison.OrdinalIgnoreCase)
                && FfmpegSettings.Detect(FolderPathBox.Text).Path is not null
                && DownloadButton.IsEnabled && SaveButton.IsEnabled;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ffmpeg-settings-verification.txt"),
                (success ? "成功" : "失敗") + "\n" + FolderPathBox.Text + "\n" + DownloadStatus.Text + "\n" + ErrorText.Text);
            Application.Current.Shutdown(success ? 0 : 1);
        };
    }

    private async void RefreshStatus()
    {
        versionCancellation?.Cancel();
        versionCancellation?.Dispose();
        versionCancellation = new CancellationTokenSource();
        var token = versionCancellation.Token;
        var current = ++generation;
        var input = automatic ? "" : FolderPathBox.Text;
        FolderPathBox.Foreground = (System.Windows.Media.Brush)FindResource(automatic ? "UiMutedBrush" : "UiOnLightBrush");
        FolderPathBox.Background = (System.Windows.Media.Brush)FindResource(automatic ? "PanelBrush" : "UiControlBackgroundBrush");
        DetectionStatus.Text = "検出中…";
        VersionText.Text = "";
        var detected = await Task.Run(() => FfmpegSettings.Detect(input));
        if (current != generation) return;
        DetectionStatus.Text = detected.Path is null ? "未検出" : detected.Source switch
        {
            "tools内の自動検出" => "toolsフォルダ内のffmpegを使用",
            "PATHからの自動検出" => "pathに設定されたffmpegを使用",
            _ => detected.Source
        };
        DetectionStatus.ToolTip = detected.Source;
        if (detected.Path is null) return;
        if (automatic)
        {
            updatingPath = true;
            FolderPathBox.Text = Path.GetDirectoryName(detected.Path) ?? "";
            updatingPath = false;
        }
        VersionText.Text = "バージョン取得中…";
        var result = await FfmpegSettings.GetVersionAsync(detected.Path, token);
        if (current == generation) VersionText.Text = result;
    }

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

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) FolderPathBox.Text = dialog.FolderName;
    }

    private void Website_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
            { UseShellExecute = true });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { ErrorText.Text = "ブラウザーを開けませんでした：" + error.Message; }
    }

    private void Detect_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { FfmpegSettings.Save(automatic ? "" : FolderPathBox.Text); DialogResult = true; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { ErrorText.Text = exception.Message; }
    }
}
