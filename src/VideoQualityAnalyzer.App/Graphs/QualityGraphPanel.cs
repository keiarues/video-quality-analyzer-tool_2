using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public enum GraphImageTarget { Frames, Files }

public sealed partial class QualityGraphPanel : UserControl
{
    private VideoDescriptor[] videos;
    private VideoDescriptor[]? allVideos;
    private IReadOnlyDictionary<string, string>? groups;
    private readonly HashSet<string> selectedIds;
    private readonly GraphPalette palette;
    private readonly StackPanel legend = new();
    private readonly Canvas playbackCanvas;
    private readonly QualityGraphLegendController legendController;
    private readonly Action<string>? selectVideoRequested;
    private readonly Canvas timeline = new() { ClipToBounds = true };
    private readonly Canvas playbackOverlay = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Canvas averages = new() { ClipToBounds = true };
    private readonly TabControl metric = new();
    private readonly Dictionary<string, Brush> videoColors = new();
    private readonly TextBlock leftPlaybackStatus = MakePlaybackLabel(TextAlignment.Right);
    private readonly TextBlock rightPlaybackStatus = MakePlaybackLabel(TextAlignment.Left);
    private readonly TextBlock playbackTimeLabel = MakePlaybackLabel(TextAlignment.Left);
    private readonly IDictionary<(string VideoId, QualityMetric Metric), QualityResult> playbackResults;
    private VideoDescriptor? playbackLeft;
    private VideoDescriptor? playbackRight;
    private string? playbackReferenceId;
    private double maximumTime;
    private TimeSpan playbackPosition;
    private bool playheadDragging;
    private VideoDescriptor? evaluationVideo;
    private TimeSpan? evaluationPosition;
    private readonly Line playhead = new() { StrokeThickness = 2,
        Tag = "再生位置", IsHitTestVisible = false };
    private readonly Action<TimeSpan>? seekRequested;
    public event Action<TimeSpan>? SeekPreviewed;
    private readonly Brush[] colors = [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod];

