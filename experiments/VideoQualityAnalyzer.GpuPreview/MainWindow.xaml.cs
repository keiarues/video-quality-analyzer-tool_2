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
    private readonly CancellationTokenSource cancellation = new();
    private GpuVideoHost? host;
    private GpuVideoHost? rightHost;
    private DualGpuPlayback? dualPlayback;
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(string path, string[] rightPaths, bool direct, bool statistics)
    {
        this.path = path;
        this.rightPaths = rightPaths;
        this.direct = direct;
        this.statistics = statistics;
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
            statusTimer.Tick += (_, _) => StatusText.Text = dualPlayback?.Status ?? "終了";
            statusTimer.Start();
            dualPlayback.Start(path, rightPaths[0]);
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
