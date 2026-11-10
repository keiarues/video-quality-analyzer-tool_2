using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.App;

// 通常画面と同じプレビュー・折れ線部品を使い、評価計算と再生測定を分離する。
internal sealed class GraphPlaybackTestWindow : Window
{
    private readonly SsimTestCache cache;
    private readonly string cachePath;
    private readonly Image image = new() { Stretch = System.Windows.Media.Stretch.Uniform };
    private readonly ContentControl graphHost = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox count = new() { Width = 70 };
    private readonly CheckBox dynamicGraph = new() { Content = "再生位置・評価値を更新", IsChecked = true };
    private readonly CheckBox separateLayer = new() { Content = "再生位置を別レイヤー", IsChecked = true };
    private readonly CheckBox staticCache = new() { Content = "静的グラフを保持", IsChecked = true };
    private readonly Av1PreviewPlayer player;
    private QualityGraphPanel? graph;
    private bool running;
    private readonly List<(double Pts, double Wall)> frames = [];
    private readonly Stopwatch clock = new();
    private double graphMilliseconds;
    private Exception? playbackError;
    private readonly CancellationTokenSource lifetime = new();

    public GraphPlaybackTestWindow(SsimTestCache cache, string cachePath, bool automatic, bool verifyOperations = false)
    {
        this.cache = cache;
        this.cachePath = cachePath;
        Title = "SSIMグラフ・プレビュー負荷検証";
        Width = 1200; Height = 850;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        var root = new DockPanel { Margin = new Thickness(8) };
        Content = root;
        var toolbar = new WrapPanel();
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock { Text = "グラフ本数：", VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i <= cache.Entries.Length; i++) count.Items.Add(i);
        count.SelectedIndex = 1;
        toolbar.Children.Add(count); toolbar.Children.Add(dynamicGraph);
        toolbar.Children.Add(separateLayer);
        toolbar.Children.Add(staticCache);
        var run = new Button { Content = "全条件を測定（各35秒）" };
        toolbar.Children.Add(run);
        var play = new Button { Content = "再生" }; toolbar.Children.Add(play);
        var pause = new Button { Content = "一時停止" }; toolbar.Children.Add(pause);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new Grid(); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
        grid.Children.Add(image); Grid.SetRow(graphHost, 1); grid.Children.Add(graphHost); root.Children.Add(grid);
        player = new Av1PreviewPlayer(image);
        player.Failed = error => { playbackError = error; status.Text = error.Message; };
        player.FrameShown = position =>
        {
            if (running) frames.Add((position.TotalSeconds, clock.Elapsed.TotalMilliseconds));
            if (dynamicGraph.IsChecked != true) return;
            var start = Stopwatch.GetTimestamp();
            graph?.SetPlaybackPosition(position);
            if (running) graphMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        };
        count.SelectionChanged += (_, _) => BuildGraph();
        separateLayer.Checked += (_, _) => BuildGraph();
        separateLayer.Unchecked += (_, _) => BuildGraph();
        staticCache.Checked += (_, _) => BuildGraph();
        staticCache.Unchecked += (_, _) => BuildGraph();
        play.Click += (_, _) => { if (!running) player.Open(cache.Reference, TimeSpan.Zero, true); };
        pause.Click += (_, _) => { if (!running) player.Pause(); };
        run.Click += async (_, _) => await RunAsync(false);
        Loaded += async (_, _) =>
        {
            BuildGraph();
            status.Text = $"保存済みSSIM {cache.Entries.Length}本を読み込み済み。評価計算なし。保存先：{cachePath}";
            if (verifyOperations)
            {
                try
                {
                    Activate();
                    count.SelectedIndex = cache.Entries.Length;
                    UpdateLayout();
                    graph!.VerifyCachedGraphPixels();
                    Width = 980; Height = 720;
                    await Task.Delay(200);
                    graph.VerifyCachedGraphPixels();
                    Width = 1200; Height = 850;
                    await Task.Delay(200);
                    graph.VerifyCachedGraphPixels();
                    graph.SelectFile(cache.Entries.Last().Video.Id);
                    if (!graph.DisplayedVideoIds.SequenceEqual(new[] { cache.Entries.Last().Video.Id }))
                        throw new InvalidOperationException("ファイル選択後の表示系列が不正です。");
                    graph.VerifyCachedGraphPixels();
                    graph.SelectMetric(QualityMetric.Psnr);
                    if (graph.DisplayedVideoIds.Count != 0) throw new InvalidOperationException("未評価の指標に古い系列が残っています。");
                    graph.VerifyCachedGraphPixels();
                    graph.SelectMetric(QualityMetric.Ssim);
                    graph.VerifyCachedGraphPixels();
                    player.Open(cache.Reference, TimeSpan.Zero, true);
                    await Task.Delay(600);
                    player.Pause();
                    graph.BeginPlayheadDrag(.5); graph.EndPlayheadDrag();
                    await Task.Delay(300);
                    graph.VerifyCachedGraphPixels();
                    graph.UpdateData([], [], null);
                    if (graph.DisplayedVideoIds.Count != 0 || graph.FilePointCount != 0)
                        throw new InvalidOperationException("データ消去後のグラフが残っています。");
                    graph.VerifyCachedGraphPixels();
                    await File.WriteAllTextAsync(cachePath + ".operations.txt", "合格：拡大縮小・選択・指標切替・再生・停止中シーク・消去。各状態でキャッシュと再描画の画素平均差3以下。実画面提示は未測定。");
                    Application.Current.Shutdown(0);
                }
                catch (Exception error)
                {
                    await File.WriteAllTextAsync(cachePath + ".operations.txt", error.ToString());
                    Application.Current.Shutdown(1);
                }
                return;
            }
            if (automatic)
            {
                Activate();
                await Task.Delay(2000, lifetime.Token);
                await RunAsync(true);
            }
        };
        Closed += (_, _) => { lifetime.Cancel(); player.Dispose(); };
    }

