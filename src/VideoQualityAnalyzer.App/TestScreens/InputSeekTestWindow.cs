using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class InputSeekTestWindow : Window
{
    private readonly TextBox referencePath = new(), candidatePath = new();
    private readonly PartialEvaluationSettingsControl settings = new();
    private readonly ComboBox metric = new() { ItemsSource = new[] { "SSIM", "PSNR" }, SelectedIndex = 0 };
    private readonly Button execute = new() { Content = "先頭からの復号と区間シークを比較" };
    private readonly CheckBox verifyPixels = new() { Content = "画素ハッシュも照合する（処理時間に含む）" };
    private readonly bool continuousInput;
    private readonly ProgressBar sourceProgress = new() { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(4, 8, 4, 8) };
    private readonly System.Windows.Threading.DispatcherTimer responsivenessTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private int responsivenessTicks;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private CancellationTokenSource? cancellation;
    private int loadVersion;
    private readonly List<object> reports = [];
    private readonly string reportDirectory;

    public InputSeekTestWindow(bool automatic, string[] paths, bool continuousInput = false)
    {
        this.continuousInput = continuousInput;
        reportDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../measurement-results/" + (continuousInput ? "continuous-input" : "input-seek")));
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = continuousInput ? "部分評価・中間キャッシュなしの連続入力検証" : "部分評価・区間入力シーク検証"; Width = 980; Height = 900;
        if (continuousInput) { metric.ItemsSource = new[] { "SSIM", "PSNR", "VMAF" }; metric.SelectedIndex = 0; }
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "区間入力シークの正確さと処理時間", Style = (Style)FindResource("UiTitleStyle") });
        top.Children.Add(new TextBlock { Text = continuousInput
            ? "検証専用：同解像度・同fps・yuv420pのCFR動画。左右のパイプから一つの評価器へ継続入力します。\n元の色情報を維持し、時刻を補正した先頭復号の基準と比較します。VMAFも区間を連結して評価します。"
            : "検証専用：同解像度・同fpsのCFR動画。左右の画素ハッシュと各フレームの評価値を照合します。\nVMAFは区間境界の時間特徴を別途検証するため対象外。時間にはハッシュ出力も含みます。",
            TextWrapping = TextWrapping.Wrap });
        AddFile("元動画", referencePath); AddFile("比較動画", candidatePath);
        top.Children.Add(settings); top.Children.Add(metric);
        if (continuousInput)
        {
            top.Children.Add(verifyPixels);
            top.Children.Add(new TextBlock { Text = "元動画上の評価位置（区間外をスキップ）", Style = (Style)FindResource("UiSmallTextStyle") });
            top.Children.Add(sourceProgress);
        }
        var actions = new WrapPanel(); top.Children.Add(actions); actions.Children.Add(execute);
        var cancel = new Button { Content = "中止" }; actions.Children.Add(cancel);
        cancel.Click += (_, _) => cancellation?.Cancel();
        execute.Click += async (_, _) => await RunManualAsync();
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(output);
        status.Text = "動画と部分評価のカードを選択してください。全編評価はこの検証の対象外です。";
        referencePath.TextChanged += async (_, _) => await LoadAsync();
        candidatePath.TextChanged += async (_, _) => await LoadAsync();
        Closing += (_, e) => { if (cancellation is not null) { e.Cancel = true; cancellation.Cancel(); } };
        if (continuousInput) { responsivenessTimer.Tick += (_, _) => responsivenessTicks++; responsivenessTimer.Start(); }
        Closed += (_, _) => responsivenessTimer.Stop();
        if (paths.Length >= 2) { referencePath.Text = paths[0]; candidatePath.Text = paths[1]; }
        settings.SetSelection(new(PartialEvaluationMode.Uniform, LengthSeconds: automatic ? 0.1 : 10, Count: 6));
        if (automatic) Loaded += async (_, _) =>
        {
            try { if (continuousInput) await RunContinuousAutomaticAsync(paths); else await RunAutomaticAsync(paths); SaveScreen(); Application.Current.Shutdown(0); }
            catch (Exception error) { Directory.CreateDirectory(reportDirectory); await File.WriteAllTextAsync(Path.Combine(reportDirectory, "failure.txt"), error.ToString()); Application.Current.Shutdown(1); }
        };
        return;
        void AddFile(string label, TextBox box)
        {
            top.Children.Add(new TextBlock { Text = label });
            var row = new DockPanel(); var browse = new Button { Content = "参照" }; DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse); row.Children.Add(box); top.Children.Add(row);
            browse.Click += (_, _) => { var dialog = new OpenFileDialog(); if (dialog.ShowDialog(this) == true) box.Text = dialog.FileName; };
        }
    }

    private static FfprobeService Probe() => new(FfprobeService.FindExecutable() ?? throw new InvalidOperationException("FFprobeがありません。"));
    private static InputSeekEvaluationService Service()
    {
        var probe = FfprobeService.FindExecutable() ?? throw new InvalidOperationException("FFprobeがありません。");
        return new(Path.Combine(Path.GetDirectoryName(probe)!, "ffmpeg.exe"), probe);
    }
    private async Task LoadAsync()
    {
        var version = ++loadVersion;
        try
        {
            var reference = await Probe().ProbeAsync(referencePath.Text, CancellationToken.None);
            var candidate = await Probe().ProbeAsync(candidatePath.Text, CancellationToken.None);
            if (version != loadVersion) return;
            settings.SetReference(reference); settings.SetComparisonVideos([candidate]);
        }
        catch { if (version == loadVersion) settings.SetReference(null); }
    }
    private async Task RunManualAsync()
    {
        cancellation = new(); execute.IsEnabled = false; settings.IsEnabled = false;
        referencePath.IsEnabled = candidatePath.IsEnabled = metric.IsEnabled = verifyPixels.IsEnabled = false;
        try
        {
            var reference = await Probe().ProbeAsync(referencePath.Text, cancellation.Token);
            var candidate = await Probe().ProbeAsync(candidatePath.Text, cancellation.Token);
            settings.SetReference(reference); settings.SetComparisonVideos([candidate]);
            var ranges = settings.GetSelection().WithComparisonTimeline(reference, [candidate]).CreatePlan(reference)
                ?? throw new ArgumentException("部分評価のカードを選択してください。");
            await CompareAsync(reference, candidate, ranges, metric.SelectedIndex switch { 0 => QualityMetric.Ssim, 1 => QualityMetric.Psnr, _ => QualityMetric.Vmaf }, "手動", cancellation.Token);
            status.Text = "照合完了。結果を確認してください。";
        }
        catch (OperationCanceledException) { status.Text = "中止しました。未完了の結果は採用していません。"; }
        catch (Exception error) { status.Text = error.Message; }
        finally { cancellation.Dispose(); cancellation = null; execute.IsEnabled = settings.IsEnabled = true;
            referencePath.IsEnabled = candidatePath.IsEnabled = metric.IsEnabled = verifyPixels.IsEnabled = true; }
    }
    private async Task CompareAsync(VideoDescriptor reference, VideoDescriptor candidate, IReadOnlyList<EvaluationFrameRange> ranges,
        QualityMetric selectedMetric, string name, CancellationToken token)
    {
        var events = new List<InputSeekProgress>();
        var initialTicks = responsivenessTicks;
        var progress = new ImmediateProgress(value =>
        {
            lock (events) events.Add(value);
            Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                status.Text = $"{value.Stage} {value.Interval}/{value.Intervals}区間　{value.CompletedFrames}/{value.TotalFrames}フレーム　元動画 {value.SourceTime:hh\\:mm\\:ss\\.fff}";
                sourceProgress.Maximum = Math.Max(0.001, reference.Duration.TotalSeconds);
                sourceProgress.Value = Math.Clamp(value.SourceTime.TotalSeconds, 0, sourceProgress.Maximum);
            });
        });
        var probePath = FfprobeService.FindExecutable()!;
        var hashEnabled = !continuousInput || verifyPixels.IsChecked == true;
        var result = continuousInput
            ? await new ContinuousInputEvaluationService(Path.Combine(Path.GetDirectoryName(probePath)!, "ffmpeg.exe"), probePath)
                .CompareAsync(reference, candidate, ranges, selectedMetric, hashEnabled, progress, token)
            : await Service().CompareAsync(reference, candidate, ranges, selectedMetric, progress, token);
        if (continuousInput)
        {
            foreach (var stage in events.GroupBy(value => value.Stage))
            {
                if (stage.Last().CompletedFrames != ranges.Sum(range => range.End - range.Start) ||
                    stage.Zip(stage.Skip(1)).Any(pair => pair.First.CompletedFrames > pair.Second.CompletedFrames || pair.First.SourceTime > pair.Second.SourceTime) ||
                    stage.Any(value => value.SourceTime != PartialEvaluationProgress.ToSourceTime(ranges, reference.AverageFrameRate,
                        TimeSpan.FromSeconds(value.CompletedFrames / reference.AverageFrameRate))))
                    throw new InvalidDataException("連続入力の進捗が不正です。");
            }
        }
        reports.Add(new { Case = name, ReferenceCodec = reference.Codec, CandidateCodec = candidate.Codec, Metric = selectedMetric.ToString(),
            Ranges = ranges, Frames = ranges.Sum(range => range.End - range.Start), Values = result.Seek.Result.Frames.Count,
            PixelVerification = hashEnabled, ProgressUpdates = events.Count,
            DirectReferenceVerified = continuousInput,
            UiTimerTicks = responsivenessTicks - initialTicks,
            IntermediateProgress = events.Any(value => value.Stage.Contains("シーク") && value.CompletedFrames > 0 && value.CompletedFrames < value.TotalFrames),
            SequentialSeconds = result.Sequential.Elapsed.TotalSeconds, SeekSeconds = result.Seek.Elapsed.TotalSeconds,
            result.ReferencePixelMismatches, result.CandidatePixelMismatches, result.ValueMismatches,
            MaximumValueDifference = double.IsFinite(result.MaximumValueDifference) ? (double?)result.MaximumValueDifference : null, result.Passed,
            ReferenceExamples = result.Sequential.ReferenceFrames.Zip(result.Seek.ReferenceFrames).Where(pair => pair.First != pair.Second).Take(3).Select(pair => new { Expected = pair.First, Actual = pair.Second }) });
        Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }), token);
        output.AppendText($"{name} {candidate.Codec} {selectedMetric}: {(result.Passed ? "一致" : "不一致")}　{result.Seek.Result.Frames.Count}評価値\n先頭復号 {result.Sequential.Elapsed.TotalSeconds:F3}秒 / 区間シーク {result.Seek.Elapsed.TotalSeconds:F3}秒\n画素照合{(hashEnabled ? $" 左{result.ReferencePixelMismatches} 右{result.CandidatePixelMismatches}" : "なし")} / 評価値不一致{result.ValueMismatches}\n\n");
        if (!result.Passed) throw new InvalidDataException("フレームまたは評価値が一致しません。");
    }
    private async Task RunContinuousAutomaticAsync(string[] paths)
    {
        var reference = await Probe().ProbeAsync(paths[0], CancellationToken.None);
        var probePath = FfprobeService.FindExecutable()!;
        var service = new ContinuousInputEvaluationService(Path.Combine(Path.GetDirectoryName(probePath)!, "ffmpeg.exe"), probePath);
        for (var index = 1; index < paths.Length; index++)
        {
            var candidate = await Probe().ProbeAsync(paths[index], CancellationToken.None);
            var small = Math.Min(EvaluationSelection.FrameCount(reference), EvaluationSelection.FrameCount(candidate)) < 200;
            verifyPixels.IsChecked = true;
            foreach (var selectedMetric in Enum.GetValues<QualityMetric>())
            {
                var selections = small ? new[] { new EvaluationSelection(PartialEvaluationMode.TimeRange, 0.1, 0.7),
                    new(PartialEvaluationMode.Periodic, PeriodSeconds: 1, LengthSeconds: 0.1),
                    new(PartialEvaluationMode.Uniform, LengthSeconds: 0.1, Count: 6) }
                    : [new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 0.1, Count: 6)];
                foreach (var selection in selections)
                    await CompareAsync(reference, candidate, small ? selection.WithComparisonTimeline(reference, [candidate]).CreatePlan(reference)! : [new(0, 3), new(300, 303), new(1500, 1503)], selectedMetric,
                        $"画素照合・入力{index}・{(small ? selection.Description : "3位置×3フレーム")}", CancellationToken.None);
            }
            if (!small)
            {
                verifyPixels.IsChecked = false;
                foreach (var selectedMetric in Enum.GetValues<QualityMetric>())
                    await CompareAsync(reference, candidate, new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 10, Count: 6)
                        .WithComparisonTimeline(reference, [candidate]).CreatePlan(reference)!, selectedMetric, $"性能・入力{index}・均等10秒×6", CancellationToken.None);
            }
        }
        var other = await Probe().ProbeAsync(paths[1], CancellationToken.None);
        var isSmall = EvaluationSelection.FrameCount(reference) < 200;
        if (isSmall)
        {
            var previous = reports.Count;
            metric.SelectedIndex = 2; verifyPixels.IsChecked = true;
            settings.SetReference(reference); settings.SetComparisonVideos([other]);
            settings.SetSelection(new(PartialEvaluationMode.Uniform, LengthSeconds: 0.1, Count: 6));
            await RunManualAsync();
            if (reports.Count != previous + 1 || !execute.IsEnabled || !settings.IsEnabled || !verifyPixels.IsEnabled)
                throw new InvalidOperationException("連続入力画面の実行・操作部品の復帰に失敗しました。");
        }
        using var before = new CancellationTokenSource(); before.Cancel();
        try { await service.CompareAsync(reference, other, [new(0, 3)], QualityMetric.Vmaf, false, null, before.Token); throw new InvalidOperationException("事前中止が無効です。"); }
        catch (OperationCanceledException) { }
        foreach (var stage in new[] { "直接復号基準", "連続入力・先頭復号", "連続入力シーク" })
        {
            using var stopped = new CancellationTokenSource();
            var trigger = new ImmediateProgress(value => { if (value.Stage == stage && value.CompletedFrames > 0) stopped.Cancel(); });
            try { await service.CompareAsync(reference, other, [new(0, 3), new(7, 10)], QualityMetric.Vmaf, true, trigger, stopped.Token); throw new InvalidOperationException("処理中止が無効です。"); }
            catch (OperationCanceledException) { }
        }
        var decoderFailure = false;
        if (isSmall)
        {
            var temporaryVideo = Path.Combine(Path.GetTempPath(), "vqa-failure-" + Guid.NewGuid().ToString("N") + Path.GetExtension(other.Path));
            File.Copy(other.Path, temporaryVideo);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var trigger = new ImmediateProgress(value =>
                {
                    if (value.Stage == "連続入力シーク" && value.CompletedFrames == 0) File.Delete(temporaryVideo);
                });
                try { await service.CompareAsync(reference, other with { Path = temporaryVideo }, [new(0, 3), new(7, 10)], QualityMetric.Ssim, true, trigger, timeout.Token); }
                catch (InvalidDataException) { decoderFailure = true; }
                catch (InvalidOperationException) { decoderFailure = true; }
                if (!decoderFailure) throw new InvalidOperationException("復号器失敗の検証が成立しません。");
            }
            finally { File.Delete(temporaryVideo); }
        }
        // 失敗・中止後も、新しいパイプで次の計算を開始できることを確認する。
        await CompareAsync(reference, other, [new(0, 3), new(7, 10)], QualityMetric.Ssim, "中止後の再実行", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "cancellation.json"), JsonSerializer.Serialize(new { BeforeStart = true, DuringDirect = true, DuringSequential = true, DuringSeek = true, DecoderFailure = decoderFailure, Restart = true }));
    }
    private async Task RunAutomaticAsync(string[] paths)
    {
        var reference = await Probe().ProbeAsync(paths[0], CancellationToken.None);
        if (EvaluationSelection.FrameCount(reference) < 100)
        {
            await RunManualAsync();
            if (reports.Count != 1 || !execute.IsEnabled || !settings.IsEnabled)
                throw new InvalidOperationException("画面の実行操作または操作部品の復帰に失敗しました。");
        }
        for (var index = 1; index < paths.Length; index++)
        {
            var candidate = await Probe().ProbeAsync(paths[index], CancellationToken.None);
            var total = Math.Min(EvaluationSelection.FrameCount(reference), EvaluationSelection.FrameCount(candidate));
            if (total < 100)
            {
                foreach (var selectedMetric in new[] { QualityMetric.Ssim, QualityMetric.Psnr })
                foreach (var selection in new[] { new EvaluationSelection(PartialEvaluationMode.TimeRange, 0.1, 0.7),
                    new(PartialEvaluationMode.Periodic, PeriodSeconds: 1, LengthSeconds: 0.1),
                    new(PartialEvaluationMode.Uniform, LengthSeconds: 0.1, Count: 6) })
                    await CompareAsync(reference, candidate, selection.WithComparisonTimeline(reference, [candidate]).CreatePlan(reference)!, selectedMetric, $"{selection.Description}・入力{index}", CancellationToken.None);
            }
            else
            {
                var starts = new long[] { 0, 7, 29, 300, total / 2, total - 301, total - 31, total - 3 };
                await CompareAsync(reference, candidate, starts.Select(start => new EvaluationFrameRange(start, start + 3)).ToArray(), QualityMetric.Ssim, $"境界8位置・入力{index}", CancellationToken.None);
                if (index == 1) await CompareAsync(reference, candidate,
                    new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 10, Count: 6).WithComparisonTimeline(reference, [candidate]).CreatePlan(reference)!, QualityMetric.Ssim, "均等10秒×6", CancellationToken.None);
            }
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var other = await Probe().ProbeAsync(paths[1], CancellationToken.None);
        try { await Service().CompareAsync(reference, other, [new(0, 3)], QualityMetric.Ssim, null, canceled.Token); throw new InvalidOperationException("事前中止が無効です。"); }
        catch (OperationCanceledException) { }
        foreach (var stage in new[] { "先頭から復号", "区間シーク" })
        {
            using var midRun = new CancellationTokenSource();
            var trigger = new ImmediateProgress(value => { if (value.Stage == stage && value.CompletedFrames > 0) midRun.Cancel(); });
            try { await Service().CompareAsync(reference, other, [new(0, 3)], QualityMetric.Ssim, trigger, midRun.Token); throw new InvalidOperationException("実行中止が無効です。"); }
            catch (OperationCanceledException) { }
        }
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "cancellation.json"),
            JsonSerializer.Serialize(new { BeforeStart = true, DuringSequential = true, DuringSeek = true }));
    }
    private void SaveScreen()
    {
        // 保存画像にも入力パスを残さない。
        referencePath.Text = "元動画（検証済み）"; candidatePath.Text = "比較動画（検証済み）";
        status.Text = "自動照合完了：画素・評価値一致、事前中止・処理中止を確認。";
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(reportDirectory, "screen.png")); encoder.Save(file);
    }
    private sealed class ImmediateProgress(Action<InputSeekProgress> report) : IProgress<InputSeekProgress>
    { public void Report(InputSeekProgress value) => report(value); }
}
