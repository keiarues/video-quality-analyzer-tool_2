using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class PartialEvaluationTestWindow : Window
{
    private readonly StackPanel settings = new();
    private readonly TextBox referencePath = new(), candidatePath = new();
    private readonly TextBox startSeconds = new() { Text = "0" }, endSeconds = new() { Text = "10" };
    private readonly TextBox period = new() { Text = "60" }, periodicLength = new() { Text = "10" };
    private readonly TextBox count = new() { Text = "6" }, uniformLength = new() { Text = "10" };
    private readonly Dictionary<PartialEvaluationMode, RadioButton> modes = [];
    private readonly UniformGrid cards = new() { Columns = 3, Margin = new Thickness(0, 12, 0, 12) };
    private readonly StackPanel modeSettings = new();
    private readonly Dictionary<PartialEvaluationMode, WrapPanel> modeInputs = [];
    private readonly Dictionary<PartialEvaluationMode, string> modeTitles = [];
    private readonly TextBlock modeTitle = new() { Margin = new Thickness(4) };
    private readonly Dictionary<PartialEvaluationMode, IntervalPreviewBar> intervalBars = [];
    private readonly Dictionary<PartialEvaluationMode, TextBlock> frameStatuses = [];
    private VideoDescriptor? previewReference;
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
        settings.Children.Add(cards);
        modeTitle.Style = (Style)FindResource("UiSectionStyle");
        modeSettings.Children.Add(modeTitle);
        settings.Children.Add(modeSettings);
        AddMode(PartialEvaluationMode.TimeRange, "時間範囲指定", "気になる場面を開始・終了時刻で指定",
            [("開始（秒）", startSeconds), ("終了（秒・含まない）", endSeconds)]);
        AddMode(PartialEvaluationMode.Periodic, "周期的な区間指定", "各周期の先頭の数秒を繰り返し評価",
            [("周期（秒）", period), ("各周期の先頭から（秒）", periodicLength)]);
        AddMode(PartialEvaluationMode.Uniform, "全体に均等配置", "指定した数の区間を動画全体に分散",
            [("区間数", count), ("各区間の長さ（秒）", uniformLength)]);
        modes[PartialEvaluationMode.TimeRange].IsChecked = true;
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
        foreach (var input in new[] { startSeconds, endSeconds, period, periodicLength, count, uniformLength })
            input.TextChanged += (_, _) => UpdateCardPreviews();
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

    private void AddMode(PartialEvaluationMode mode, string title, string description,
        (string Label, TextBox Input)[] fields)
    {
        var content = new StackPanel();
        var heading = new DockPanel();
        var selected = new TextBlock { Text = "✓", Visibility = Visibility.Hidden,
            Foreground = (Brush)FindResource("UiEmphasisBrush"), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(selected, Dock.Right); heading.Children.Add(selected);
        heading.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap,
            Style = (Style)FindResource("UiSectionStyle") });
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
            MinHeight = 42, Margin = new Thickness(0, 8, 0, 8) });
        var bar = new IntervalPreviewBar((Brush)FindResource("BackgroundBrush"), (Brush)FindResource("UiEmphasisBrush"));
        intervalBars.Add(mode, bar); content.Children.Add(bar);
        var frameStatus = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            MinHeight = 34, Style = (Style)FindResource("UiSmallTextStyle") };
        frameStatuses.Add(mode, frameStatus); content.Children.Add(frameStatus);
        var radio = new RadioButton { Content = content, GroupName = "部分評価方式", Margin = new Thickness(4),
            BorderBrush = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(radio, title);
        // 選択状態とキーボード操作はRadioButtonに任せ、外観だけカードにする。
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "CardBorder" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.BackgroundProperty, FindResource("PanelBrush"));
        border.SetValue(Border.PaddingProperty, new Thickness(12));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        active.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("UiEmphasisBrush"), "CardBorder"));
        template.Triggers.Add(active);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("UiWarningBrush"), "CardBorder"));
        template.Triggers.Add(focus);
        radio.Template = template;
        modes.Add(mode, radio); modeTitles.Add(mode, title); cards.Children.Add(radio);
        var inputs = new WrapPanel { Margin = new Thickness(0, 6, 0, 8), Visibility = Visibility.Collapsed };
        modeInputs.Add(mode, inputs);
        radio.Checked += (_, _) =>
        {
            inputs.Visibility = Visibility.Visible;
            selected.Visibility = Visibility.Visible;
            modeTitle.Text = title + "の設定";
        };
        radio.Unchecked += (_, _) => { inputs.Visibility = Visibility.Collapsed; selected.Visibility = Visibility.Hidden; };
        foreach (var field in fields)
        {
            inputs.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
            field.Input.Width = 85; field.Input.Margin = new Thickness(4); inputs.Children.Add(field.Input);
        }
        modeSettings.Children.Add(inputs);
    }

    private static double ReadNumber(TextBox input) => double.TryParse(input.Text, NumberStyles.Float,
        CultureInfo.CurrentCulture, out var value) && double.IsFinite(value)
        ? value : throw new ArgumentException("設定には有効な数値を入力してください。");

    private IReadOnlyList<EvaluationFrameRange> CreatePlan(VideoDescriptor reference, PartialEvaluationMode mode) =>
        PartialEvaluationPlan.Create(reference, mode,
            mode == PartialEvaluationMode.TimeRange ? ReadNumber(startSeconds) : 0,
            mode == PartialEvaluationMode.TimeRange ? ReadNumber(endSeconds) : 0,
            mode == PartialEvaluationMode.Periodic ? ReadNumber(period) : 0,
            mode == PartialEvaluationMode.Periodic ? ReadNumber(periodicLength) : mode == PartialEvaluationMode.Uniform ? ReadNumber(uniformLength) : 0,
            mode == PartialEvaluationMode.Uniform ? int.TryParse(count.Text, out var n) ? n : throw new ArgumentException("区間数には整数を入力してください。") : 0);

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

    private static string FormatPosition(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" +
            (time.Milliseconds == 0 ? "" : $".{time.Milliseconds:000}");
    }

    private void UpdateCardPreviews(string unavailable = "元動画を選択してください", string? detail = null)
    {
        foreach (var mode in modes.Keys)
        {
            var bar = intervalBars[mode];
            var label = frameStatuses[mode];
            bar.SetRanges([], 0);
            label.ToolTip = detail;
            if (previewReference is not { } reference) { label.Text = unavailable; continue; }
            try
            {
                var ranges = CreatePlan(reference, mode);
                var frames = ranges.Sum(range => range.End - range.Start);
                var total = reference.TotalFrames ?? (long)Math.Ceiling(reference.Duration.TotalSeconds * reference.AverageFrameRate);
                bar.SetRanges(ranges, total);
                label.Text = mode switch
                {
                    PartialEvaluationMode.TimeRange => $"{FormatPosition(ranges[0].Start / reference.AverageFrameRate)} ～ {FormatPosition(ranges[^1].End / reference.AverageFrameRate)} {frames}フレーム",
                    PartialEvaluationMode.Periodic => $"{ReadNumber(period):0.###}秒ごとに{ReadNumber(periodicLength):0.###}秒 {ranges.Count}区間 {frames}フレーム",
                    _ => $"{ReadNumber(uniformLength):0.###}秒 {ranges.Count}区間 {frames}フレーム"
                };
                label.ToolTip = "バー全体は元動画全体、色付き部分は評価対象です。終了時刻は含みません。";
            }
            catch (ArgumentException error)
            {
                label.Text = "設定を確認してください";
                label.ToolTip = error.Message;
            }
        }
    }

    private sealed class IntervalPreviewBar(Brush background, Brush foreground) : FrameworkElement
    {
        public IReadOnlyList<EvaluationFrameRange> Ranges { get; private set; } = [];
        public long TotalFrames { get; private set; }

        public void SetRanges(IReadOnlyList<EvaluationFrameRange> ranges, long total)
        {
            Ranges = ranges; TotalFrames = total;
            Height = 12;
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            drawing.DrawRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (TotalFrames <= 0) return;
            foreach (var range in Ranges)
                drawing.DrawRectangle(foreground, null, new Rect(ActualWidth * range.Start / TotalFrames, 0,
                    ActualWidth * (range.End - range.Start) / TotalFrames, ActualHeight));
        }
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
            modeInputs.Any(pair => pair.Value.Visibility != (pair.Key == mode ? Visibility.Visible : Visibility.Collapsed)))
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
            var mode = modes.Single(pair => pair.Value.IsChecked == true).Key;
            previewReference = reference;
            UpdateCardPreviews();
            var ranges = CreatePlan(reference, mode);
            var targetFrames = ranges.Sum(range => range.End - range.Start);
            output.Text = $"方式：{modeTitles[mode]}\n対象：{targetFrames}フレーム / {ranges.Count}区間\n" +
                string.Join("\n", ranges.Select((range, i) => $"{i + 1}: {range.Start / reference.AverageFrameRate:F3}～{range.End / reference.AverageFrameRate:F3}秒 / フレーム {range.Start}～{range.End - 1}"));
            if (!evaluate) { status.Text = "対象区間を確認しました。終了時刻・終了フレームは含みません。"; return; }
            var selected = metrics.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
            if (selected.Length == 0) throw new ArgumentException("評価指標を選択してください。");
            var service = new QualityEvaluationService(FfmpegRgbReader.FindExecutable() ?? throw new InvalidOperationException("FFmpegが見つかりません。"));
            foreach (var metric in selected)
            {
                var progress = new Progress<TimeSpan>(time => { if (cancellation == source) status.Text = $"{metric}: 評価対象の{time.TotalSeconds:F1}秒を処理中…"; });
                var result = await service.EvaluatePartialAsync(reference, candidate, metric, ranges, progress, source.Token);
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