    private void BuildGraph()
    {
        var results = cache.Entries.ToDictionary(e => (e.Video.Id, QualityMetric.Ssim), e => e.Result);
        graph = new QualityGraphPanel(cache.Entries.Take(count.SelectedIndex).Select(e => e.Video).ToArray(),
            results, seek: position => { if (!running) player.Seek(position); }, frameOnly: true,
            separatePlaybackLayer: separateLayer.IsChecked == true, cacheStaticGraph: staticCache.IsChecked == true);
        graphHost.Content = graph;
        graph.SetPlaybackSources(cache.Reference, cache.Entries[0].Video, cache.Reference.Id);
    }

    private async Task RunAsync(bool close)
    {
        if (running) return;
        running = true;
        var rows = new List<object>();
        try
        {
            if (cache.Reference.Duration.TotalSeconds < 35) throw new InvalidOperationException("35秒以上の動画が必要です。");
            count.IsEnabled = dynamicGraph.IsEnabled = separateLayer.IsEnabled = staticCache.IsEnabled = false;
            foreach (var update in new[] { false, true })
            foreach (var n in new[] { 0, 1, 2, cache.Entries.Length }.Distinct().Where(n => n <= cache.Entries.Length))
            {
                player.Close(); count.SelectedIndex = n; dynamicGraph.IsChecked = update; BuildGraph();
                await Task.Delay(500, lifetime.Token);
                frames.Clear(); graphMilliseconds = 0; playbackError = null;
                var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
                clock.Restart(); player.Open(cache.Reference, TimeSpan.Zero, true);
                status.Text = $"測定中：{n}本 / 再生位置更新 {update}（SSIM計算なし）";
                await Task.Delay(TimeSpan.FromSeconds(35), lifetime.Token);
                player.Close(); clock.Stop(); process.Refresh();
                if (playbackError is not null) throw playbackError;
                var measured = frames.Where(f => f.Pts >= 2 && f.Pts < 32).ToArray();
                var unique = measured.Select(f => (long)Math.Round(f.Pts * cache.Reference.AverageFrameRate)).Distinct().Count();
                var expected = (int)(Math.Ceiling(32 * cache.Reference.AverageFrameRate - .000001) -
                    Math.Ceiling(2 * cache.Reference.AverageFrameRate - .000001));
                var intervals = measured.Zip(measured.Skip(1), (a, b) => b.Wall - a.Wall).Order().ToArray();
                rows.Add(new { SeparatePlaybackLayer = separateLayer.IsChecked == true,
                    StaticGraphCache = separateLayer.IsChecked == true && staticCache.IsChecked == true,
                    GraphCount = n, UpdatePlayhead = update, Expected = expected, Reflected = unique,
                    MissingPercent = 100.0 * Math.Max(0, expected - unique) / expected,
                    MaxIntervalMs = intervals.LastOrDefault(), P95IntervalMs = intervals.Length == 0 ? 0 : intervals[(int)((intervals.Length - 1) * .95)],
                    GraphUpdateTotalMs = graphMilliseconds, CpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / clock.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                    MemoryMiB = process.WorkingSet64 / 1048576.0 });
            }
            var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cachePath))!, $"playback-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Note = "WPF画像反映の観測。実画面提示fps・GPU負荷は未測定。SSIM評価呼び出し0。各条件1回。", Rows = rows }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = $"測定完了：{output}";
            if (close) Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            status.Text = $"測定失敗：{error.Message}";
            if (close) Application.Current.Shutdown(1);
        }
        finally { running = false; count.IsEnabled = dynamicGraph.IsEnabled = separateLayer.IsEnabled = staticCache.IsEnabled = true; }
    }
}
