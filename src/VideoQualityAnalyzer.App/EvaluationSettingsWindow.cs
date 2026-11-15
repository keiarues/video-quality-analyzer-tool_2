using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationSettingsWindow : Window
{
    private readonly PartialEvaluationSettingsControl ranges = new();
    private readonly Button start = new() { Content = "評価を開始", IsDefault = true };
    private readonly QualityMetricSelectionControl metricCards = new();
    public EvaluationSelection Selection { get; private set; }
    public QualityMetric[] SelectedMetrics => metricCards.SelectedMetrics;

    public EvaluationSettingsWindow(VideoDescriptor reference, EvaluationSelection selection, IEnumerable<QualityMetric>? selectedMetrics = null,
        IEnumerable<VideoDescriptor>? comparisonVideos = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価範囲の設定"; Width = 1060; Height = 720; MinWidth = 850; MinHeight = 540;
        Selection = selection;
        var root = new DockPanel { Margin = new Thickness(16), Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush") }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        actions.Children.Add(new Button { Content = "キャンセル", IsCancel = true });
        actions.Children.Add(start);
        var panel = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = $"元動画：{reference.Name}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(ranges);
        panel.Children.Add(new TextBlock { Text = "評価指標", Style = (Style)FindResource("UiSectionStyle"), Margin = new Thickness(4, 16, 4, 8) });
        metricCards.SetSelectedMetrics(selectedMetrics ?? [QualityMetric.Ssim]);
        panel.Children.Add(metricCards);
        panel.Children.Add(new TextBlock { Text = "部分評価の平均は評価対象のみの値です。完了済みの同じ条件の結果は再利用します。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 4) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Warning, Margin = new Thickness(4) };
        panel.Children.Add(error);
        var candidates = (comparisonVideos ?? []).ToArray();
        ranges.SetReference(reference); ranges.SetComparisonVideos(candidates); ranges.SetSelection(selection);
        var common = new EvaluationSelection(PartialEvaluationMode.Uniform).WithComparisonTimeline(reference, candidates);
        if (common.FrameLimit is { } limit)
            panel.Children.Add(new TextBlock { Text = $"部分評価は比較対象との共通範囲（先頭から{limit}フレーム）に配置します。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), Foreground = UiTheme.Warning });
        start.Click += (_, _) =>
        {
            try
            {
                if (SelectedMetrics.Length == 0) throw new InvalidOperationException("評価指標を1つ以上選択してください。");
                Selection = ranges.GetSelection();
                DialogResult = true;
            }
            catch (Exception exception) { error.Text = exception.Message; }
        };
    }

    internal void ApplyForVerification(EvaluationSelection selection, QualityMetric[]? selectedMetrics = null)
    {
        ranges.SetSelection(selection);
        if (selectedMetrics is not null)
            metricCards.SetSelectedMetrics(selectedMetrics);
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
