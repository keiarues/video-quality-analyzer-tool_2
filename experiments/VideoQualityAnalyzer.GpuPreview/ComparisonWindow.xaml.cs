using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoQualityAnalyzer.Media;

namespace VideoQualityAnalyzer.GpuPreview;

public partial class ComparisonWindow : Window
{
    private readonly FrameComparisonService comparison;
    private readonly string ffmpegPath;
    private readonly string ffprobePath;
    private CancellationTokenSource? loadCancellation;
    private CancellationTokenSource? playbackCancellation;
    private ComparisonFrame? currentLeft;
    private ComparisonFrame? currentRight;
    private volatile bool playbackPaused;
    private volatile bool wipeMode;
    private volatile int heatmapGain = 16;
    private WriteableBitmap? playingLeft;
    private WriteableBitmap? playingRight;
    private WriteableBitmap? playingHeatmap;

    public ComparisonWindow(string leftPath, string rightPath, long frameNumber, bool showWipe = false)
    {
        InitializeComponent();
        ffmpegPath = FfmpegRgbReader.FindExecutable() ??
            throw new FileNotFoundException("FFmpegが見つかりません。");
        ffprobePath = FfprobeService.FindExecutable() ??
            throw new FileNotFoundException("FFprobeが見つかりません。");
        comparison = new FrameComparisonService(ffmpegPath, ffprobePath);
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var paths = Directory.EnumerateFiles(Path.Combine(workspace, "test-data"))
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".mkv" or ".mp4")
            .Append(leftPath).Append(rightPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in paths)
        {
            LeftSelect.Items.Add(new VideoChoice(Path.GetFileName(path), path));
            RightSelect.Items.Add(new VideoChoice(Path.GetFileName(path), path));
        }
        LeftSelect.SelectedItem = LeftSelect.Items.Cast<VideoChoice>()
            .First(choice => string.Equals(choice.Path, leftPath, StringComparison.OrdinalIgnoreCase));
        RightSelect.SelectedItem = RightSelect.Items.Cast<VideoChoice>()
            .First(choice => string.Equals(choice.Path, rightPath, StringComparison.OrdinalIgnoreCase));
        FrameNumberBox.Text = frameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GainSelect.SelectedIndex = 1;
        ViewSelect.SelectedIndex = showWipe ? 1 : 0;
        UpdateWipeBoundary();
        ResultText.Text = "左右に同じ元フレーム番号を指定して比較します。再生中も表示を切り替えられます。";
        Loaded += (_, _) => _ = LoadComparisonAsync();
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback(keepRequestedFrame: true);
        _ = LoadComparisonAsync();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (playbackCancellation is not null)
        {
            playbackPaused = !playbackPaused;
            PlayPauseButton.Content = playbackPaused ? "再生" : "一時停止";
            return;
        }
        if (LeftSelect.SelectedItem is not VideoChoice left ||
            RightSelect.SelectedItem is not VideoChoice right ||
            !long.TryParse(FrameNumberBox.Text, out var frameNumber) || frameNumber < 0)
        {
            ResultText.Text = "0以上の元フレーム番号と左右の動画を指定してください。";
            return;
        }
        loadCancellation?.Cancel();
        playbackPaused = false;
        PlayPauseButton.Content = "一時停止";
        var cancellation = new CancellationTokenSource();
        playbackCancellation = cancellation;
        _ = PlayAsync(left.Path, right.Path, frameNumber, cancellation);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback(bool keepRequestedFrame = false)
    {
        var wasPlaying = playbackCancellation is not null;
        playbackCancellation?.Cancel();
        playbackCancellation = null;
        playbackPaused = false;
        PlayPauseButton.Content = "再生";
        if (!keepRequestedFrame && currentLeft is not null)
            FrameNumberBox.Text = currentLeft.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (wasPlaying && currentLeft is not null)
            ResultText.Text = $"比較再生を停止しました。表示中の元フレーム #{currentLeft.FrameNumber}。";
    }

