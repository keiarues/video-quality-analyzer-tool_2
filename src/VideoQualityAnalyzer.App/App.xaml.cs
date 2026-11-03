using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
