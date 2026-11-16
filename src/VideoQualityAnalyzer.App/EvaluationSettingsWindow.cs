using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationSettingsWindow : Window
{
    private readonly PartialEvaluationSettingsControl ranges = new();
    private readonly Button start = new() { Content = "実行", IsDefault = true };
    private readonly QualityMetricSelectionControl metricCards = new();
    private readonly ScrollViewer contentScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock targetSummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly VideoDescriptor[] selectedVideos;
    public EvaluationSelection Selection { get; private set; }
    public QualityMetric[] SelectedMetrics => metricCards.SelectedMetrics;
    public VideoDescriptor[] SelectedVideos => selectedVideos;
    internal string TargetSummaryText => targetSummary.Text;

    public EvaluationSettingsWindow(VideoDescriptor reference, EvaluationSelection selection, IEnumerable<QualityMetric>? selectedMetrics = null,
        IEnumerable<VideoDescriptor>? comparisonVideos = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価範囲の設定"; Width = 1060; Height = 800; MinWidth = 850; MinHeight = 540;
        Selection = selection;
        selectedVideos = (comparisonVideos ?? []).ToArray();
        var root = new DockPanel { Margin = new Thickness(16), Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush") }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        actions.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        actions.Children.Add(start);
        var panel = new StackPanel();
        contentScroll.Content = panel; root.Children.Add(contentScroll);
        panel.Children.Add(new TextBlock { Text = $"元動画：{reference.Name}", TextWrapping = TextWrapping.Wrap });
        targetSummary.Text = FormatTargetSummary(selectedVideos);
        panel.Children.Add(targetSummary);
        panel.Children.Add(ranges);
        panel.Children.Add(new TextBlock { Text = "評価指標", Style = (Style)FindResource("UiSectionStyle"), Margin = new Thickness(4, 16, 4, 8) });
        metricCards.SetSelectedMetrics(selectedMetrics ?? [QualityMetric.Ssim]);
        panel.Children.Add(metricCards);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Warning, Margin = new Thickness(4) };
        panel.Children.Add(error);
        ranges.SetReference(reference); ranges.SetComparisonVideos(selectedVideos); ranges.SetSelection(selection);
        start.Click += (_, _) =>
        {
            try
            {
                if (SelectedVideos.Length == 0) throw new InvalidOperationException("評価対象動画を1つ以上選択してください。");
                if (SelectedMetrics.Length == 0) throw new InvalidOperationException("評価指標を1つ以上選択してください。");
                Selection = ranges.GetSelection();
                DialogResult = true;
            }
            catch (Exception exception) { error.Text = exception.Message; }
        };
    }

    private static string FormatTargetSummary(VideoDescriptor[] videos) => videos.Length == 0
        ? "評価対象動画：未選択"
        : "評価対象動画：" + string.Join("、", videos.Take(5).Select(video => video.Name)) +
            (videos.Length > 5 ? $"\n全{videos.Length}ファイルを評価計算します" : "");

    internal void ApplyForVerification(EvaluationSelection selection, QualityMetric[]? selectedMetrics = null)
    {
        ranges.SetSelection(selection);
        if (selectedMetrics is not null)
            metricCards.SetSelectedMetrics(selectedMetrics);
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal void VerifyStableRangeLayout()
    {
        UpdateLayout();
        var initialPosition = metricCards.TranslatePoint(new Point(), this).Y;
        var initialHeight = ActualHeight;
        foreach (var selection in new[]
        {
            EvaluationSelection.Full,
            new EvaluationSelection(PartialEvaluationMode.TimeRange, 0.5, 1.5),
            new EvaluationSelection(PartialEvaluationMode.Periodic, PeriodSeconds: 1, LengthSeconds: 0.25),
            new EvaluationSelection(PartialEvaluationMode.Uniform, LengthSeconds: 0.25, Count: 2),
            EvaluationSelection.Full
        })
        {
            ranges.SetSelection(selection); UpdateLayout();
            if (ranges.videoDuration.Visibility != (selection.IsPartial ? Visibility.Visible : Visibility.Hidden) ||
                !ranges.videoDuration.Text.Contains("動画の長さ："))
                throw new InvalidOperationException("時間範囲指定の動画時間表示が不正です。");
            if (Math.Abs(metricCards.TranslatePoint(new Point(), this).Y - initialPosition) > 0.5 || ActualHeight != initialHeight)
                throw new InvalidOperationException("範囲切り替えで設定欄の高さ・画面配置・スクロール状態が変わりました。");
        }
        ranges.SetSelection(Selection); UpdateLayout();
    }
}