    public QualityGraphPanel(VideoDescriptor[] videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results,
        Action<TimeSpan>? seek = null, Action<string>? select = null, bool verify = false,
        VideoDescriptor[]? allVideos = null, IReadOnlyDictionary<string, string>? groups = null,
        bool frameOnly = false, bool separatePlaybackLayer = false, bool cacheStaticGraph = false, Action? clearSelection = null,
        bool useDarkPalette = true)
    {
        palette = useDarkPalette ? GraphPalette.Screen : GraphPalette.Export;
        colors = palette.Series;
        timeline.Background = palette.Plot;
        averages.Background = palette.Plot;
        playhead.Stroke = palette.Text;
        foreach (var label in new[] { leftPlaybackStatus, rightPlaybackStatus, playbackTimeLabel })
        {
            label.Foreground = palette.Text;
            label.Background = palette.Plot;
        }
        playbackResults = results;
        seekRequested = seek;
        selectVideoRequested = select;
        this.videos = videos;
        this.allVideos = allVideos;
        this.groups = groups;
        selectedIds = videos.Select(video => video.Id).ToHashSet();
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        Background = palette.Surface;
        Foreground = palette.Text;
        var panel = new DockPanel { Margin = new Thickness(4) };
        Content = panel;
        metric.Background = (Brush)FindResource("UiPreviewChromeBrush");
        metric.BorderBrush = (Brush)FindResource("UiPanelBorderBrush");
        metric.BorderThickness = new Thickness(1);
        foreach (var value in Enum.GetValues<QualityMetric>())
            metric.Items.Add(new TabItem
            {
                Header = new TextBlock { Text = value.ToString().ToUpperInvariant(),
                    Style = (Style)FindResource("UiPreviewTabHeaderStyle") },
                HeaderTemplate = (DataTemplate)FindResource("UiPreviewTabHeaderTemplate"),
                Tag = value,
                MinHeight = UiTheme.ControlMinHeight,
                BorderBrush = (Brush)FindResource("UiControlBorderBrush"),
                BorderThickness = new Thickness(1)
            });
        metric.SelectedIndex = 0;
        panel.Children.Add(metric);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(10) });
        var leftGraph = MakeGraph("フレームグラフ：時刻（秒）と品質", timeline);
        playbackCanvas = separatePlaybackLayer ? playbackOverlay : timeline;
        if (separatePlaybackLayer)
        {
            // 静的な軸・折れ線だけを保持し、再生位置と評価値は毎回描画する。
            if (cacheStaticGraph) timeline.CacheMode = new BitmapCache { EnableClearType = true };
            leftGraph.Children.Remove(timeline);
            var layers = new Grid();
            layers.Children.Add(timeline);
            layers.Children.Add(playbackOverlay);
            leftGraph.Children.Add(layers);
        }
        var rightGraph = MakeGraph("ファイルグラフ：平均ビットレート（kbps）と平均品質", averages);
        var legendScroll = new ScrollViewer { Content = legend, MaxHeight = 180,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var legendBody = new DockPanel();
        var legendHeader = new DockPanel();
        var collapseLegend = new Button { Content = "−", MinWidth = 24, Padding = new Thickness(2),
            ToolTip = "凡例を折りたたむ" };
        DockPanel.SetDock(collapseLegend, Dock.Right);
        legendHeader.Children.Add(collapseLegend);
        var legendDrag = new System.Windows.Controls.Primitives.Thumb { Height = 24,
            Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "ドラッグして凡例を移動",
            Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb))
            {
                VisualTree = MakeLegendHeader()
            } };
        legendHeader.Children.Add(legendDrag);
        DockPanel.SetDock(legendHeader, Dock.Top);
        legendBody.Children.Add(legendHeader);
        legendBody.Children.Add(legendScroll);
        var legendBox = new Border { Width = 350, Background = palette.Plot, BorderBrush = palette.Grid,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5), Child = legendBody };
        var overlay = new Canvas { ClipToBounds = true };
        overlay.Children.Add(legendBox);
        rightGraph.Children.Remove(averages);
        var fileLayers = new Grid();
        fileLayers.Children.Add(averages);
        fileLayers.Children.Add(overlay);
        rightGraph.Children.Add(fileLayers);
        createExportImage = (target, width, height, fontScale, imageTheme) =>
        {
            var selectedMetric = (QualityMetric)((TabItem)metric.SelectedItem).Tag;
            var available = (this.allVideos ?? this.videos).Where(video => playbackResults.ContainsKey((video.Id, selectedMetric))).ToArray();
            var exportPalette = GraphPalette.ForExport(imageTheme);
            var exportColors = exportPalette.Series;
            var series = available.Select((video, index) => new QualityGraphImageSeries(video,
                playbackResults[(video.Id, selectedMetric)], exportColors[index % exportColors.Length], selectedIds.Contains(video.Id),
                Bitrate(video), this.groups is not null && this.groups.TryGetValue(video.Id, out var group) ? group : null)).ToArray();
            return QualityGraphImageRenderer.Render(target, width, height, fontScale, selectedMetric, series, exportPalette);
        };
        legendController = new QualityGraphLegendController(legendBox, legendScroll, overlay, averages,
            legendDrag, collapseLegend, () => PlotArea(averages));
        Grid.SetColumn(rightGraph, 2);
        grid.Children.Add(leftGraph);
        grid.Children.Add(rightGraph);
        if (frameOnly)
        {
            rightGraph.Visibility = Visibility.Collapsed;
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            metric.SelectedIndex = (int)QualityMetric.Ssim;
        }
        var graphSplitter = new GridSplitter { Width = 10, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext, Cursor = System.Windows.Input.Cursors.SizeWE,
            Tag = "Vertical", Template = (ControlTemplate)FindResource("ResizeBarTemplate"), ToolTip = "グラフの横幅を変更" };
        Grid.SetColumn(graphSplitter, 1);
        grid.Children.Add(graphSplitter);
        var paneController = new QualityGraphPaneController(grid, leftGraph, rightGraph, graphSplitter, frameOnly);
        var minimizeFrame = paneController.FrameButton;
        var minimizeFile = paneController.FileButton;
        void ClearFileSelection()
        {
            selectedIds.Clear();
            // 主画面では未選択を全動画表示として扱う。
            clearSelection?.Invoke();
            Draw();
        }
        averages.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != averages) return;
            ClearFileSelection();
            e.Handled = true;
        };
        ((TabItem)metric.SelectedItem).Content = grid;
        metric.SelectionChanged += (_, e) =>
        {
            if (e.Source != metric) return;
            foreach (TabItem tab in metric.Items) tab.Content = null;
            if (metric.SelectedItem is TabItem active) active.Content = grid;
            Draw();
        };
        timeline.SizeChanged += (_, _) => Draw();
        averages.SizeChanged += (_, _) => Draw();
        InitializePlayheadInput();
        Loaded += (_, _) =>
        {
            Draw();
            if (verify) VerifyGraphLayout(grid, legendBox, legendScroll, legendDrag, collapseLegend,
                minimizeFrame, minimizeFile, paneController, ClearFileSelection, clearSelection);
        };
    }

    private readonly Func<GraphImageTarget, int, int, double, GraphImageTheme, System.Windows.Media.Imaging.BitmapSource> createExportImage;
    public System.Windows.Media.Imaging.BitmapSource CreateExportImage(GraphImageTarget target, int width, int height,
        double fontScale = 1, GraphImageTheme imageTheme = GraphImageTheme.Light) =>
        createExportImage(target, width, height, fontScale, imageTheme);

    public void ExportPng(string path, GraphImageTarget target, int width, int height,
        double fontScale = 1, GraphImageTheme imageTheme = GraphImageTheme.Light)
    {
        var bitmap = CreateExportImage(target, width, height, fontScale, imageTheme);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private readonly HashSet<string> displayedVideoIds = [];
    public IReadOnlyCollection<string> DisplayedVideoIds => displayedVideoIds;
    public int FilePointCount => averages.Children.OfType<Ellipse>().Count();
    internal void VerifyScreenPalette()
    {
        static bool SameColor(Brush actual, Brush expected) => actual is SolidColorBrush a &&
            expected is SolidColorBrush b && a.Color == b.Color;
        if (!SameColor(palette.Surface, GraphPalette.Screen.Surface) ||
            !SameColor(Background, palette.Surface) || !SameColor(timeline.Background, palette.Plot) ||
            !SameColor(averages.Background, palette.Plot) || !SameColor(playhead.Stroke, palette.Text) ||
            !SameColor(legend.Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First().Foreground, palette.Text) ||
            timeline.Children.OfType<Line>().Any(line => Equals(line.Tag, "グリッド") && !SameColor(line.Stroke, palette.Grid)))
            throw new InvalidOperationException("画面内グラフの背景・文字・グリッドがパレットと一致しません。");
    }
    internal int FrameSegmentCount => timeline.Children.OfType<Polyline>().Count(line => line.Tag is null);
    public void UpdateData(VideoDescriptor[] selected, VideoDescriptor[] all, IReadOnlyDictionary<string, string>? groups)
    {
        videos = selected;
        allVideos = all;
        this.groups = groups;
        selectedIds.Clear();
        selectedIds.UnionWith(videos.Select(video => video.Id));
        Draw();
    }
    public void SetEvaluationProgress(VideoDescriptor video, QualityMetric value, TimeSpan position)
    {
        var changed = evaluationVideo?.Id != video.Id || evaluationPosition is null || (int)value != metric.SelectedIndex;
        evaluationVideo = video;
        evaluationPosition = position;
        playbackPosition = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, video.Duration.TotalSeconds)));
        EvaluationProgressUpdateCount++;
        LastEvaluationProgressPosition = playbackPosition;
        SelectMetric(value);
        if (changed) Draw(); else UpdatePlayhead();
    }

    public void EndEvaluationProgress(TimeSpan position)
    {
        evaluationVideo = null;
        evaluationPosition = null;
        playbackPosition = position;
        Draw();
    }

    public QualityMetric SelectedMetric => (QualityMetric)((TabItem)metric.SelectedItem).Tag;
    public void SelectMetric(QualityMetric value) => metric.SelectedIndex = (int)value;
    public void SeekToFraction(double fraction)
    {
        var position = TimeSpan.FromSeconds(maximumTime * Math.Clamp(fraction, 0, 1));
        SetPlaybackPosition(position);
        seekRequested?.Invoke(position);
    }
    public void SelectFile(string id)
    {
        var point = averages.Children.OfType<Ellipse>().Single(item => Equals(item.Tag, id));
        point.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
    }

}
