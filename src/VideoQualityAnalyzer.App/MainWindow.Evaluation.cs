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

    private void ClearEvaluation_Click(object sender, RoutedEventArgs e)
    {
        if (evaluationCancellation is not null) return;
        if (MessageBox.Show(this, "すべての動画の評価結果（VMAF・SSIM・PSNR）をクリアしますか？",
            "評価クリア", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        qualityResults.Clear();
        RefreshQualityValues();
        RefreshVideoOrder();
        SetWindowStatus("すべての評価結果をクリアしました。");
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
            foreach (var metric in selected)
            foreach (var candidate in candidates)
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
        EvaluationClearButton.IsEnabled = !running;
        EvaluationStartButton.Content = running ? "評価を中止" : "評価を開始";
        EvaluationStartButton.IsEnabled = true;
        VmafCheck.IsEnabled = SsimCheck.IsEnabled = PsnrCheck.IsEnabled = !running;
        AddVideoButton.IsEnabled = SetReferenceButton.IsEnabled = RemoveVideoButton.IsEnabled = !running;
    }

    private Task ExportQualityCsvAsync(string path, bool frames)
    {
        var reference = videos.FirstOrDefault(entry => entry.IsReference)?.Descriptor
            ?? throw new InvalidOperationException("元動画がありません。");
        var items = videos.Where(entry => !entry.IsReference)
            .Select(entry => new QualityCsvItem(entry.Descriptor, entry.Group)).ToArray();
        var results = new Dictionary<(string VideoId, QualityMetric Metric), QualityResult>(qualityResults);
        if (!QualityCsvExporter.HasResults(items, results))
            throw new InvalidOperationException("完了した評価結果がありません。");
        var text = frames ? QualityCsvExporter.Frames(reference, items, results)
            : QualityCsvExporter.Summary(reference, items, results);
        return QualityCsvExporter.SaveAsync(path, text);
    }

    private async void ExportFrameCsv_Click(object sender, RoutedEventArgs e) => await ExportCsvFromMenuAsync(true);
    private async void ExportFileCsv_Click(object sender, RoutedEventArgs e) => await ExportCsvFromMenuAsync(false);

    private async Task ExportCsvFromMenuAsync(bool frames)
    {
        var items = videos.Where(entry => !entry.IsReference)
            .Select(entry => new QualityCsvItem(entry.Descriptor, entry.Group)).ToArray();
        var reference = videos.FirstOrDefault(entry => entry.IsReference)?.Descriptor;
        if (reference is null || !QualityCsvExporter.HasResults(items, qualityResults))
        { SetWindowStatus("CSVに出力できる完了済みの評価結果がありません。"); return; }
        var dialog = QualityCsvSaveDialog.Create(DateTime.Now, reference, frames, items, qualityResults);
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await ExportQualityCsvAsync(dialog.FileName, frames);
            SetWindowStatus(frames ? "フレームCSVを保存しました。" : "ファイルCSVを保存しました。");
        }
        catch (Exception exception)
        {
            SetWindowStatus("CSVを保存できません: " + exception.Message);
        }
    }
}
