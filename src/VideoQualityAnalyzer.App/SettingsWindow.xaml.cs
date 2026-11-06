using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class SettingsWindow : Window
{
    private bool automatic;
    private bool updatingPath;
    private int generation;
    private CancellationTokenSource? versionCancellation;

    public SettingsWindow()
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
        Closed += (_, _) => { generation++; versionCancellation?.Cancel(); };
        Loaded += (_, _) => RefreshStatus();
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
        DetectionStatus.Text = detected.Path is null ? "未検出" : detected.Source;
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

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) FolderPathBox.Text = dialog.FolderName;
    }

    private void Detect_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { FfmpegSettings.Save(automatic ? "" : FolderPathBox.Text); DialogResult = true; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { ErrorText.Text = exception.Message; }
    }
}
