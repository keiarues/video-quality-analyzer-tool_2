using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public sealed class GroupWindow : Window
{
    public GroupWindow(VideoEntry[] selected, bool verify = false)
    {
        Title = "グループ設定";
        Width = 420;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "グループ名（空欄で解除）", Margin = new Thickness(0, 0, 0, 8) });
        var name = new TextBox { Text = selected.Select(entry => entry.Group).Distinct().Count() == 1 ? selected[0].Group : "" };
        panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        var apply = new Button { Content = "適用", IsDefault = true };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        apply.Click += (_, _) => { VideoLibrary.SetGroup(selected, name.Text); DialogResult = true; };
        Loaded += (_, _) =>
        {
            name.Focus();
            name.SelectAll();
            if (!verify) return;
            name.Text = "検証グループ";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
    }
}
