using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.App;

// 共通寸法を通常画面へ適用する前に、各ThemeModeの標準部品で確認する。
internal sealed class UiSizingTestWindow : Window
{
    public UiSizingTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "UIサイズ検証";
        Width = 720; Height = 420;
        var root = new StackPanel { Margin = new Thickness(UiTheme.Space16) };
        Content = root;
        var title = new TextBlock { Text = "共通書式の確認", Style = (Style)FindResource("UiTitleStyle") };
        var body = new TextBlock { Text = "本文とボタン・入力欄", Margin = new Thickness(0, UiTheme.Space12, 0, 0) };
        var caption = new TextBlock { Text = "補足テキスト", Style = (Style)FindResource("UiSmallTextStyle") };
        root.Children.Add(title); root.Children.Add(body); root.Children.Add(caption);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, UiTheme.Space16, 0, 0) };
        var button = new Button { Content = "操作ボタン" };
        var combo = new ComboBox { Width = 120, ItemsSource = new[] { "VMAF", "SSIM" }, SelectedIndex = 0,
            Margin = new Thickness(UiTheme.Space8, 0, 0, 0) };
        var input = new TextBox { Width = 120, Text = "入力欄", Margin = new Thickness(UiTheme.Space8, 0, 0, 0),
        };
        foreach (Control control in new Control[] { button, combo, input }) UiTheme.ApplyStandardSizing(control);
        row.Children.Add(button); row.Children.Add(combo); row.Children.Add(input); root.Children.Add(row);
        var check = new CheckBox { Content = "チェックボックス", Margin = new Thickness(0, UiTheme.Space12, 0, 0) };
        var radio = new RadioButton { Content = "ラジオボタン", Margin = new Thickness(0, UiTheme.Space8, 0, 0) };
        UiTheme.ApplyStandardSizing(check); UiTheme.ApplyStandardSizing(radio);
        root.Children.Add(check); root.Children.Add(radio);
        var tabs = new TabControl { Height = 100, Margin = new Thickness(0, UiTheme.Space16, 0, 0) };
        var previewTab = new TabItem { Header = "プレビュー", Content = "通常のタブ" };
        var graphTab = new TabItem { Header = "グラフ", Content = "通常のタブ" };
        UiTheme.ApplyStandardSizing(previewTab); UiTheme.ApplyStandardSizing(graphTab);
        tabs.Items.Add(previewTab); tabs.Items.Add(graphTab);
        root.Children.Add(tabs);
        if (automatic) Loaded += (_, _) => Dispatcher.BeginInvoke(() => Verify(title, body, caption,
            button, combo, input, check, radio, tabs));
    }

    private void Verify(TextBlock title, TextBlock body, TextBlock caption, Button button, ComboBox combo,
        TextBox input, CheckBox check, RadioButton radio, TabControl tabs)
    {
        try
        {
            UpdateLayout();
            if (title.FontSize != 20 || body.FontSize != 14 || caption.FontSize != 12 ||
                title.FontFamily.Source != "Yu Gothic UI" ||
                new Control[] { button, combo, input, check, radio, (TabItem)tabs.Items[0] }
                    .Any(control => control.MinHeight != UiTheme.ControlMinHeight ||
                        control.ActualHeight < UiTheme.ControlMinHeight || control.Template is null) ||
                button.Padding != new Thickness(12, 4, 12, 4) || combo.Padding != new Thickness(8, 4, 8, 4) ||
                input.Padding != new Thickness(8, 4, 8, 4))
                throw new InvalidOperationException("共通文字サイズ・部品寸法・標準テンプレートが一致しません。");
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/ui-sizing"));
            Directory.CreateDirectory(output);
            var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(output,
                DisplayStyleSettings.ActiveMode.ToLowerInvariant() + ".png"))) encoder.Save(stream);
            Console.WriteLine("UIサイズ専用画面: 共通書式が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
    }
}
