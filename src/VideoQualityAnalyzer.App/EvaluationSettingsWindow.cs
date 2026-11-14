using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class EvaluationSettingsWindow : Window
{
    private readonly PartialEvaluationSettingsControl partial = new();
    private readonly CheckBox usePartial = new() { Content = "部分評価を使用する", Margin = new Thickness(4, 12, 4, 4) };
    private readonly Button apply = new() { Content = "適用", IsDefault = true };
    public EvaluationSelection Selection { get; private set; }

    public EvaluationSettingsWindow(VideoDescriptor reference, EvaluationSelection selection)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "評価範囲の設定"; Width = 880; Height = 490; MinWidth = 680; MinHeight = 450;
        Selection = selection;
        var root = new DockPanel { Margin = new Thickness(16), Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush") }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        actions.Children.Add(apply); actions.Children.Add(cancel);
        var panel = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = $"元動画：{reference.Name}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(usePartial); panel.Children.Add(partial);
        panel.Children.Add(new TextBlock { Text = "部分評価の平均は評価対象のみの値です。条件を切り替えると、その条件の完了結果を表示します。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 4) });
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Warning, Margin = new Thickness(4) };
        panel.Children.Add(error);
        partial.SetReference(reference); partial.SetSelection(selection);
        usePartial.IsChecked = selection.IsPartial; partial.IsEnabled = selection.IsPartial;
        usePartial.Checked += (_, _) => partial.IsEnabled = true;
        usePartial.Unchecked += (_, _) => partial.IsEnabled = false;
        apply.Click += (_, _) =>
        {
            try { Selection = usePartial.IsChecked == true ? partial.GetSelection() : EvaluationSelection.Full; DialogResult = true; }
            catch (Exception exception) { error.Text = exception.Message; }
        };
    }

    internal void ApplyForVerification(EvaluationSelection selection)
    {
        partial.SetSelection(selection);
        usePartial.IsChecked = selection.IsPartial;
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
