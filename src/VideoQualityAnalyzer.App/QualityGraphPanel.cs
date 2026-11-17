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
    private readonly StackPanel legend = new();
    private readonly Canvas playbackCanvas;
    private readonly QualityGraphLegendController legendController;
    private readonly Action<string>? selectVideoRequested;
    private readonly Canvas timeline = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly Canvas playbackOverlay = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Canvas averages = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly TabControl metric = new();
    private readonly Dictionary<string, Brush> videoColors = new();
    private readonly TextBlock leftPlaybackStatus = MakePlaybackLabel(TextAlignment.Right);
    private readonly TextBlock rightPlaybackStatus = MakePlaybackLabel(TextAlignment.Left);
    private readonly TextBlock playbackTimeLabel = MakePlaybackLabel(TextAlignment.Left);
    private readonly IDictionary<(string VideoId, QualityMetric Metric), QualityResult> playbackResults;
    private VideoDescriptor? playbackLeft;
    private VideoDescriptor? playbackRight;
    private string? playbackReferenceId;
    private Action redrawPlaybackGraph = () => { };
    private double maximumTime;
    private TimeSpan playbackPosition;
    private bool playheadDragging;
    private VideoDescriptor? evaluationVideo;
    private TimeSpan? evaluationPosition;
    private readonly Line playhead = new() { Stroke = UiTheme.OnLight, StrokeThickness = 2,
        Tag = "再生位置", IsHitTestVisible = false };
    private readonly Action<TimeSpan>? seekRequested;
    public event Action<TimeSpan>? SeekPreviewed;
    private readonly Brush[] colors = [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod];

    public QualityGraphPanel(VideoDescriptor[] videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results,
        Action<TimeSpan>? seek = null, Action<string>? select = null, bool verify = false,
        VideoDescriptor[]? allVideos = null, IReadOnlyDictionary<string, string>? groups = null,
        bool frameOnly = false, bool separatePlaybackLayer = false, bool cacheStaticGraph = false, Action? clearSelection = null)
    {
        playbackResults = results;
        seekRequested = seek;
        selectVideoRequested = select;
        this.videos = videos;
        this.allVideos = allVideos;
        this.groups = groups;
        selectedIds = videos.Select(video => video.Id).ToHashSet();
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        Background = Brushes.White;
        Foreground = UiTheme.OnLight;
        var panel = new DockPanel { Margin = new Thickness(4) };
        Content = panel;
        foreach (var value in Enum.GetValues<QualityMetric>())
            metric.Items.Add(new TabItem { Header = value.ToString().ToUpperInvariant(), Tag = value });
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
        var legendBox = new Border { Width = 350, Background = Brushes.WhiteSmoke, BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5), Child = legendBody };
        var overlay = new Canvas { ClipToBounds = true };
        overlay.Children.Add(legendBox);
        rightGraph.Children.Remove(averages);
        var fileLayers = new Grid();
        fileLayers.Children.Add(averages);
        fileLayers.Children.Add(overlay);
        rightGraph.Children.Add(fileLayers);
        createExportImage = (target, width, height, fontScale) =>
        {
            var selectedMetric = (QualityMetric)((TabItem)metric.SelectedItem).Tag;
            var available = (this.allVideos ?? this.videos).Where(video => playbackResults.ContainsKey((video.Id, selectedMetric))).ToArray();
            var series = available.Select((video, index) => new QualityGraphImageSeries(video,
                playbackResults[(video.Id, selectedMetric)], colors[index % colors.Length], selectedIds.Contains(video.Id),
                Bitrate(video), this.groups is not null && this.groups.TryGetValue(video.Id, out var group) ? group : null)).ToArray();
            return QualityGraphImageRenderer.Render(target, width, height, fontScale, selectedMetric, series);
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
        redrawPlaybackGraph = Draw;
        updateData = (selectedVideos, registeredVideos, groupNames) =>
        {
            this.videos = selectedVideos;
            this.allVideos = registeredVideos;
            this.groups = groupNames;
            selectedIds.Clear();
            selectedIds.UnionWith(this.videos.Select(video => video.Id));
            Draw();
        };
        seekToFraction = fraction =>
        {
            var position = TimeSpan.FromSeconds(maximumTime * Math.Clamp(fraction, 0, 1));
            SetPlaybackPosition(position);
            seek?.Invoke(position);
        };
        metric.SelectionChanged += (_, e) =>
        {
            if (e.Source != metric) return;
            foreach (TabItem tab in metric.Items) tab.Content = null;
            if (metric.SelectedItem is TabItem active) active.Content = grid;
            Draw();
        };
        timeline.SizeChanged += (_, _) => Draw();
        averages.SizeChanged += (_, _) => Draw();
        timeline.Cursor = System.Windows.Input.Cursors.SizeWE;
        timeline.MouseLeftButtonDown += (_, e) =>
        {
            var area = PlotArea(timeline);
            var position = e.GetPosition(timeline);
            if (maximumTime <= 0 || !area.Contains(position) || !timeline.CaptureMouse()) return;
            BeginPlayheadDrag((position.X - area.Left) / area.Width);
            e.Handled = true;
        };
        timeline.MouseMove += (_, e) =>
        {
            if (!playheadDragging) return;
            var area = PlotArea(timeline);
            MovePlayheadDrag((e.GetPosition(timeline).X - area.Left) / area.Width);
            e.Handled = true;
        };
        timeline.MouseLeftButtonUp += (_, e) =>
        {
            if (!playheadDragging) return;
            var area = PlotArea(timeline);
            MovePlayheadDrag((e.GetPosition(timeline).X - area.Left) / area.Width);
            EndPlayheadDrag();
            timeline.ReleaseMouseCapture();
            e.Handled = true;
        };
        timeline.LostMouseCapture += (_, _) => { if (playheadDragging) EndPlayheadDrag(); };
        Loaded += (_, _) =>
        {
            Draw();
            if (verify) VerifyGraphLayout(grid, legendBox, legendScroll, legendDrag, collapseLegend,
                minimizeFrame, minimizeFile, paneController, ClearFileSelection, clearSelection);
        };
    }

    private readonly Func<GraphImageTarget, int, int, double, System.Windows.Media.Imaging.BitmapSource> createExportImage;
    public System.Windows.Media.Imaging.BitmapSource CreateExportImage(GraphImageTarget target, int width, int height, double fontScale = 1) =>
        createExportImage(target, width, height, fontScale);

    public void ExportPng(string path, GraphImageTarget target, int width, int height, double fontScale = 1)
    {
        var bitmap = CreateExportImage(target, width, height, fontScale);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private readonly HashSet<string> displayedVideoIds = [];
    private readonly Action<VideoDescriptor[], VideoDescriptor[], IReadOnlyDictionary<string, string>?> updateData;
    private readonly Action<double> seekToFraction;
    public IReadOnlyCollection<string> DisplayedVideoIds => displayedVideoIds;
    public int FilePointCount => averages.Children.OfType<Ellipse>().Count();
    internal int FrameSegmentCount => timeline.Children.OfType<Polyline>().Count(line => line.Tag is null);
    public void UpdateData(VideoDescriptor[] selected, VideoDescriptor[] all, IReadOnlyDictionary<string, string>? groups) => updateData(selected, all, groups);
    public void SetEvaluationProgress(VideoDescriptor video, QualityMetric value, TimeSpan position)
    {
        var changed = evaluationVideo?.Id != video.Id || evaluationPosition is null || (int)value != metric.SelectedIndex;
        evaluationVideo = video;
        evaluationPosition = position;
        playbackPosition = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, video.Duration.TotalSeconds)));
        EvaluationProgressUpdateCount++;
        LastEvaluationProgressPosition = playbackPosition;
        SelectMetric(value);
        if (changed) redrawPlaybackGraph(); else UpdatePlayhead();
    }

    public void EndEvaluationProgress(TimeSpan position)
    {
        evaluationVideo = null;
        evaluationPosition = null;
        playbackPosition = position;
        redrawPlaybackGraph();
    }

    public QualityMetric SelectedMetric => (QualityMetric)((TabItem)metric.SelectedItem).Tag;
    public void SelectMetric(QualityMetric value) => metric.SelectedIndex = (int)value;
    public void SeekToFraction(double fraction) => seekToFraction(fraction);
    public void SelectFile(string id)
    {
        var point = averages.Children.OfType<Ellipse>().Single(item => Equals(item.Tag, id));
        point.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
    }

    public TimeSpan PlayheadPosition => playbackPosition;
    internal int EvaluationProgressUpdateCount { get; private set; }
    internal TimeSpan LastEvaluationProgressPosition { get; private set; }
    public double PlayheadX => playhead.X1;
    // 専用操作試験用。キャッシュ表示と、その場で描き直した静的グラフを照合する。
    internal void VerifyCachedGraphPixels()
    {
        UpdateLayout();
        byte[] Capture()
        {
            var width = Math.Max(1, (int)timeline.ActualWidth);
            var height = Math.Max(1, (int)timeline.ActualHeight);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(timeline);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        var mode = timeline.CacheMode;
        if (mode is not BitmapCache) throw new InvalidOperationException("静的グラフのキャッシュが有効ではありません。");
        var cached = Capture();
        try
        {
            timeline.CacheMode = null;
            var fresh = Capture();
            var difference = cached.Zip(fresh, (a, b) => Math.Abs(a - b)).Average();
            if (difference > 3) throw new InvalidOperationException($"キャッシュと再描画した画像が一致しません（平均差 {difference:F3}）。");
        }
        finally { timeline.CacheMode = mode; }
    }
    public void SetPlaybackPosition(TimeSpan position)
    {
        if (playheadDragging || evaluationPosition is not null) return;
        playbackPosition = position;
        UpdatePlayhead();
    }

    public void BeginPlayheadDrag(double fraction)
    {
        if (maximumTime <= 0 || evaluationPosition is not null) return;
        playheadDragging = true;
        MovePlayheadDrag(fraction);
    }

    public void MovePlayheadDrag(double fraction)
    {
        if (!playheadDragging) return;
        playbackPosition = TimeSpan.FromSeconds(maximumTime * Math.Clamp(fraction, 0, 1));
        UpdatePlayhead();
        SeekPreviewed?.Invoke(playbackPosition);
    }

    public void EndPlayheadDrag()
    {
        if (!playheadDragging) return;
        playheadDragging = false;
        seekRequested?.Invoke(playbackPosition);
    }

    public void SetPlaybackSources(VideoDescriptor? left, VideoDescriptor? right, string? referenceId)
    {
        playbackLeft = left;
        playbackRight = right;
        playbackReferenceId = referenceId;
        redrawPlaybackGraph();
    }

    private static TextBlock MakePlaybackLabel(TextAlignment alignment) => new()
    {
        FontSize = UiTheme.Small, Foreground = UiTheme.OnLight, Background = Brushes.WhiteSmoke,
        TextAlignment = alignment, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false
    };

    private string PlaybackValue(VideoDescriptor? video)
    {
        if (video is null || video.Id == playbackReferenceId ||
            metric.SelectedItem is not TabItem { Tag: QualityMetric selected } ||
            !playbackResults.TryGetValue((video.Id, selected), out var result)) return "--";
        var number = (long)Math.Floor(playbackPosition.TotalSeconds * video.AverageFrameRate + 0.000001);
        // 長時間動画でも毎回全フレームを走査しない。
        var lower = 0;
        var upper = result.Frames.Count - 1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / 2;
            var frame = result.Frames[middle];
            if (frame.Number == number) return frame.Value.ToString("F4");
            if (frame.Number < number) lower = middle + 1; else upper = middle - 1;
        }
        return "--";
    }

    private void UpdatePlayhead()
    {
        var area = PlotArea(timeline);
        playhead.Visibility = maximumTime > 0 ? Visibility.Visible : Visibility.Collapsed;
        playhead.X1 = playhead.X2 = area.Left + Math.Clamp(playbackPosition.TotalSeconds / Math.Max(0.001, maximumTime), 0, 1) * area.Width;
        playhead.Y1 = area.Top;
        playhead.Y2 = area.Bottom;
        leftPlaybackStatus.Foreground = playbackLeft is not null && videoColors.TryGetValue(playbackLeft.Id, out var leftColor)
            ? leftColor : UiTheme.OnLight;
        rightPlaybackStatus.Foreground = playbackRight is not null && videoColors.TryGetValue(playbackRight.Id, out var rightColor)
            ? rightColor : UiTheme.OnLight;
        leftPlaybackStatus.Text = $"{PlaybackValue(playbackLeft)}\n{playbackLeft?.Name ?? "--"}";
        rightPlaybackStatus.Text = $"{PlaybackValue(playbackRight)}\n{playbackRight?.Name ?? "--"}";
        playbackTimeLabel.Text = $"{playbackPosition.TotalSeconds:0.##}秒";
        var x = playhead.X1;
        void Place(TextBlock label, double start, double width, double bottom)
        {
            label.Visibility = playhead.Visibility;
            label.Width = Math.Max(0, width);
            label.Measure(new Size(label.Width, double.PositiveInfinity));
            Canvas.SetLeft(label, start);
            Canvas.SetTop(label, Math.Max(area.Top, bottom - label.DesiredSize.Height));
        }
        Place(leftPlaybackStatus, Math.Max(0, x - 226), Math.Min(220, Math.Max(0, x - 6)), area.Bottom - 4);
        Place(rightPlaybackStatus, x + 6, Math.Min(220, Math.Max(0, timeline.ActualWidth - x - 6)), area.Bottom - 4);
        Place(playbackTimeLabel, x + 6, Math.Min(220, Math.Max(0, timeline.ActualWidth - x - 6)),
            area.Bottom - rightPlaybackStatus.DesiredSize.Height - 8);
    }

    private static FrameworkElementFactory MakeLegendHeader()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, "凡例（移動可）");
        text.SetValue(TextBlock.ForegroundProperty, UiTheme.OnLight);
        text.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent);
        return text;
    }

    private static DockPanel MakeGraph(string title, Canvas canvas)
    {
        var panel = new DockPanel { Margin = new Thickness(6) };
        var label = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label);
        panel.Children.Add(canvas);
        return panel;
    }

    private static double Bitrate(VideoDescriptor video) => video.AverageBitrateKbps ?? 0;

    // 軸・目盛りと描画データは同じ領域を基準に配置する。
    private Rect PlotArea(Canvas canvas) => new(88, 28,
        Math.Max(1, canvas.ActualWidth - 114), Math.Max(1, canvas.ActualHeight - 82));

    private void DrawAxes(Canvas canvas, Rect area, double maximumX, double minimumY, double maximumY,
        string unit, QualityMetric selected)
    {
        const int divisions = 5;
        for (var index = 0; index <= divisions; index++)
        {
            var fraction = index / (double)divisions;
            var x = area.Left + fraction * area.Width;
            var y = area.Bottom - fraction * area.Height;
            canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = area.Top, Y2 = area.Bottom,
                Stroke = Brushes.LightGray, StrokeThickness = 1, Tag = "グリッド", IsHitTestVisible = false });
            canvas.Children.Add(new Line { X1 = area.Left, X2 = area.Right, Y1 = y, Y2 = y,
                Stroke = Brushes.LightGray, StrokeThickness = 1, Tag = "グリッド", IsHitTestVisible = false });
            AddTick(canvas, (maximumX * fraction).ToString(unit == "kbps" ? "N0" : "0.##") + (unit == "kbps" ? " kbps" : "秒"), x, area.Bottom + 6, true);
            AddTick(canvas, (minimumY + (maximumY - minimumY) * fraction).ToString("G4"), area.Left - 7, y, false);
        }
        canvas.Children.Add(new Polyline { Stroke = Brushes.DimGray, StrokeThickness = 1,
            IsHitTestVisible = false, Tag = "軸", Points = new PointCollection
                { new(area.Left, area.Top), new(area.Left, area.Bottom), new(area.Right, area.Bottom) } });
        var axisName = new TextBlock { Text = selected.ToString().ToUpperInvariant(),
            FontSize = (double)FindResource("UiFontMedium"), Foreground = UiTheme.OnLight,
            RenderTransform = new RotateTransform(-90), Tag = "縦軸名", IsHitTestVisible = false };
        axisName.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(axisName, 6);
        Canvas.SetTop(axisName, area.Top + (area.Height + axisName.DesiredSize.Width) / 2);
        canvas.Children.Add(axisName);
        var qualityGuide = new TextBlock { Text = "↑ 高画質", FontSize = UiTheme.Small,
            Foreground = UiTheme.OnLight, Tag = "品質方向", IsHitTestVisible = false };
        Canvas.SetLeft(qualityGuide, area.Left + 4);
        Canvas.SetTop(qualityGuide, 2);
        canvas.Children.Add(qualityGuide);
        var caption = new TextBlock { Text = unit, FontSize = (double)FindResource("UiFontMedium"), Foreground = UiTheme.OnLight, IsHitTestVisible = false };
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, area.Left + (area.Width - caption.DesiredSize.Width) / 2);
        Canvas.SetTop(caption, area.Bottom + 28);
        canvas.Children.Add(caption);
    }

    private void AddTick(Canvas canvas, string text, double x, double y, bool horizontal)
    {
        var label = new TextBlock { Text = text, Foreground = UiTheme.OnLight, FontSize = UiTheme.Small,
            Tag = "目盛り", IsHitTestVisible = false };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, horizontal ? Math.Clamp(x - label.DesiredSize.Width / 2, 0,
            Math.Max(0, canvas.ActualWidth - label.DesiredSize.Width)) : x - label.DesiredSize.Width);
        Canvas.SetTop(label, horizontal ? y : y - label.DesiredSize.Height / 2);
        canvas.Children.Add(label);
    }
}
