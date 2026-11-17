using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

internal sealed record GraphImageSaveSource(VideoDescriptor Reference, VideoDescriptor[] SelectedVideos,
    VideoDescriptor[] AllVideos, Dictionary<(string VideoId, QualityMetric Metric), QualityResult> Results,
    IReadOnlyDictionary<string, string> Groups, QualityMetric SelectedMetric);

// 専用テスト画面で確認した画像生成とプレビュー操作を本体の保存画面でも使用する。
internal sealed partial class GraphImageSaveWindow : Window
{
    private readonly ContentControl graphHost = new();
    private readonly Button saveButton;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 22 };
    private readonly ComboBox target = new() { Width = 145 };
    private readonly ComboBox metricSelection = new() { Width = 85 };
    private readonly ComboBox imageTheme = new() { Width = 100 };
    private readonly ComboBox size = new() { Width = 160 };
    private readonly TextBox dimension = new() { Width = 70, Text = "1024" };
    private readonly TextBox heightDimension = new() { Width = 70, Text = "768" };
    private readonly ComboBox font = new() { Width = 80 };
    private readonly Image preview = new() { Stretch = Stretch.Fill };
    private readonly Grid viewport = new() { Background = Brushes.LightSlateGray };
    private readonly ScrollViewer scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Border previewFrame;
    private readonly Slider zoom = new() { Minimum = 10, Maximum = 500, TickFrequency = 10,
        SmallChange = 10, LargeChange = 10, IsSnapToTickEnabled = true, Width = 230, Value = 100 };
    private readonly TextBlock zoomLabel = new() { Width = 65, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly (int Width, int Height)[] presets = [(800, 600), (1024, 768), (1280, 960),
        (1600, 1200), (1920, 1440), (2560, 1920), (3840, 2880)];
    private QualityGraphPanel graph = null!;
    private string referenceFileName = "サンプル基準動画.mkv";
    private bool fitToWindow;
    private bool updatingZoom;
    private bool updatingSizeInputs;
    private double displayScale = 1;

    internal BitmapSource? CurrentPreviewImage => preview.Source as BitmapSource;
    internal string ReferenceFileName => referenceFileName;
    internal QualityMetric CurrentMetric => graph.SelectedMetric;
    internal GraphImageTheme CurrentImageTheme => (GraphImageTheme)imageTheme.SelectedIndex;
    internal int FilePointCount => graph.FilePointCount;
    internal int SelectedVideoCount => graph.DisplayedVideoIds.Count;

    public GraphImageSaveWindow(GraphImageSaveSource source) : this(false, "", source) { }

    public GraphImageSaveWindow(bool automatic, string outputDirectory) : this(automatic, outputDirectory, null) { }

    private GraphImageSaveWindow(bool automatic, string outputDirectory, GraphImageSaveSource? source)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        viewport.Background = (Brush)FindResource("PanelBrush");
        Title = source is null ? "グラフ画像保存テスト" : "グラフを画像にして保存";
        Width = 1400; Height = 850; MinWidth = 900; MinHeight = 600;
        var root = new DockPanel { Margin = new Thickness(8) }; Content = root;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var saveOptions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (Control input in new Control[] { target, metricSelection, imageTheme, size, dimension, heightDimension, font })
        {
            input.Height = 32;
            input.Margin = new Thickness(3, 2, 3, 2);
            input.VerticalContentAlignment = VerticalAlignment.Center;
        }
        foreach (var text in new[] { "フレームグラフ", "ファイルグラフ" }) target.Items.Add(text);
        target.SelectedIndex = 1;
        saveOptions.Children.Add(Group(Label("保存対象："), target));
        foreach (var value in Enum.GetValues<QualityMetric>()) metricSelection.Items.Add(value.ToString().ToUpperInvariant());
        metricSelection.SelectedIndex = 0;
        saveOptions.Children.Add(Group(Label("評価方式："), metricSelection));
        imageTheme.Items.Add("ライト"); imageTheme.Items.Add("ダーク");
        imageTheme.SelectedIndex = 0;
        saveOptions.Children.Add(Group(Label("画像のテーマ："), imageTheme));
        foreach (var text in new[] { "現在の表示サイズ", "幅と高さを指定" }) size.Items.Add(text);
        foreach (var preset in presets) size.Items.Add($"{preset.Width} × {preset.Height}");
        size.SelectedIndex = 3;
        saveOptions.Children.Add(Group(Label("画像サイズ："), size));
        saveOptions.Children.Add(Group(dimension, Label("×"), heightDimension, Label("px")));
        foreach (var text in new[] { "0.5倍", "0.8倍", "1倍", "1.2倍", "1.5倍", "2倍" }) font.Items.Add(text);
        font.SelectedIndex = 2;
        font.ToolTip = "出力画像上の文字サイズ：目盛り・凡例は18px、見出し・単位は22pxに倍率を掛けます。";
        saveOptions.Children.Add(Group(Label("フォント："), font));
        saveButton = BarButton("PNG画像を保存", 128); saveOptions.Children.Add(saveButton);
        var load = BarButton("実データを読み込む", 140);
        if (source is null) toolbar.Children.Add(load);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        if (source is null)
        {
            var testGraph = new Expander { Header = "グラフ操作（テスト用）", IsExpanded = true,
                Height = 250, Content = graphHost };
            DockPanel.SetDock(testGraph, Dock.Top); root.Children.Add(testGraph);
        }
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
        root.Children.Add(previewBody);
        if (source is null) SetSample();
        else
        {
            SetGraph(source.SelectedVideos, source.AllVideos, source.Results, source.Reference.Name, source.Groups);
            graph.SelectMetric(source.SelectedMetric);
            metricSelection.SelectedIndex = (int)source.SelectedMetric;
        }
        UpdateSizeInputs();
        target.SelectionChanged += (_, _) => RefreshPreview();
        imageTheme.SelectionChanged += (_, _) => RefreshPreview();
        metricSelection.SelectionChanged += (_, _) =>
        {
            if (metricSelection.SelectedIndex < 0) return;
            graph.SelectMetric((QualityMetric)metricSelection.SelectedIndex);
            RefreshPreview();
        };
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
        saveButton.Click += (_, _) =>
        {
            var dialog = CreateSaveDialog(DateTime.Now);
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var bitmap = SavePng(dialog.FileName);
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
                var loadedVideos = cache.Entries.Select(entry => entry.Video).ToArray();
                SetGraph(loadedVideos, loadedVideos,
                    cache.Entries.ToDictionary(entry => (entry.Video.Id, QualityMetric.Ssim), entry => entry.Result),
                    cache.Reference.Name);
                graph.SelectMetric(QualityMetric.Ssim); metricSelection.SelectedIndex = (int)QualityMetric.Ssim;
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
        Content = text, Width = width, MinWidth = width, Height = UiTheme.ActionButtonHeight,
        Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(3, 2, 3, 2),
        VerticalContentAlignment = VerticalAlignment.Center, BorderBrush = UiTheme.ControlBorder
    };
    private void SetGraph(VideoDescriptor[] selectedVideos, VideoDescriptor[] allVideos,
        Dictionary<(string VideoId, QualityMetric Metric), QualityResult> results, string referenceName,
        IReadOnlyDictionary<string, string>? groups = null)
    {
        referenceFileName = referenceName;
        graph = new QualityGraphPanel(selectedVideos, results, allVideos: allVideos,
            groups: groups, separatePlaybackLayer: true);
        graphHost.Content = graph;
    }


}
