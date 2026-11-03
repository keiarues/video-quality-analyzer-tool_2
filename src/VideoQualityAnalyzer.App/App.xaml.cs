using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
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
