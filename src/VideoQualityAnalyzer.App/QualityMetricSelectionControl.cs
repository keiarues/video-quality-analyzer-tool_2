using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 指標ごとの独立したCheckBoxをカードとして表示する。
public sealed class QualityMetricSelectionControl : UserControl
{
    private readonly Dictionary<QualityMetric, CheckBox> metrics = [];
    private readonly TextBlock selectionText = new() { Margin = new Thickness(4, 8, 4, 4), TextWrapping = TextWrapping.Wrap };
    public QualityMetric[] SelectedMetrics => metrics.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();

    public QualityMetricSelectionControl()
    {
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        var panel = new StackPanel(); Content = panel;
        panel.Children.Add(new TextBlock { Text = "評価する指標を選択してください（複数選択可）",
            Style = (Style)FindResource("UiSmallTextStyle"), Margin = new Thickness(4, 0, 4, 8) });
        var cards = new UniformGrid { Columns = 3 }; panel.Children.Add(cards);
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            var (summary, detail, note) = metric switch
            {
                QualityMetric.Vmaf => ("人が感じる映像品質を推定", "一般に0～100。大きいほど高品質。", "libvmaf対応のFFmpegが必要"),
                QualityMetric.Ssim => ("明るさ・コントラスト・構造を比較", "1に近いほど元動画に似ています。", ""),
                _ => ("画素の誤差を数値で比較", "単位はdB。大きいほど誤差が小さい。", "完全一致の無限大は平均から除外")
            };
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap,
                Style = (Style)FindResource("UiSectionStyle"), Margin = new Thickness(0, 12, 0, 8) });
            content.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
            if (note.Length > 0) content.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap,
                Style = (Style)FindResource("UiSmallTextStyle"), Margin = new Thickness(0, 12, 0, 0) });
            var check = new CheckBox { Content = content, Margin = new Thickness(4),
                Foreground = (Brush)FindResource("TextBrush"), VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 170,
                Template = CreateCardTemplate(metric.ToString().ToUpperInvariant()) };
            AutomationProperties.SetName(check, metric.ToString().ToUpperInvariant());
            AutomationProperties.SetHelpText(check, QualityMetricHelp.GetText(metric));
            check.Checked += (_, _) => RefreshSelection();
            check.Unchecked += (_, _) => RefreshSelection();
            metrics.Add(metric, check); cards.Children.Add(check);
        }
        selectionText.Style = (Style)FindResource("UiSmallTextStyle"); panel.Children.Add(selectionText);
        SetSelectedMetrics([QualityMetric.Ssim]);
    }

    public void SetSelectedMetrics(IEnumerable<QualityMetric> selectedMetrics)
    {
        var selected = selectedMetrics.ToHashSet();
        foreach (var pair in metrics) pair.Value.IsChecked = selected.Contains(pair.Key);
        RefreshSelection();
    }

    private void RefreshSelection() => selectionText.Text = SelectedMetrics.Length == 0
        ? "未選択：評価指標を1つ以上選択してください。"
        : "選択中：" + string.Join("・", SelectedMetrics.Select(metric => metric.ToString().ToUpperInvariant()));

    private ControlTemplate CreateCardTemplate(string title)
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "CardBorder" };
        border.SetValue(Border.BackgroundProperty, FindResource("PanelBrush"));
        border.SetValue(Border.BorderBrushProperty, FindResource("UiMutedBrush"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(12));
        var body = new FrameworkElementFactory(typeof(StackPanel)); border.AppendChild(body);
        var heading = new FrameworkElementFactory(typeof(DockPanel)); body.AppendChild(heading);
        var box = new FrameworkElementFactory(typeof(Border)) { Name = "CheckBoxSquare" };
        box.SetValue(WidthProperty, 20d); box.SetValue(HeightProperty, 20d);
        box.SetValue(Border.BorderBrushProperty, FindResource("UiMutedBrush"));
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        box.SetValue(MarginProperty, new Thickness(0, 0, 10, 0));
        box.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var mark = new FrameworkElementFactory(typeof(TextBlock)) { Name = "CheckMark" };
        mark.SetValue(TextBlock.TextProperty, "✓");
        mark.SetValue(TextBlock.ForegroundProperty, FindResource("UiOnLightBrush"));
        mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetValue(VisibilityProperty, Visibility.Hidden);
        box.AppendChild(mark); heading.AppendChild(box);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, title);
        text.SetValue(TextBlock.StyleProperty, FindResource("UiTitleStyle")); heading.AppendChild(text);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content"); body.AppendChild(presenter);
        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = border };
        var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        active.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("UiEmphasisBrush"), "CardBorder"));
        active.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("UiEmphasisBrush"), "CheckBoxSquare"));
        active.Setters.Add(new Setter(Border.BackgroundProperty, FindResource("UiEmphasisBrush"), "CheckBoxSquare"));
        active.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "CheckMark"));
        template.Triggers.Add(active);
        var focused = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, FindResource("UiWarningBrush"), "CardBorder"));
        template.Triggers.Add(focused);
        return template;
    }

    internal void VerifyMultipleSelection()
    {
        void Toggle(QualityMetric metric) => ((IToggleProvider)new CheckBoxAutomationPeer(metrics[metric])
            .GetPattern(PatternInterface.Toggle)).Toggle();
        SetSelectedMetrics([QualityMetric.Ssim]);
        Toggle(QualityMetric.Vmaf);
        if (!SelectedMetrics.SequenceEqual([QualityMetric.Vmaf, QualityMetric.Ssim]))
            throw new InvalidOperationException("指標カードを複数選択できません。");
        Toggle(QualityMetric.Ssim); Toggle(QualityMetric.Psnr);
        if (!SelectedMetrics.SequenceEqual([QualityMetric.Vmaf, QualityMetric.Psnr]))
            throw new InvalidOperationException("1枚の切り替えが他の指標へ影響しています。");
        Toggle(QualityMetric.Vmaf); Toggle(QualityMetric.Psnr);
        if (SelectedMetrics.Length != 0) throw new InvalidOperationException("指標を解除できません。");
        SetSelectedMetrics([QualityMetric.Vmaf, QualityMetric.Ssim]);
    }
}
