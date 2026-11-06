using System.Windows;
using System.Windows.Controls;

namespace VideoQualityAnalyzer.App;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "バージョン情報";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "動画品質アナライザー", Style = (Style)Application.Current.FindResource("UiTitleStyle") });
        panel.Children.Add(new TextBlock { Text = $"バージョン {typeof(AboutWindow).Assembly.GetName().Version?.ToString(3)}", Margin = new Thickness(0, 12, 0, 12) });
        var close = new Button { Content = "閉じる", IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
    }
}

