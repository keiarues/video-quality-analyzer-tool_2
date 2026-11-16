using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyMainContinuousEvaluationAsync()
    {
        try
        {
            var reference = videos.First(entry => entry.IsReference).Descriptor;
            var candidate = videos.First(entry => !entry.IsReference);
            VideoList.SelectedItem = candidate;
            await comparisonChangeTask;
            var configured = reference.Duration.TotalSeconds > 30
                ? new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 10, Count: 6)
                : new EvaluationSelection(PartialEvaluationMode.TimeRange, 0, Math.Min(reference.Duration.TotalSeconds, candidate.Descriptor.Duration.TotalSeconds));
            var selection = configured.WithComparisonTimeline(reference, [candidate.Descriptor]);
            var ranges = selection.CreatePlan(reference)!;
            var numbers = ranges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset)).ToArray();
            var expectedMethod = Environment.GetCommandLineArgs().Contains("--expect-sequential")
                ? QualityEvaluationMethod.PartialSequential : QualityEvaluationMethod.PartialContinuous;
            var initialProgress = qualityGraphPanel!.EvaluationProgressUpdateCount;
            var clock = Stopwatch.StartNew();
            StartEvaluationForVerification(selection, [QualityMetric.Vmaf, QualityMetric.Ssim, QualityMetric.Psnr]);
            await evaluationTask; clock.Stop();
            var rows = new List<object>();
            foreach (var metric in Enum.GetValues<QualityMetric>())
            {
                if (!qualityResults.TryGetValue((candidate.Descriptor.Id, metric), out var result) ||
                    result.Method != expectedMethod || !result.IsReusableFor(selection) || !result.Frames.Select(frame => frame.Number).SequenceEqual(numbers))
                    throw new InvalidDataException($"通常画面の{metric}の方式・元番号・件数が不正です。");
                qualityGraphPanel.SelectMetric(metric);
                if (qualityGraphPanel.FilePointCount != 1 || qualityGraphPanel.FrameSegmentCount != ranges.Count)
                    throw new InvalidDataException("通常画面のグラフに3指標を反映できません。");
                rows.Add(new { Metric = metric.ToString(), Method = result.Method.ToString(), result.PartialVersion, Frames = result.Frames.Count, result.Average });
            }
            if (qualityGraphPanel.EvaluationProgressUpdateCount < initialProgress + 6 || evaluationCancellation is not null || !EvaluationRangeButton.IsEnabled)
                throw new InvalidOperationException("通常画面の進捗・操作復帰が不正です。");
            var fallbackNotice = expectedMethod != QualityEvaluationMethod.PartialSequential || WindowStatusText.Text.Contains("先頭復号");
            if (!fallbackNotice) throw new InvalidOperationException("先頭復号への切り替えが通知されません。");
            var retained = qualityResults.Values.ToArray();
            await EvaluateMainSelectionAsync();
            if (!retained.All(result => qualityResults.Values.Any(current => ReferenceEquals(current, result))))
                throw new InvalidOperationException("完了済みの新方式結果を再計算しました。");
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results/production-partial"));
            Directory.CreateDirectory(directory);
            await ExportQualityCsvAsync(Path.Combine(directory, $"main-{candidate.Descriptor.Codec}-summary.csv"), false);
            var csvPath = Path.Combine(directory, $"main-{candidate.Descriptor.Codec}-frames.csv");
            await ExportQualityCsvAsync(csvPath, true);
            var csv = await File.ReadAllTextAsync(csvPath);
            if (!csv.Contains("部分評価v2") || csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 1 + numbers.Length * 3)
                throw new InvalidDataException("通常画面のCSVの方式・件数が不正です。");
            await File.WriteAllTextAsync(Path.Combine(directory, $"main-{candidate.Descriptor.Codec}.json"), JsonSerializer.Serialize(new
            { Codec = candidate.Descriptor.Codec, Conditions = selection.Description, WallSeconds = clock.Elapsed.TotalSeconds,
                ProgressUpdates = qualityGraphPanel.EvaluationProgressUpdateCount - initialProgress, Results = rows, Reused = true, Csv = true,
                FallbackNotice = fallbackNotice }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"通常画面：{candidate.Descriptor.Codec}・3指標各{numbers.Length}フレーム・{expectedMethod}・グラフ・CSV・再利用が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
    }
}
