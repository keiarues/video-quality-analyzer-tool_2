using System.Windows;

namespace VideoQualityAnalyzer.GpuPreview;

// 専用テスト画面も主画面と同じ比較部品を使用する。
public sealed class ComparisonWindow : Window
{
    public ComparisonWindow(string leftPath, string rightPath, long frameNumber,
        bool showWipe = false, bool benchmarkMode = false, bool captureBenchmark = false,
        bool verifyComparison = false, bool verifySelection = false,
        IEnumerable<string>? videoPaths = null, bool showBlur = true)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "フレーム比較";
        Width = 1500;
        Height = 700;
        var panel = new ComparisonPanel(leftPath, rightPath, frameNumber, showWipe,
            benchmarkMode, captureBenchmark, verifyComparison, verifySelection, videoPaths, showBlur);
        if (Environment.GetCommandLineArgs().Contains("--zoom-test")) panel.EnableZoomTest();
        if (Environment.GetCommandLineArgs().Contains("--zoom-test")) Title = "拡大縮小・表示位置同期テスト";
        Content = panel;
        Closed += (_, _) => panel.Dispose();
    }
}
