using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class PartialEvaluationTestWindow : Window
{
    private readonly StackPanel settings = new();
    private readonly TextBox referencePath = new(), candidatePath = new();
    private readonly PartialEvaluationSettingsControl partialSettings = new();
    private TextBox startSeconds => partialSettings.startSeconds;
    private TextBox endSeconds => partialSettings.endSeconds;
    private TextBox period => partialSettings.period;
    private TextBox periodicLength => partialSettings.periodicLength;
    private TextBox count => partialSettings.count;
    private TextBox uniformLength => partialSettings.uniformLength;
    private Dictionary<PartialEvaluationMode, RadioButton> modes => partialSettings.modes;
    private Dictionary<PartialEvaluationMode, WrapPanel> modeInputs => partialSettings.modeInputs;
    private Dictionary<PartialEvaluationMode, string> modeTitles => partialSettings.modeTitles;
    private Dictionary<PartialEvaluationMode, PartialEvaluationSettingsControl.IntervalPreviewBar> intervalBars => partialSettings.intervalBars;
    private Dictionary<PartialEvaluationMode, TextBlock> frameStatuses => partialSettings.frameStatuses;
    private VideoDescriptor? previewReference { get => partialSettings.previewReference; set => partialSettings.previewReference = value; }
    private CancellationTokenSource? referenceProbeCancellation;
    private Task referenceLoading = Task.CompletedTask;
    private readonly Dictionary<QualityMetric, CheckBox> metrics = [];
    private readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly Button preview = new() { Content = "対象区間を確認" }, execute = new() { Content = "部分評価を実行" };
    private CancellationTokenSource? cancellation;

    public PartialEvaluationTestWindow(bool automatic = false, string[]? paths = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "部分品質評価テスト";
        Width = 880; Height = 760; MinWidth = 650; MinHeight = 550;
        var root = new DockPanel { Margin = new Thickness(16) };
        Content = root;
        DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
        AddFile("元動画", referencePath); AddFile("比較動画", candidatePath);
        if (paths is { Length: >= 2 }) { referencePath.Text = paths[0]; candidatePath.Text = paths[1]; }
        settings.Children.Add(partialSettings);
        settings.Children.Add(new TextBlock { Text = "均等配置：先頭・末尾を含めて等間隔（1区間の場合は中央）。周期指定の末尾は動画末尾で切ります。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var metricPanel = new WrapPanel(); settings.Children.Add(metricPanel);
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var check = new CheckBox { Content = metric.ToString().ToUpperInvariant(), IsChecked = metric == QualityMetric.Ssim,
                Margin = new Thickness(8), ToolTip = QualityMetricHelp.GetText(metric) };
            metrics.Add(metric, check); metricPanel.Children.Add(check);
        }
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        actions.Children.Add(preview); actions.Children.Add(execute);
        var cancel = new Button { Content = "中断" }; actions.Children.Add(cancel);
        cancel.Click += (_, _) => cancellation?.Cancel();
        preview.Click += async (_, _) => await RunAsync(false);
        execute.Click += async (_, _) => await RunAsync(true);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(output);
        status.Text = "CFR・同解像度・同fps用。平均値は評価対象のみ。区間外の復号も必要なため、計算時間の短縮率は未測定です。";
        Closing += (_, e) => { if (cancellation is not null) { e.Cancel = true; cancellation.Cancel(); } };
        Closed += (_, _) => referenceProbeCancellation?.Cancel();
        referencePath.TextChanged += (_, _) => referenceLoading = LoadPreviewReferenceAsync();
        Loaded += (_, _) => referenceLoading = LoadPreviewReferenceAsync();
        UpdateCardPreviews();
        if (automatic) Loaded += async (_, _) =>
        {
            try
            {
                await referenceLoading;
                var videoPath = referencePath.Text;
                referencePath.Text = "";
                if (intervalBars.Values.Any(bar => bar.Ranges.Count != 0))
                    throw new InvalidOperationException("元動画を解除してもバーが残っています。");
                referencePath.Text = videoPath;
                var olderLoading = referenceLoading;
                referencePath.Text = "";
                referencePath.Text = videoPath;
                await Task.WhenAll(olderLoading, referenceLoading);
                if (previewReference?.Path != videoPath)
                    throw new InvalidOperationException("連続した動画変更で最新の動画情報が反映されていません。");
                VerifyCardPreviews();
                partialSettings.SetSelection(EvaluationSelection.Full);
                if (partialSettings.GetSelection() != EvaluationSelection.Full)
                    throw new InvalidOperationException("全編評価のカードが選択できません。");
                await RunAsync(true, true);
                startSeconds.Text = "0.1";
                foreach (var mode in Enum.GetValues<PartialEvaluationMode>())
                {
                    modes[mode].IsChecked = true;
                    VerifyModeSelection(mode);
                }
                if (startSeconds.Text != "0.1") throw new InvalidOperationException("方式切り替えで設定値が失われました。");
                startSeconds.Text = "0";
                foreach (var mode in Enum.GetValues<PartialEvaluationMode>())
                {
                    modes[mode].IsChecked = true;
                    VerifyModeSelection(mode);
                    endSeconds.Text = "1"; period.Text = "1"; periodicLength.Text = "0.25";
                    count.Text = "2"; uniformLength.Text = "0.25";
                    SaveScreen(mode);
                    await RunAsync(true, true);
                }
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        };
    }

    private void AddFile(string label, TextBox path)
    {
        var row = new DockPanel { Margin = new Thickness(4) };
        row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        var browse = new Button { Content = "選択" }; DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        browse.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "動画ファイル|*.*" }; if (dialog.ShowDialog(this) == true) path.Text = dialog.FileName; };
        row.Children.Add(path); settings.Children.Add(row);
    }

    private IReadOnlyList<EvaluationFrameRange> CreatePlan(VideoDescriptor reference, PartialEvaluationMode mode) => partialSettings.CreatePlan(reference, mode);
    private void UpdateCardPreviews(string unavailable = "元動画を選択してください", string? detail = null) => partialSettings.UpdateCardPreviews(unavailable, detail);

    private async Task LoadPreviewReferenceAsync()
    {
        referenceProbeCancellation?.Cancel();
        using var source = new CancellationTokenSource();
        referenceProbeCancellation = source;
        previewReference = null;
        var path = referencePath.Text;
        UpdateCardPreviews(string.IsNullOrWhiteSpace(path) ? "元動画を選択してください" : "動画情報を取得中…");
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            await Task.Delay(250, source.Token);
            var probe = new FfprobeService(FfprobeService.FindExecutable() ?? throw new InvalidOperationException("FFprobeが見つかりません。"));
            var video = await probe.ProbeAsync(path, source.Token);
            if (referenceProbeCancellation != source || source.IsCancellationRequested) return;
            previewReference = video;
            UpdateCardPreviews();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (referenceProbeCancellation == source) UpdateCardPreviews("動画情報を取得できません", error.Message);
        }
        finally { if (referenceProbeCancellation == source) referenceProbeCancellation = null; }
    }

    private void VerifyCardPreviews()
    {
        var actualReference = previewReference ?? throw new InvalidOperationException("プレビュー用の動画情報がありません。");
        try
        {
            previewReference = new VideoDescriptor("preview", "プレビュー検証", 64, 64, "ffv1", 30, 30,
                TimeSpan.FromSeconds(125), false, 3750, 1);
            startSeconds.Text = "5"; endSeconds.Text = "60";
            period.Text = "60"; periodicLength.Text = "10"; count.Text = "6"; uniformLength.Text = "10";
            UpdateCardPreviews();
            if (frameStatuses[PartialEvaluationMode.TimeRange].Text != "00:00:05 ～ 00:01:00 1650フレーム" ||
                frameStatuses[PartialEvaluationMode.Periodic].Text != "60秒ごとに10秒 3区間 750フレーム" ||
                frameStatuses[PartialEvaluationMode.Uniform].Text != "10秒 6区間 1800フレーム" ||
                !intervalBars[PartialEvaluationMode.TimeRange].Ranges.SequenceEqual([new EvaluationFrameRange(150, 1800)]) ||
                !intervalBars[PartialEvaluationMode.Periodic].Ranges.SequenceEqual(
                    [new EvaluationFrameRange(0, 300), new(1800, 2100), new(3600, 3750)]) ||
                !intervalBars[PartialEvaluationMode.Uniform].Ranges.SequenceEqual(
                    [new EvaluationFrameRange(0, 300), new(690, 990), new(1380, 1680), new(2070, 2370), new(2760, 3060), new(3450, 3750)]) ||
                intervalBars.Values.Any(bar => bar.TotalFrames != 3750))
                throw new InvalidOperationException("カードの時刻・区間数・フレーム数またはバー範囲が不正です。");
            startSeconds.Text = "5.01";
            if (!frameStatuses[PartialEvaluationMode.TimeRange].Text.EndsWith("1649フレーム") ||
                intervalBars[PartialEvaluationMode.TimeRange].Ranges[0].Start != 151)
                throw new InvalidOperationException("端数フレームの反映が不正です。");
            endSeconds.Text = "不正";
            if (intervalBars[PartialEvaluationMode.TimeRange].Ranges.Count != 0 ||
                frameStatuses[PartialEvaluationMode.TimeRange].Text != "設定を確認してください")
                throw new InvalidOperationException("不正入力で古いプレビューが残っています。");
            previewReference = previewReference with { Duration = TimeSpan.FromSeconds(95), TotalFrames = 2850 };
            UpdateCardPreviews();
            if (frameStatuses[PartialEvaluationMode.Periodic].Text != "60秒ごとに10秒 2区間 600フレーム" ||
                intervalBars[PartialEvaluationMode.Periodic].TotalFrames != 2850)
                throw new InvalidOperationException("動画長の変更が反映されていません。");
            previewReference = null;
            UpdateCardPreviews();
            if (intervalBars.Values.Any(bar => bar.Ranges.Count != 0) ||
                frameStatuses.Values.Any(label => label.Text != "元動画を選択してください"))
                throw new InvalidOperationException("動画未選択で古いプレビューが残っています。");
        }
        finally
        {
            previewReference = actualReference;
            startSeconds.Text = "0"; endSeconds.Text = "1"; period.Text = "1"; periodicLength.Text = "0.25";
            count.Text = "2"; uniformLength.Text = "0.25";
            UpdateCardPreviews();
        }
    }

    private void VerifyModeSelection(PartialEvaluationMode mode)
    {
        if (modes.Count(pair => pair.Value.IsChecked == true) != 1 || modes[mode].IsChecked != true ||
            modeInputs.Any(pair => pair.Value.Visibility != (pair.Key == mode ? Visibility.Visible : Visibility.Hidden)))
            throw new InvalidOperationException("カードの排他選択または設定欄の切り替えが不正です。");
    }

    private void SaveScreen(PartialEvaluationMode mode)
    {
        UpdateLayout();
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
            "../../../../../measurement-results/partial-evaluation"));
        System.IO.Directory.CreateDirectory(directory);
        using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, $"screen-{mode}.png"));
        encoder.Save(stream);
    }

    private async Task RunAsync(bool evaluate, bool verify = false)
    {
        if (cancellation is not null) return;
        using var source = new CancellationTokenSource(); cancellation = source;
        settings.IsEnabled = preview.IsEnabled = execute.IsEnabled = false;
        try
        {
            var probe = new FfprobeService(FfprobeService.FindExecutable() ?? throw new InvalidOperationException("FFprobeが見つかりません。"));
            var reference = await probe.ProbeAsync(referencePath.Text, source.Token);
            var candidate = await probe.ProbeAsync(candidatePath.Text, source.Token);
            previewReference = reference;
            UpdateCardPreviews();
            var selection = partialSettings.GetSelection();
            var ranges = selection.CreatePlan(reference) ?? [new EvaluationFrameRange(0,
                reference.TotalFrames ?? (long)Math.Ceiling(reference.Duration.TotalSeconds * reference.AverageFrameRate))];
            var targetFrames = ranges.Sum(range => range.End - range.Start);
            output.Text = $"方式：{selection.Description}\n対象：{targetFrames}フレーム / {ranges.Count}区間\n" +
                string.Join("\n", ranges.Select((range, i) => $"{i + 1}: {range.Start / reference.AverageFrameRate:F3}～{range.End / reference.AverageFrameRate:F3}秒 / フレーム {range.Start}～{range.End - 1}"));
            if (!evaluate) { status.Text = "対象区間を確認しました。終了時刻・終了フレームは含みません。"; return; }
            var selected = metrics.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
            if (selected.Length == 0) throw new ArgumentException("評価指標を選択してください。");
            var service = new QualityEvaluationService(FfmpegRgbReader.FindExecutable() ?? throw new InvalidOperationException("FFmpegが見つかりません。"));
            foreach (var metric in selected)
            {
                var progress = new Progress<TimeSpan>(time => { if (cancellation == source) status.Text = $"{metric}: 評価対象の{time.TotalSeconds:F1}秒を処理中…"; });
                var result = selection.IsPartial
                    ? await service.EvaluatePartialAsync(reference, candidate, metric, ranges, progress, source.Token)
                    : await service.EvaluateAsync(reference, candidate, metric, progress, source.Token);
                if (verify && (result.Frames.Count != targetFrames ||
                    !result.Frames.Select(frame => frame.Number).SequenceEqual(ranges.SelectMany(range => Enumerable.Range(0, checked((int)(range.End - range.Start))).Select(offset => range.Start + offset))) ||
                    result.Average < 0.999))
                    throw new InvalidOperationException("評価フレーム数・元番号・同一動画のSSIMが不正です。");
                output.AppendText($"\n{metric}: 評価対象平均 {result.Average:F6} / 集計 {result.Frames.Count}フレーム");
            }
            status.Text = "部分評価が完了しました。結果はこのテスト画面内のみで表示します。";
        }
        catch (OperationCanceledException) { status.Text = "中断しました。途中結果は完了結果として扱いません。"; if (verify) throw; }
        catch (Exception error) { status.Text = error.Message; if (verify) throw; }
        finally { cancellation = null; settings.IsEnabled = preview.IsEnabled = execute.IsEnabled = true; }
    }
}