    private async Task PlayAsync(string leftPath, string rightPath, long firstFrameNumber,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        ResultText.Text = "連続復号を準備中…";
        try
        {
            await Task.Run(async () =>
            {
            await using var reader = await StreamingFramePairReader.OpenAsync(ffmpegPath, ffprobePath,
                leftPath, rightPath, firstFrameNumber, token);
            var frameRate = reader.Descriptor.AverageFrameRate;
            var clock = new Stopwatch();
            var displayed = 0;
            var skipped = 0;
            while (!token.IsCancellationRequested)
            {
                if (playbackPaused)
                {
                    clock.Stop();
                    await Task.Delay(20, token);
                    continue;
                }
                var pair = await reader.ReadNextAsync(token);
                if (pair is null) break;
                while (playbackPaused)
                {
                    clock.Stop();
                    await Task.Delay(20, token);
                }
                clock.Start();
                var (left, right) = pair.Value;
                var due = TimeSpan.FromSeconds((left.FrameNumber - firstFrameNumber) / frameRate);
                var wait = due - clock.Elapsed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token);
                if (clock.Elapsed - due > TimeSpan.FromMilliseconds(100))
                {
                    skipped++;
                    continue;
                }
                var difference = wipeMode ? null : FrameComparisonService.CreateHeatmap(left, right, heatmapGain);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    currentLeft = left;
                    currentRight = right;
                    UpdatePlayingBitmaps(left, right, difference);
                    displayed++;
                    if (displayed % 6 == 0)
                    {
                        FrameNumberBox.Text = left.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        ResultText.Text = $"再生比較 #{left.FrameNumber} / 描画要求 {displayed / Math.Max(0.001, clock.Elapsed.TotalSeconds):F1} fps" +
                            $" / 遅延破棄 {skipped}枚" +
                            (difference is null ? "" : $" / 平均RGB差 {difference.MeanAbsoluteRgbDifference:F2}") +
                            "。実画面提示fpsは未測定。";
                    }
                });
            }
            if (!token.IsCancellationRequested)
                await Dispatcher.InvokeAsync(() => ResultText.Text =
                    $"再生比較終了 / 描画要求 {displayed}枚 / 遅延破棄 {skipped}枚。実画面提示fpsは未測定。");
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                ResultText.Text = $"再生比較に失敗しました（0x{exception.HResult:X8} {exception.Message}）。";
        }
        finally
        {
            if (ReferenceEquals(playbackCancellation, cancellation))
            {
                playbackCancellation = null;
                PlayPauseButton.Content = "再生";
            }
            cancellation.Dispose();
        }
    }

    private int SelectedGain() => GainSelect.SelectedItem is ComboBoxItem { Tag: string text } &&
        int.TryParse(text, out var value) ? value : 16;

    private void UpdatePlayingBitmaps(ComparisonFrame left, ComparisonFrame right, FrameDifferenceResult? difference)
    {
        if (playingLeft is null || playingLeft.PixelWidth != left.Width || playingLeft.PixelHeight != left.Height)
        {
            playingLeft = CreateWritable(left.Width, left.Height);
            playingRight = CreateWritable(right.Width, right.Height);
            playingHeatmap = CreateWritable(left.Width, left.Height);
            LeftImage.Source = WipeLeftImage.Source = playingLeft;
            RightImage.Source = WipeRightImage.Source = playingRight;
            HeatmapImage.Source = playingHeatmap;
        }
        var bounds = new Int32Rect(0, 0, left.Width, left.Height);
        playingLeft.WritePixels(bounds, left.Pixels, left.Width * 4, 0);
        playingRight!.WritePixels(bounds, right.Pixels, right.Width * 4, 0);
        if (difference is not null)
        {
            HeatmapImage.Source = playingHeatmap;
            playingHeatmap!.WritePixels(bounds, difference.Heatmap, difference.Width * 4, 0);
        }
        UpdateWipeBoundary();
    }

    private static WriteableBitmap CreateWritable(int width, int height) =>
        new(width, height, 96, 96, PixelFormats.Bgra32, null);

    private void GainSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        heatmapGain = SelectedGain();
        if (playbackCancellation is null && currentLeft is not null && currentRight is not null)
            RefreshHeatmap();
    }

    private void ViewSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThreeViewGrid is null || WipeViewGrid is null) return;
        var wipe = ViewSelect.SelectedIndex == 1;
        wipeMode = wipe;
        ThreeViewGrid.Visibility = wipe ? Visibility.Collapsed : Visibility.Visible;
        WipeViewGrid.Visibility = wipe ? Visibility.Visible : Visibility.Collapsed;
        if (wipe) UpdateWipeBoundary();
        else if (currentLeft is not null && currentRight is not null) RefreshHeatmap();
    }

    private void WipeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateWipeBoundary();

    private void WipeViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateWipeBoundary();

    private void UpdateWipeBoundary()
    {
        if (WipeViewport is null || WipeSlider is null || WipeRightLayer is null ||
            WipeBoundary is null || WipePositionText is null) return;
        var width = WipeViewport.ActualWidth;
        var height = WipeViewport.ActualHeight;
        var fraction = WipeSlider.Value / 100;
        var aspect = currentLeft is null ? 4d / 3 : (double)currentLeft.Width / currentLeft.Height;
        var imageWidth = Math.Min(width, height * aspect);
        var imageLeft = (width - imageWidth) / 2;
        var boundary = imageLeft + imageWidth * fraction;
        WipeRightLayer.Clip = new RectangleGeometry(new Rect(boundary, 0,
            Math.Max(0, width - boundary), height));
        WipeBoundary.Margin = new Thickness(boundary - WipeBoundary.Width / 2, 0, 0, 0);
        WipeBoundary.Visibility = fraction > 0 && fraction < 1 ? Visibility.Visible : Visibility.Collapsed;
        WipePositionText.Text = $"境界 {WipeSlider.Value:F0}%（左側: 左動画 / 右側: 右動画）";
    }

    private async Task LoadComparisonAsync()
    {
        if (LeftSelect.SelectedItem is not VideoChoice left ||
            RightSelect.SelectedItem is not VideoChoice right ||
            !long.TryParse(FrameNumberBox.Text, out var frameNumber) || frameNumber < 0)
        {
            ResultText.Text = "0以上の元フレーム番号と左右の動画を指定してください。";
            return;
        }
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = new CancellationTokenSource();
        var token = loadCancellation.Token;
        ResultText.Text = $"フレーム #{frameNumber} を復号中…";
        try
        {
            var leftTask = comparison.ReadFrameAsync(left.Path, frameNumber, token);
            var rightTask = comparison.ReadFrameAsync(right.Path, frameNumber, token);
            await Task.WhenAll(leftTask, rightTask);
            var leftFrame = await leftTask;
            var rightFrame = await rightTask;
            if (token.IsCancellationRequested) return;
            currentLeft = leftFrame;
            currentRight = rightFrame;
            playingLeft = playingRight = playingHeatmap = null;
            var leftBitmap = CreateBitmap(leftFrame.Width, leftFrame.Height, leftFrame.Pixels);
            var rightBitmap = CreateBitmap(rightFrame.Width, rightFrame.Height, rightFrame.Pixels);
            LeftImage.Source = WipeLeftImage.Source = leftBitmap;
            RightImage.Source = WipeRightImage.Source = rightBitmap;
            UpdateWipeBoundary();
            RefreshHeatmap();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
                ResultText.Text = $"比較画像を作成できませんでした（0x{exception.HResult:X8}）。";
        }
    }

    private void RefreshHeatmap()
    {
        if (currentLeft is null || currentRight is null) return;
        var gain = SelectedGain();
        var difference = FrameComparisonService.CreateHeatmap(currentLeft, currentRight, gain);
        HeatmapImage.Source = CreateBitmap(difference.Width, difference.Height, difference.Heatmap);
        ResultText.Text = $"元フレーム #{currentLeft.FrameNumber} / 平均RGB差 {difference.MeanAbsoluteRgbDifference:F2}（0～255）" +
            $" / 差分{gain}倍表示。色範囲や圧縮方式の差も含み、品質の優劣を示す値ではありません。";
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
            null, pixels, checked(width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        StopPlayback();
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
    }

    private sealed record VideoChoice(string Name, string Path);
}
