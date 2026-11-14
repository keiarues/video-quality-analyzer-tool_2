using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 通常画面の設定と専用テスト画面で同じ選択・表示処理を使う。
public sealed class PartialEvaluationSettingsControl : UserControl
{
    internal readonly TextBox startSeconds = new() { Text = "0" }, endSeconds = new() { Text = "10" };
    internal readonly TextBox period = new() { Text = "60" }, periodicLength = new() { Text = "10" };
    internal readonly TextBox count = new() { Text = "6" }, uniformLength = new() { Text = "10" };
    internal readonly Dictionary<PartialEvaluationMode, RadioButton> modes = [];
    private readonly UniformGrid cards = new() { Columns = 3, Margin = new Thickness(0, 12, 0, 12) };
    private readonly StackPanel modeSettings = new();
    internal readonly Dictionary<PartialEvaluationMode, WrapPanel> modeInputs = [];
    internal readonly Dictionary<PartialEvaluationMode, string> modeTitles = [];
    private readonly TextBlock modeTitle = new() { Margin = new Thickness(4) };
    internal readonly Dictionary<PartialEvaluationMode, IntervalPreviewBar> intervalBars = [];
    internal readonly Dictionary<PartialEvaluationMode, TextBlock> frameStatuses = [];
    internal VideoDescriptor? previewReference;
    public PartialEvaluationSettingsControl()
    {
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        var panel = new StackPanel();
        Content = panel;
        panel.Children.Add(cards);
        modeTitle.Style = (Style)FindResource("UiSectionStyle");
        modeSettings.Children.Add(modeTitle);
        panel.Children.Add(modeSettings);
        AddMode(PartialEvaluationMode.TimeRange, "時間範囲指定", "気になる場面を開始・終了時刻で指定",
            [("開始（秒）", startSeconds), ("終了（秒・含まない）", endSeconds)]);
        AddMode(PartialEvaluationMode.Periodic, "周期的な区間指定", "各周期の先頭の数秒を繰り返し評価",
            [("周期（秒）", period), ("各周期の先頭から（秒）", periodicLength)]);
        AddMode(PartialEvaluationMode.Uniform, "全体に均等配置", "指定した数の区間を動画全体に分散",
            [("区間数", count), ("各区間の長さ（秒）", uniformLength)]);
        modes[PartialEvaluationMode.TimeRange].IsChecked = true;
        foreach (var input in new[] { startSeconds, endSeconds, period, periodicLength, count, uniformLength })
            input.TextChanged += (_, _) => UpdateCardPreviews();
        UpdateCardPreviews();
    }

    public void SetReference(VideoDescriptor? reference)
    {
        previewReference = reference;
        UpdateCardPreviews();
    }

    public EvaluationSelection GetSelection()
    {
        var mode = modes.Single(pair => pair.Value.IsChecked == true).Key;
        if (previewReference is null) throw new InvalidOperationException("元動画がありません。");
        CreatePlan(previewReference, mode);
        return mode switch
        {
            PartialEvaluationMode.TimeRange => new(mode, ReadNumber(startSeconds), ReadNumber(endSeconds)),
            PartialEvaluationMode.Periodic => new(mode, PeriodSeconds: ReadNumber(period), LengthSeconds: ReadNumber(periodicLength)),
            _ => new(mode, LengthSeconds: ReadNumber(uniformLength), Count: int.Parse(count.Text))
        };
    }

    public void SetSelection(EvaluationSelection selection)
    {
        if (selection.Mode is not { } mode) return;
        modes[mode].IsChecked = true;
        if (mode == PartialEvaluationMode.TimeRange)
        { startSeconds.Text = selection.StartSeconds.ToString(CultureInfo.CurrentCulture); endSeconds.Text = selection.EndSeconds.ToString(CultureInfo.CurrentCulture); }
        else if (mode == PartialEvaluationMode.Periodic)
        { period.Text = selection.PeriodSeconds.ToString(CultureInfo.CurrentCulture); periodicLength.Text = selection.LengthSeconds.ToString(CultureInfo.CurrentCulture); }
        else
        { count.Text = selection.Count.ToString(CultureInfo.CurrentCulture); uniformLength.Text = selection.LengthSeconds.ToString(CultureInfo.CurrentCulture); }
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

    internal IReadOnlyList<EvaluationFrameRange> CreatePlan(VideoDescriptor reference, PartialEvaluationMode mode) =>
        PartialEvaluationPlan.Create(reference, mode,
            mode == PartialEvaluationMode.TimeRange ? ReadNumber(startSeconds) : 0,
            mode == PartialEvaluationMode.TimeRange ? ReadNumber(endSeconds) : 0,
            mode == PartialEvaluationMode.Periodic ? ReadNumber(period) : 0,
            mode == PartialEvaluationMode.Periodic ? ReadNumber(periodicLength) : mode == PartialEvaluationMode.Uniform ? ReadNumber(uniformLength) : 0,
            mode == PartialEvaluationMode.Uniform ? int.TryParse(count.Text, out var n) ? n : throw new ArgumentException("区間数には整数を入力してください。") : 0);

    private static string FormatPosition(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}" +
            (time.Milliseconds == 0 ? "" : $".{time.Milliseconds:000}");
    }

    internal void UpdateCardPreviews(string unavailable = "元動画を選択してください", string? detail = null)
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

    internal sealed class IntervalPreviewBar(Brush background, Brush foreground) : FrameworkElement
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

}
