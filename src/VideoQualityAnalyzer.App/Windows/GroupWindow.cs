using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Core;

namespace VideoQualityAnalyzer.App;

public sealed class GroupWindow : Window
{
    public GroupWindow(VideoEntry[] selected, bool verify = false, IEnumerable<string>? existingGroups = null, bool verifyExisting = false)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "グループ設定";
        Width = 420;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(16) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "グループ名（空欄で解除）", Margin = new Thickness(0, 0, 0, 8) });
        var groups = (existingGroups ?? selected.Select(entry => entry.Group))
            .Select(group => group.Trim()).Where(group => group.Length > 0)
            .Distinct().OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var name = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = false,
            ItemsSource = groups,
            Text = selected.Length > 0 && selected.Select(entry => entry.Group).Distinct().Count() == 1 ? selected[0].Group : ""
        };
        panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        var apply = new Button { Content = "適用", IsDefault = true,
            Height = UiTheme.ActionButtonHeight, MinWidth = 96,
            Margin = new Thickness(0, 0, 8, 0), BorderBrush = UiTheme.ControlBorder };
        var cancel = new Button { Content = "キャンセル", IsCancel = true,
            Height = UiTheme.ActionButtonHeight, MinWidth = 96, BorderBrush = UiTheme.ControlBorder };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        apply.Click += (_, _) => { VideoLibrary.SetGroup(selected, name.Text); DialogResult = true; };
        Loaded += (_, _) =>
        {
            name.Focus();
            if (name.Template.FindName("PART_EditableTextBox", name) is TextBox editor) editor.SelectAll();
            if (!verify) return;
            UpdateLayout();
            var applyRight = apply.TranslatePoint(new Point(apply.ActualWidth, 0), this).X;
            var cancelLeft = cancel.TranslatePoint(new Point(), this).X;
            if (cancelLeft - applyRight < 7 ||
                Math.Abs(apply.ActualHeight - cancel.ActualHeight) > 0.5 || apply.ActualHeight < 31)
                throw new InvalidOperationException("グループ設定ボタンの間隔または高さが不正です。");
            if (verifyExisting) name.SelectedItem = groups.First();
            else name.Text = "検証グループ";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
    }
}
