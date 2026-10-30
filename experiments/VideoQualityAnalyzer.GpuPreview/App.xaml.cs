using System.IO;
using System.Windows;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var direct = e.Args.Contains("--direct");
        var verifyFrames = e.Args.Contains("--verify-frames");
        var statistics = e.Args.Contains("--stats") || verifyFrames;
        var comparisonOption = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--compare-frame=", StringComparison.Ordinal) ||
            argument.StartsWith("--wipe-frame=", StringComparison.Ordinal));
        var paths = e.Args.Where(argument => argument != "--direct" && argument != "--stats" &&
            argument != "--verify-frames" && argument != comparisonOption).ToArray();
        if (comparisonOption is not null)
        {
            var showWipe = comparisonOption.StartsWith("--wipe-frame=", StringComparison.Ordinal);
            var prefixLength = showWipe ? "--wipe-frame=".Length : "--compare-frame=".Length;
            if (!long.TryParse(comparisonOption.AsSpan(prefixLength), out var frameNumber) ||
                frameNumber < 0 || paths.Length < 2 || !File.Exists(paths[0]) || !File.Exists(paths[1]))
            {
                MessageBox.Show("元フレーム番号と左右の動画パスを指定してください。", "静止フレーム比較");
                Shutdown(2);
                return;
            }
            new ComparisonWindow(paths[0], paths[1], frameNumber, showWipe).Show();
            return;
        }
        var path = paths.FirstOrDefault() ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test-data/攻殻機動隊_ av1 --qvbr 48 --vpp-yadif.mkv"));
        var rightPaths = paths.Skip(1).ToArray();
        if (!File.Exists(path) || rightPaths.Any(candidate => !File.Exists(candidate)))
        {
            MessageBox.Show("動画ファイルのパスを指定してください。", "GPUプレビュー検証");
            Shutdown(2);
            return;
        }
        new MainWindow(path, rightPaths, direct, statistics, verifyFrames).Show();
    }
}
