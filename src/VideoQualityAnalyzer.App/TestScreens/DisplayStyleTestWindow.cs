using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoQualityAnalyzer.App;

// 各ThemeModeと独自パレットを設定画面へ組み込む前に確認する。
internal sealed class DisplayStyleTestWindow : Window
{
    public DisplayStyleTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "表示スタイルテスト";
        Width = 800; Height = 500;
        var root = new DockPanel { Margin = new Thickness(16), Background = (Brush)FindResource("BackgroundBrush") };
        Content = root;
        var title = new TextBlock { Text = "表示スタイル: " + DisplayStyleSettings.ActiveMode,
            Style = (Style)FindResource("UiTitleStyle"), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var button = new Button { Content = "操作ボタン",
            BorderBrush = (Brush)FindResource("UiControlBorderBrush") };
        controls.Children.Add(button);
        controls.Children.Add(new CheckBox { Content = "選択肢", Margin = new Thickness(12, 0, 0, 0) });
        controls.Children.Add(new ComboBox { ItemsSource = new[] { "VMAF", "SSIM", "PSNR" },
            SelectedIndex = 1, Width = 110, Margin = new Thickness(12, 0, 0, 0) });
        DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls);
        var list = new ListBox { ItemsSource = new[] { "比較動画 A", "比較動画 B" }, SelectedIndex = 0,
            Height = 95, Margin = new Thickness(0, 12, 0, 12) };
        DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
        var panel = new Border { Background = (Brush)FindResource("GraphSurfaceBrush"),
            BorderBrush = (Brush)FindResource("UiPanelBorderBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(12) };
        panel.Child = new TextBlock { Text = "画面内グラフの文字", Foreground = (Brush)FindResource("GraphTextBrush") };
        root.Children.Add(panel);

        if (automatic) Loaded += (_, _) => Dispatcher.BeginInvoke(() => Verify(root, title, button));
    }

    private void Verify(DockPanel root, TextBlock title, Button button)
    {
        try
        {
            var mode = DisplayStyleSettings.ActiveMode;
            if (DisplayStyleSettings.PaletteFor("System", true) != "Light" ||
                DisplayStyleSettings.PaletteFor("System", false) != "Dark" ||
                DisplayStyleSettings.PaletteFor("None", false) != "Light")
                throw new InvalidOperationException("システム配色と標準配色の選択が不正です。");
            var expected = DisplayStyleSettings.ActivePalette == "Dark" ? Color.FromRgb(20, 25, 34) :
                Color.FromRgb(232, 232, 234);
            if (Background is not SolidColorBrush window || window.Color != expected ||
                root.Background is not SolidColorBrush rootBrush || rootBrush.Color != expected ||
                title.Foreground is not SolidColorBrush text ||
                text.Color != ((SolidColorBrush)FindResource("TextBrush")).Color ||
                ((SolidColorBrush)GraphPalette.Screen.Surface).Color !=
                    ((SolidColorBrush)FindResource("GraphSurfaceBrush")).Color)
                throw new InvalidOperationException("ウィンドウ・文字・グラフのパレットが一致しません。");
            if (DisplayStyleSettings.ActivePalette == "Light" &&
                (button.BorderBrush is not SolidColorBrush border ||
                 border.Color != ((SolidColorBrush)FindResource("UiControlBorderBrush")).Color))
                throw new InvalidOperationException("ライト表示のボタン罫線が適用されていません。");
#pragma warning disable WPF0001 // .NET 10のFluentテーマは実験的API。
            var actualMode = Application.Current.ThemeMode;
            var expectedMode = mode switch
            {
                "None" => ThemeMode.None, "System" => ThemeMode.System,
                "Light" => ThemeMode.Light, _ => ThemeMode.Dark
            };
#pragma warning restore WPF0001
            if (!actualMode.Equals(expectedMode)) throw new InvalidOperationException("ThemeModeが選択値と一致しません。");
            var path = Path.Combine(Path.GetTempPath(), "display-style-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                DisplayStyleSettings.Save(mode, path);
                if (DisplayStyleSettings.Load(path) != mode)
                    throw new InvalidOperationException("表示スタイルの保存・読み込みが不正です。");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            UpdateLayout();
            var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(this);
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "../../../../../measurement-results/display-style"));
            Directory.CreateDirectory(output);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(output, mode.ToLowerInvariant() + ".png"))) encoder.Save(stream);
            Console.WriteLine($"表示スタイル専用画面: {mode} / {DisplayStyleSettings.ActivePalette} が合格");
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
    }
}
