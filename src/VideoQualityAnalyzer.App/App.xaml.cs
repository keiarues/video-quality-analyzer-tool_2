using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
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
            Shutdown(applied == true && entry.Group == "検証グループ" ? 0 : 1);
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
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
