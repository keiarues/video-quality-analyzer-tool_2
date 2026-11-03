using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class QualityGraphPanel : UserControl
{
    private readonly Canvas timeline = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly Canvas averages = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly TabControl metric = new();
    private readonly TextBlock status = new() { FontSize = UiTheme.Small, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private double maximumTime;
    private TimeSpan playbackPosition;
    private bool playheadDragging;
    private readonly Line playhead = new() { Stroke = UiTheme.OnLight, StrokeThickness = 2,
        Tag = "再生位置", IsHitTestVisible = false };
    private readonly Action<TimeSpan>? seekRequested;
    public event Action<TimeSpan>? SeekPreviewed;
    private readonly Brush[] colors = [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod];

    public QualityGraphPanel(VideoDescriptor[] videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results,
        Action<TimeSpan>? seek = null, Action<string>? select = null, bool verify = false,
        VideoDescriptor[]? allVideos = null, IReadOnlyDictionary<string, string>? groups = null)
    {
        seekRequested = seek;
        Style = (Style)Application.Current.FindResource(typeof(UserControl));
        Background = Brushes.White;
        Foreground = UiTheme.OnLight;
        var panel = new DockPanel { Margin = new Thickness(4) };
        Content = panel;
        foreach (var value in Enum.GetValues<QualityMetric>())
            metric.Items.Add(new TabItem { Header = value.ToString().ToUpperInvariant(), Tag = value });
        metric.SelectedIndex = 0;
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        panel.Children.Add(metric);
        var selectedIds = videos.Select(video => video.Id).ToHashSet();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var leftGraph = MakeGraph("フレームグラフ：時刻（秒）と品質", timeline);
        var rightGraph = MakeGraph("ファイルグラフ：平均ビットレート（kbps）と平均品質", averages);
        Grid.SetColumn(rightGraph, 1);
        grid.Children.Add(leftGraph);
        grid.Children.Add(rightGraph);
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
            timeline.Children.Clear();
            averages.Children.Clear();
            if (metric.SelectedItem is not TabItem { Tag: QualityMetric selected }) return;
            var available = (allVideos ?? videos).Where(video => results.ContainsKey((video.Id, selected))).ToArray();
            if (available.Length == 0) { maximumTime = 0; status.Text = "この指標の完了結果はありません。"; return; }
            var values = available.SelectMany(video => results[(video.Id, selected)].Frames.Select(frame => frame.Value)).ToArray();
            var min = values.Min();
            var max = values.Max();
            if (max - min < 0.000001) { min -= 0.5; max += 0.5; }
            maximumTime = available.Max(video => results[(video.Id, selected)].Frames.Max(frame => frame.Number) / video.AverageFrameRate);
            var rates = available.Select(Bitrate).ToArray();
            var maxRate = Math.Max(1, rates.Max());
            var timeArea = PlotArea(timeline);
            var fileArea = PlotArea(averages);
            DrawAxes(timeline, timeArea, maximumTime, min, max, "秒");
            DrawAxes(averages, fileArea, maxRate, min, max, "kbps");
            var previousPoints = new Dictionary<string, Point>();
            for (var index = 0; index < available.Length; index++)
            {
                var video = available[index];
                var result = results[(video.Id, selected)];
                var color = colors[index % colors.Length];
                var line = new Polyline { Stroke = color, StrokeThickness = 1, ToolTip = video.Name };
                foreach (var frame in result.Frames)
                    line.Points.Add(new Point(timeArea.Left + frame.Number / video.AverageFrameRate / Math.Max(0.001, maximumTime) * timeArea.Width,
                        timeArea.Top + (max - frame.Value) / (max - min) * timeArea.Height));
                var isSelected = selectedIds.Contains(video.Id);
                if (isSelected) { timeline.Children.Add(line); displayedVideoIds.Add(video.Id); }
                var point = new Ellipse { Width = 12, Height = 12, Fill = color,
                    Stroke = isSelected ? Brushes.Black : null, StrokeThickness = 2,
                    Tag = video.Id, ToolTip = $"{video.Name}: {result.Average:F4} / {rates[index]:N0} kbps", Cursor = System.Windows.Input.Cursors.Hand };
                Canvas.SetLeft(point, fileArea.Left + rates[index] / maxRate * fileArea.Width - 6);
                Canvas.SetTop(point, fileArea.Top + (max - result.Average) / (max - min) * fileArea.Height - 6);
                point.MouseLeftButtonDown += (_, e) => { SelectVideo(video.Id); status.Text = video.Name; e.Handled = true; };
                if (groups is not null && groups.TryGetValue(video.Id, out var group))
                {
                    var center = new Point(Canvas.GetLeft(point) + 6, Canvas.GetTop(point) + 6);
                    if (previousPoints.TryGetValue(group, out var previous))
                        averages.Children.Add(new Line { X1 = previous.X, Y1 = previous.Y,
                            X2 = center.X, Y2 = center.Y, Stroke = Brushes.Gray, StrokeThickness = 1, IsHitTestVisible = false });
                    previousPoints[group] = center;
                }
                averages.Children.Add(point);
            }
            timeline.Children.Add(playhead);
            UpdatePlayhead();
            status.Text = "左の縦線をドラッグするとシーク、右の点をクリックするとその動画のフレーム評価を表示します。点にマウスを合わせると動画名と平均値を表示します。";
        }
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
                if (metric.Items.Count != 3 || grid.ColumnDefinitions.Count != 2 ||
                    !timeline.Children.OfType<Polyline>().Any(line => line.Tag is null) || !averages.Children.OfType<Ellipse>().Any())
                    throw new InvalidOperationException("グラフのタブまたは横並び表示が不正です。");
                foreach (var canvas in new[] { timeline, averages })
                    if (canvas.Children.OfType<Line>().Count(line => Equals(line.Tag, "グリッド")) != 12 ||
                        canvas.Children.OfType<TextBlock>().Count(label => Equals(label.Tag, "目盛り")) != 12)
                        throw new InvalidOperationException("軸の目盛りまたはグリッドが不足しています。");
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

    private readonly HashSet<string> displayedVideoIds = [];
    private readonly Action<VideoDescriptor[], VideoDescriptor[], IReadOnlyDictionary<string, string>?> updateData;
    private readonly Action<double> seekToFraction;
    public IReadOnlyCollection<string> DisplayedVideoIds => displayedVideoIds;
    public int FilePointCount => averages.Children.OfType<Ellipse>().Count();
    public void UpdateData(VideoDescriptor[] selected, VideoDescriptor[] all, IReadOnlyDictionary<string, string>? groups) => updateData(selected, all, groups);
    public void SelectMetric(QualityMetric value) => metric.SelectedIndex = (int)value;
    public void SeekToFraction(double fraction) => seekToFraction(fraction);
    public void SelectFile(string id)
    {
        var point = averages.Children.OfType<Ellipse>().Single(item => Equals(item.Tag, id));
        point.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
    }

    public TimeSpan PlayheadPosition => playbackPosition;
    public double PlayheadX => playhead.X1;
    public void SetPlaybackPosition(TimeSpan position)
    {
        if (playheadDragging) return;
        playbackPosition = position;
        UpdatePlayhead();
    }

    public void BeginPlayheadDrag(double fraction)
    {
        if (maximumTime <= 0) return;
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

    private void UpdatePlayhead()
    {
        var area = PlotArea(timeline);
        playhead.Visibility = maximumTime > 0 ? Visibility.Visible : Visibility.Collapsed;
        playhead.X1 = playhead.X2 = area.Left + Math.Clamp(playbackPosition.TotalSeconds / Math.Max(0.001, maximumTime), 0, 1) * area.Width;
        playhead.Y1 = area.Top;
        playhead.Y2 = area.Bottom;
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

    private static double Bitrate(VideoDescriptor video) => video.AverageBitrate is > 0 ? video.AverageBitrate.Value / 1_000d : System.IO.File.Exists(video.Path) && video.Duration.TotalSeconds > 0
        ? new System.IO.FileInfo(video.Path).Length * 8d / video.Duration.TotalSeconds / 1_000_000 : 0;

    // 軸・目盛りと描画データは同じ領域を基準に配置する。
    private static Rect PlotArea(Canvas canvas) => new(64, 18,
        Math.Max(1, canvas.ActualWidth - 90), Math.Max(1, canvas.ActualHeight - 72));

    private static void DrawAxes(Canvas canvas, Rect area, double maximumX, double minimumY, double maximumY, string unit)
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
            AddTick(canvas, (maximumX * fraction).ToString(unit == "kbps" ? "N0" : "G4"), x, area.Bottom + 6, true);
            AddTick(canvas, (minimumY + (maximumY - minimumY) * fraction).ToString("G4"), area.Left - 7, y, false);
        }
        canvas.Children.Add(new Polyline { Stroke = Brushes.DimGray, StrokeThickness = 1,
            IsHitTestVisible = false, Tag = "軸", Points = new PointCollection
                { new(area.Left, area.Top), new(area.Left, area.Bottom), new(area.Right, area.Bottom) } });
        var caption = new TextBlock { Text = unit, Foreground = UiTheme.OnLight, IsHitTestVisible = false };
        caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(caption, area.Left + (area.Width - caption.DesiredSize.Width) / 2);
        Canvas.SetTop(caption, area.Bottom + 28);
        canvas.Children.Add(caption);
    }

    private static void AddTick(Canvas canvas, string text, double x, double y, bool horizontal)
    {
        var label = new TextBlock { Text = text, Foreground = UiTheme.OnLight, FontSize = UiTheme.Small,
            Tag = "目盛り", IsHitTestVisible = false };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, horizontal ? x - label.DesiredSize.Width / 2 : x - label.DesiredSize.Width);
        Canvas.SetTop(label, horizontal ? y : y - label.DesiredSize.Height / 2);
        canvas.Children.Add(label);
    }
}
