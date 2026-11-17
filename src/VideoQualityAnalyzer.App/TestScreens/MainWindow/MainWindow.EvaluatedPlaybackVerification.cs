using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public partial class MainWindow
{
    private async Task VerifyEvaluatedPlaybackAsync()
    {
        try
        {
            var reference = videos.First(entry => entry.IsReference).Descriptor;
            var paths = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).Select(Path.GetFullPath).ToArray();
            var candidate = videos.First(entry => Path.GetFullPath(entry.Descriptor.Path) == paths[1]);
            var unassessed = videos.First(entry => Path.GetFullPath(entry.Descriptor.Path) == paths[2]);
            VideoList.SelectedItem = candidate; await comparisonChangeTask;
            MuteCheck.IsChecked = true;
            if (EvaluatedOnlyCheck.IsEnabled || EvaluatedOnlyCheck.IsChecked == true) throw new InvalidOperationException("未評価で区間再生を有効にできます。");
            var rows = new List<object>();
            var selections = new[] { new EvaluationSelection(PartialEvaluationMode.TimeRange, 0.2, 0.6),
                new(PartialEvaluationMode.Periodic, PeriodSeconds: Math.Max(1, Math.Ceiling(reference.Duration.TotalSeconds / 3)), LengthSeconds: 0.2),
                new(PartialEvaluationMode.Uniform, LengthSeconds: 0.2, Count: 3) };
            foreach (var configured in selections)
            {
                comparisonPanel!.SelectMode((int)configured.Mode!.Value);
                ApplyEvaluationSelection(configured.WithComparisonTimeline(reference, [candidate.Descriptor]));
                evaluationMetrics = [QualityMetric.Ssim]; await EvaluateMainSelectionAsync();
                if (!EvaluatedOnlyCheck.IsEnabled) throw new InvalidOperationException("評価後もチェックボックスが無効です。");
                EvaluatedOnlyCheck.IsChecked = true; await comparisonChangeTask;
                var plan = evaluatedPlaybackPlan ?? throw new InvalidOperationException("再生区間がありません。");
                var shown = new List<long>();
                void Observe() { if (comparisonPanel!.CurrentFrameNumber is { } number) shown.Add(number); }
                comparisonPanel!.FrameShown += Observe;
                Play_Click(this, new RoutedEventArgs());
                var deadline = Stopwatch.StartNew();
                while (isPlaying && deadline.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
                comparisonPanel.FrameShown -= Observe;
                if (isPlaying || shown.Count == 0 || shown.Any(number => !plan.Contains(number)) || shown[^1] != plan.LastFrame ||
                    plan.Ranges.Any(range => !shown.Any(number => number >= range.Start && number < range.End)) || !RightMedia.IsMuted)
                    throw new InvalidDataException("通常画面で未評価フレームを提示したか、区間・末尾停止・右消音が不正です。");
                await comparisonPanel.VerifyCurrentPixelsAsync();
                Play_Click(this, new RoutedEventArgs()); await comparisonChangeTask; await Task.Delay(40); PausePlayback();
                if (!plan.Contains(comparisonPanel.CurrentFrameNumber ?? -1)) throw new InvalidOperationException("末尾からの再開が不正です。");
                StopPlayback(); await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != plan.FirstFrame) throw new InvalidOperationException("停止で評価区間の先頭へ戻りません。");
                if (plan.Ranges.Count > 1)
                {
                    ApplySeek(TimeSpan.FromSeconds((plan.Ranges[0].End - 1) / reference.AverageFrameRate)); await comparisonChangeTask;
                    await MoveFrameAsync(1); await comparisonChangeTask;
                    if (comparisonPanel.CurrentFrameNumber != plan.Ranges[1].Start) throw new InvalidOperationException("コマ送りが区間外へ入りました。");
                    await MoveFrameAsync(-1); await comparisonChangeTask;
                    if (comparisonPanel.CurrentFrameNumber != plan.Ranges[0].End - 1) throw new InvalidOperationException("逆コマ送りが区間外へ入りました。");
                    CommitGraphSeek(TimeSpan.FromSeconds((plan.Ranges[0].End + 2) / reference.AverageFrameRate)); await comparisonChangeTask;
                    if (comparisonPanel.CurrentFrameNumber != plan.Ranges[1].Start) throw new InvalidOperationException("未評価位置へのグラフシークが不正です。");
                }
                EvaluatedOnlyCheck.IsChecked = false; await comparisonChangeTask;
                ApplySeek(TimeSpan.Zero); await comparisonChangeTask;
                if (comparisonPanel.CurrentFrameNumber != 0) throw new InvalidOperationException("OFFで通常シークへ戻りません。");
                rows.Add(new { Mode = configured.Mode.ToString(), Ranges = plan.Ranges, Shown = shown, Outside = 0, EndedAt = plan.LastFrame, Steps = true, Seek = true, Replay = true });
            }
            var gap = evaluationResults.Selection.CreatePlan(reference)![0].End;
            ApplySeek(TimeSpan.FromSeconds((gap + 2) / reference.AverageFrameRate)); await comparisonChangeTask;
            var outsideDuringToggle = false;
            void ObserveToggle()
            {
                if (EvaluatedOnlyCheck.IsChecked == true && evaluatedPlaybackPlan is { } active &&
                    comparisonPanel!.CurrentFrameNumber is { } number && !active.Contains(number)) outsideDuringToggle = true;
            }
            comparisonPanel!.FrameShown += ObserveToggle;
            Play_Click(this, new RoutedEventArgs());
            EvaluatedOnlyCheck.IsChecked = true; await comparisonChangeTask; PausePlayback();
            if (outsideDuringToggle || !evaluatedPlaybackPlan!.Contains(comparisonPanel.CurrentFrameNumber ?? -1))
                throw new InvalidOperationException("再生中にONにすると未評価フレームへ進みます。");
            Play_Click(this, new RoutedEventArgs());
            EvaluatedOnlyCheck.IsChecked = false; await comparisonChangeTask; PausePlayback();
            comparisonPanel.FrameShown -= ObserveToggle;
            ApplySeek(TimeSpan.FromSeconds((gap + 2) / reference.AverageFrameRate)); await comparisonChangeTask;
            if (comparisonPanel.CurrentFrameNumber != gap + 2) throw new InvalidOperationException("再生中のOFFで通常シークへ戻りません。");
            EvaluatedOnlyCheck.IsChecked = true; await comparisonChangeTask;
            Play_Click(this, new RoutedEventArgs());
            VideoList.SelectedItem = unassessed; await comparisonChangeTask;
            if (isPlaying || EvaluatedOnlyCheck.IsEnabled || EvaluatedOnlyCheck.IsChecked == true) throw new InvalidOperationException("未評価動画への変更で制限が残るか、通常再生を続けています。");
            PreviewLeftSelect.SelectedItem = unassessed; PreviewRightSelect.SelectedItem = candidate; await comparisonChangeTask;
            if (EvaluatedOnlyCheck.IsEnabled) throw new InvalidOperationException("左側の未評価動画を検出できません。");
            previewLeftOverride = previewRightOverride = null;
            VideoList.SelectedItem = candidate; await comparisonChangeTask;
            UpdateSelection(); await comparisonChangeTask;
            if (!EvaluatedOnlyCheck.IsEnabled || EvaluatedOnlyCheck.IsChecked == true) throw new InvalidOperationException("評価済み動画への復帰が不正です。");
            EvaluatedOnlyCheck.IsChecked = true; await comparisonChangeTask;
            var selection = evaluationResults.Selection;
            ApplyEvaluationSelection(EvaluationSelection.Full); await comparisonChangeTask;
            if (EvaluatedOnlyCheck.IsEnabled || EvaluatedOnlyCheck.IsChecked == true) throw new InvalidOperationException("全編条件で制限が残っています。");
            ApplyEvaluationSelection(selection); EvaluatedOnlyCheck.IsChecked = true; await comparisonChangeTask;
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results/evaluated-playback"));
            Directory.CreateDirectory(directory);
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render((Visual)Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(directory, "main.png"))) encoder.Save(file);
            evaluationResults.Clear(); RefreshQualityValues(); await comparisonChangeTask;
            if (EvaluatedOnlyCheck.IsEnabled || EvaluatedOnlyCheck.IsChecked == true) throw new InvalidOperationException("結果クリア後に区間制限が残っています。");
            await File.WriteAllTextAsync(Path.Combine(directory, "main.json"), JsonSerializer.Serialize(new
            { Results = rows, LiveToggle = true, BothSourcesChecked = true, UnassessedStopped = true, FullDisabled = true, ClearDisabled = true, RightMuted = true }, new JsonSerializerOptions { WriteIndented = true }));
            File.Copy(Path.Combine(directory, "main.json"), Path.Combine(directory, $"main-{candidate.Descriptor.Codec}.json"), true);
            Console.WriteLine("通常画面の評価区間再生：3方式・ON/OFF・区間外提示0・末尾停止・コマ送り・シーク・未評価動画変更・クリアが合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
    }
}
