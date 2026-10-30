using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class MainWindow : Window
{
    private readonly string path;
    private readonly string[] rightPaths;
    private readonly bool direct;
    private readonly bool statistics;
    private readonly bool verifyFrames;
    private readonly CancellationTokenSource cancellation = new();
    private GpuVideoHost? host;
    private GpuVideoHost? rightHost;
    private DualGpuPlayback? dualPlayback;
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(string path, string[] rightPaths, bool direct, bool statistics, bool verifyFrames)
    {
        this.path = path;
        this.rightPaths = rightPaths;
        this.direct = direct;
        this.statistics = statistics;
        this.verifyFrames = verifyFrames;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        host = new GpuVideoHost(1440, 1080, statistics) { Width = 720, Height = 540,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        HostContainer.Children.Add(host);
        if (rightPaths.Length > 0)
        {
            rightHost = new GpuVideoHost(1440, 1080, statistics) { Width = 720, Height = 540,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RightHostContainer.Children.Add(rightHost);
            if (!direct)
            {
                StatusText.Text = "2本のGPU同期表示には --direct を指定してください。";
                return;
            }
            foreach (var candidate in rightPaths)
                RightSelect.Items.Add(new ComboBoxItem { Content = Path.GetFileName(candidate), Tag = candidate });
            RightSelect.SelectedIndex = 0;
            dualPlayback = new DualGpuPlayback(host, rightHost);
            statusTimer.Tick += (_, _) =>
            {
                dualPlayback?.SaveWindowMetricsIfReady();
                StatusText.Text = dualPlayback?.Status ?? "終了";
                PauseButton.Content = dualPlayback?.IsPaused == true ? "再生" : "一時停止";
            };
            statusTimer.Start();
            dualPlayback.Start(path, rightPaths[0]);
            if (verifyFrames) _ = VerifyFrameOperationsAsync();
            return;
        }
        ControlBar.Visibility = Visibility.Collapsed;
        RightBorder.Visibility = Visibility.Collapsed;
        _ = DecodeAsync(cancellation.Token);
    }

    private void RightSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RightSelect.SelectedItem is ComboBoxItem { Tag: string selected })
            dualPlayback?.SwitchRight(selected);
    }

    private void BackFive_Click(object sender, RoutedEventArgs e) =>
        dualPlayback?.Seek((dualPlayback.Position - TimeSpan.FromSeconds(5)));

    private void ForwardFive_Click(object sender, RoutedEventArgs e) =>
        dualPlayback?.Seek((dualPlayback.Position + TimeSpan.FromSeconds(5)));

    private void Start_Click(object sender, RoutedEventArgs e) => dualPlayback?.Seek(TimeSpan.Zero);

    private void Pause_Click(object sender, RoutedEventArgs e) => dualPlayback?.TogglePause();

    private void Step_Click(object sender, RoutedEventArgs e) => dualPlayback?.Step();

    private void Stop_Click(object sender, RoutedEventArgs e) => dualPlayback?.Stop();

    private async Task VerifyFrameOperationsAsync()
    {
        if (dualPlayback is null) return;
        var observations = new List<object>();
        try
        {
            await Task.Delay(2000, cancellation.Token);
            dualPlayback.TogglePause();
            observations.Add(await CaptureWhenReadyAsync("一時停止"));
            for (var index = 1; index <= 3; index++)
            {
                dualPlayback.Step();
                observations.Add(await CaptureWhenReadyAsync($"{index}コマ進む"));
            }
            dualPlayback.Stop();
            observations.Add(await CaptureWhenReadyAsync("停止"));
            dualPlayback.Seek(TimeSpan.FromSeconds(5));
            observations.Add(await CaptureWhenReadyAsync("停止中に5秒へシーク"));
            dualPlayback.Seek(TimeSpan.FromSeconds(15));
            observations.Add(await CaptureWhenReadyAsync("停止中に15秒へシーク"));
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../measurement-results"));
            Directory.CreateDirectory(directory);
            var report = new { LeftVideo = Path.GetFileName(path), RightVideo = Path.GetFileName(rightPaths[0]),
                RecordedAt = DateTimeOffset.Now, Observations = observations };
            var filename = $"frame-operations-{Guid.NewGuid():N}.json";
            File.WriteAllText(Path.Combine(directory, filename),
                System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"操作検証を保存: {filename}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"操作検証に失敗: 0x{exception.HResult:X8} {exception.GetType().Name}";
        }
        finally { Close(); }
    }

    private async Task<object> CaptureWhenReadyAsync(string operation)
    {
        if (dualPlayback is null) throw new InvalidOperationException("再生が準備されていません。");
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var frame = dualPlayback.CaptureFramePair();
            if (frame.LeftReady && frame.RightReady &&
                frame.LeftPresentedPtsTicks == frame.LeftPtsTicks &&
                frame.RightPresentedPtsTicks == frame.RightPtsTicks)
                return new { Operation = operation, frame.LeftPtsTicks, frame.RightPtsTicks,
                    frame.LeftPresentedPtsTicks, frame.RightPresentedPtsTicks,
                    frame.LeftFirstPtsTicks, frame.RightFirstPtsTicks,
                    DifferenceMilliseconds = Math.Abs(frame.LeftPtsTicks - frame.RightPtsTicks) / 10000.0 };
            await Task.Delay(50, cancellation.Token);
        }
        throw new TimeoutException($"{operation}後の左右フレームがそろいません。");
    }

    private async Task DecodeAsync(CancellationToken token)
    {
        try
        {
            await Task.Run(async () =>
            {
                if (direct) await DecodeDirectAsync(token);
                else await DecodeRgbAsync(token);
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                StatusText.Text = $"描画に失敗しました: 0x{exception.HResult:X8} {exception.GetType().Name}";
        }
    }

    private async Task DecodeDirectAsync(CancellationToken token)
    {
                using var reader = new GpuSurfaceReader(path, host!.Device);
                var clock = Stopwatch.StartNew();
                var count = 0;
                while (!token.IsCancellationRequested && count < 300)
                {
                    using var frame = reader.ReadNext();
                    if (frame is null) break;
                    var wait = frame.Position - clock.Elapsed;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
                    if (clock.Elapsed - frame.Position > TimeSpan.FromMilliseconds(150)) continue;
                    await Dispatcher.InvokeAsync(() => host?.Render(frame));
                    count++;
                    if (count % 30 == 0)
                    {
                        var displayed = count;
                        await Dispatcher.InvokeAsync(() => StatusText.Text =
                            $"DXGI直接描画: {displayed}フレーム / PTS {frame.Position.TotalSeconds:F2}秒（実提示fpsは未測定）");
                    }
                }
                await Dispatcher.InvokeAsync(() => StatusText.Text += " / 読み取り終了");
    }

    private async Task DecodeRgbAsync(CancellationToken token)
    {
        using var reader = new MediaFoundationRgbReader(path);
        var clock = Stopwatch.StartNew();
        var count = 0;
        while (!token.IsCancellationRequested && count < 300)
        {
            var frame = reader.ReadNext(1440 * 1080 * 4);
            if (frame is null) break;
            var wait = frame.Position - clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
            if (clock.Elapsed - frame.Position > TimeSpan.FromMilliseconds(150)) continue;
            await Dispatcher.InvokeAsync(() => host?.Render(frame.Pixels));
            count++;
            if (count % 30 == 0)
            {
                var displayed = count;
                await Dispatcher.InvokeAsync(() => StatusText.Text =
                    $"GPU描画へ送信: {displayed}フレーム / PTS {frame.Position.TotalSeconds:F2}秒（実提示fpsは未測定）");
            }
        }
        await Dispatcher.InvokeAsync(() => StatusText.Text += " / 読み取り終了");
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        statusTimer.Stop();
        dualPlayback?.Dispose();
        cancellation.Cancel();
        cancellation.Dispose();
        host?.Dispose();
        rightHost?.Dispose();
    }
}
