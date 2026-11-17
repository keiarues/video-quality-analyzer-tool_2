using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            RecordStartup("未処理エラー: " + args.Exception);
            MessageBox.Show("起動処理でエラーが発生しました。\n" + args.Exception.GetBaseException().Message +
                "\n\n詳細: " + StartupLogPath, "起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        RecordStartup("起動開始");
        if (TryOpenTestWindow(e)) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "tools"));
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("アプリのフォルダーにtoolsフォルダーを作成できませんでした。\n" +
                "書き込み可能な場所にアプリを配置するか、設定画面でFFmpegフォルダーを指定してください。\n\n" + exception.Message,
                "toolsフォルダーの作成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        if (await TryOpenVerificationAsync(e)) return;
        base.OnStartup(e);
        RecordStartup("メイン画面の作成開始");
        MainWindow = new MainWindow();
        RecordStartup("メイン画面の表示開始");
        MainWindow.Show();
        RecordStartup("メイン画面の表示完了");
        if (VideoQualityAnalyzer.Media.FfmpegSettings.Detect(
            VideoQualityAnalyzer.Media.FfmpegSettings.Load()).Path is null)
        {
            RecordStartup("FFmpeg未検出のため設定画面を表示");
            new SettingsWindow { Owner = MainWindow }.ShowDialog();
        }
    }

    private static string StartupLogPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoQualityAnalyzer", "startup.log");

    internal static void RecordStartup(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StartupLogPath)!);
            System.IO.File.AppendAllText(StartupLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { }
    }
}
