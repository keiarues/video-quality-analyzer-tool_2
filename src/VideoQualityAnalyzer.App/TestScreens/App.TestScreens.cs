using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class App
{
    private bool TryOpenTestWindow(StartupEventArgs e)
    {
        var automatic = e.Args.Contains("--auto");
        string[] Files() => e.Args.Where(System.IO.File.Exists).ToArray();
        Func<Window>? createWindow = null;

        if (e.Args.Contains("--test-evaluation-target-summary"))
            createWindow = () => new EvaluationTargetSummaryTestWindow(automatic);
        else if (e.Args.Contains("--test-evaluated-playback"))
            createWindow = () => new EvaluatedPlaybackTestWindow(Files());
        else if (e.Args.Contains("--test-production-partial"))
            createWindow = () => new InputSeekTestWindow(automatic, Files(), continuousInput: true, productionVerification: true);
        else if (e.Args.Contains("--test-continuous-input"))
            createWindow = () => new InputSeekTestWindow(automatic, Files(), continuousInput: true);
        else if (e.Args.Contains("--test-input-seek"))
            createWindow = () => new InputSeekTestWindow(automatic, Files());
        else if (e.Args.Contains("--test-metric-cards"))
            createWindow = () => new QualityMetricCardsTestWindow(automatic);
        else if (e.Args.Contains("--test-partial-evaluation"))
            createWindow = () => new PartialEvaluationTestWindow(automatic, Files());
        else if (e.Args.Contains("--test-csv-export"))
            createWindow = () => new CsvExportTestWindow(automatic);
        else if (e.Args.Contains("--test-graph-image"))
        {
            var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/graph-image"));
            createWindow = () => new GraphImageSaveWindow(automatic, output);
        }
        else if (e.Args.Contains("--test-folder-drop"))
        {
            var index = Array.IndexOf(e.Args, "--test-folder-drop");
            var folder = index + 1 < e.Args.Length ? e.Args[index + 1] : null;
            createWindow = () => new FolderDropTestWindow(folder, automatic);
        }
        else if (e.Args.Contains("--verify-ffmpeg-settings"))
            createWindow = () => new SettingsWindow(verifyDownload: true);
        else if (e.Args.Contains("--test-ffmpeg-download"))
            createWindow = () => new FfmpegDownloadTestWindow(automatic);

        if (createWindow is null) return false;
        base.OnStartup(e);
        MainWindow = createWindow();
        MainWindow.Show();
        return true;
    }
}
