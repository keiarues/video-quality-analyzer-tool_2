using System.IO;
using System.Windows;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var direct = e.Args.Contains("--direct");
        var statistics = e.Args.Contains("--stats");
        var paths = e.Args.Where(argument => argument != "--direct" && argument != "--stats").ToArray();
        var path = paths.FirstOrDefault() ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test-data/攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv"));
        var rightPaths = paths.Skip(1).ToArray();
        if (!File.Exists(path) || rightPaths.Any(candidate => !File.Exists(candidate)))
        {
            MessageBox.Show("動画ファイルのパスを指定してください。", "GPUプレビュー検証");
            Shutdown(2);
            return;
        }
        new MainWindow(path, rightPaths, direct, statistics).Show();
    }
}
