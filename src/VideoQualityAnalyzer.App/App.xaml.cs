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
        if (e.Args.Contains("--test-graph-image"))
        {
            base.OnStartup(e);
            var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/graph-image"));
            MainWindow = new GraphImageTestWindow(e.Args.Contains("--auto"), output);
            MainWindow.Show();
            return;
        }
        if (e.Args.Contains("--test-folder-drop"))
        {
            base.OnStartup(e);
            var index = Array.IndexOf(e.Args, "--test-folder-drop");
            var folder = index + 1 < e.Args.Length ? e.Args[index + 1] : null;
            MainWindow = new FolderDropTestWindow(folder, e.Args.Contains("--auto"));
            MainWindow.Show();
            return;
        }
        if (e.Args.Contains("--verify-ffmpeg-settings"))
        {
            base.OnStartup(e);
            MainWindow = new SettingsWindow(verifyDownload: true);
            MainWindow.Show();
            return;
        }
        if (e.Args.Contains("--test-ffmpeg-download"))
        {
            base.OnStartup(e);
            MainWindow = new FfmpegDownloadTestWindow(e.Args.Contains("--auto"));
            MainWindow.Show();
            return;
        }
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
        if (e.Args.Length >= 2 && e.Args[0] is "--prepare-graph-test" or "--graph-playback-test")
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var path = System.IO.Path.GetFullPath(e.Args[1]);
                if (e.Args[0] == "--prepare-graph-test")
                {
                    var probePath = VideoQualityAnalyzer.Media.FfprobeService.FindExecutable()
                        ?? throw new InvalidOperationException("FFprobeが見つかりません。");
                    var probe = new VideoQualityAnalyzer.Media.FfprobeService(probePath);
                    var reference = await probe.ProbeAsync(e.Args[2], CancellationToken.None);
                    var referenceStamp = VideoQualityAnalyzer.Media.TestVideoStamp.Read(reference.Path);
                    var entries = new List<VideoQualityAnalyzer.Media.SsimTestEntry>();
                    var service = new VideoQualityAnalyzer.Media.QualityEvaluationService(
                        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(probePath)!, "ffmpeg.exe"));
                    foreach (var videoPath in e.Args.Skip(3))
                    {
                        var video = await probe.ProbeAsync(videoPath, CancellationToken.None);
                        var stamp = VideoQualityAnalyzer.Media.TestVideoStamp.Read(video.Path);
                        var result = await service.EvaluateAsync(reference, video, VideoQualityAnalyzer.Media.QualityMetric.Ssim, null, CancellationToken.None);
                        if (stamp != VideoQualityAnalyzer.Media.TestVideoStamp.Read(video.Path)) throw new InvalidOperationException("評価中に動画が変更されました。");
                        entries.Add(new(video, stamp, result));
                    }
                    if (entries.Count == 0 || referenceStamp != VideoQualityAnalyzer.Media.TestVideoStamp.Read(reference.Path))
                        throw new InvalidOperationException("比較動画がないか、基準動画が変更されました。");
                    await new VideoQualityAnalyzer.Media.SsimTestCache(1, reference, referenceStamp, entries.ToArray()).SaveAsync(path);
                    Shutdown(0);
                }
                else
                {
                    var cache = await VideoQualityAnalyzer.Media.SsimTestCache.LoadAsync(path);
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                    MainWindow = new GraphPlaybackTestWindow(cache, path, e.Args.Contains("--auto"), e.Args.Contains("--verify-operations"));
                    MainWindow.Show();
                }
            }
            catch (Exception error)
            {
                System.IO.File.WriteAllText(System.IO.Path.GetFullPath(e.Args[1]) + ".error.txt", error.ToString());
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--verify-groups"))
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var entry = new VideoQualityAnalyzer.Core.VideoEntry(new("test", "test", 64, 64,
                "ffv1", 30, 30, TimeSpan.FromSeconds(1), false, 30, 1));
            var dialog = new GroupWindow([entry], true);
            var applied = dialog.ShowDialog();
            var newNameApplied = applied == true && entry.Group == "検証グループ";
            var existingDialog = new GroupWindow([entry], true, ["既存グループ", "既存グループ", ""], true);
            var existingApplied = existingDialog.ShowDialog();
            Shutdown(newNameApplied && existingApplied == true && entry.Group == "既存グループ" ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--verify-evaluation"))
        {
            base.OnStartup(e);
            try
            {
                var probe = new VideoQualityAnalyzer.Media.FfprobeService(
                    VideoQualityAnalyzer.Media.FfprobeService.FindExecutable()!);
                var paths = e.Args.Where(System.IO.File.Exists).ToArray();
                var reference = await probe.ProbeAsync(paths[0], CancellationToken.None);
                var candidate = await probe.ProbeAsync(paths[1], CancellationToken.None);
                MainWindow = new EvaluationWindow(reference, [candidate], new Dictionary<
                    (string, VideoQualityAnalyzer.Media.QualityMetric), VideoQualityAnalyzer.Media.QualityResult>(), true);
                MainWindow.Show();
            }
            catch { Shutdown(1); }
            return;
        }
        // 本体に組み込んだ比較画面を既存の自動操作で検証する。
        var verification = e.Args.FirstOrDefault(arg => arg.StartsWith("--verify-comparison="));
        if (verification is not null)
        {
            base.OnStartup(e);
            var paths = e.Args.Where(System.IO.File.Exists).ToArray();
            if (paths.Length < 2 || !long.TryParse(verification.Split('=')[1], out var frameNumber))
            {
                Shutdown(1);
                return;
            }
            MainWindow = new VideoQualityAnalyzer.GpuPreview.ComparisonWindow(
                paths[0], paths[1], frameNumber, verifyComparison: true,
                videoPaths: paths, showBlur: false);
            MainWindow.Show();
            return;
        }
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
