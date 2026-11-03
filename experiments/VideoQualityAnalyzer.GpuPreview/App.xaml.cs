using System.IO;
using System.Windows;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--wipe-drag-test"))
        {
            new WipeDragTestWindow().Show();
            return;
        }
        var selectionTestOption = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--selection-switch-test=", StringComparison.Ordinal));
        if (selectionTestOption is not null)
        {
            var selectionPaths = e.Args.Where(argument => argument != selectionTestOption).ToArray();
            if (selectionPaths.Length != 3 ||
                !long.TryParse(selectionTestOption[(selectionTestOption.IndexOf('=') + 1)..], out var selectionFrame) ||
                selectionFrame < 0)
            {
                MessageBox.Show("元フレーム番号と3本の動画パスを指定してください。", "動画選択の検証");
                Shutdown(2);
                return;
            }
            new SelectionSwitchTestWindow(selectionPaths[0], selectionPaths[1],
                selectionPaths[2], selectionFrame).Show();
            return;
        }
        var seekTestOption = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--seek-handoff-test=", StringComparison.Ordinal));
        if (seekTestOption is not null)
        {
            var seekPaths = e.Args.Where(argument => argument != seekTestOption).ToArray();
            if (seekPaths.Length != 2 ||
                !long.TryParse(seekTestOption[(seekTestOption.IndexOf('=') + 1)..], out var frameNumber) ||
                frameNumber < 0)
            {
                MessageBox.Show("元フレーム番号と左右の動画パスを指定してください。", "シーク検証");
                Shutdown(2);
                return;
            }
            new SeekHandoffTestWindow(seekPaths[0], seekPaths[1], frameNumber, automatic: true).Show();
            return;
        }
        var direct = e.Args.Contains("--direct");
        var verifyFrames = e.Args.Contains("--verify-frames");
        var statistics = e.Args.Contains("--stats") || verifyFrames;
        var comparisonOption = e.Args.FirstOrDefault(argument =>
            argument.StartsWith("--compare-frame=", StringComparison.Ordinal) ||
            argument.StartsWith("--wipe-frame=", StringComparison.Ordinal) ||
            argument.StartsWith("--benchmark-wipe=", StringComparison.Ordinal) ||
            argument.StartsWith("--benchmark-heatmap=", StringComparison.Ordinal) ||
            argument.StartsWith("--benchmark-capture-wipe=", StringComparison.Ordinal) ||
            argument.StartsWith("--benchmark-capture-heatmap=", StringComparison.Ordinal) ||
            argument.StartsWith("--verify-comparison=", StringComparison.Ordinal) ||
            argument.StartsWith("--verify-selection=", StringComparison.Ordinal));
        var paths = e.Args.Where(argument => argument != "--direct" && argument != "--stats" &&
            argument != "--verify-frames" && argument != comparisonOption).ToArray();
        if (comparisonOption is not null)
        {
            var showWipe = comparisonOption.StartsWith("--wipe-frame=", StringComparison.Ordinal) ||
                comparisonOption.Contains("wipe=", StringComparison.Ordinal);
            var benchmark = comparisonOption.StartsWith("--benchmark-", StringComparison.Ordinal);
            var capture = comparisonOption.StartsWith("--benchmark-capture-", StringComparison.Ordinal);
            var verifyComparison = comparisonOption.StartsWith("--verify-comparison=", StringComparison.Ordinal);
            var verifySelection = comparisonOption.StartsWith("--verify-selection=", StringComparison.Ordinal);
            var prefixLength = comparisonOption.IndexOf('=') + 1;
            if (!long.TryParse(comparisonOption.AsSpan(prefixLength), out var frameNumber) ||
                frameNumber < 0 || paths.Length < 2 || !File.Exists(paths[0]) || !File.Exists(paths[1]))
            {
                MessageBox.Show("元フレーム番号と左右の動画パスを指定してください。", "静止フレーム比較");
                Shutdown(2);
                return;
            }
            new ComparisonWindow(paths[0], paths[1], frameNumber, showWipe,
                benchmark, capture, verifyComparison, verifySelection).Show();
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
