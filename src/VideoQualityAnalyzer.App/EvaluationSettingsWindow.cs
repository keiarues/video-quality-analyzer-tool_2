using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationSettingsWindow : Window
{
    private readonly PartialEvaluationSettingsControl ranges = new();
    private readonly Button start = new() { Content = "評価を開始", IsDefault = true };
    private readonly Dictionary<QualityMetric, CheckBox> metrics = [];
    private readonly TextBox help = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100, Padding = new Thickness(10) };
    public EvaluationSelection Selection { get; private set; }
    public QualityMetric[] SelectedMetrics => metrics.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();

    public EvaluationSettingsWindow(VideoDescriptor reference, EvaluationSelection selection, IEnumerable<QualityMetric>? selectedMetrics = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価範囲の設定"; Width = 1060; Height = 620; MinWidth = 850; MinHeight = 540;
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
        var metricPanel = new DockPanel(); panel.Children.Add(metricPanel);
        var checks = new StackPanel { Width = 130, Margin = new Thickness(12, 0, 4, 0) };
        DockPanel.SetDock(checks, Dock.Right); metricPanel.Children.Add(checks);
        help.Background = (System.Windows.Media.Brush)FindResource("PanelBrush");
        help.Foreground = (System.Windows.Media.Brush)FindResource("TextBrush");
        help.BorderBrush = (System.Windows.Media.Brush)FindResource("UiMutedBrush");
        System.Windows.Automation.AutomationProperties.SetName(help, "評価指標の解説");
        metricPanel.Children.Add(help);
        var selected = (selectedMetrics ?? [QualityMetric.Ssim]).ToHashSet();
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var check = new CheckBox { Content = metric.ToString().ToUpperInvariant(), IsChecked = selected.Contains(metric),
                Margin = new Thickness(4, 5, 4, 5), ToolTip = QualityMetricHelp.GetText(metric) };
            check.MouseEnter += (_, _) => help.Text = QualityMetricHelp.GetText(metric);
            check.GotKeyboardFocus += (_, _) => help.Text = QualityMetricHelp.GetText(metric);
            check.Checked += (_, _) => help.Text = QualityMetricHelp.GetText(metric);
            metrics.Add(metric, check); checks.Children.Add(check);
        }
        help.Text = QualityMetricHelp.GetText(selected.FirstOrDefault(QualityMetric.Ssim));
        panel.Children.Add(new TextBlock { Text = "部分評価の平均は評価対象のみの値です。完了済みの同じ条件の結果は再利用します。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 4) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Warning, Margin = new Thickness(4) };
        panel.Children.Add(error);
        ranges.SetReference(reference); ranges.SetSelection(selection);
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
            foreach (var pair in metrics) pair.Value.IsChecked = selectedMetrics.Contains(pair.Key);
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
