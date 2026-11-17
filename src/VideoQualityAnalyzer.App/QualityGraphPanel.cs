using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public enum GraphImageTarget { Frames, Files }

public sealed class QualityGraphPanel : UserControl
{
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
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        Background = Brushes.White;
        Foreground = UiTheme.OnLight;
        var panel = new DockPanel { Margin = new Thickness(4) };
        Content = panel;
        foreach (var value in Enum.GetValues<QualityMetric>())
            metric.Items.Add(new TabItem { Header = value.ToString().ToUpperInvariant(), Tag = value });
        metric.SelectedIndex = 0;
        panel.Children.Add(metric);
        var selectedIds = videos.Select(video => video.Id).ToHashSet();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(10) });
        var leftGraph = MakeGraph("フレームグラフ：時刻（秒）と品質", timeline);
        var playbackCanvas = separatePlaybackLayer ? playbackOverlay : timeline;
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
        void AttachPlaybackVisuals()
        {
            playbackCanvas.Children.Add(playhead);
            playbackCanvas.Children.Add(leftPlaybackStatus);
            playbackCanvas.Children.Add(rightPlaybackStatus);
            playbackCanvas.Children.Add(playbackTimeLabel);
        }
        var rightGraph = MakeGraph("ファイルグラフ：平均ビットレート（kbps）と平均品質", averages);
        var legend = new StackPanel();
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
            var available = (allVideos ?? videos).Where(video => results.ContainsKey((video.Id, selectedMetric))).ToArray();
            var series = available.Select((video, index) => new QualityGraphImageSeries(video,
                results[(video.Id, selectedMetric)], colors[index % colors.Length], selectedIds.Contains(video.Id),
                Bitrate(video), groups is not null && groups.TryGetValue(video.Id, out var group) ? group : null)).ToArray();
            return QualityGraphImageRenderer.Render(target, width, height, fontScale, selectedMetric, series);
        };
        var legendController = new QualityGraphLegendController(legendBox, legendScroll, overlay, averages,
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
        var minimizedColumn = -1;
        GridLength savedLeft = new(1, GridUnitType.Star), savedRight = new(1, GridUnitType.Star);
        var graphToggles = new Dictionary<int, Button>();
        Button AddMinimize(DockPanel graph, int column, string name)
        {
            var title = graph.Children[0];
            var header = new DockPanel();
            var toggle = new Button { Content = column == 0 ? "◀" : "▶", MinWidth = 24,
                Padding = new Thickness(2), ToolTip = name + "を最小化" };
            graphToggles[column] = toggle;
            graph.Children.Remove(title);
            DockPanel.SetDock(toggle, column == 0 ? Dock.Right : Dock.Left);
            header.Children.Add(toggle);
            header.Children.Add(title);
            DockPanel.SetDock(header, Dock.Top);
            graph.Children.Insert(0, header);
            toggle.Click += (_, _) =>
            {
                var restore = minimizedColumn != -1;
                if (minimizedColumn == -1) { savedLeft = grid.ColumnDefinitions[0].Width; savedRight = grid.ColumnDefinitions[2].Width; }
                minimizedColumn = restore ? -1 : column;
                foreach (var (pane, paneColumn) in new[] { (leftGraph, 0), (rightGraph, 2) })
                {
                    var minimized = minimizedColumn == paneColumn;
                    var paneHeader = (DockPanel)pane.Children[0];
                    paneHeader.Children[1].Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
                    foreach (UIElement child in pane.Children)
                        if (child != paneHeader) child.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
                    grid.ColumnDefinitions[paneColumn].Width = minimized ? new GridLength(0) :
                        minimizedColumn == -1 ? (paneColumn == 0 ? savedLeft : savedRight) : new GridLength(1, GridUnitType.Star);
                    graphToggles[paneColumn].Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
                    graphToggles[paneColumn].Content = paneColumn == 0 ? "◀" : "▶";
                    graphToggles[paneColumn].ToolTip = minimizedColumn == -1
                        ? (paneColumn == 0 ? "フレームグラフ" : "ファイルグラフ") + "を最小化"
                        : (minimizedColumn == 0 ? "フレームグラフ" : "ファイルグラフ") + "を復元";
                }
                graphSplitter.IsEnabled = minimizedColumn == -1;
                graphSplitter.Visibility = minimizedColumn == -1 && !frameOnly ? Visibility.Visible : Visibility.Collapsed;
                grid.ColumnDefinitions[1].Width = new GridLength(minimizedColumn == -1 && !frameOnly ? 10 : 0);
            };
            return toggle;
        }
        var minimizeFrame = AddMinimize(leftGraph, 0, "フレームグラフ");
        var minimizeFile = AddMinimize(rightGraph, 2, "ファイルグラフ");
        if (frameOnly) { graphSplitter.Visibility = Visibility.Collapsed; grid.ColumnDefinitions[1].Width = new GridLength(0); }
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
        void SelectVideo(string id)
        {
            selectedIds.Clear();
            selectedIds.Add(id);
            select?.Invoke(id);
            Draw();
        }
        void Draw()
        {
            displayedVideoIds.Clear();
            videoColors.Clear();
            timeline.Children.Clear();
            playbackOverlay.Children.Clear();
            averages.Children.Clear();
            legend.Children.Clear();
            if (metric.SelectedItem is not TabItem { Tag: QualityMetric selected }) return;
            var available = (allVideos ?? videos).Where(video => results.ContainsKey((video.Id, selected))).ToArray();
            if (available.Length == 0)
            {
                var durations = new[] { playbackLeft, playbackRight }.OfType<VideoDescriptor>()
                    .Where(video => video.Duration > TimeSpan.Zero).Select(video => video.Duration.TotalSeconds).ToArray();
                maximumTime = evaluationVideo?.Duration.TotalSeconds ?? (durations.Length > 0 ? durations.Min() : 0);
                if (maximumTime > 0) DrawAxes(timeline, PlotArea(timeline), maximumTime, 0, 1, "秒", selected);
                AttachPlaybackVisuals();
                UpdatePlayhead();
                legendController.Place();
                return;
            }
            // フレーム値とファイル平均値は変動幅が異なるため、表示対象ごとに軸を求める。
            var frameVideos = available.Where(video => selectedIds.Contains(video.Id)).ToArray();
            var (min, max) = QualityGraphScale.VerticalRange(frameVideos.SelectMany(video =>
                results[(video.Id, selected)].Frames.Select(frame => frame.Value)));
            var (fileMin, fileMax) = QualityGraphScale.VerticalRange(available.Select(video => results[(video.Id, selected)].Average));
            maximumTime = available.Max(video => results[(video.Id, selected)].Frames.Max(frame => frame.Number) / video.AverageFrameRate);
            if (available.Any(video => results[(video.Id, selected)].Selection.IsPartial))
                maximumTime = available.Max(video => video.Duration.TotalSeconds);
            if (evaluationVideo is not null) maximumTime = evaluationVideo.Duration.TotalSeconds;
            var rates = available.Select(Bitrate).ToArray();
            var maxRate = Math.Max(1, rates.Max());
            var timeArea = PlotArea(timeline);
            var fileArea = PlotArea(averages);
            DrawAxes(timeline, timeArea, maximumTime, min, max, "秒", selected);
            DrawAxes(averages, fileArea, maxRate, fileMin, fileMax, "kbps", selected);
            var previousPoints = new Dictionary<string, Point>();
            for (var index = 0; index < available.Length; index++)
            {
                var video = available[index];
                var result = results[(video.Id, selected)];
                var color = colors[index % colors.Length];
                videoColors[video.Id] = color;
                var line = new Polyline { Stroke = color, StrokeThickness = 1, ToolTip = video.Name };
                var isSelected = selectedIds.Contains(video.Id);
                long? previousNumber = null;
                foreach (var frame in result.Frames)
                {
                    if (result.Selection.IsPartial && previousNumber is { } previous && frame.Number != previous + 1)
                    {
                        if (isSelected) timeline.Children.Add(line);
                        line = new Polyline { Stroke = color, StrokeThickness = 1, ToolTip = video.Name };
                    }
                    line.Points.Add(new Point(timeArea.Left + frame.Number / video.AverageFrameRate / Math.Max(0.001, maximumTime) * timeArea.Width,
                        timeArea.Top + (max - frame.Value) / (max - min) * timeArea.Height));
                    previousNumber = frame.Number;
                }
                if (isSelected) { timeline.Children.Add(line); displayedVideoIds.Add(video.Id); }
                var point = new Ellipse { Width = 12, Height = 12, Fill = color,
                    Stroke = isSelected ? Brushes.Black : null, StrokeThickness = 2,
                    Tag = video.Id, ToolTip = $"{video.Name}: {result.Average:F4} / {rates[index]:N0} kbps\n{result.Selection.Description}" +
                        (result.Selection.IsPartial ? "（評価対象のみの平均）" : ""), Cursor = System.Windows.Input.Cursors.Hand };
                Canvas.SetLeft(point, fileArea.Left + rates[index] / maxRate * fileArea.Width - 6);
                Canvas.SetTop(point, fileArea.Top + (fileMax - result.Average) / (fileMax - fileMin) * fileArea.Height - 6);
                point.MouseLeftButtonDown += (_, e) => { SelectVideo(video.Id); e.Handled = true; };
                if (groups is not null && groups.TryGetValue(video.Id, out var group))
                {
                    var center = new Point(Canvas.GetLeft(point) + 6, Canvas.GetTop(point) + 6);
                    if (previousPoints.TryGetValue(group, out var previous))
                        averages.Children.Add(new Line { X1 = previous.X, Y1 = previous.Y,
                            X2 = center.X, Y2 = center.Y, Stroke = Brushes.Gray, StrokeThickness = 1, IsHitTestVisible = false });
                    previousPoints[group] = center;
                }
                averages.Children.Add(point);
                var label = new TextBlock { Text = $"{result.Average:F4} / {rates[index]:N0} kbps",
                    FontSize = UiTheme.Small, Foreground = UiTheme.OnLight, Background = Brushes.WhiteSmoke,
                    IsHitTestVisible = false, Tag = "点の評価値" };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, Math.Clamp(Canvas.GetLeft(point) + 14, fileArea.Left,
                    Math.Max(fileArea.Left, averages.ActualWidth - label.DesiredSize.Width - 4)));
                Canvas.SetTop(label, Math.Clamp(Canvas.GetTop(point) - label.DesiredSize.Height,
                    0, Math.Max(0, averages.ActualHeight - label.DesiredSize.Height)));
                averages.Children.Add(label);
                var legendItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 10, 2),
                    ToolTip = $"{video.Name} / {result.Average:F4} / {rates[index]:N0} kbps" };
                legendItem.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = color, Margin = new Thickness(0, 0, 5, 0) });
                legendItem.Children.Add(new TextBlock { Text = video.Name, FontSize = UiTheme.Small,
                    Foreground = UiTheme.OnLight, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis });
                legendItem.Children.Add(new TextBlock { Text = $"  {result.Average:F4} / {rates[index]:N0} kbps",
                    FontSize = UiTheme.Small, Foreground = UiTheme.OnLight });
                legendItem.Background = Brushes.Transparent;
                legendItem.Cursor = System.Windows.Input.Cursors.Hand;
                legendItem.MouseLeftButtonDown += (_, e) => { SelectVideo(video.Id); e.Handled = true; };
                legend.Children.Add(legendItem);
            }
            legendController.Place();
            AttachPlaybackVisuals();
            UpdatePlayhead();
        }
        redrawPlaybackGraph = Draw;
        updateData = (selectedVideos, registeredVideos, groupNames) =>
        {
            videos = selectedVideos;
            allVideos = registeredVideos;
            groups = groupNames;
            selectedIds.Clear();
            selectedIds.UnionWith(videos.Select(video => video.Id));
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
            if (!verify) return;
            metric.SelectedIndex = 1;
            Draw();
            try
            {
                UpdateLayout();
                if (metric.Items.Count != 3 || grid.ColumnDefinitions.Count != 3 ||
                    !timeline.Children.OfType<Polyline>().Any(line => line.Tag is null) || !averages.Children.OfType<Ellipse>().Any())
                    throw new InvalidOperationException("グラフのタブまたは横並び表示が不正です。");
                foreach (var canvas in new[] { timeline, averages })
                {
                    if (canvas.Children.OfType<Line>().Count(line => Equals(line.Tag, "グリッド")) != 12 ||
                        canvas.Children.OfType<TextBlock>().Count(label => Equals(label.Tag, "目盛り")) != 12)
                        throw new InvalidOperationException("軸の目盛りまたはグリッドが不足しています。");
                    if (!canvas.Children.OfType<TextBlock>().Any(label => Equals(label.Tag, "縦軸名") &&
                            label.Text == "SSIM" && label.RenderTransform is RotateTransform { Angle: -90 }) ||
                        !canvas.Children.OfType<TextBlock>().Any(label => Equals(label.Tag, "品質方向") && label.Text == "↑ 高画質"))
                        throw new InvalidOperationException("縦軸名または品質方向の案内がありません。");
                }
                // 外れたフレーム値に平均値の軸が引っ張られず、単一値でも描画できる。
                var frameRange = QualityGraphScale.VerticalRange([0.1, 0.99]);
                var averageRange = QualityGraphScale.VerticalRange([0.94, 0.95]);
                var singleRange = QualityGraphScale.VerticalRange([0.95]);
                if (averageRange.Maximum - averageRange.Minimum >= frameRange.Maximum - frameRange.Minimum ||
                    singleRange.Minimum >= 0.95 || singleRange.Maximum <= 0.95 ||
                    QualityGraphScale.VerticalRange([]) != (0d, 1d))
                    throw new InvalidOperationException("グラフ別の縦軸範囲または単一値・空データの範囲が不正です。");
                if (averages.Children.OfType<TextBlock>().Count(label => Equals(label.Tag, "点の評価値")) != FilePointCount ||
                    legend.Children.Count != FilePointCount)
                    throw new InvalidOperationException("点の評価値・ビットレート表示または凡例が不足しています。");
                // 凡例の現在位置に点を追加し、内容更新時に別の端へ退避することを確認する。
                legendController.Place();
                var originalLegend = new Point(Canvas.GetLeft(legendBox), Canvas.GetTop(legendBox));
                var obstacle = new Ellipse { Width = 12, Height = 12 };
                Canvas.SetLeft(obstacle, originalLegend.X + legendBox.DesiredSize.Width / 2);
                Canvas.SetTop(obstacle, originalLegend.Y + legendBox.DesiredSize.Height / 2);
                averages.Children.Add(obstacle);
                legendController.Place();
                if (new Point(Canvas.GetLeft(legendBox), Canvas.GetTop(legendBox)) == originalLegend)
                    throw new InvalidOperationException("凡例が追加された点を避けて移動しません。");
                averages.Children.Remove(obstacle);
                legendController.Place();
                if (legend.Children.OfType<StackPanel>().Any(item =>
                    !item.Children.OfType<TextBlock>().Any(text => text.Text.Contains("kbps"))))
                    throw new InvalidOperationException("凡例に評価値とビットレートがありません。");
                collapseLegend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (legendScroll.Visibility != Visibility.Collapsed) throw new InvalidOperationException("凡例を折りたためません。");
                collapseLegend.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                legendDrag.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-20, 10)
                    { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                if (legendScroll.Visibility != Visibility.Visible || Canvas.GetLeft(legendBox) < 0 || Canvas.GetTop(legendBox) < 0)
                    throw new InvalidOperationException("凡例の復元または移動が不正です。");
                // 別の動画が表示対象だった状態からファイル点を選択して切り替える。
                var target = averages.Children.OfType<Ellipse>().First();
                selectedIds.Clear();
                selectedIds.Add("別の動画");
                target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
                if (timeline.Children.OfType<Polyline>().Count(line => line.Tag is null) != 1 || selectedIds.Single() != (string)target.Tag)
                    throw new InvalidOperationException("ファイル選択がフレームグラフに反映されません。");
                metric.SelectedIndex = 2;
                UpdateLayout();
                if (!timeline.Children.OfType<Polyline>().Any(line => line.Tag is null)) throw new InvalidOperationException("PSNRタブへ切り替わりません。");
                var playbackVideo = (allVideos ?? videos).First(video => results.ContainsKey((video.Id, QualityMetric.Psnr)));
                SetPlaybackSources(playbackVideo with { Id = "検証用元動画", Path = "元動画.mkv" }, playbackVideo, "検証用元動画");
                SetPlaybackPosition(TimeSpan.FromSeconds(results[(playbackVideo.Id, QualityMetric.Psnr)].Frames[0].Number / playbackVideo.AverageFrameRate));
                if (!leftPlaybackStatus.Text.StartsWith("--\n") || rightPlaybackStatus.Text.StartsWith("--\n") ||
                    !rightPlaybackStatus.Text.Contains(playbackVideo.Name) || !playbackTimeLabel.Text.EndsWith("秒"))
                    throw new InvalidOperationException("左右の再生ステータスまたは元動画の評価値表示が不正です。");
                if (!Equals(rightPlaybackStatus.Foreground, videoColors[playbackVideo.Id]) ||
                    !Equals(leftPlaybackStatus.Foreground, UiTheme.OnLight) || !Equals(playbackTimeLabel.Foreground, UiTheme.OnLight))
                    throw new InvalidOperationException("再生ステータスの系列色または秒数の標準色が不正です。");
                UpdateLayout();
                var legendTarget = legend.Children.OfType<StackPanel>().First();
                legendTarget.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
                if (selectedIds.Count != 1) throw new InvalidOperationException("凡例から動画を選択できません。");
                ClearFileSelection();
                if (clearSelection is null && selectedIds.Count != 0) throw new InvalidOperationException("グラフの選択を解除できません。");
                foreach (var toggle in new[] { minimizeFrame, minimizeFile })
                {
                    var other = toggle == minimizeFrame ? minimizeFile : minimizeFrame;
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (toggle.Visibility != Visibility.Collapsed || other.Visibility != Visibility.Visible)
                        throw new InvalidOperationException("最小化したグラフのボタン表示が不正です。");
                    other.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (minimizedColumn != -1 || toggle.Visibility != Visibility.Visible || other.Visibility != Visibility.Visible ||
                        grid.ColumnDefinitions[0].Width != savedLeft || grid.ColumnDefinitions[2].Width != savedRight)
                        throw new InvalidOperationException("反対側のボタンでグラフのサイズを復元できません。");
                }
                UpdateLayout();
                foreach (var ranges in new EvaluationFrameRange[][]
                {
                    [new(3, 9)],
                    [new(0, 3), new(15, 18)],
                    [new(0, 3), new(27, 30)]
                })
                {
                    foreach (var processed in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.15) })
                    {
                        var sourceTime = PartialEvaluationProgress.ToSourceTime(ranges, 30, processed);
                        SetEvaluationProgress(playbackVideo, QualityMetric.Ssim, sourceTime);
                        SetPlaybackPosition(TimeSpan.Zero);
                        if (Math.Abs(PlayheadPosition.TotalSeconds - sourceTime.TotalSeconds) > 0.000001)
                            throw new InvalidOperationException("部分評価の処理位置が元動画の時刻を示していません。");
                    }
                }
                SetEvaluationProgress(playbackVideo, QualityMetric.Ssim, TimeSpan.FromSeconds(playbackVideo.Duration.TotalSeconds / 2));
                var progressPosition = PlayheadPosition;
                SetPlaybackPosition(TimeSpan.Zero);
                if (metric.SelectedIndex != (int)QualityMetric.Ssim || PlayheadPosition != progressPosition ||
                    Math.Abs(progressPosition.TotalSeconds - playbackVideo.Duration.TotalSeconds / 2) > 0.001)
                    throw new InvalidOperationException("計算中のタブまたは進捗位置が不正です。");
                EndEvaluationProgress(TimeSpan.Zero);
                SelectMetric(QualityMetric.Psnr);
                UpdateLayout();
                var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                snapshot.Render((Visual)Content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(snapshot));
                var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory,
                    "../../../../../measurement-results/quality-graph-layout.png"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
                using (var stream = System.IO.File.Create(output)) encoder.Save(stream);
                Console.WriteLine("グラフ: 目盛り・グリッド・指標タブ・横並び・ファイル選択連動が合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
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
