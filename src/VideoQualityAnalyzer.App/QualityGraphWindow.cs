using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VideoQualityAnalyzer.Core;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

public sealed class QualityGraphWindow : Window
{
    private readonly Canvas timeline = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly Canvas averages = new() { Background = Brushes.WhiteSmoke, ClipToBounds = true };
    private readonly TabControl metric = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private double maximumTime;
    private readonly Brush[] colors = [Brushes.RoyalBlue, Brushes.OrangeRed, Brushes.SeaGreen, Brushes.DarkViolet, Brushes.DarkGoldenrod];

    public QualityGraphWindow(VideoDescriptor[] videos,
        IDictionary<(string VideoId, QualityMetric Metric), QualityResult> results,
        Action<TimeSpan>? seek = null, Action<string>? select = null, bool verify = false,
        VideoDescriptor[]? allVideos = null, IReadOnlyDictionary<string, string>? groups = null)
    {
        Title = "品質グラフ";
        Width = 1000;
        Height = 650;
        var panel = new DockPanel { Margin = new Thickness(12) };
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
        var rightGraph = MakeGraph("ファイルグラフ：平均ビットレート（Mbps）と平均品質", averages);
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
            timeline.Children.Clear();
            averages.Children.Clear();
            if (metric.SelectedItem is not TabItem { Tag: QualityMetric selected }) return;
            var available = (allVideos ?? videos).Where(video => results.ContainsKey((video.Id, selected))).ToArray();
            if (available.Length == 0) { status.Text = "この指標の完了結果はありません。"; return; }
            var values = available.SelectMany(video => results[(video.Id, selected)].Frames.Select(frame => frame.Value)).ToArray();
            var min = values.Min();
            var max = values.Max();
            if (max - min < 0.000001) { min -= 0.5; max += 0.5; }
            maximumTime = available.Max(video => results[(video.Id, selected)].Frames.Max(frame => frame.Number) / video.AverageFrameRate);
            var rates = available.Select(Bitrate).ToArray();
            var maxRate = Math.Max(1, rates.Max());
            var previousPoints = new Dictionary<string, Point>();
            for (var index = 0; index < available.Length; index++)
            {
                var video = available[index];
                var result = results[(video.Id, selected)];
                var color = colors[index % colors.Length];
                var line = new Polyline { Stroke = color, StrokeThickness = 1, ToolTip = video.Name };
                foreach (var frame in result.Frames)
                    line.Points.Add(new Point(30 + frame.Number / video.AverageFrameRate / Math.Max(0.001, maximumTime) * Math.Max(1, timeline.ActualWidth - 45),
                        10 + (max - frame.Value) / (max - min) * Math.Max(1, timeline.ActualHeight - 30)));
                var isSelected = selectedIds.Contains(video.Id);
                if (isSelected) timeline.Children.Add(line);
                var point = new Ellipse { Width = 12, Height = 12, Fill = color,
                    Stroke = isSelected ? Brushes.Black : null, StrokeThickness = 2,
                    Tag = video.Id, ToolTip = $"{video.Name}: {result.Average:F4} / {rates[index]:F2} Mbps", Cursor = System.Windows.Input.Cursors.Hand };
                Canvas.SetLeft(point, 30 + rates[index] / maxRate * Math.Max(1, averages.ActualWidth - 50) - 6);
                Canvas.SetTop(point, 10 + (max - result.Average) / (max - min) * Math.Max(1, averages.ActualHeight - 30) - 6);
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
            AddLabel(timeline, $"{min:F3} ～ {max:F3} / 0 ～ {maximumTime:F2}秒");
            AddLabel(averages, $"{min:F3} ～ {max:F3} / 0 ～ {maxRate:F2} Mbps");
            status.Text = "左のグラフをクリックするとシーク、右の点をクリックするとその動画のフレーム評価を表示します。点にマウスを合わせると動画名と平均値を表示します。";
        }
        metric.SelectionChanged += (_, e) =>
        {
            if (e.Source != metric) return;
            foreach (TabItem tab in metric.Items) tab.Content = null;
            if (metric.SelectedItem is TabItem active) active.Content = grid;
            Draw();
        };
        SizeChanged += (_, _) => Draw();
        timeline.MouseLeftButtonDown += (_, e) =>
        {
            var fraction = Math.Clamp((e.GetPosition(timeline).X - 30) / Math.Max(1, timeline.ActualWidth - 45), 0, 1);
            seek?.Invoke(TimeSpan.FromSeconds(maximumTime * fraction));
        };
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
                    !timeline.Children.OfType<Polyline>().Any() || !averages.Children.OfType<Ellipse>().Any())
                    throw new InvalidOperationException("グラフのタブまたは横並び表示が不正です。");
                // 別の動画が表示対象だった状態からファイル点を選択して切り替える。
                var target = averages.Children.OfType<Ellipse>().First();
                selectedIds.Clear();
                selectedIds.Add("別の動画");
                target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = MouseLeftButtonDownEvent });
                if (timeline.Children.OfType<Polyline>().Count() != 1 || selectedIds.Single() != (string)target.Tag)
                    throw new InvalidOperationException("ファイル選択がフレームグラフに反映されません。");
                metric.SelectedIndex = 2;
                UpdateLayout();
                if (!timeline.Children.OfType<Polyline>().Any()) throw new InvalidOperationException("PSNRタブへ切り替わりません。");
                Console.WriteLine("グラフ: 指標タブ・横並び・ファイル選択連動が合格");
                Application.Current.Shutdown(0);
            }
            catch (Exception exception) { Console.Error.WriteLine(exception.Message); Application.Current.Shutdown(1); }
        };
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

    private static double Bitrate(VideoDescriptor video) => video.AverageBitrate is > 0 ? video.AverageBitrate.Value / 1_000_000d : System.IO.File.Exists(video.Path) && video.Duration.TotalSeconds > 0
        ? new System.IO.FileInfo(video.Path).Length * 8d / video.Duration.TotalSeconds / 1_000_000 : 0;

    private static void AddLabel(Canvas canvas, string text)
    {
        var label = new TextBlock { Text = text, Foreground = Brushes.Black, Background = Brushes.White };
        Canvas.SetLeft(label, 5);
        Canvas.SetTop(label, Math.Max(0, canvas.ActualHeight - 20));
        canvas.Children.Add(label);
    }
}
