using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class SettingsWindow : Window
{
    private bool closed;
    private bool automatic;
    private bool updatingPath;
    private int generation;
    private CancellationTokenSource? versionCancellation;

    public SettingsWindow(bool verifyDownload = false, bool verifyDetection = false)
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
        Loaded += (_, _) => { if (!verifyDetection) RefreshStatus(); };
        if (verifyDetection) Loaded += async (_, _) => await VerifyDetectionAsync();
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

    private async void RefreshStatus() => await RefreshStatusAsync();

    private async Task RefreshStatusAsync()
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
            try { FolderPathBox.Text = Path.GetDirectoryName(detected.Path) ?? ""; }
            finally { updatingPath = false; }
        }
        VersionText.Text = "バージョン取得中…";
        var result = await FfmpegSettings.GetVersionAsync(detected.Path, token);
        if (current == generation) VersionText.Text = result;
    }

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
