using System.Windows;

namespace VideoQualityAnalyzer.App;

public partial class App
{
    private bool TryOpenTestWindow(StartupEventArgs e)
    {
        if (e.Args.Contains("--test-evaluation-target-summary"))
        {
            base.OnStartup(e);
            MainWindow = new EvaluationTargetSummaryTestWindow(e.Args.Contains("--auto"));
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-evaluated-playback"))
        {
            base.OnStartup(e);
            MainWindow = new EvaluatedPlaybackTestWindow(e.Args.Where(System.IO.File.Exists).ToArray());
            MainWindow.Show(); return true;
        }
        if (e.Args.Contains("--test-production-partial"))
        {
            base.OnStartup(e);
            MainWindow = new InputSeekTestWindow(e.Args.Contains("--auto"), e.Args.Where(System.IO.File.Exists).ToArray(), continuousInput: true, productionVerification: true);
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-continuous-input"))
        {
            base.OnStartup(e);
            MainWindow = new InputSeekTestWindow(e.Args.Contains("--auto"), e.Args.Where(System.IO.File.Exists).ToArray(), continuousInput: true);
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-input-seek"))
        {
            base.OnStartup(e);
            MainWindow = new InputSeekTestWindow(e.Args.Contains("--auto"), e.Args.Where(System.IO.File.Exists).ToArray());
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-metric-cards"))
        {
            base.OnStartup(e);
            MainWindow = new QualityMetricCardsTestWindow(e.Args.Contains("--auto"));
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-partial-evaluation"))
        {
            base.OnStartup(e);
            MainWindow = new PartialEvaluationTestWindow(e.Args.Contains("--auto"), e.Args.Where(System.IO.File.Exists).ToArray());
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-csv-export"))
        {
            base.OnStartup(e);
            MainWindow = new CsvExportTestWindow(e.Args.Contains("--auto"));
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-graph-image"))
        {
            base.OnStartup(e);
            var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/graph-image"));
            MainWindow = new GraphImageSaveWindow(e.Args.Contains("--auto"), output);
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-folder-drop"))
        {
            base.OnStartup(e);
            var index = Array.IndexOf(e.Args, "--test-folder-drop");
            var folder = index + 1 < e.Args.Length ? e.Args[index + 1] : null;
            MainWindow = new FolderDropTestWindow(folder, e.Args.Contains("--auto"));
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--verify-ffmpeg-settings"))
        {
            base.OnStartup(e);
            MainWindow = new SettingsWindow(verifyDownload: true);
            MainWindow.Show();
            return true;
        }
        if (e.Args.Contains("--test-ffmpeg-download"))
        {
            base.OnStartup(e);
            MainWindow = new FfmpegDownloadTestWindow(e.Args.Contains("--auto"));
            MainWindow.Show();
            return true;
        }
        return false;
    }
}
