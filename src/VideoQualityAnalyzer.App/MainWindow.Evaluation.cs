using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private CancellationTokenSource? evaluationCancellation;
    private Task evaluationTask = Task.CompletedTask;
    private bool evaluationCloseRequested;
    private int pendingVideoLoads;

    private void InitializeEvaluationControls()
    {
        foreach (var (check, metric) in new[] { (VmafCheck, QualityMetric.Vmaf), (SsimCheck, QualityMetric.Ssim), (PsnrCheck, QualityMetric.Psnr) })
        {
            check.MouseEnter += (_, _) => EvaluationHelpText.Text = QualityMetricHelp.GetText(metric);
        }
    }

    private void Evaluate_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null)
        {
            evaluationCancellation.Cancel();
            EvaluationStartButton.Content = "中止中…";
            EvaluationStartButton.IsEnabled = false;
            return;
        }
        evaluationTask = EvaluateMainSelectionAsync();
    }

    private async Task EvaluateMainSelectionAsync()
    {
        if (pendingVideoLoads > 0) { SetWindowStatus("動画情報の取得が終わるまでお待ちください。", evaluation: true); return; }
        var reference = videos.FirstOrDefault(entry => entry.Descriptor.Id == referenceId)?.Descriptor;
        var candidates = SelectedOrAllVideoEntries().Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray();
        var selected = new[] { (VmafCheck, QualityMetric.Vmaf), (SsimCheck, QualityMetric.Ssim), (PsnrCheck, QualityMetric.Psnr) }
            .Where(pair => pair.Item1.IsChecked == true).Select(pair => pair.Item2).ToArray();
        if (reference is null || candidates.Length == 0 || selected.Length == 0)
        { SetWindowStatus("元動画、比較動画と評価指標を選択してください。", evaluation: true); return; }
        var executable = FfmpegRgbReader.FindExecutable();
        if (executable is null) { SetWindowStatus("FFmpegが見つかりません。", evaluation: true); return; }
        PausePlayback();
        using var source = new CancellationTokenSource();
        evaluationCancellation = source;
        SetEvaluationState(true);
        var failures = new List<string>();
        var progressGeneration = 0;
        try
        {
            var evaluator = new QualityEvaluationService(executable);
            foreach (var candidate in candidates)
            foreach (var metric in selected)
            {
                source.Token.ThrowIfCancellationRequested();
                var key = (candidate.Id, metric);
                if (qualityResults.ContainsKey(key)) continue;
                var currentProgressGeneration = ++progressGeneration;
                qualityGraphPanel?.SetEvaluationProgress(candidate, metric, TimeSpan.Zero);
                SetWindowStatus($"{candidate.Name} / {metric.ToString().ToUpperInvariant()} を評価中…", evaluation: true);
                try
                {
                    var progress = new Progress<TimeSpan>(position =>
                    {
                        if (ReferenceEquals(evaluationCancellation, source) && !source.IsCancellationRequested && currentProgressGeneration == progressGeneration)
                        {
                            qualityGraphPanel?.SetEvaluationProgress(candidate, metric, position);
                            SetWindowStatus($"{candidate.Name} / {metric.ToString().ToUpperInvariant()}: {position.TotalSeconds:F1}秒を処理済み", evaluation: true);
                        }
                    });
                    var result = await evaluator.EvaluateAsync(reference, candidate, metric, progress, source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    qualityResults[key] = result;
                    RefreshQualityValues();
                    RefreshVideoOrder();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { failures.Add($"{candidate.Name} / {metric}: {exception.Message}"); }
            }
            WindowStatusText.Foreground = failures.Count == 0 ? UiTheme.Emphasis : UiTheme.Warning;
            SetWindowStatus(failures.Count == 0 ? "評価が完了しました。" : $"評価を終了しました。失敗{failures.Count}件。{failures[^1]}", evaluation: true);
        }
        catch (OperationCanceledException) { WindowStatusText.Foreground = UiTheme.Warning; SetWindowStatus("評価を中止しました。完了済みの結果は保持しています。", evaluation: true); }
        finally
        {
            qualityGraphPanel?.EndEvaluationProgress(CurrentPosition());
            evaluationCancellation = null;
            SetEvaluationState(false);
            RefreshQualityValues();
            RefreshVideoOrder();
            if (evaluationCloseRequested) Close();
        }
    }

    private void SetEvaluationState(bool running)
    {
        if (running) WindowStatusText.Foreground = UiTheme.Emphasis;
        EvaluationStartButton.Content = running ? "評価を中止" : "評価を開始";
        EvaluationStartButton.IsEnabled = true;
        VmafCheck.IsEnabled = SsimCheck.IsEnabled = PsnrCheck.IsEnabled = !running;
        AddVideoButton.IsEnabled = SetReferenceButton.IsEnabled = RemoveVideoButton.IsEnabled = !running;
    }

    // 今後のメニューから呼び出せるよう、CSV出力を画面ボタンから独立して保持する。
    private Task ExportQualityCsvAsync(string path, bool frames)
    {
        var candidates = videos.Where(entry => !entry.IsReference).Select(entry => entry.Descriptor).ToArray();
        var text = frames ? QualityCsvExporter.Frames(candidates, qualityResults) : QualityCsvExporter.Summary(candidates, qualityResults);
        return System.IO.File.WriteAllTextAsync(path, text, new System.Text.UTF8Encoding(true));
    }
}
