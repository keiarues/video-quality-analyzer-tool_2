using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 本画面への組み込み前に、画像の配置・保存・表示倍率を検証する。
internal sealed class GraphImageTestWindow : Window
{
    private readonly ContentControl graphHost = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 22 };
    private readonly ComboBox target = new() { Width = 145 };
    private readonly ComboBox size = new() { Width = 160 };
    private readonly TextBox dimension = new() { Width = 70, Text = "1920" };
    private readonly TextBox heightDimension = new() { Width = 70, Text = "1080" };
    private readonly ComboBox font = new() { Width = 80 };
    private readonly Image preview = new() { Stretch = Stretch.Fill };
    private readonly Grid viewport = new() { Background = Brushes.LightSlateGray };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Border previewFrame;
    private readonly TabControl tabs = new();
    private readonly TabItem previewTab;
    private readonly Slider zoom = new() { Minimum = 10, Maximum = 500, TickFrequency = 10,
        SmallChange = 10, LargeChange = 10, IsSnapToTickEnabled = true, Width = 230, Value = 100 };
    private readonly TextBlock zoomLabel = new() { Width = 65, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly (int Width, int Height)[] presets = [(800, 600), (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160)];
    private QualityGraphPanel graph = null!;
    private bool fitToWindow = true;
    private bool updatingZoom;
    private bool updatingSizeInputs;
    private double displayScale = 1;

    public GraphImageTestWindow(bool automatic, string outputDirectory)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "グラフ画像保存テスト";
        Width = 1400; Height = 850; MinWidth = 900; MinHeight = 600;
        var root = new DockPanel { Margin = new Thickness(8) }; Content = root;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var saveOptions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (Control input in new Control[] { target, size, dimension, heightDimension, font })
        {
            input.Height = 32;
            input.Margin = new Thickness(3, 2, 3, 2);
            input.VerticalContentAlignment = VerticalAlignment.Center;
        }
        foreach (var text in new[] { "フレームグラフ", "ファイルグラフ" }) target.Items.Add(text);
        target.SelectedIndex = 1;
        saveOptions.Children.Add(Group(Label("保存対象："), target));
        foreach (var text in new[] { "現在の表示サイズ", "横幅を指定", "縦幅を指定", "幅と高さを指定" }) size.Items.Add(text);
        foreach (var preset in presets) size.Items.Add($"{preset.Width} × {preset.Height}");
        size.SelectedIndex = 0;
        saveOptions.Children.Add(Group(Label("画像サイズ："), size));
        saveOptions.Children.Add(Group(dimension, Label("×"), heightDimension, Label("px")));
        foreach (var text in new[] { "0.5倍", "0.8倍", "1倍", "1.2倍", "1.5倍", "2倍", "3倍" }) font.Items.Add(text);
        font.SelectedIndex = 2;
        font.ToolTip = "出力画像上の文字サイズ：目盛り・凡例は18px、見出し・単位は22pxに倍率を掛けます。";
        saveOptions.Children.Add(Group(Label("フォント："), font));
        var save = BarButton("PNG画像を保存", 128); saveOptions.Children.Add(save);
        var load = BarButton("実データを読み込む", 140); toolbar.Children.Add(load);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        tabs.Items.Add(new TabItem { Header = "グラフ操作", Content = graphHost });
        previewFrame = new Border { BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(2),
            Background = Brushes.White, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = preview };
        scroll.Content = previewFrame; viewport.Children.Add(scroll);
        var previewBody = new DockPanel();
        var controls = new Grid();
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Children.Add(saveOptions);
        var zoomBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(zoomBar, 1); controls.Children.Add(zoomBar);
        DockPanel.SetDock(controls, Dock.Top); previewBody.Children.Add(controls);
        zoom.Width = 170; zoom.Height = 32; zoom.Margin = new Thickness(3, 2, 3, 2);
        zoomLabel.Width = 52; zoomLabel.Margin = new Thickness(3, 0, 3, 0);
        zoomBar.Children.Add(Label("表示倍率：")); zoomBar.Children.Add(zoom); zoomBar.Children.Add(zoomLabel);
        var fit = BarButton("⛶", 36); fit.ToolTip = "ウィンドウに合わせる"; zoomBar.Children.Add(fit);
        var actual = BarButton("100%", 60); zoomBar.Children.Add(actual);
        previewBody.Children.Add(viewport);
        previewTab = new TabItem { Header = "プレビュー", Content = previewBody }; tabs.Items.Add(previewTab);
        root.Children.Add(tabs);
        SetSample(); UpdateSizeInputs();
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs && tabs.SelectedItem == previewTab) QueueRefresh();
        };
        target.SelectionChanged += (_, _) => RefreshPreview();
        size.SelectionChanged += (_, _) => { UpdateSizeInputs(); RefreshPreview(); };
        dimension.TextChanged += (_, _) => { if (!updatingSizeInputs) RefreshPreview(); };
        heightDimension.TextChanged += (_, _) => { if (!updatingSizeInputs) RefreshPreview(); };
        font.SelectionChanged += (_, _) => RefreshPreview();
        viewport.SizeChanged += (_, _) => { ApplyZoom(); QueueRefresh(); };
        resizeTimer.Tick += (_, _) => { resizeTimer.Stop(); RefreshPreview(); };
        Closed += (_, _) => resizeTimer.Stop();
        zoom.ValueChanged += (_, _) =>
        {
            if (updatingZoom) return;
            fitToWindow = false; ApplyZoom();
        };
        fit.Click += (_, _) => { fitToWindow = true; ApplyZoom(); };
        actual.Click += (_, _) => { fitToWindow = false; zoom.Value = 100; ApplyZoom(); };
        save.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "PNG画像 (*.png)|*.png", DefaultExt = ".png",
                FileName = "品質グラフ.png", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                // ズームは表示だけに使用し、保存には元の出力画像を使う。
                if (tabs.SelectedItem == previewTab) RefreshPreview();
                var bitmap = tabs.SelectedItem == previewTab && preview.Source is BitmapSource current ? current : CreateImage();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(dialog.FileName); encoder.Save(stream);
                status.Text = $"保存しました：{dialog.FileName}（{bitmap.PixelWidth} × {bitmap.PixelHeight}）";
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "画像保存エラー", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        load.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "SSIM検証データ (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var cache = await SsimTestCache.LoadAsync(dialog.FileName);
                SetGraph(cache.Entries.Select(entry => entry.Video).ToArray(),
                    cache.Entries.ToDictionary(entry => (entry.Video.Id, QualityMetric.Ssim), entry => entry.Result));
                graph.SelectMetric(QualityMetric.Ssim); tabs.SelectedIndex = 0;
                status.Text = "保存済みの実測SSIMデータを表示中です。";
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        Loaded += async (_, _) =>
        {
            if (!automatic) return;
            try { Directory.CreateDirectory(outputDirectory); await VerifyAsync(outputDirectory); Application.Current.Shutdown(0); }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(outputDirectory, "error.txt"), error.ToString());
                Console.Error.WriteLine(error); Application.Current.Shutdown(1);
            }
        };
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 2, 0) };
    private static StackPanel Group(params UIElement[] children)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) group.Children.Add(child);
        return group;
    }
    private static Button BarButton(string text, double width) => new()
    {
        Content = text, Width = width, MinWidth = width, Height = 32,
        Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(3, 2, 3, 2),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private double FontScale() => new[] { 0.5, 0.8, 1, 1.2, 1.5, 2, 3 }[font.SelectedIndex];
    private void UpdateSizeInputs()
    {
        dimension.IsEnabled = size.SelectedIndex is 1 or 3;
        heightDimension.IsEnabled = size.SelectedIndex is 2 or 3;
    }
    private void QueueRefresh()
    {
        if (tabs.SelectedItem != previewTab) return;
        resizeTimer.Stop(); resizeTimer.Start();
    }
    private Size AvailableSize() => new(Math.Max(300, (viewport.ActualWidth > 0 ? viewport.ActualWidth : graphHost.ActualWidth) - 28),
        Math.Max(300, (viewport.ActualHeight > 0 ? viewport.ActualHeight : graphHost.ActualHeight) - 28));
    private (int Width, int Height) ImageSize()
    {
        var available = AvailableSize();
        var dpi = VisualTreeHelper.GetDpi(this);
        double width, height;
        if (size.SelectedIndex >= 4) return presets[size.SelectedIndex - 4];
        if (size.SelectedIndex == 0) { width = available.Width * dpi.DpiScaleX; height = available.Height * dpi.DpiScaleY; }
        else
        {
            static int Parse(string text)
            {
                if (!int.TryParse(text, out var value) || value < 1 || value > 16384)
                    throw new InvalidOperationException("幅と高さは1～16384ピクセルの整数で入力してください。");
                return value;
            }
            width = size.SelectedIndex == 2 ? Parse(heightDimension.Text) * available.Width / available.Height : Parse(dimension.Text);
            height = size.SelectedIndex == 1 ? width * available.Height / available.Width : Parse(heightDimension.Text);
        }
        if (width > 16384 || height > 16384 || width * height > 40_000_000)
            throw new InvalidOperationException("画像は各辺16384ピクセル、総画素数4000万以下にしてください。");
        return (Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
    }
    private BitmapSource CreateImage()
    {
        var (width, height) = ImageSize();
        return graph.CreateExportImage((GraphImageTarget)target.SelectedIndex, width, height, FontScale());
    }
    private void RefreshPreview()
    {
        if (tabs.SelectedItem != previewTab || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0) return;
        try
        {
            preview.Source = CreateImage(); ApplyZoom();
            var bitmap = RequirePreview();
            updatingSizeInputs = true;
            if (!dimension.IsEnabled) dimension.Text = bitmap.PixelWidth.ToString();
            if (!heightDimension.IsEnabled) heightDimension.Text = bitmap.PixelHeight.ToString();
            updatingSizeInputs = false;
            status.Text = $"出力予定の画像：{target.SelectedItem}（{bitmap.PixelWidth} × {bitmap.PixelHeight}）";
        }
        catch (Exception error) { preview.Source = null; status.Text = "プレビューを作成できませんでした：" + error.Message; }
    }
    private BitmapSource RequirePreview() => preview.Source as BitmapSource
        ?? throw new InvalidOperationException("プレビュー画像が生成されていません。" + status.Text);

    private void ApplyZoom()
    {
        if (preview.Source is not BitmapSource bitmap) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var available = AvailableSize();
        displayScale = fitToWindow ? Math.Min(available.Width * dpi.DpiScaleX / bitmap.PixelWidth,
            available.Height * dpi.DpiScaleY / bitmap.PixelHeight) : zoom.Value / 100;
        preview.Width = bitmap.PixelWidth * displayScale / dpi.DpiScaleX;
        preview.Height = bitmap.PixelHeight * displayScale / dpi.DpiScaleY;
        zoomLabel.Text = $"{displayScale * 100:0}%";
        if (fitToWindow)
        {
            updatingZoom = true;
            zoom.Value = Math.Clamp(Math.Round(displayScale * 10) * 10, zoom.Minimum, zoom.Maximum);
            updatingZoom = false;
        }
    }

    private async Task VerifyAsync(string directory)
    {
        tabs.SelectedItem = previewTab; UpdateLayout(); resizeTimer.Stop(); RefreshPreview();
        if (size.Items.Count != 9 || size.SelectedIndex != 0) throw new InvalidOperationException("画像サイズの選択肢が不正です。");
        foreach (var targetIndex in new[] { 0, 1 })
        foreach (var metric in Enum.GetValues<QualityMetric>())
        {
            tabs.SelectedIndex = 0; graph.SelectMetric(metric); UpdateLayout();
            tabs.SelectedItem = previewTab; UpdateLayout(); target.SelectedIndex = targetIndex; RefreshPreview();
            if (graph.FilePointCount != 30 || graph.DisplayedVideoIds.Count != 30)
                throw new InvalidOperationException("サンプルの表示件数が30件ではありません。");
            if (preview.Source is not BitmapSource) throw new InvalidOperationException("プレビューがありません。");
            var image = RequirePreview();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, $"responsive-{metric}-{targetIndex}.png")); encoder.Save(stream);
        }
        foreach (var mode in Enumerable.Range(0, size.Items.Count))
        {
            size.SelectedIndex = mode; RefreshPreview();
            var expected = ImageSize();
            if (preview.Source is null && mode != 5 && status.Text.Contains("不足")) continue;
            if (preview.Source is not BitmapSource image || image.PixelWidth != expected.Width || image.PixelHeight != expected.Height)
                throw new InvalidOperationException("出力寸法が選択に一致しません。");
            if (mode >= 4 && expected != presets[mode - 4]) throw new InvalidOperationException("プリセットが不正です。");
        }
        size.SelectedIndex = 0; RefreshPreview();
        var before = RequirePreview();
        Width = 1000; Height = 720; UpdateLayout();
        // 実際のリサイズ通知からの自動更新を待つ。明示的な再生成では代用しない。
        await Task.Delay(350);
        var after = RequirePreview();
        if (before.PixelWidth == after.PixelWidth || Math.Abs(before.PixelWidth / (double)before.PixelHeight -
            after.PixelWidth / (double)after.PixelHeight) < 0.01) throw new InvalidOperationException("ウィンドウの縦横比に追従しません。");
        // 30件の凡例と最大倍率が収まる4K出力で全倍率を照合する。
        size.SelectedIndex = 8; RefreshPreview();
        for (var index = 0; index < font.Items.Count; index++)
        {
            font.SelectedIndex = index; RefreshPreview();
            var image = RequirePreview();
            var expected = CreateImage();
            var fontEncoder = new PngBitmapEncoder(); fontEncoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(Path.Combine(directory, $"responsive-font-{index}.png"))) fontEncoder.Save(stream);
            var stride = image.PixelWidth * 4; var pixels = new byte[stride * image.PixelHeight];
            var other = new byte[pixels.Length]; image.CopyPixels(pixels, stride, 0); expected.CopyPixels(other, stride, 0);
            var colored = 0;
            for (var pixel = 0; pixel < pixels.Length; pixel += 4)
                if (Math.Max(pixels[pixel], Math.Max(pixels[pixel + 1], pixels[pixel + 2])) -
                    Math.Min(pixels[pixel], Math.Min(pixels[pixel + 1], pixels[pixel + 2])) > 60) colored++;
            if (colored < 100 || !pixels.SequenceEqual(other)) throw new InvalidOperationException("系列の描画または出力画像の一致が不正です。");
        }
        // 同じ出力設定は通常・最大化・別サイズのウィンドウで全画素一致しなければならない。
        static byte[] Pixels(BitmapSource bitmap)
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
        size.SelectedIndex = 8;
        var fixedImages = new Dictionary<(int Target, int Font), byte[]>();
        foreach (var targetIndex in new[] { 0, 1 })
        for (var fontIndex = 0; fontIndex < font.Items.Count; fontIndex++)
        {
            target.SelectedIndex = targetIndex; font.SelectedIndex = fontIndex; RefreshPreview();
            fixedImages[(targetIndex, fontIndex)] = Pixels(RequirePreview());
        }
        foreach (var state in new[] { WindowState.Maximized, WindowState.Normal })
        {
            WindowState = state;
            if (state == WindowState.Normal) { Width = 1600; Height = 950; }
            UpdateLayout(); await Task.Delay(350);
            foreach (var targetIndex in new[] { 0, 1 })
            for (var fontIndex = 0; fontIndex < font.Items.Count; fontIndex++)
            {
                target.SelectedIndex = targetIndex; font.SelectedIndex = fontIndex; RefreshPreview();
                if (preview.Source is not BitmapSource image || image.PixelWidth != 3840 || image.PixelHeight != 2160 ||
                    !fixedImages[(targetIndex, fontIndex)].SequenceEqual(Pixels(image)))
                    throw new InvalidOperationException("固定解像度の画像がウィンドウサイズによって変化しました。");
            }
        }
        // 出力解像度が変わっても、同じピクセル指定のタイトルは同じ画素で描画される。
        static byte[] TitlePixels(BitmapSource bitmap)
        {
            var pixels = new byte[600 * 20 * 4];
            bitmap.CopyPixels(new Int32Rect(12, 12, 600, 20), pixels, 600 * 4, 0); return pixels;
        }
        font.SelectedIndex = 2;
        foreach (var targetIndex in new[] { 0, 1 })
        {
            target.SelectedIndex = targetIndex; size.SelectedIndex = 5; RefreshPreview();
            var title = TitlePixels(RequirePreview());
            size.SelectedIndex = 6; RefreshPreview();
            if (!title.SequenceEqual(TitlePixels(RequirePreview())))
                throw new InvalidOperationException("解像度変更で文字のピクセルサイズが変化しました。");
            size.SelectedIndex = 1; dimension.Text = "1280"; RefreshPreview();
            WindowState = WindowState.Maximized; UpdateLayout(); await Task.Delay(350);
            if (preview.Source is not BitmapSource { PixelWidth: 1280 } fixedWidth ||
                !title.SequenceEqual(TitlePixels(fixedWidth)))
                throw new InvalidOperationException("最大化した横幅1280px指定で文字サイズが変化しました。");
            WindowState = WindowState.Normal; UpdateLayout(); await Task.Delay(350);
        }
        target.SelectedIndex = 1; size.SelectedIndex = 5; font.SelectedIndex = 3; RefreshPreview();
        var original = preview.Source;
        fitToWindow = false; zoom.Value = 200; ApplyZoom(); UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this); var bitmap = RequirePreview();
        if (Math.Abs(preview.Width - bitmap.PixelWidth * 2 / dpi.DpiScaleX) > 0.1 || !ReferenceEquals(original, preview.Source) ||
            scroll.ExtentWidth <= scroll.ViewportWidth || scroll.ExtentHeight <= scroll.ViewportHeight)
            throw new InvalidOperationException("拡大表示が画像サイズを変更しました。");
        zoom.Value = 100; ApplyZoom(); UpdateLayout();
        if (Math.Abs(preview.Width - bitmap.PixelWidth / dpi.DpiScaleX) > 0.1) throw new InvalidOperationException("100%表示が不正です。");
        fitToWindow = true; ApplyZoom(); UpdateLayout();
        if (preview.Width > AvailableSize().Width + 1 || preview.Height > AvailableSize().Height + 1)
            throw new InvalidOperationException("ウィンドウに収まりません。");
        font.SelectedIndex = 3; RefreshPreview(); UpdateLayout();
        var screenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        screenshot.Render(this); var shotEncoder = new PngBitmapEncoder(); shotEncoder.Frames.Add(BitmapFrame.Create(screenshot));
        using (var stream = File.Create(Path.Combine(directory, "preview-window.png"))) shotEncoder.Save(stream);
        Width = MinWidth; UpdateLayout(); await Task.Delay(350);
        var narrowScreenshot = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        narrowScreenshot.Render(this);
        var narrowEncoder = new PngBitmapEncoder(); narrowEncoder.Frames.Add(BitmapFrame.Create(narrowScreenshot));
        using (var stream = File.Create(Path.Combine(directory, "preview-window-narrow.png"))) narrowEncoder.Save(stream);
        size.SelectedIndex = 1;
        dimension.Text = "入力エラー";
        if (preview.Source is not null) throw new InvalidOperationException("不正な入力で古い画像が残っています。");
        dimension.Text = "1280";
        if (preview.Source is not BitmapSource { PixelWidth: 1280 }) throw new InvalidOperationException("入力修正後に画像が復元されません。");
        size.SelectedIndex = 3; dimension.Text = "200"; heightDimension.Text = "100"; RefreshPreview();
        if (preview.Source is not null || !status.Text.Contains("不足"))
            throw new InvalidOperationException("描画領域が不足しても文字を縮小せずエラーを表示する必要があります。");
        size.SelectedIndex = 5; RefreshPreview();
        graph.SelectFile("sample-1"); RefreshPreview();
        if (!graph.DisplayedVideoIds.SequenceEqual(new[] { "sample-1" })) throw new InvalidOperationException("動画選択が反映されません。");
        graph.UpdateData([], [], null); RefreshPreview();
        if (preview.Source is null) throw new InvalidOperationException("空のデータを出力できません。");
        File.WriteAllText(Path.Combine(directory, "result.txt"), "合格：30件、3指標×2対象、9サイズ選択（領域不足時の案内を含む）、7フォント倍率、通常・最大化・リサイズ時の全画素一致、解像度変更時のタイトル画素一致、横幅1280px、200%・100%・全体表示。サンプルは架空データ。");
        resizeTimer.Stop(); Console.WriteLine("グラフ画像保存テスト：合格");
    }

    private void SetGraph(VideoDescriptor[] videos, Dictionary<(string, QualityMetric), QualityResult> results)
    {
        graph = new QualityGraphPanel(videos, results, allVideos: videos, separatePlaybackLayer: true);
        graphHost.Content = graph;
    }

    private void SetSample()
    {
        var videos = Enumerable.Range(0, 30).Select(index => new VideoDescriptor($"sample-{index}",
            $"サンプル動画{index + 1:00}.mkv", 1920, 1080, "av1", 30, 30, TimeSpan.FromSeconds(30), false, 900, 1,
            1_000_000 + index * 100_000)).ToArray();
        var results = new Dictionary<(string, QualityMetric), QualityResult>();
        foreach (var metric in Enum.GetValues<QualityMetric>())
        for (var index = 0; index < videos.Length; index++)
        {
            var qualityLevel = index * 3d / (videos.Length - 1);
            var frames = Enumerable.Range(0, 900).Select(number => new QualityFrame(number,
                metric switch
                {
                    QualityMetric.Ssim => 0.94 + qualityLevel * 0.012 + 0.01 * Math.Sin(number / 30d) - (number % 120 == 0 ? 0.08 : 0),
                    QualityMetric.Vmaf => 80 + qualityLevel * 4 + 3 * Math.Sin(number / 30d),
                    _ => 34 + qualityLevel * 3 + 2 * Math.Sin(number / 30d)
                })).ToArray();
            results[(videos[index].Id, metric)] = new(metric, frames.Average(frame => frame.Value), frames);
        }
        SetGraph(videos, results);
        status.Text = "30本の架空サンプルを表示中（実測品質ではありません）。実データはSSIM検証用JSONを読み込めます。凡例・選択・指標を変えて保存を確認してください。";
    }
}
