using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// Fluent Darkの標準部品と、独自描画のグラフ配色を確認する。
internal sealed class DarkPaletteTestWindow : Window
{
    private readonly QualityGraphPanel graph;

    public DarkPaletteTestWindow(bool automatic)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "ダーク配色テスト";
        Width = 1400; Height = 850; MinWidth = 950; MinHeight = 600;
        var panel = (Brush)FindResource("PanelBrush");
        var text = (Brush)FindResource("TextBrush");
        var control = new SolidColorBrush(Color.FromRgb(39, 51, 69));
        var muted = (Brush)FindResource("UiMutedBrush");

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;
        var menu = new Menu { Background = panel, Foreground = text };
        var file = new MenuItem { Header = "ファイル" };
        file.Items.Add(new MenuItem { Header = "動画を追加" });
        file.Items.Add(new MenuItem { Header = "グラフを保存" });
        menu.Items.Add(file);
        menu.Items.Add(new MenuItem { Header = "設定" });
        DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        var title = new TextBlock { Text = "画面全体のダーク配色", Style = (Style)FindResource("UiTitleStyle"),
            Margin = new Thickness(8, 12, 8, 8) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var toolbar = new WrapPanel { Margin = new Thickness(4, 0, 4, 10) };
        toolbar.Children.Add(new Button { Content = "動画を追加" });
        toolbar.Children.Add(new Button { Content = "評価を開始" });
        toolbar.Children.Add(new CheckBox { Content = "評価した部分のみ再生", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0) });
        var method = new ComboBox { Width = 110, Margin = new Thickness(4) };
        foreach (var value in new[] { "VMAF", "SSIM", "PSNR" }) method.Items.Add(value);
        method.SelectedIndex = 1;
        toolbar.Children.Add(method);
        toolbar.Children.Add(new TextBox { Text = "比較動画", Width = 135, Margin = new Thickness(4) });
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var status = new TextBlock { Text = "配色確認：本文・操作・一覧・グラフ・凡例", Foreground = muted,
            Margin = new Thickness(8, 8, 8, 4) };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(layout);
        var list = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            Background = panel, Foreground = text, RowBackground = panel, AlternatingRowBackground = control,
            Margin = new Thickness(4, 0, 4, 8), RowHeaderWidth = 0 };
        list.Columns.Add(new DataGridTextColumn { Header = "比較動画", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "コーデック", Binding = new System.Windows.Data.Binding("Codec"), Width = 120 });
        list.Columns.Add(new DataGridTextColumn { Header = "平均SSIM", Binding = new System.Windows.Data.Binding("Score"), Width = 120 });
        list.ItemsSource = new[] { new { Name = "比較動画_A.mkv", Codec = "AV1", Score = "0.9521" },
            new { Name = "比較動画_B.mkv", Codec = "H.265", Score = "0.9384" } };
        list.SelectedIndex = 0;
        layout.Children.Add(list);
        var videos = new[]
        {
            new VideoDescriptor("a", "比較動画_A.mkv", 1920, 1080, "AV1", 30, 30,
                TimeSpan.FromSeconds(10), false, 300, 1, 2_000_000),
            new VideoDescriptor("b", "比較動画_B.mkv", 1920, 1080, "H.265", 30, 30,
                TimeSpan.FromSeconds(10), false, 300, 1, 3_000_000)
        };
        var results = new Dictionary<(string VideoId, QualityMetric Metric), QualityResult>();
        foreach (var metric in Enum.GetValues<QualityMetric>())
            for (var index = 0; index < videos.Length; index++)
            {
                var frames = Enumerable.Range(0, 300).Select(number => new QualityFrame(number,
                    metric == QualityMetric.Psnr ? 37 + index + Math.Sin(number / 14d) :
                    metric == QualityMetric.Vmaf ? 91 + index + Math.Sin(number / 14d) * 2 :
                    .94 + index * .01 + Math.Sin(number / 14d) * .008)).ToArray();
                results[(videos[index].Id, metric)] = new QualityResult(metric, frames.Average(frame => frame.Value), frames);
            }
        graph = new QualityGraphPanel(videos, results, allVideos: videos);
        graph.SelectMetric(QualityMetric.Ssim);
        Grid.SetRow(graph, 1); layout.Children.Add(graph);
        if (automatic) Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateLayout();
                graph.VerifyDarkPalette();
                var image = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(this);
                var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../../measurement-results/dark-palette"));
                Directory.CreateDirectory(output);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(Path.Combine(output, "screen.png"))) encoder.Save(stream);
                file.IsSubmenuOpen = true;
                SavePopup(file, Path.Combine(output, "menu-popup.png"));
                file.IsSubmenuOpen = false;
                method.IsDropDownOpen = true;
                SavePopup(method, Path.Combine(output, "combo-popup.png"));
                method.IsDropDownOpen = false;
                Console.WriteLine("ダーク配色テスト：背景・文字・グリッド・凡例・メニューと選択欄の展開を確認。画面画像を保存しました。");
                Application.Current.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); Application.Current.Shutdown(1); }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    internal static void SavePopup(DependencyObject control, string path)
    {
        static Popup? FindPopup(DependencyObject root)
        {
            if (root is Popup popup) return popup;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                if (FindPopup(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
            return null;
        }
        var child = FindPopup(control)?.Child as FrameworkElement
            ?? throw new InvalidOperationException("選択メニューのポップアップを描画できません。");
        child.UpdateLayout();
        if (child.ActualWidth < 1 || child.ActualHeight < 1)
            throw new InvalidOperationException("選択メニューのポップアップ寸法が不正です。");
        var image = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth),
            (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(child);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
