using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.App;

// 標準WPF部品のFluent Dark表示を本体へ組み込む前に確認する。
internal sealed class FluentThemeTestWindow : Window
{
    public FluentThemeTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Fluent Dark テスト";
        Width = 1000; Height = 650;
        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;
        var menu = new Menu();
        var file = new MenuItem { Header = "ファイル" };
        file.Items.Add(new MenuItem { Header = "動画を追加" });
        file.Items.Add(new MenuItem { Header = "終了" });
        menu.Items.Add(file);
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(new Button { Content = "動画を追加", Margin = new Thickness(4) });
        buttons.Children.Add(new CheckBox { Content = "評価した部分のみ再生", Margin = new Thickness(12, 4, 4, 4) });
        var method = new ComboBox { Width = 120, Margin = new Thickness(4) };
        foreach (var value in new[] { "VMAF", "SSIM", "PSNR" }) method.Items.Add(value);
        method.SelectedIndex = 1;
        buttons.Children.Add(method);
        buttons.Children.Add(new TextBox { Text = "比較動画", Width = 150, Margin = new Thickness(4) });
        DockPanel.SetDock(buttons, Dock.Top); root.Children.Add(buttons);
        var list = new ListBox { Height = 100, Margin = new Thickness(4) };
        list.Items.Add("比較動画 A"); list.Items.Add("比較動画 B"); list.SelectedIndex = 0;
        DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
        var tabs = new TabControl();
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true,
            ItemsSource = new[] { new { 動画 = "比較動画 A", 指標 = "SSIM", 値 = 0.95 },
                new { 動画 = "比較動画 B", 指標 = "SSIM", 値 = 0.94 } } };
        tabs.Items.Add(new TabItem { Header = "一覧", Content = grid });
        tabs.Items.Add(new TabItem { Header = "グラフ", Content = new TextBlock { Text = "独自描画のグラフは別途配色する。" } });
        tabs.SelectedIndex = 0;
        root.Children.Add(tabs);
        if (automatic) Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateLayout();
                var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(this);
                var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../../measurement-results/fluent-theme"));
                Directory.CreateDirectory(output);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(Path.Combine(output, "dark-controls.png"))) encoder.Save(stream);
                file.IsSubmenuOpen = true;
                DarkPaletteTestWindow.SavePopup(file, Path.Combine(output, "dark-menu.png"));
                file.IsSubmenuOpen = false;
                method.IsDropDownOpen = true;
                DarkPaletteTestWindow.SavePopup(method, Path.Combine(output, "dark-combo.png"));
                method.IsDropDownOpen = false;
                Console.WriteLine("Fluent Dark 専用画面：標準部品を表示して画像を保存しました。");
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
