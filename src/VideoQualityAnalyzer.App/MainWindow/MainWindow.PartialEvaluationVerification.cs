using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private void StartEvaluationForVerification(EvaluationSelection selection, QualityMetric[] metrics)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<EvaluationSettingsWindow>().Single();
            dialog.ApplyForVerification(selection, metrics);
        }));
        EvaluationRangeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private async Task VerifyMainPartialEvaluationAsync()
    {
        try
        {
            var reference = videos.First(entry => entry.IsReference).Descriptor;
            var candidate = videos.First(entry => !entry.IsReference);
            VideoList.SelectedItem = candidate;
            await comparisonChangeTask;
            var key = (candidate.Descriptor.Id, QualityMetric.Ssim);
            await EvaluateMainSelectionAsync();
            var full = qualityResults[key];
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results/partial-evaluation"));
            Directory.CreateDirectory(directory);
            void Save(Window window, string name)
            {
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render((Visual)window.Content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
            }
            async Task Select(EvaluationSelection selection)
            {
                selection = selection.WithComparisonTimeline(reference, [candidate.Descriptor]);
                var progressCount = qualityGraphPanel!.EvaluationProgressUpdateCount;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = Application.Current.Windows.OfType<EvaluationSettingsWindow>().Single();
                    dialog.VerifyStableRangeLayout();
                    Save(dialog, "main-settings.png");
                    dialog.ApplyForVerification(selection, [QualityMetric.Ssim]);
                }));
                EvaluationRangeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var started = evaluationCancellation is not null;
                var plannedRanges = selection.CreatePlan(reference);
                if (started && plannedRanges is not null && Math.Abs(qualityGraphPanel.PlayheadPosition.TotalSeconds -
                    plannedRanges[0].Start / reference.AverageFrameRate) > 0.000001)
                    throw new InvalidOperationException("部分評価の処理位置が先頭の評価区間から始まっていません。");
                await evaluationTask;
                if (started && plannedRanges is not null &&
                    (qualityGraphPanel.EvaluationProgressUpdateCount < progressCount + 2 ||
                    qualityGraphPanel.LastEvaluationProgressPosition.TotalSeconds <= plannedRanges[0].Start / reference.AverageFrameRate))
                    throw new InvalidOperationException("部分評価の実計算中にグラフの処理位置が進んでいません。");
                if (!evaluationMetrics.SequenceEqual([QualityMetric.Ssim]))
                    throw new InvalidOperationException("設定画面の指標が主画面に同期されません。");
                if (evaluationResults.Selection != selection) throw new InvalidOperationException("設定画面から条件が適用されません。");
            }
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<EvaluationSettingsWindow>().Single();
                dialog.ApplyForVerification(new(PartialEvaluationMode.TimeRange, 0, reference.Duration.TotalSeconds + 1));
                if (dialog.DialogResult is not null) throw new InvalidOperationException("不正な範囲が適用されました。");
                dialog.ApplyForVerification(EvaluationSelection.Full, []);
                if (dialog.DialogResult is not null) throw new InvalidOperationException("指標なしで評価を開始できてしまいます。");
                dialog.Close();
            }));
            EvaluationRangeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (evaluationResults.Selection != EvaluationSelection.Full || !ReferenceEquals(full, qualityResults[key]))
                throw new InvalidOperationException("設定のキャンセルで条件または結果が変わりました。");
            evaluationMetrics = [QualityMetric.Vmaf];
            foreach (var configuredSelection in new[]
            {
                new EvaluationSelection(PartialEvaluationMode.TimeRange, 0.5, 1.5),
                new EvaluationSelection(PartialEvaluationMode.Periodic, PeriodSeconds: 1, LengthSeconds: 0.25),
                new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 0.25, Count: 2)
            })
            {
                var selection = configuredSelection.WithComparisonTimeline(reference, [candidate.Descriptor]);
                await Select(selection);
                if (qualityResults.Count != 1 || evaluationResults.Selection != selection || !evaluationResults.Selection.IsPartial)
                    throw new InvalidOperationException("異なる条件の結果が混在しています。");
                var result = qualityResults[key];
                var ranges = selection.CreatePlan(reference)!;
                var numbers = ranges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset));
                if (result.Selection != selection || !result.Frames.Select(frame => frame.Number).SequenceEqual(numbers) || result.Average < 0.999 ||
                    result.PartialVersion != QualityResult.CurrentPartialVersion || result.Method != QualityEvaluationMethod.PartialContinuous)
                    throw new InvalidOperationException("部分評価の条件・元フレーム番号・SSIMが不正です。");
                qualityGraphPanel!.SelectMetric(QualityMetric.Ssim);
                if (qualityGraphPanel.FrameSegmentCount != ranges.Count || qualityGraphPanel.FilePointCount != 1 || candidate.SsimAverage != result.Average)
                    throw new InvalidOperationException("未評価区間の線分分離または一覧・平均グラフが不正です。");
                await ExportQualityCsvAsync(Path.Combine(directory, "main-partial-summary.csv"), false);
                await ExportQualityCsvAsync(Path.Combine(directory, "main-partial-frames.csv"), true);
                var text = await File.ReadAllTextAsync(Path.Combine(directory, "main-partial-frames.csv"));
                if (!text.Contains("評価条件") || !text.Contains(selection.Description) || !text.Contains(result.MethodDescription))
                    throw new InvalidOperationException("CSVに評価条件がありません。");
                await Select(EvaluationSelection.Full);
                if (!ReferenceEquals(full, qualityResults[key])) throw new InvalidOperationException("全編評価が失われました。");
                await Select(selection);
                if (!ReferenceEquals(result, qualityResults[key])) throw new InvalidOperationException("部分評価が復元されません。");
                await EvaluateMainSelectionAsync();
                if (!ReferenceEquals(result, qualityResults[key])) throw new InvalidOperationException("完了結果が再計算されました。");
                Save(this, "main-partial.png");
                var bitmap = QualityGraphImageRenderer.Render(GraphImageTarget.Frames, 1000, 500, 1,
                    QualityMetric.Ssim, [new QualityGraphImageSeries(candidate.Descriptor, result, System.Windows.Media.Brushes.RoyalBlue,
                        true, candidate.Descriptor.AverageBitrateKbps ?? 0, null)]);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(directory, "main-partial-graph.png"))) encoder.Save(stream);
            }
            var activeSelection = evaluationResults.Selection;
            var old = qualityResults[key] with { PartialVersion = 0, Method = QualityEvaluationMethod.Legacy };
            qualityResults[key] = old;
            evaluationMetrics = [QualityMetric.Ssim, QualityMetric.Psnr, QualityMetric.Vmaf];
            await EvaluateMainSelectionAsync();
            if (ReferenceEquals(old, qualityResults[key])) throw new InvalidOperationException("旧方式の部分評価が再利用されました。");
            var expectedNumbers = activeSelection.CreatePlan(reference)!.SelectMany(range =>
                Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset)).ToArray();
            foreach (var metric in evaluationMetrics)
            {
                if (!qualityResults.TryGetValue((candidate.Descriptor.Id, metric), out var actual) ||
                    !actual.IsReusableFor(activeSelection) || actual.Method != QualityEvaluationMethod.PartialContinuous ||
                    !actual.Frames.Select(frame => frame.Number).SequenceEqual(expectedNumbers))
                    throw new InvalidOperationException($"主画面の{metric}部分評価が不正です。");
                qualityGraphPanel!.SelectMetric(metric);
                if (qualityGraphPanel.FilePointCount != 1 || qualityGraphPanel.FrameSegmentCount != activeSelection.CreatePlan(reference)!.Count)
                    throw new InvalidOperationException("3指標の部分評価グラフが不正です。");
            }
            await ExportQualityCsvAsync(Path.Combine(directory, "main-production-summary.csv"), false);
            await ExportQualityCsvAsync(Path.Combine(directory, "main-production-frames.csv"), true);
            var frameCsv = await File.ReadAllTextAsync(Path.Combine(directory, "main-production-frames.csv"));
            if (!frameCsv.Contains("評価方式") || !frameCsv.Contains("部分評価v2") ||
                !frameCsv.Contains("VMAF") || !frameCsv.Contains("PSNR") || frameCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 1 + expectedNumbers.Length * 3)
                throw new InvalidOperationException("3指標のCSVに元番号・件数・方式を保持できません。");
            await File.WriteAllTextAsync(Path.Combine(directory, "main-production.json"), System.Text.Json.JsonSerializer.Serialize(new
            { Metrics = 3, FramesPerMetric = expectedNumbers.Length, PartialVersion = QualityResult.CurrentPartialVersion,
                OldResultRecomputed = true, Graphs = true, Csv = true }));
            qualityResults.Remove((candidate.Descriptor.Id, QualityMetric.Psnr));
            StartEvaluationForVerification(evaluationResults.Selection, [QualityMetric.Ssim, QualityMetric.Psnr]);
            EvaluationRangeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await evaluationTask;
            if (qualityResults.ContainsKey((candidate.Descriptor.Id, QualityMetric.Psnr)) || !EvaluationRangeButton.IsEnabled)
                throw new InvalidOperationException("部分評価の中止後に不正な結果または操作制限が残っています。");
            evaluationMetrics = [QualityMetric.Ssim];
            evaluationResults.Clear();
            ApplyEvaluationSelection(EvaluationSelection.Full);
            if (qualityResults.Count != 0) throw new InvalidOperationException("全条件のクリア後に結果が復活しました。");
            Console.WriteLine("主画面の部分評価：設定・3方式・条件別再利用・中止・一覧・グラフ区間分離・CSVが合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
    }
}
